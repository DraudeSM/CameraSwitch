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

    static string GetActiveMonitorKey()
    {
        var hwnd = Win32.GetForegroundWindow();
        var hMon = Win32.MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(hMon, ref mi);
        return $"{mi.rcMonitor.Left}_{mi.rcMonitor.Top}";
    }

    static void Main()
    {
        var obs = new OBSWebsocket();
        var connected = new ManualResetEventSlim(false);

        obs.Connected += (s, e) => { Console.WriteLine("Conectado a OBS"); connected.Set(); };
        obs.Disconnected += (s, e) => Console.WriteLine("Desconectado: " + e.DisconnectReason);

        obs.ConnectAsync("ws://127.0.0.1:4455", "");

        if (!connected.Wait(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("Timeout esperando conexión");
            return;
        }

        Console.WriteLine("Monitorizando pantalla activa. Ctrl+C para salir.");

        string? lastKey = null;
        while (true)
        {
            var key = GetActiveMonitorKey();
            if (key != lastKey)
            {
                var scene = SceneMap.TryGetValue(key, out var s) ? s : DefaultScene;
                Console.WriteLine($"Posición: {key} -> Escena: {scene}");
                try { obs.SetCurrentProgramScene(scene); }
                catch (Exception ex) { Console.WriteLine($"Error al cambiar escena: {ex.Message}"); }
                lastKey = key;
            }
            Thread.Sleep(500);
        }
    }
}