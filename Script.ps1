# monitor-camera-switch.ps# monitor-camera-switch.ps1

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int flag);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
"@

# Mapeo confirmado: 0_0 = Monitor1, -1920_0 = Monitor2. Pendiente confirmar posición del portátil (default por ahora).
$sceneMap = @{
    "0_0"     = "Monitor1"
    "-1920_0" = "Monitor2"
}
$defaultScene = "Portatil"

function Get-ActiveMonitorKey {
    $hwnd = [Win32]::GetForegroundWindow()
    $hMon = [Win32]::MonitorFromWindow($hwnd, 2)
    $mi = New-Object Win32+MONITORINFO
    $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mi)
    [Win32]::GetMonitorInfo($hMon, [ref]$mi) | Out-Null
    return "$($mi.rcMonitor.Left)_$($mi.rcMonitor.Top)"
}

$obsUri = [Uri]"ws://127.0.0.1:4455"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource

function Receive-OBSMessage {
    $buf = New-Object byte[] 8192
    $ms = New-Object System.IO.MemoryStream
    do {
        $seg = New-Object System.ArraySegment[byte] (,$buf)
        $result = $ws.ReceiveAsync($seg, $cts.Token).Result
        if ($result.Count -gt 0) { $ms.Write($buf, 0, $result.Count) }
    } while (-not $result.EndOfMessage)
    return [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
}

function Connect-OBS {
    $ws.ConnectAsync($obsUri, $cts.Token).Wait()

    $hello = Receive-OBSMessage
    Write-Host "HELLO recibido: $hello"

    $identify = @{ op = 1; d = @{ rpcVersion = 1; eventSubscriptions = 0 } } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($identify)
    $ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), 1, $true, $cts.Token).Wait()

    $identified = Receive-OBSMessage
    Write-Host "IDENTIFIED recibido: $identified"
}

function Set-OBSScene($sceneName) {
    $req = @{
        op = 6
        d = @{
            requestType = "SetCurrentProgramScene"
            requestId = [guid]::NewGuid().ToString()
            requestData = @{ sceneName = $sceneName }
        }
    } | ConvertTo-Json -Compress -Depth 5
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($req)
    $ws.SendAsync((New-Object System.ArraySegment[byte] (,$bytes)), 1, $true, $cts.Token).Wait()

    $resp = Receive-OBSMessage
    if ($resp -notmatch '"result":true') {
        Write-Host "Aviso: respuesta inesperada de OBS: $resp"
    }
}

Connect-OBS
Write-Host "Conectado a OBS. Cambiando cámara según pantalla activa..."

$lastKey = $null
while ($true) {
    $key = Get-ActiveMonitorKey
    if ($key -ne $lastKey) {
        $scene = if ($sceneMap.ContainsKey($key)) { $sceneMap[$key] } else { $defaultScene }
        Write-Host "Posición: $key -> Escena: $scene"
        Set-OBSScene $scene
        $lastKey = $key
    }
    Start-Sleep -Milliseconds 500
}