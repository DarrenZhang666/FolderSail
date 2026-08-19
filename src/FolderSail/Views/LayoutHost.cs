using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FolderSail.Core.Models;
using FolderSail.Helpers;
using FolderSail.ViewModels;

namespace FolderSail.Views;

public class LayoutHost : ItemsControl
{
    private const double Grip = 6;
    private const double MinRelWidth = 0.12;
    private const double MinRelHeight = 0.12;
    private const double EdgeEps = 0.02;
    private INotifyCollectionChanged? _collectionSubscription;
    private readonly List<PaneSlot> _slots = [];

    private sealed class PaneSlot
    {
        public required FrameworkElement Chrome { get; init; }
        public required PaneBounds Bounds { get; set; }
        public int Index { get; init; }
    }

    private enum EdgeKind
    {
        Left,
        Right,
        Top,
        Bottom
    }

    static LayoutHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LayoutHost), new FrameworkPropertyMetadata(typeof(LayoutHost)));
    }

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(nameof(LayoutMode), typeof(LayoutMode), typeof(LayoutHost),
            new PropertyMetadata(LayoutMode.Dual, OnLayoutChanged));

    public LayoutMode LayoutMode
    {
        get => (LayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (Template?.FindName("PART_Host", this) is FrameworkElement host)
        {
            host.SizeChanged -= OnHostSizeChanged;
            host.SizeChanged += OnHostSizeChanged;
        }

        Rebuild();
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);

        if (_collectionSubscription != null)
        {
            _collectionSubscription.CollectionChanged -= OnCollectionChanged;
            _collectionSubscription = null;
        }

        if (newValue is INotifyCollectionChanged collection)
        {
            _collectionSubscription = collection;
            _collectionSubscription.CollectionChanged += OnCollectionChanged;
        }

        Rebuild();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LayoutModeProperty || e.Property == DataContextProperty)
        {
            Rebuild();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LayoutHost host)
        {
            host.Rebuild();
        }
    }

    private MainViewModel? Owner =>
        DataContext as MainViewModel
        ?? Window.GetWindow(this)?.DataContext as MainViewModel;

    private Canvas? HostCanvas => Template?.FindName("PART_Host", this) as Canvas;

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => ApplySlotLayout();

    private void Rebuild()
    {
        if (HostCanvas is not { } canvas)
        {
            return;
        }

        canvas.Children.Clear();
        _slots.Clear();

        var definition = LayoutMode.GetDefinition();
        var defaults = CreateDefaultBounds(definition);
        var saved = Owner?.GetLayoutPaneBounds(LayoutMode);
        var bounds = NormalizeSavedBounds(saved, defaults);

        var index = 0;
        foreach (var item in Items)
        {
            if (index >= bounds.Count)
            {
                break;
            }

            var slotBounds = bounds[index].Clone();
            var chrome = CreateChrome(item, index);
            canvas.Children.Add(chrome);
            _slots.Add(new PaneSlot
            {
                Chrome = chrome,
                Bounds = slotBounds,
                Index = index
            });
            index++;
        }

        ApplySlotLayout();
    }

    private FrameworkElement CreateChrome(object item, int paneIndex)
    {
        var paneView = new PaneView { DataContext = item };
        var root = new Grid();
        root.Children.Add(paneView);

        root.Children.Add(CreateEdgeThumb(paneIndex, EdgeKind.Left));
        root.Children.Add(CreateEdgeThumb(paneIndex, EdgeKind.Right));
        root.Children.Add(CreateEdgeThumb(paneIndex, EdgeKind.Top));
        root.Children.Add(CreateEdgeThumb(paneIndex, EdgeKind.Bottom));
        return root;
    }

    private Thumb CreateEdgeThumb(int paneIndex, EdgeKind edge)
    {
        var thumb = new Thumb
        {
            Focusable = false,
            Background = Brushes.Transparent,
            Cursor = edge is EdgeKind.Left or EdgeKind.Right ? Cursors.SizeWE : Cursors.SizeNS,
            ToolTip = Loc.Get("Loc.ResizePane"),
            Tag = (paneIndex, edge)
        };

        if (TryFindResource("PaneEdgeThumb") is Style style)
        {
            thumb.Style = style;
            thumb.Cursor = edge is EdgeKind.Left or EdgeKind.Right ? Cursors.SizeWE : Cursors.SizeNS;
        }

        switch (edge)
        {
            case EdgeKind.Left:
                thumb.Width = Grip;
                thumb.HorizontalAlignment = HorizontalAlignment.Left;
                thumb.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case EdgeKind.Right:
                thumb.Width = Grip;
                thumb.HorizontalAlignment = HorizontalAlignment.Right;
                thumb.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case EdgeKind.Top:
                thumb.Height = Grip;
                thumb.VerticalAlignment = VerticalAlignment.Top;
                thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
            case EdgeKind.Bottom:
                thumb.Height = Grip;
                thumb.VerticalAlignment = VerticalAlignment.Bottom;
                thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
        }

        Panel.SetZIndex(thumb, 20);
        thumb.DragDelta += OnEdgeDragDelta;
        thumb.DragCompleted += OnEdgeDragCompleted;
        return thumb;
    }

    private void OnEdgeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: ValueTuple<int, EdgeKind> tag } || HostCanvas is not { } canvas)
        {
            return;
        }

        var (paneIndex, edge) = tag;
        if (paneIndex < 0 || paneIndex >= _slots.Count)
        {
            return;
        }

        var hostW = Math.Max(1, canvas.ActualWidth);
        var hostH = Math.Max(1, canvas.ActualHeight);
        var slot = _slots[paneIndex];

        switch (edge)
        {
            case EdgeKind.Right:
                MoveVerticalEdge(slot, slot.Bounds.X + slot.Bounds.Width, e.HorizontalChange / hostW);
                break;
            case EdgeKind.Left:
                MoveVerticalEdge(slot, slot.Bounds.X, e.HorizontalChange / hostW);
                break;
            case EdgeKind.Bottom:
                MoveHorizontalEdge(slot, slot.Bounds.Y + slot.Bounds.Height, e.VerticalChange / hostH);
                break;
            case EdgeKind.Top:
                MoveHorizontalEdge(slot, slot.Bounds.Y, e.VerticalChange / hostH);
                break;
        }

        ApplySlotLayout();
    }

    private void OnEdgeDragCompleted(object sender, DragCompletedEventArgs e) =>
        Owner?.SaveLayoutPaneBounds(LayoutMode, _slots.Select(slot => slot.Bounds.Clone()).ToList());

    private void MoveVerticalEdge(PaneSlot primary, double edge, double delta)
    {
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        var leftPanes = _slots
            .Where(slot => slot.Index != primary.Index
                           && Nearly(slot.Bounds.X + slot.Bounds.Width, edge)
                           && OverlapsY(primary, slot))
            .ToList();
        var rightPanes = _slots
            .Where(slot => slot.Index != primary.Index
                           && Nearly(slot.Bounds.X, edge)
                           && OverlapsY(primary, slot))
            .ToList();

        var primaryIsLeft = Nearly(primary.Bounds.X + primary.Bounds.Width, edge);
        var primaryIsRight = Nearly(primary.Bounds.X, edge);

        if (primaryIsLeft && rightPanes.Count > 0)
        {
            // Drag this pane's right edge against neighbors on the right only.
            leftPanes = [primary];
        }
        else if (primaryIsRight && leftPanes.Count > 0)
        {
            // Drag this pane's left edge against neighbors on the left only.
            rightPanes = [primary];
        }
        else
        {
            ResizePrimaryOnlyHorizontal(primary, edge, delta);
            return;
        }

        delta = ClampVerticalDelta(leftPanes, rightPanes, delta);
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        foreach (var slot in leftPanes)
        {
            slot.Bounds.Width += delta;
        }

        foreach (var slot in rightPanes)
        {
            slot.Bounds.X += delta;
            slot.Bounds.Width -= delta;
        }
    }

    private void MoveHorizontalEdge(PaneSlot primary, double edge, double delta)
    {
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        var topPanes = _slots
            .Where(slot => slot.Index != primary.Index
                           && Nearly(slot.Bounds.Y + slot.Bounds.Height, edge)
                           && OverlapsX(primary, slot))
            .ToList();
        var bottomPanes = _slots
            .Where(slot => slot.Index != primary.Index
                           && Nearly(slot.Bounds.Y, edge)
                           && OverlapsX(primary, slot))
            .ToList();

        var primaryIsTop = Nearly(primary.Bounds.Y + primary.Bounds.Height, edge);
        var primaryIsBottom = Nearly(primary.Bounds.Y, edge);

        if (primaryIsTop && bottomPanes.Count > 0)
        {
            topPanes = [primary];
        }
        else if (primaryIsBottom && topPanes.Count > 0)
        {
            bottomPanes = [primary];
        }
        else
        {
            ResizePrimaryOnlyVertical(primary, edge, delta);
            return;
        }

        delta = ClampHorizontalDelta(topPanes, bottomPanes, delta);
        if (Math.Abs(delta) < 0.0001)
        {
            return;
        }

        foreach (var slot in topPanes)
        {
            slot.Bounds.Height += delta;
        }

        foreach (var slot in bottomPanes)
        {
            slot.Bounds.Y += delta;
            slot.Bounds.Height -= delta;
        }
    }

    private static void ResizePrimaryOnlyHorizontal(PaneSlot primary, double edge, double delta)
    {
        if (Nearly(primary.Bounds.X + primary.Bounds.Width, edge))
        {
            var max = 1 - primary.Bounds.X;
            var next = Math.Clamp(primary.Bounds.Width + delta, MinRelWidth, max);
            primary.Bounds.Width = next;
            return;
        }

        if (Nearly(primary.Bounds.X, edge))
        {
            var right = primary.Bounds.X + primary.Bounds.Width;
            var nextX = Math.Clamp(primary.Bounds.X + delta, 0, right - MinRelWidth);
            primary.Bounds.Width = right - nextX;
            primary.Bounds.X = nextX;
        }
    }

    private static void ResizePrimaryOnlyVertical(PaneSlot primary, double edge, double delta)
    {
        if (Nearly(primary.Bounds.Y + primary.Bounds.Height, edge))
        {
            var max = 1 - primary.Bounds.Y;
            var next = Math.Clamp(primary.Bounds.Height + delta, MinRelHeight, max);
            primary.Bounds.Height = next;
            return;
        }

        if (Nearly(primary.Bounds.Y, edge))
        {
            var bottom = primary.Bounds.Y + primary.Bounds.Height;
            var nextY = Math.Clamp(primary.Bounds.Y + delta, 0, bottom - MinRelHeight);
            primary.Bounds.Height = bottom - nextY;
            primary.Bounds.Y = nextY;
        }
    }

    private static double ClampVerticalDelta(List<PaneSlot> leftPanes, List<PaneSlot> rightPanes, double delta)
    {
        if (delta > 0)
        {
            var maxGrow = leftPanes.Min(slot => 1 - (slot.Bounds.X + slot.Bounds.Width));
            var maxShrink = rightPanes.Min(slot => slot.Bounds.Width - MinRelWidth);
            return Math.Min(delta, Math.Min(maxGrow, maxShrink));
        }

        var maxLeftShrink = leftPanes.Min(slot => slot.Bounds.Width - MinRelWidth);
        var maxRightGrow = rightPanes.Min(slot => slot.Bounds.X);
        return -Math.Min(-delta, Math.Min(maxLeftShrink, maxRightGrow));
    }

    private static double ClampHorizontalDelta(List<PaneSlot> topPanes, List<PaneSlot> bottomPanes, double delta)
    {
        if (delta > 0)
        {
            var maxGrow = topPanes.Min(slot => 1 - (slot.Bounds.Y + slot.Bounds.Height));
            var maxShrink = bottomPanes.Min(slot => slot.Bounds.Height - MinRelHeight);
            return Math.Min(delta, Math.Min(maxGrow, maxShrink));
        }

        var maxTopShrink = topPanes.Min(slot => slot.Bounds.Height - MinRelHeight);
        var maxBottomGrow = bottomPanes.Min(slot => slot.Bounds.Y);
        return -Math.Min(-delta, Math.Min(maxTopShrink, maxBottomGrow));
    }

    private bool OverlapsY(PaneSlot a, PaneSlot b)
    {
        var a0 = a.Bounds.Y;
        var a1 = a.Bounds.Y + a.Bounds.Height;
        var b0 = b.Bounds.Y;
        var b1 = b.Bounds.Y + b.Bounds.Height;
        return a0 < b1 - EdgeEps && b0 < a1 - EdgeEps;
    }

    private bool OverlapsX(PaneSlot a, PaneSlot b)
    {
        var a0 = a.Bounds.X;
        var a1 = a.Bounds.X + a.Bounds.Width;
        var b0 = b.Bounds.X;
        var b1 = b.Bounds.X + b.Bounds.Width;
        return a0 < b1 - EdgeEps && b0 < a1 - EdgeEps;
    }

    private static bool Nearly(double a, double b) => Math.Abs(a - b) <= EdgeEps;

    private void ApplySlotLayout()
    {
        if (HostCanvas is not { } canvas || _slots.Count == 0)
        {
            return;
        }

        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            var bounds = slot.Bounds;
            Canvas.SetLeft(slot.Chrome, bounds.X * w);
            Canvas.SetTop(slot.Chrome, bounds.Y * h);
            slot.Chrome.Width = Math.Max(80, bounds.Width * w);
            slot.Chrome.Height = Math.Max(70, bounds.Height * h);
        }
    }

    private static List<PaneBounds> CreateDefaultBounds(LayoutDefinition definition)
    {
        var rowWeights = definition.RowWeights;
        var colWeights = definition.ColumnWeights;
        var rowTotal = Math.Max(0.0001, rowWeights.Sum());
        var colTotal = Math.Max(0.0001, colWeights.Sum());

        var rowStarts = new double[rowWeights.Count];
        var rowSizes = new double[rowWeights.Count];
        var y = 0d;
        for (var i = 0; i < rowWeights.Count; i++)
        {
            rowStarts[i] = y;
            rowSizes[i] = rowWeights[i] / rowTotal;
            y += rowSizes[i];
        }

        var colStarts = new double[colWeights.Count];
        var colSizes = new double[colWeights.Count];
        var x = 0d;
        for (var i = 0; i < colWeights.Count; i++)
        {
            colStarts[i] = x;
            colSizes[i] = colWeights[i] / colTotal;
            x += colSizes[i];
        }

        var list = new List<PaneBounds>(definition.Placements.Count);
        foreach (var placement in definition.Placements)
        {
            var left = colStarts[placement.Column];
            var top = rowStarts[placement.Row];
            var width = 0d;
            var height = 0d;
            for (var c = 0; c < placement.ColumnSpan; c++)
            {
                width += colSizes[placement.Column + c];
            }

            for (var r = 0; r < placement.RowSpan; r++)
            {
                height += rowSizes[placement.Row + r];
            }

            list.Add(new PaneBounds
            {
                X = left,
                Y = top,
                Width = width,
                Height = height
            });
        }

        return list;
    }

    private static List<PaneBounds> NormalizeSavedBounds(IReadOnlyList<PaneBounds>? saved, List<PaneBounds> defaults)
    {
        if (saved is not { Count: > 0 } || saved.Count != defaults.Count)
        {
            return defaults.Select(bounds => bounds.Clone()).ToList();
        }

        return saved.Select(bounds => new PaneBounds
        {
            X = Math.Clamp(bounds.X, 0, 1),
            Y = Math.Clamp(bounds.Y, 0, 1),
            Width = Math.Clamp(bounds.Width, MinRelWidth, 1),
            Height = Math.Clamp(bounds.Height, MinRelHeight, 1)
        }).ToList();
    }
}
