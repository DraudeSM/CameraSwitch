using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OBSWebsocketDotNet;

class Win32
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flag);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

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
    static readonly Dictionary<string, string> SceneMap = new()
    {
        { "0_0", "Monitor1" },
        { "-1920_0", "Monitor2" }
    };
    const string DefaultScene = "Portatil";
    const string LogPath = @"C:\ProgramData\CameraSwitch\camera-switch.log";
    const string TaskName = "CameraSwitch";

    static OBSWebsocket obs = new();
    static bool isConnected = false;

    static void Log(string msg)
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
                var scene = SceneMap.TryGetValue(key, out var s) ? s : DefaultScene;
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
            Thread.Sleep(500);
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

        Log("Aplicación iniciada.");

        EnsureScheduledTask();

        obs.Connected += (s, e) => { isConnected = true; Log("Conectado a OBS"); };
        obs.Disconnected += (s, e) => { isConnected = false; Log("Desconectado de OBS: " + e.DisconnectReason); };

        TryConnect();

        var cts = new CancellationTokenSource();
        var monitorThread = new Thread(() => MonitorLoop(cts.Token)) { IsBackground = true };
        monitorThread.Start();

        using var trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "CameraSwitch",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Salir", null, (s, e) =>
        {
            Log("Aplicación detenida manualmente desde el icono de la bandeja.");
            cts.Cancel();
            trayIcon.Visible = false;
            Application.Exit();
        });
        trayIcon.ContextMenuStrip = menu;

        Application.Run();
    }
}
