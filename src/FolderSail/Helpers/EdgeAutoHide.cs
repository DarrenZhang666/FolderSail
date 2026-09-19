using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FolderSail.Helpers;

public enum DockEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// Drag a FolderSail window to a screen edge to collapse it to a thin peek strip.
/// Hover the strip to expand; leave the window to tuck it away again.
/// While dragging, faint edge hints show where docking is available.
/// Multiple windows on the same edge share the edge as stacked peeks.
/// </summary>
internal sealed class EdgeAutoHide : IDisposable
{
    private const int PeekThickness = 10;
    private const int DockThresholdPx = 36;
    private const int HintHotThresholdPx = 56;
    private const int UndockThresholdPx = 80;
    private const int WmExitSizeMove = 0x0232;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmMoving = 0x0216;
    private const double CollapseDelayMs = 480;
    private const double PollMs = 80;
    private const double HintThickness = 6;

    private static readonly List<EdgeAutoHide> Instances = [];
    private static readonly object Gate = new();

    private readonly Window _window;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _hintTimer;
    private readonly Border _peekChrome;
    private readonly EdgeHintOverlay _hints = new();

    private HwndSource? _source;
    private Rect _restoreBounds;
    private double _savedMinWidth;
    private double _savedMinHeight;
    private DockEdge _edge = DockEdge.None;
    private bool _collapsed;
    private bool _expandedFromPeek;
    private bool _disposed;
    private bool _moving;
    private int _stackIndex;
    private DateTime _dockedUtc = DateTime.MinValue;
    private FrameworkElement? _originalContent;

    public EdgeAutoHide(Window window)
    {
        _window = window;
        _peekChrome = new Border
        {
            Background = TryAccentBrush(),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Opacity = 0.95,
            Child = new TextBlock
            {
                Text = "FolderSail",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMs) };
        _pollTimer.Tick += (_, _) => OnPoll();
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CollapseDelayMs) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_edge != DockEdge.None && _expandedFromPeek && !IsMouseOverWindowOrPeek())
            {
                CollapseToPeek();
            }
        };
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _hintTimer.Tick += (_, _) => UpdateDragHints();

        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnClosed;
        _window.StateChanged += OnStateChanged;

        lock (Gate)
        {
            Instances.Add(this);
        }
    }

    public DockEdge Edge => _edge;
    public bool IsDocked => _edge != DockEdge.None;
    public bool IsCollapsed => _collapsed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Stop();
        _collapseTimer.Stop();
        _hintTimer.Stop();
        _hints.Hide();
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnClosed;
        _window.StateChanged -= OnStateChanged;
        _source?.RemoveHook(Hook);

        lock (Gate)
        {
            Instances.Remove(this);
        }

        if (_edge != DockEdge.None)
        {
            var edge = _edge;
            _edge = DockEdge.None;
            RelayoutEdge(edge);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState != WindowState.Normal && _edge != DockEdge.None)
        {
            Undock(restoreBounds: true);
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmEnterSizeMove)
        {
            _moving = true;
            _collapseTimer.Stop();
            if (_collapsed)
            {
                ExpandForInteraction(keepDocked: false);
            }

            if (_window.WindowState == WindowState.Normal)
            {
                _hints.Show(GetWorkAreaDip(_window));
                _hintTimer.Start();
                UpdateDragHints();
            }
        }
        else if (msg == WmMoving)
        {
            if (_moving)
            {
                UpdateDragHints();
            }
        }
        else if (msg == WmExitSizeMove)
        {
            _moving = false;
            _hintTimer.Stop();
            _hints.Hide();
            TryDockFromCurrentPosition();
        }

        return IntPtr.Zero;
    }

    private void UpdateDragHints()
    {
        if (!_moving || _window.WindowState != WindowState.Normal)
        {
            return;
        }

        var work = GetWorkAreaDip(_window);
        _hints.EnsureLayout(work);

        var bounds = GetWindowBounds(_window);
        var cursor = GetCursorDip(_window);
        var hot = DetectEdge(bounds, work, cursor, HintHotThresholdPx);
        _hints.SetHotEdge(hot);
    }

    private void TryDockFromCurrentPosition()
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return;
        }

        var work = GetWorkAreaDip(_window);
        var bounds = GetWindowBounds(_window);
        var cursor = GetCursorDip(_window);
        var edge = DetectEdge(bounds, work, cursor, DockThresholdPx);

        if (edge == DockEdge.None)
        {
            if (_edge != DockEdge.None)
            {
                var stillNear = DetectEdge(bounds, work, cursor, UndockThresholdPx) == _edge;
                if (!stillNear)
                {
                    Undock(restoreBounds: false);
                }
            }

            return;
        }

        Dock(edge, bounds);
    }

    private void Dock(DockEdge edge, Rect currentBounds)
    {
        var previous = _edge;
        if (!_collapsed && !_expandedFromPeek)
        {
            _restoreBounds = NormalizeRestoreBounds(currentBounds);
        }

        _edge = edge;
        _dockedUtc = DateTime.UtcNow;
        _window.Topmost = true;
        _pollTimer.Start();

        if (previous != edge && previous != DockEdge.None)
        {
            RelayoutEdge(previous);
        }

        CollapseToPeek();
        RelayoutEdge(edge);
    }

    private void Undock(bool restoreBounds)
    {
        var previous = _edge;
        _collapseTimer.Stop();
        _pollTimer.Stop();
        _hintTimer.Stop();
        _hints.Hide();
        _edge = DockEdge.None;
        _collapsed = false;
        _expandedFromPeek = false;
        _window.Topmost = false;
        HidePeekChrome();
        RestoreMinSize();

        if (restoreBounds && _restoreBounds.Width > 0 && _restoreBounds.Height > 0)
        {
            ApplyBounds(_restoreBounds);
        }

        if (previous != DockEdge.None)
        {
            RelayoutEdge(previous);
        }
    }

    private void CollapseToPeek()
    {
        if (_edge == DockEdge.None)
        {
            return;
        }

        EnsureMinSizeRelaxed();
        _collapsed = true;
        _expandedFromPeek = false;
        _window.Topmost = true;
        ShowPeekChrome();
        ApplyPeekBounds(GetWorkAreaDip(_window), _stackIndex, CountOnEdge(_edge));
    }

    private void ExpandFromPeek()
    {
        if (_edge == DockEdge.None || !_collapsed)
        {
            return;
        }

        ExpandForInteraction(keepDocked: true);
    }

    private void ExpandForInteraction(bool keepDocked)
    {
        EnsureMinSizeRelaxed();
        _collapsed = false;
        _expandedFromPeek = keepDocked;
        HidePeekChrome();

        var work = GetWorkAreaDip(_window);
        var restore = _restoreBounds.Width > 0 ? _restoreBounds : GetWindowBounds(_window);
        restore = NormalizeRestoreBounds(restore);

        var left = restore.Left;
        var top = restore.Top;
        switch (_edge)
        {
            case DockEdge.Left:
                left = work.Left;
                top = Clamp(restore.Top, work.Top, work.Bottom - restore.Height);
                break;
            case DockEdge.Right:
                left = work.Right - restore.Width;
                top = Clamp(restore.Top, work.Top, work.Bottom - restore.Height);
                break;
            case DockEdge.Top:
                top = work.Top;
                left = Clamp(restore.Left, work.Left, work.Right - restore.Width);
                break;
            case DockEdge.Bottom:
                top = work.Bottom - restore.Height;
                left = Clamp(restore.Left, work.Left, work.Right - restore.Width);
                break;
        }

        RestoreMinSize();
        ApplyBounds(new Rect(left, top, restore.Width, restore.Height));
        _window.Topmost = keepDocked;

        if (!keepDocked)
        {
            Undock(restoreBounds: false);
        }
        else
        {
            RelayoutEdge(_edge);
        }
    }

    private void ShowPeekChrome()
    {
        if (_window.Content is Border { Tag: "EdgePeek" })
        {
            UpdatePeekLabelOrientation();
            return;
        }

        if (_window.Content is FrameworkElement original)
        {
            _originalContent = original;
        }

        UpdatePeekLabelOrientation();
        _peekChrome.Tag = "EdgePeek";
        _window.Content = _peekChrome;
    }

    private void HidePeekChrome()
    {
        if (_originalContent is null)
        {
            return;
        }

        if (ReferenceEquals(_window.Content, _peekChrome))
        {
            _window.Content = _originalContent;
        }

        _originalContent = null;
    }

    private void UpdatePeekLabelOrientation()
    {
        if (_peekChrome.Child is not TextBlock label)
        {
            return;
        }

        if (_edge is DockEdge.Left or DockEdge.Right)
        {
            label.LayoutTransform = new RotateTransform(90);
            label.Text = "FS";
            label.Margin = new Thickness(0);
        }
        else
        {
            label.LayoutTransform = Transform.Identity;
            label.Text = "FolderSail";
            label.Margin = new Thickness(6, 0, 6, 0);
        }
    }

    private void OnPoll()
    {
        if (_moving || _edge == DockEdge.None || _window.WindowState != WindowState.Normal)
        {
            return;
        }

        var over = IsMouseOverWindowOrPeek();
        if (_collapsed)
        {
            if (over)
            {
                ExpandFromPeek();
            }

            return;
        }

        if (_expandedFromPeek)
        {
            if (over)
            {
                _collapseTimer.Stop();
            }
            else if (!_collapseTimer.IsEnabled)
            {
                _collapseTimer.Start();
            }
        }
    }

    private bool IsMouseOverWindowOrPeek()
    {
        var pt = GetCursorDip(_window);
        var bounds = GetWindowBounds(_window);
        bounds.Inflate(8, 8);
        return bounds.Contains(pt);
    }

    private void ApplyPeekBounds(Rect work, int index, int count)
    {
        count = Math.Max(1, count);
        index = Math.Clamp(index, 0, count - 1);

        double left, top, width, height;
        if (_edge is DockEdge.Left or DockEdge.Right)
        {
            height = work.Height / count;
            top = work.Top + (height * index);
            width = PeekThickness;
            // Keep 1 DIP inside the work area so DPI rounding cannot push the strip off-screen.
            left = _edge == DockEdge.Left
                ? work.Left
                : work.Right - PeekThickness - 1;
            if (index == count - 1)
            {
                height = work.Bottom - top;
            }
        }
        else
        {
            width = work.Width / count;
            left = work.Left + (width * index);
            height = PeekThickness;
            top = _edge == DockEdge.Top
                ? work.Top
                : work.Bottom - PeekThickness - 1;
            if (index == count - 1)
            {
                width = work.Right - left;
            }
        }

        ApplyBounds(new Rect(left, top, Math.Max(PeekThickness, width), Math.Max(PeekThickness, height)));
    }

    private void EnsureMinSizeRelaxed()
    {
        if (_savedMinWidth <= 0)
        {
            _savedMinWidth = _window.MinWidth;
            _savedMinHeight = _window.MinHeight;
        }

        _window.MinWidth = PeekThickness;
        _window.MinHeight = PeekThickness;
    }

    private void RestoreMinSize()
    {
        if (_savedMinWidth > 0)
        {
            _window.MinWidth = _savedMinWidth;
            _window.MinHeight = _savedMinHeight;
        }
    }

    private void ApplyBounds(Rect bounds)
    {
        _window.WindowState = WindowState.Normal;
        _window.Left = bounds.Left;
        _window.Top = bounds.Top;
        _window.Width = Math.Max(_window.MinWidth, bounds.Width);
        _window.Height = Math.Max(_window.MinHeight, bounds.Height);
    }

    private static Rect NormalizeRestoreBounds(Rect bounds)
    {
        if (bounds.Width < 400 || bounds.Height < 300)
        {
            return new Rect(bounds.Left, bounds.Top, 1100, 720);
        }

        return bounds;
    }

    /// <summary>
    /// Prefer the edge the cursor is pressing against; fall back to window chrome distance.
    /// This fixes right-edge docking on DPI-scaled displays where width rounding drifts.
    /// </summary>
    private static DockEdge DetectEdge(Rect bounds, Rect work, Point cursor, int threshold)
    {
        var best = DockEdge.None;
        var bestDist = double.MaxValue;

        void Consider(DockEdge edge, double windowDist, double cursorDist)
        {
            var dist = Math.Min(windowDist, cursorDist);
            if (dist <= threshold && dist < bestDist)
            {
                best = edge;
                bestDist = dist;
            }
        }

        Consider(
            DockEdge.Left,
            Math.Abs(bounds.Left - work.Left),
            Math.Abs(cursor.X - work.Left));
        Consider(
            DockEdge.Right,
            Math.Abs(bounds.Right - work.Right),
            Math.Abs(cursor.X - work.Right));
        Consider(
            DockEdge.Top,
            Math.Abs(bounds.Top - work.Top),
            Math.Abs(cursor.Y - work.Top));
        Consider(
            DockEdge.Bottom,
            Math.Abs(bounds.Bottom - work.Bottom),
            Math.Abs(cursor.Y - work.Bottom));

        return best;
    }

    private static int CountOnEdge(DockEdge edge)
    {
        lock (Gate)
        {
            return Instances.Count(i => i._edge == edge && !i._disposed);
        }
    }

    private static void RelayoutEdge(DockEdge edge)
    {
        if (edge == DockEdge.None)
        {
            return;
        }

        List<EdgeAutoHide> list;
        lock (Gate)
        {
            list = Instances
                .Where(i => i._edge == edge && !i._disposed)
                .OrderBy(i => i._dockedUtc)
                .ToList();
        }

        for (var i = 0; i < list.Count; i++)
        {
            list[i]._stackIndex = i;
            if (list[i]._collapsed)
            {
                list[i].ApplyPeekBounds(GetWorkAreaDip(list[i]._window), i, list.Count);
            }
        }
    }

    private static Rect GetWindowBounds(Window window) =>
        new(window.Left, window.Top,
            window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            window.ActualHeight > 0 ? window.ActualHeight : window.Height);

    private static void GetDpi(Window window, out double dpiX, out double dpiY)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
            if (dpiX > 0 && dpiY > 0)
            {
                return;
            }
        }

        dpiX = 1;
        dpiY = 1;
    }

    private static Point GetCursorDip(Window window)
    {
        GetDpi(window, out var dpiX, out var dpiY);
        if (!GetCursorPos(out var pt))
        {
            return new Point(window.Left + (window.Width / 2), window.Top + 8);
        }

        return new Point(pt.X / dpiX, pt.Y / dpiY);
    }

    private static Rect GetWorkAreaDip(Window window)
    {
        GetDpi(window, out var dpiX, out var dpiY);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(hwnd, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var r = info.Work;
                return new Rect(
                    r.Left / dpiX,
                    r.Top / dpiY,
                    (r.Right - r.Left) / dpiX,
                    (r.Bottom - r.Top) / dpiY);
            }
        }

        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    private static Brush TryAccentBrush()
    {
        if (Application.Current?.TryFindResource("Accent.Base") is Brush brush)
        {
            return brush;
        }

        var fallback = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);

    /// <summary>
    /// Soft glowing strips on all four work-area edges while the window is being dragged.
    /// </summary>
    private sealed class EdgeHintOverlay
    {
        private Window? _host;
        private Border? _left;
        private Border? _right;
        private Border? _top;
        private Border? _bottom;
        private DockEdge _hot = DockEdge.None;

        public void Show(Rect workDip)
        {
            EnsureHost(workDip);
            if (_host is null)
            {
                return;
            }

            _host.Left = workDip.Left;
            _host.Top = workDip.Top;
            _host.Width = workDip.Width;
            _host.Height = workDip.Height;
            if (!_host.IsVisible)
            {
                _host.Show();
            }

            SetHotEdge(DockEdge.None);
        }

        public void EnsureLayout(Rect workDip)
        {
            if (_host is null)
            {
                Show(workDip);
                return;
            }

            _host.Left = workDip.Left;
            _host.Top = workDip.Top;
            _host.Width = workDip.Width;
            _host.Height = workDip.Height;
        }

        public void Hide()
        {
            if (_host is null)
            {
                return;
            }

            _host.Hide();
            _hot = DockEdge.None;
        }

        public void SetHotEdge(DockEdge edge)
        {
            _hot = edge;
            ApplyOpacity(_left, edge == DockEdge.Left);
            ApplyOpacity(_right, edge == DockEdge.Right);
            ApplyOpacity(_top, edge == DockEdge.Top);
            ApplyOpacity(_bottom, edge == DockEdge.Bottom);
        }

        private void EnsureHost(Rect workDip)
        {
            if (_host != null)
            {
                return;
            }

            var accent = TryAccentBrush().Clone();
            if (accent.CanFreeze)
            {
                accent.Freeze();
            }

            _left = MakeStrip(accent);
            _right = MakeStrip(accent);
            _top = MakeStrip(accent);
            _bottom = MakeStrip(accent);

            var root = new Grid { IsHitTestVisible = false };
            root.Children.Add(Place(_left, HorizontalAlignment.Left, VerticalAlignment.Stretch, HintThickness, double.NaN));
            root.Children.Add(Place(_right, HorizontalAlignment.Right, VerticalAlignment.Stretch, HintThickness, double.NaN));
            root.Children.Add(Place(_top, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, HintThickness));
            root.Children.Add(Place(_bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, HintThickness));

            _host = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Focusable = false,
                IsHitTestVisible = false,
                Left = workDip.Left,
                Top = workDip.Top,
                Width = workDip.Width,
                Height = workDip.Height,
                Content = root
            };
        }

        private static Border MakeStrip(Brush accent) =>
            new()
            {
                Background = accent,
                Opacity = 0.22,
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false
            };

        private static Border Place(
            Border strip,
            HorizontalAlignment horizontal,
            VerticalAlignment vertical,
            double width,
            double height)
        {
            strip.HorizontalAlignment = horizontal;
            strip.VerticalAlignment = vertical;
            if (!double.IsNaN(width))
            {
                strip.Width = width;
            }

            if (!double.IsNaN(height))
            {
                strip.Height = height;
            }

            return strip;
        }

        private static void ApplyOpacity(Border? strip, bool hot)
        {
            if (strip is null)
            {
                return;
            }

            strip.Opacity = hot ? 0.72 : 0.22;
            strip.Width = strip.HorizontalAlignment is HorizontalAlignment.Left or HorizontalAlignment.Right
                ? (hot ? HintThickness + 4 : HintThickness)
                : double.NaN;
            strip.Height = strip.VerticalAlignment is VerticalAlignment.Top or VerticalAlignment.Bottom
                ? (hot ? HintThickness + 4 : HintThickness)
                : double.NaN;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
