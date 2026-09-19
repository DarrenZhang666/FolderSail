using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace FolderSail.Helpers;

/// <summary>
/// Keeps FolderSail from jumping in front of a drop-target app when the user
/// clicks the file list to select or start a drag (Explorer-style side-by-side workflow).
/// Title-bar / non-list chrome clicks still activate normally.
/// </summary>
internal static class WindowClickActivation
{
    private const int WmMouseActivate = 0x0021;
    private const int HtClient = 1;
    private const int MaNoActivate = 3;

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook((IntPtr _, int msg, IntPtr _w, IntPtr lParam, ref bool handled) =>
                Hook(window, msg, lParam, ref handled));
        };
    }

    /// <summary>
    /// Focus a control only when this window is already foreground, so Focus()
    /// cannot call SetForegroundWindow and cover the drop target.
    /// </summary>
    public static void FocusWithoutRaising(UIElement? element)
    {
        if (element is null)
        {
            return;
        }

        var window = Window.GetWindow(element);
        if (window is null || !window.IsActive)
        {
            return;
        }

        element.Focus();
    }

    private static IntPtr Hook(Window window, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseActivate)
        {
            return IntPtr.Zero;
        }

        var hitTest = (int)((long)lParam & 0xFFFF);
        if (hitTest != HtClient)
        {
            return IntPtr.Zero;
        }

        if (!IsOverFileList(window))
        {
            return IntPtr.Zero;
        }

        // Accept the click (selection / drag) but do not bring this window to the front.
        handled = true;
        return new IntPtr(MaNoActivate);
    }

    private static bool IsOverFileList(Window window)
    {
        if (!GetCursorPos(out var screen))
        {
            return false;
        }

        var screenPoint = new Point(screen.X, screen.Y);
        Point client;
        try
        {
            client = window.PointFromScreen(screenPoint);
        }
        catch
        {
            return false;
        }

        var hit = VisualTreeHelper.HitTest(window, client);
        for (var current = hit?.VisualHit as DependencyObject;
             current != null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListBox { Name: "FilesList" })
            {
                return true;
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);
}
