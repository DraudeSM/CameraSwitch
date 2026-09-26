using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OBSWebsocketDotNet;

class Win32
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flag);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, int flag);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}

class Program
{
    // Mapeo de respaldo, usado mientras no exista config.json (p.ej. justo tras actualizar
    // desde una versión anterior a la configuración dinámica, o si el usuario nunca abre el asistente).
    static readonly Dictionary<string, string> FallbackSceneMap = new()
    {
        { "0_0", "Monitor1" },
        { "-1920_0", "Monitor2" }
    };
    const string FallbackDefaultScene = "Portatil";

    const string LogPath = @"C:\ProgramData\CameraSwitch\camera-switch.log";
    const string ConfigPath = @"C:\ProgramData\CameraSwitch\config.json";
    const string TaskName = "CameraSwitch";

    static volatile Dictionary<string, string> sceneMap = new(FallbackSceneMap);
    static volatile string defaultScene = FallbackDefaultScene;

    static OBSWebsocket obs = new();
    static bool isConnected = false;

    internal static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {msg}{Environment.NewLine}");
        }
        catch { /* no bloquear el proceso por fallo de log */ }
    }

    static string GetActiveMonitorKey()
    {
        // Basado en la posición del cursor (el propósito original de la app), no en qué ventana
        // tiene el foco: durante una videollamada la ventana de Teams puede seguir "activa" aunque
        // el usuario mueva el ratón a otro monitor sin llegar a hacer clic ahí, y con la detección
        // por ventana activa eso no disparaba el cambio de escena.
        if (Win32.GetCursorPos(out var pt))
        {
            var hMonCursor = Win32.MonitorFromPoint(pt, 2);
            var miCursor = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(hMonCursor, ref miCursor))
                return $"{miCursor.rcMonitor.Left}_{miCursor.rcMonitor.Top}";
        }

        // Respaldo por si por lo que sea no se puede leer la posición del cursor.
        var hwnd = Win32.GetForegroundWindow();
        var hMon = Win32.MonitorFromWindow(hwnd, 2);
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(hMon, ref mi);
        return $"{mi.rcMonitor.Left}_{mi.rcMonitor.Top}";
    }

    static void TryConnect()
    {
        try
        {
            obs.ConnectAsync("ws://127.0.0.1:4455", "");
        }
        catch (Exception ex)
        {
            Log($"Error al intentar conectar: {ex.Message}");
        }
    }

    static void MonitorLoop(CancellationToken token)
    {
        string? lastKey = null;
        var lastReconnectAttempt = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!isConnected)
                {
                    // Reintenta cada 5s sin bloquear el bucle
                    if ((DateTime.Now - lastReconnectAttempt).TotalSeconds >= 5)
                    {
                        lastReconnectAttempt = DateTime.Now;
                        TryConnect();
                    }
                    Thread.Sleep(500);
                    continue;
                }

                var key = GetActiveMonitorKey();
                if (key != lastKey)
                {
                    var scene = sceneMap.TryGetValue(key, out var s) ? s : defaultScene;
                    try
                    {
                        obs.SetCurrentProgramScene(scene);
                        Log($"Posición: {key} -> Escena: {scene}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Error al cambiar escena ({scene}): {ex.Message}");
                    }
                    lastKey = key;
                }
            }
            catch (Exception ex)
            {
                // Nunca dejar morir este hilo en silencio: sin esto, un fallo inesperado aquí
                // detiene la conmutación para siempre hasta reiniciar la app.
                Log($"Error inesperado en MonitorLoop (se sigue intentando): {ex}");
            }

            Thread.Sleep(500);
        }
    }

    static void ShowHelp()
    {
        var mapLines = string.Join(Environment.NewLine,
            sceneMap.Select(kv => $"  • Monitor en posición {kv.Key} -> escena \"{kv.Value}\""));

        var text =
            "CameraSwitch conmuta automáticamente la escena activa de OBS según en qué " +
            "monitor tengas la ventana en primer plano.\n\n" +
            "Requiere que OBS esté abierto con el servidor WebSocket activo (puerto 4455, sin contraseña).\n\n" +
            "Por privacidad, la cámara virtual empieza apagada: usa \"Cámara virtual\" en este mismo " +
            "menú para activarla solo cuando vayas a hacer una llamada (p.ej. en Teams) y detenerla al terminar.\n\n" +
            "Mapeo de escenas actual:\n" + mapLines + "\n" +
            $"  • Cualquier otro monitor -> escena \"{defaultScene}\"\n\n" +
            "Usa \"Configurar cámaras...\" en este mismo menú para detectar tus monitores y cámaras " +
            "y crear o actualizar las escenas correspondientes en OBS.\n\n" +
            $"Registro de actividad: {LogPath}\n" +
            "(usa \"Abrir log\" en este mismo menú)\n\n" +
            "El inicio automático con Windows se gestiona mediante una tarea programada " +
            "llamada \"CameraSwitch\", creada automáticamente al arrancar la app.";

        MessageBox.Show(text, "CameraSwitch - Ayuda", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void ApplySceneConfig(SceneConfig config)
    {
        sceneMap = config.ToSceneMap();
        defaultScene = config.DefaultSceneName;
    }

    static void LoadSceneConfigOrFallback()
    {
        var config = SceneConfig.Load(ConfigPath);
        if (config != null && config.Scenes.Count > 0)
        {
            ApplySceneConfig(config);
            Log("Configuración de cámaras cargada desde config.json.");
        }
        else
        {
            sceneMap = new Dictionary<string, string>(FallbackSceneMap);
            defaultScene = FallbackDefaultScene;
        }
    }

    static void OpenSetupWizard()
    {
        if (!isConnected)
        {
            MessageBox.Show(
                "CameraSwitch necesita estar conectado a OBS para configurar las cámaras.\n\nAbre OBS y vuelve a intentarlo.",
                "CameraSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var currentConfig = SceneConfig.Load(ConfigPath);
        Log(currentConfig == null
            ? "OpenSetupWizard: no se pudo cargar config.json (null)."
            : $"OpenSetupWizard: config.json cargado con {currentConfig.Scenes.Count} escena(s): " +
              string.Join(" | ", currentConfig.Scenes.Select(s => $"{s.MonitorKey}->{s.SceneName}")));
        using var form = new SetupForm(obs, currentConfig);
        if (form.ShowDialog() == DialogResult.OK && form.ResultConfig != null)
        {
            form.ResultConfig.Save(ConfigPath);
            ApplySceneConfig(form.ResultConfig);
            Log("Configuración de cámaras guardada desde el asistente.");
        }
    }

    static void EnsureCameraConfig()
    {
        if (File.Exists(ConfigPath))
            return;

        // Da un margen breve a la conexión inicial con OBS, ya lanzada por MonitorLoop.
        var waitUntil = DateTime.Now.AddSeconds(3);
        while (!isConnected && DateTime.Now < waitUntil)
            Thread.Sleep(200);

        if (!isConnected)
        {
            Log("OBS no está disponible en el primer arranque; se omite el aviso de configuración de cámaras.");
            return;
        }

        var result = MessageBox.Show(
            "No se ha configurado todavía qué cámara usar para cada monitor.\n\n¿Quieres configurarlo ahora?",
            "CameraSwitch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            Log("El usuario ha rechazado configurar las cámaras en el primer arranque.");
            return;
        }

        OpenSetupWizard();
    }

    static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (!File.Exists(LogPath))
                File.WriteAllText(LogPath, string.Empty);

            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se ha podido abrir el log:\n{LogPath}\n\n{ex.Message}",
                "CameraSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    static void RefreshVirtualCamMenuItem(ToolStripMenuItem item)
    {
        if (!isConnected)
        {
            item.Enabled = false;
            item.Checked = false;
            item.Text = "Cámara virtual (sin conexión a OBS)";
            return;
        }

        try
        {
            var status = obs.GetVirtualCamStatus();
            item.Enabled = true;
            item.Checked = status.IsActive;
            item.Text = status.IsActive ? "Cámara virtual activa (clic para detener)" : "Cámara virtual detenida (clic para activar)";
        }
        catch (Exception ex)
        {
            Log($"Error al consultar el estado de la cámara virtual: {ex.Message}");
            item.Enabled = false;
            item.Text = "Cámara virtual (estado desconocido)";
        }
    }

    static void ToggleVirtualCam()
    {
        try
        {
            var status = obs.GetVirtualCamStatus();
            if (status.IsActive)
            {
                obs.StopVirtualCam();
                Log("Cámara virtual detenida manualmente desde el menú.");
            }
            else
            {
                obs.StartVirtualCam();
                Log("Cámara virtual iniciada manualmente desde el menú.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se ha podido cambiar el estado de la cámara virtual:\n{ex.Message}",
                "CameraSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    static Icon LoadAppIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CameraSwitch.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    static bool IsScheduledTaskInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\" /XML")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                return false;

            // La tarea existe, pero solo cuenta como válida si apunta al ejecutable actual;
            // si el .exe se movió (p.ej. tras instalar en una carpeta fija), hay que reregistrarla.
            var match = System.Text.RegularExpressions.Regex.Match(output, "<Command>(.*?)</Command>");
            var registeredPath = match.Success ? match.Groups[1].Value.Trim() : "";
            var currentPath = Application.ExecutablePath;
            var upToDate = string.Equals(registeredPath, currentPath, StringComparison.OrdinalIgnoreCase);
            if (!upToDate)
                Log($"Tarea programada existente apunta a '{registeredPath}', pero el ejecutable actual es '{currentPath}'.");
            return upToDate;
        }
        catch (Exception ex)
        {
            Log($"Error al comprobar la tarea programada: {ex.Message}");
            return true; // no insistir si no se puede comprobar
        }
    }

    static void RegisterScheduledTaskElevated()
    {
        var exePath = Application.ExecutablePath;
        var script =
            "$action = New-ScheduledTaskAction -Execute '" + exePath + "'; " +
            "$trigger = New-ScheduledTaskTrigger -AtLogOn; " +
            "$settings = New-ScheduledTaskSettingsSet -Hidden -ExecutionTimeLimit ([TimeSpan]::Zero); " +
            "Register-ScheduledTask -TaskName '" + TaskName + "' -Action $action -Trigger $trigger -Settings $settings -Description 'Cambia automáticamente la cámara OBS según el monitor activo' -Force";

        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            if (p != null && p.ExitCode == 0)
            {
                Log("Tarea programada 'CameraSwitch' registrada correctamente.");
            }
            else
            {
                Log($"El registro de la tarea programada finalizó con código {p?.ExitCode}.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // El usuario canceló el cuadro de UAC
            Log($"El usuario canceló la elevación de permisos: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"Error al registrar la tarea programada: {ex.Message}");
        }
    }

    static void EnsureScheduledTask()
    {
        if (IsScheduledTaskInstalled())
        {
            Log("Tarea programada 'CameraSwitch' ya está configurada.");
            return;
        }

        Log("Tarea programada ausente o desactualizada. Solicitando confirmación al usuario.");
        var result = MessageBox.Show(
            "El inicio automático de CameraSwitch con Windows no está configurado o apunta a una ubicación antigua.\n\n¿Quieres configurarlo ahora? Se solicitarán permisos de administrador.",
            "CameraSwitch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            Log("El usuario ha rechazado configurar el inicio automático.");
            return;
        }

        RegisterScheduledTaskElevated();
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();

        // Red de seguridad para diagnóstico: si algo revienta en un hilo que no controlamos
        // directamente (p.ej. dentro de la propia librería de OBS websocket), que quede
        // constancia en el log en vez de que el proceso desaparezca sin dejar rastro.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log($"Excepción no controlada, la aplicación puede cerrarse: {e.ExceptionObject}");
        Application.ThreadException += (s, e) =>
            Log($"Excepción no controlada en el hilo de interfaz: {e.Exception}");

        Log("Aplicación iniciada.");

        EnsureScheduledTask();
        LoadSceneConfigOrFallback();

        obs.Connected += (s, e) => { isConnected = true; Log("Conectado a OBS"); };
        obs.Disconnected += (s, e) => { isConnected = false; Log("Desconectado de OBS: " + e.DisconnectReason); };

        // El primer intento de conexión lo hace el propio MonitorLoop en su primera
        // iteración; no duplicar aquí la llamada, o se solapan dos ConnectAsync a la vez.
        var cts = new CancellationTokenSource();
        var monitorThread = new Thread(() => MonitorLoop(cts.Token)) { IsBackground = true };
        monitorThread.Start();

        EnsureCameraConfig();

        using var trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "CameraSwitch",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        var virtualCamItem = new ToolStripMenuItem("Cámara virtual");
        virtualCamItem.Click += (s, e) => ToggleVirtualCam();
        menu.Items.Add(virtualCamItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Configurar cámaras...", null, (s, e) => OpenSetupWizard());
        menu.Items.Add("Ayuda", null, (s, e) => ShowHelp());
        menu.Items.Add("Abrir log", null, (s, e) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (s, e) =>
        {
            Log("Aplicación detenida manualmente desde el icono de la bandeja.");
            cts.Cancel();
            trayIcon.Visible = false;
            Application.Exit();
        });
        menu.Opening += (s, e) => RefreshVirtualCamMenuItem(virtualCamItem);
        trayIcon.ContextMenuStrip = menu;

        Application.Run();
    }
}
