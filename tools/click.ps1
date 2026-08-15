# Clicks a point (in physical pixels) inside the FolderSail window.
param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [switch]$Right
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Click {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    public const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
}
"@

$proc = Get-Process FolderSail -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[Click]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400

[Click]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 250

if ($Right) {
    [Click]::mouse_event([Click]::RightDown, 0, 0, 0, [IntPtr]::Zero)
    [Click]::mouse_event([Click]::RightUp, 0, 0, 0, [IntPtr]::Zero)
} else {
    [Click]::mouse_event([Click]::LeftDown, 0, 0, 0, [IntPtr]::Zero)
    [Click]::mouse_event([Click]::LeftUp, 0, 0, 0, [IntPtr]::Zero)
}

Start-Sleep -Milliseconds 700
Write-Output "clicked $X,$Y right=$Right"
