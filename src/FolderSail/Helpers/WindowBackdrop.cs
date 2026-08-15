using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FolderSail.Helpers;

/// <summary>
/// Asks DWM for a Mica backdrop so the sidebar can frost like Finder's
/// translucent pane. Windows 10 simply keeps the solid fallback colours.
/// </summary>
internal static class WindowBackdrop
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmsbtMainWindow = 2;
    private const int DwmWcpRound = 2;

    public static void TryEnable(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var backdrop = DwmsbtMainWindow;
        var applied = DwmSetWindowAttribute(
            hwnd,
            DwmwaSystemBackdropType,
            ref backdrop,
            sizeof(int)) == 0;

        if (applied && HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
            window.Background = Brushes.Transparent;
        }

        var corners = DwmWcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size);
}
