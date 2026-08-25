using FolderSail.Helpers;
using FolderSail.Core.Models;
using FolderSail.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FolderSail.Views;

public partial class PaneView : UserControl
{
    private enum ListGestureKind
    {
        None,
        Pending,
        Marquee,
        DragDrop,
    }

    private PaneViewModel? _pane;
    private ListGestureKind _listGesture;
    private Point _gestureOrigin;
    private FileItemViewModel? _gestureItem;
    private bool _gestureStartedOnSelected;
    private bool _gestureAdditive;
    private bool _ignoreRenameLostFocus;
    private bool _updatingColumnWidths;
    private bool _cancellingAddressEdit;

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ApplyActiveVisual();
        Unloaded += (_, _) =>
        {
            FolderSail.Helpers.ThemeManager.Changed -= OnThemeChanged;
            if (_pane != null)
            {
                _pane.PropertyChanged -= OnPanePropertyChanged;
                _pane.InlineRenameStarted -= OnInlineRenameStarted;
                _pane.FilterFocusRequested -= OnFilterFocusRequested;
            }
        };
        FolderSail.Helpers.ThemeManager.Changed += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyActiveVisual();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_pane != null)
        {
            _pane.PropertyChanged -= OnPanePropertyChanged;
            _pane.InlineRenameStarted -= OnInlineRenameStarted;
            _pane.FilterFocusRequested -= OnFilterFocusRequested;
        }

        _pane = e.NewValue as PaneViewModel;

        if (_pane != null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            _pane.InlineRenameStarted += OnInlineRenameStarted;
            _pane.FilterFocusRequested += OnFilterFocusRequested;
        }

        ApplyActiveVisual();
        ApplyHeaderColumnWidths();
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PaneViewModel.IsActive):
                ApplyActiveVisual();
                break;
            case nameof(PaneViewModel.SizeColumnWidth):
            case nameof(PaneViewModel.ModifiedColumnWidth):
            case nameof(PaneViewModel.KindColumnWidth):
                ApplyHeaderColumnWidths();
                break;
            case nameof(PaneViewModel.IsAddressEditing) when _pane?.IsAddressEditing == true:
                Dispatcher.BeginInvoke(() =>
                {
                    AddressInput.Focus();
                    AddressInput.SelectAll();
                });
                break;
        }
    }

    private void ApplyHeaderColumnWidths()
    {
        if (_pane == null || _updatingColumnWidths)
        {
            return;
        }

        SizeColumnDef.Width = new GridLength(_pane.SizeColumnWidth);
        ModifiedColumnDef.Width = new GridLength(_pane.ModifiedColumnWidth);
        KindColumnDef.Width = new GridLength(_pane.KindColumnWidth);
    }

    // Name is *; only change Size so the name column absorbs the rest.
    private void OnNameSizeColumnDragDelta(object sender, DragDeltaEventArgs e) =>
        ApplyColumnWidthsLive(
            SizeColumnDef.ActualWidth - e.HorizontalChange,
            ModifiedColumnDef.ActualWidth,
            KindColumnDef.ActualWidth);

    private void OnSizeModifiedColumnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var size = SizeColumnDef.ActualWidth + e.HorizontalChange;
        var modified = ModifiedColumnDef.ActualWidth - e.HorizontalChange;
        ApplyColumnWidthsLive(size, modified, KindColumnDef.ActualWidth);
    }

    private void OnModifiedKindColumnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var modified = ModifiedColumnDef.ActualWidth + e.HorizontalChange;
        var kind = KindColumnDef.ActualWidth - e.HorizontalChange;
        ApplyColumnWidthsLive(SizeColumnDef.ActualWidth, modified, kind);
    }

    private void ApplyColumnWidthsLive(double size, double modified, double kind)
    {
        if (_pane == null)
        {
            return;
        }

        _updatingColumnWidths = true;
        try
        {
            _pane.PreviewColumnWidths(size, modified, kind);
            SizeColumnDef.Width = new GridLength(_pane.SizeColumnWidth);
            ModifiedColumnDef.Width = new GridLength(_pane.ModifiedColumnWidth);
            KindColumnDef.Width = new GridLength(_pane.KindColumnWidth);
        }
        finally
        {
            _updatingColumnWidths = false;
        }
    }

    private void OnColumnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _pane?.CommitColumnWidths(SizeColumnDef.ActualWidth, ModifiedColumnDef.ActualWidth, KindColumnDef.ActualWidth);
    }

    private void ApplyActiveVisual()
    {
        var isActive = _pane?.IsActive == true;

        Card.BorderBrush = (Brush)FindResource(isActive ? "Line.Focus" : "Line.Soft");
        Card.Effect = isActive ? (Effect)FindResource("Shadow.PaneActive") : null;
    }

    private void OnPaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_pane == null)
        {
            return;
        }

        // List empty space often does not take focus; commit the path on any click outside the box.
        if (_pane.IsAddressEditing && !IsInsideAddressInput(e.OriginalSource as DependencyObject))
        {
            _pane.GoToAddressCommand.Execute(null);
        }

        if (Window.GetWindow(this)?.DataContext is MainViewModel main)
        {
            main.ActivatePaneCommand.Execute(_pane.Index);
        }
    }

    private static bool IsInsideAddressInput(DependencyObject? origin)
    {
        for (var current = origin; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox { Name: "AddressInput" })
            {
                return true;
            }
        }

        return false;
    }

    private void OnInlineRenameStarted(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(FocusRenameBox, DispatcherPriority.Input);

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_pane?.IsInlineRenaming == true)
        {
            return;
        }

        _pane?.OpenSelectedCommand.Execute(null);
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_pane == null || sender is not FrameworkElement { DataContext: PaneTabViewModel tab })
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            _pane.CloseTabCommand.Execute(tab);
            e.Handled = true;
            return;
        }

        // Right-clicking a background tab activates it before showing the menu,
        // matching browser and file-manager tab behaviour.
        if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            _pane.ActivateTabCommand.Execute(tab);
        }
    }

    private void OnBreadcrumbMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_pane == null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Double-click anywhere on the bar → type the path (Explorer-style).
        if (e.ClickCount >= 2)
        {
            _pane.BeginEditAddressCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Single-click a breadcrumb segment → navigate; do not steal the click.
        if (IsBreadcrumbSegment(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // Single-click empty space in the bar → edit the path.
        _pane.BeginEditAddressCommand.Execute(null);
        e.Handled = true;
    }

    private static bool IsBreadcrumbSegment(DependencyObject? origin)
    {
        for (var current = origin; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: BreadcrumbViewModel })
            {
                return true;
            }
        }

        return false;
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Like Explorer: leaving the address bar confirms the path.
        if (_pane == null || _cancellingAddressEdit || !_pane.IsAddressEditing)
        {
            return;
        }

        _pane.GoToAddressCommand.Execute(null);
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (_pane == null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _pane.GoToAddressCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _cancellingAddressEdit = true;
            try
            {
                _pane.CancelEditAddressCommand.Execute(null);
            }
            finally
            {
                _cancellingAddressEdit = false;
            }

            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var move = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            e.Effects = move ? DragDropEffects.Move : DragDropEffects.Copy;
            Card.BorderBrush = (Brush)FindResource("Accent.Base");
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ApplyActiveVisual();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ApplyActiveVisual();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || _pane == null)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var move = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        _pane.HandleDrop(paths, move);
    }

    private void OnFilesPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Leave double-click / rename / Ctrl·Shift clicks to the ListBox defaults.
        if (e.ClickCount > 1 ||
            _pane?.IsInlineRenaming == true ||
            Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift or (ModifierKeys.Control | ModifierKeys.Shift) ||
            IsInsideRenameBox(e.OriginalSource as DependencyObject))
        {
            ResetListGesture();
            return;
        }

        _gestureOrigin = e.GetPosition(FilesList);
        _gestureItem = FindFileItem(e.OriginalSource as DependencyObject);
        _gestureStartedOnSelected = _gestureItem != null && FilesList.SelectedItems.Contains(_gestureItem);
        _gestureAdditive = false;
        _listGesture = ListGestureKind.Pending;

        // Own the gesture so Explorer-like rules apply:
        // - drag from empty / unselected → rubber-band multi-select
        // - drag from already-selected → file drag-drop
        // - click without drag → normal selection on mouse up
        FilesList.Focus();
        Mouse.Capture(FilesList);
        e.Handled = true;
    }

    private void OnFilesPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_listGesture == ListGestureKind.None || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(FilesList);
        if (_listGesture == ListGestureKind.Pending)
        {
            if (Math.Abs(position.X - _gestureOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _gestureOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            // Same as Explorer: only an already-selected item starts a file drag.
            // Otherwise rubber-band select so a left-drag multi-selects instead of copying.
            if (_gestureItem != null && _gestureStartedOnSelected)
            {
                BeginFileDragDrop();
                return;
            }

            _listGesture = ListGestureKind.Marquee;
            MarqueeRect.Visibility = Visibility.Visible;
        }

        if (_listGesture != ListGestureKind.Marquee)
        {
            return;
        }

        AutoScrollList(position);
        UpdateMarqueeVisual(position);
        ApplyMarqueeSelection(GetMarqueeRect(_gestureOrigin, position));
        e.Handled = true;
    }

    private void OnFilesPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_listGesture == ListGestureKind.None)
        {
            return;
        }

        if (_listGesture == ListGestureKind.Pending)
        {
            ApplyClickSelection();
        }

        ResetListGesture();
        if (Mouse.Captured == FilesList)
        {
            Mouse.Capture(null);
        }

        e.Handled = true;
    }

    private void OnFilesLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_listGesture is ListGestureKind.Pending or ListGestureKind.Marquee)
        {
            ResetListGesture();
        }
    }

    private void BeginFileDragDrop()
    {
        if (_pane == null || _gestureItem == null)
        {
            ResetListGesture();
            return;
        }

        _listGesture = ListGestureKind.DragDrop;
        var paths = _pane.GetSelectedPathsForDrag(_gestureItem.FullPath).ToArray();
        ResetListGesture();
        if (Mouse.Captured == FilesList)
        {
            Mouse.Capture(null);
        }

        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void ApplyClickSelection()
    {
        if (_gestureItem == null)
        {
            FilesList.SelectedItems.Clear();
            return;
        }

        FilesList.SelectedItems.Clear();
        FilesList.SelectedItems.Add(_gestureItem);
        FilesList.SelectedItem = _gestureItem;
    }

    private void UpdateMarqueeVisual(Point current)
    {
        var rect = GetMarqueeRect(_gestureOrigin, current);
        Canvas.SetLeft(MarqueeRect, rect.X);
        Canvas.SetTop(MarqueeRect, rect.Y);
        MarqueeRect.Width = Math.Max(0, rect.Width);
        MarqueeRect.Height = Math.Max(0, rect.Height);
    }

    private void ApplyMarqueeSelection(Rect marquee)
    {
        var min = int.MaxValue;
        var max = int.MinValue;

        for (var i = 0; i < FilesList.Items.Count; i++)
        {
            if (FilesList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
            {
                continue;
            }

            var bounds = new Rect(
                container.TranslatePoint(new Point(0, 0), FilesList),
                container.RenderSize);
            if (!marquee.IntersectsWith(bounds))
            {
                continue;
            }

            min = Math.Min(min, i);
            max = Math.Max(max, i);
        }

        ExpandIndexRangeByPoint(new Point(marquee.Left + marquee.Width / 2, marquee.Top), ref min, ref max);
        ExpandIndexRangeByPoint(new Point(marquee.Left + marquee.Width / 2, marquee.Bottom), ref min, ref max);

        if (!_gestureAdditive)
        {
            FilesList.SelectedItems.Clear();
        }

        if (min == int.MaxValue)
        {
            return;
        }

        for (var i = min; i <= max; i++)
        {
            if (FilesList.Items[i] is FileItemViewModel item && !FilesList.SelectedItems.Contains(item))
            {
                FilesList.SelectedItems.Add(item);
            }
        }
    }

    private void ExpandIndexRangeByPoint(Point point, ref int min, ref int max)
    {
        var index = IndexFromPoint(point);
        if (index < 0)
        {
            return;
        }

        min = min == int.MaxValue ? index : Math.Min(min, index);
        max = max == int.MinValue ? index : Math.Max(max, index);
    }

    private int IndexFromPoint(Point point)
    {
        var hit = FilesList.InputHitTest(point) as DependencyObject;
        var item = FindFileItem(hit);
        if (item != null)
        {
            return FilesList.Items.IndexOf(item);
        }

        // Empty band below / between rows: map Y onto the nearest realized row.
        ListBoxItem? nearest = null;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < FilesList.Items.Count; i++)
        {
            if (FilesList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), FilesList).Y;
            var bottom = top + container.RenderSize.Height;
            var distance = point.Y < top ? top - point.Y : point.Y > bottom ? point.Y - bottom : 0;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = container;
            }
        }

        return nearest == null ? -1 : FilesList.ItemContainerGenerator.IndexFromContainer(nearest);
    }

    private void AutoScrollList(Point position)
    {
        var scrollViewer = FindScrollViewer(FilesList);
        if (scrollViewer == null)
        {
            return;
        }

        const double edge = 28;
        const double step = 1; // logical item units when CanContentScroll is true
        if (position.Y < edge)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - step);
        }
        else if (position.Y > FilesList.ActualHeight - edge)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + step);
        }
    }

    private void ResetListGesture()
    {
        _listGesture = ListGestureKind.None;
        _gestureItem = null;
        _gestureStartedOnSelected = false;
        _gestureAdditive = false;
        _gestureOrigin = default;
        MarqueeRect.Visibility = Visibility.Collapsed;
        MarqueeRect.Width = 0;
        MarqueeRect.Height = 0;
    }

    private static Rect GetMarqueeRect(Point origin, Point current)
    {
        var x = Math.Min(origin.X, current.X);
        var y = Math.Min(origin.Y, current.Y);
        return new Rect(x, y, Math.Abs(current.X - origin.X), Math.Abs(current.Y - origin.Y));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsInsideRenameBox(DependencyObject? origin)
    {
        for (var current = origin; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox { Name: "RenameBox" })
            {
                return true;
            }
        }

        return false;
    }

    private void OnFilesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pane == null)
        {
            return;
        }

        _pane.SetSelectedItems(FilesList.SelectedItems.OfType<FileItemViewModel>());
    }

    private void OnFilterFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }, DispatcherPriority.Input);

    private void OnFilterLostFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _pane?.CloseFilterIfEmpty();

    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _pane == null)
        {
            return;
        }

        _pane.ClearFilterCommand.Execute(null);
        FilesList.Focus();
        e.Handled = true;
    }

    private void OnFilesTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_pane == null || _pane.IsInlineRenaming || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        _pane.FilterText += e.Text;
        e.Handled = true;
    }

    private void OnItemIconLoaded(object sender, RoutedEventArgs e) =>
        RequestRowIcon(sender);

    private void OnItemIconContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        RequestRowIcon(sender);

    private void RequestRowIcon(object sender)
    {
        if (sender is FrameworkElement { DataContext: FileItemViewModel item })
        {
            item.EnsureIcon(_pane?.IconLoadToken ?? CancellationToken.None);
            item.EnsureFolderSize(_pane?.IconLoadToken ?? CancellationToken.None);
        }
    }

    private void OnFilesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FilesList.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _pane?.HasFilter == true)
        {
            _pane.ClearFilterCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && _pane?.HasFilter == true && Keyboard.Modifiers == ModifierKeys.None)
        {
            var text = _pane.FilterText;
            _pane.FilterText = text.Length <= 1 ? string.Empty : text[..^1];
            if (string.IsNullOrEmpty(_pane.FilterText))
            {
                _pane.CloseFilterIfEmpty();
            }

            e.Handled = true;
        }
    }

    private void OnFilesRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_pane == null || Window.GetWindow(this) is not { } owner)
        {
            return;
        }

        var item = FindFileItem(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            _pane.SelectedItem = item;
        }

        e.Handled = true;
        Mouse.Capture(null);

        var path = item?.FullPath;
        var folderBackground = item == null;
        if (folderBackground)
        {
            if (_pane.IsVirtualView || string.IsNullOrWhiteSpace(_pane.CurrentPath))
            {
                return;
            }

            path = _pane.CurrentPath;
        }

        var ownCommands = new List<ShellContextMenu.OwnCommand>();
        if (item?.Kind is FileItemKind.Directory or FileItemKind.Drive)
        {
            ownCommands.Add(new ShellContextMenu.OwnCommand(
                Loc.Get("Loc.OpenInNewTab"),
                () => _pane.OpenSelectedInNewTabCommand.Execute(null)));
        }

        if (item != null && _pane.IsTagView && owner.DataContext is MainViewModel main)
        {
            ownCommands.Add(new ShellContextMenu.OwnCommand(
                Loc.Get("Loc.RemoveFromThisTag"),
                () => main.RemoveTaggedPath(item.FullPath)));
        }

        var targetPath = path!;
        var extended = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var pane = _pane;

        // Wait until WPF finishes this mouse-up. Showing a Win32 menu during the
        // event (especially on a chrome-less window) makes it close immediately.
        owner.Dispatcher.BeginInvoke(() =>
        {
            var shown = ShellContextMenu.Show(
                owner,
                targetPath,
                ownCommands,
                extended,
                folderBackground,
                onRename: () => pane.BeginInlineRename());
            if (!shown)
            {
                pane.ReportStatus(Loc.Format("Loc.ContextMenuFailed", ShellContextMenu.LastError ?? Loc.Get("Loc.UnknownError")));
            }

            if (!pane.IsInlineRenaming)
            {
                pane.RefreshItems();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnRenameBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(FocusRenameBox, DispatcherPriority.Input);
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_ignoreRenameLostFocus)
        {
            _ignoreRenameLostFocus = false;
            return;
        }

        _pane?.CommitInlineRename();
    }

    private void OnRenameBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_pane == null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ignoreRenameLostFocus = true;
            _pane.CommitInlineRename();
            FilesList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _ignoreRenameLostFocus = true;
            _pane.CancelInlineRename();
            FilesList.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameBoxMouseMove(object sender, MouseEventArgs e) => e.Handled = true;

    private void FocusRenameBox()
    {
        var box = FindRenameBox(FilesList);
        if (box == null)
        {
            return;
        }

        box.Focus();
        var length = box.DataContext is FileItemViewModel item ? item.RenameSelectLength : box.Text.Length;
        length = Math.Clamp(length, 0, box.Text.Length);
        box.Select(0, length);
        box.BringIntoView();
    }

    private static TextBox? FindRenameBox(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { Name: "RenameBox", Visibility: Visibility.Visible } box)
            {
                return box;
            }

            var nested = FindRenameBox(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static FileItemViewModel? FindFileItem(DependencyObject? origin)
    {
        for (var current = origin; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: FileItemViewModel item })
            {
                return item;
            }
        }

        return null;
    }
}
