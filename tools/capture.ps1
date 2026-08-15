# Captures the FolderSail window to a PNG so UI changes can be reviewed.
# Uses PrintWindow so the result is the app window even if another window
# happens to be in the foreground.
param(
    [string]$OutFile = "$PSScriptRoot\..\artifacts\shot.png"
)

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$proc = Get-Process FolderSail -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$handle = $proc.MainWindowHandle

if ([Win]::IsIconic($handle)) {
    [Win]::ShowWindow($handle, 3) | Out-Null   # SW_MAXIMIZE
    Start-Sleep -Milliseconds 900
}

$rect = New-Object Win+RECT
[Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.R - $rect.L
$height = $rect.B - $rect.T

if ($width -le 0 -or $height -le 0) { throw "window is minimised ($width x $height)" }

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$bmp = New-Object System.Drawing.Bitmap $width, $height
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $gfx.GetHdc()
# 2 = PW_RENDERFULLCONTENT, required for hardware-composited WPF surfaces.
[Win]::PrintWindow($handle, $dc, 2) | Out-Null
$gfx.ReleaseHdc($dc)
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose()
$bmp.Dispose()

Write-Output "saved $OutFile ($width x $height)"
