using FolderSail.Helpers;
using FolderSail.Core.Models;
using FolderSail.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FolderSail.Views;

public partial class PaneView : UserControl
{
    private PaneViewModel? _pane;
    private Point _dragOrigin;

    private bool _ignoreRenameLostFocus;

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
        if (_pane == null)
        {
            return;
        }

        SizeColumnDef.Width = new GridLength(_pane.SizeColumnWidth);
        ModifiedColumnDef.Width = new GridLength(_pane.ModifiedColumnWidth);
        KindColumnDef.Width = new GridLength(_pane.KindColumnWidth);
    }

    private void OnColumnDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
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

        if (Window.GetWindow(this)?.DataContext is MainViewModel main)
        {
            main.ActivatePaneCommand.Execute(_pane.Index);
        }
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

        _pane.BeginEditAddressCommand.Execute(null);
        e.Handled = true;
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _pane?.CancelEditAddressCommand.Execute(null);
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
            _pane.CancelEditAddressCommand.Execute(null);
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

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: FileItemViewModel item } ||
            item.IsRenaming)
        {
            _dragOrigin = default;
            return;
        }

        var position = e.GetPosition(this);

        if (_dragOrigin == default)
        {
            _dragOrigin = position;
            return;
        }

        // Require a deliberate drag so a plain click still selects the row.
        if (Math.Abs(position.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragOrigin = default;
        if (_pane == null)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, _pane.GetSelectedPathsForDrag(item.FullPath).ToArray());
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
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
                "在 FolderSail 新标签页中打开",
                () => _pane.OpenSelectedInNewTabCommand.Execute(null)));
        }

        if (item != null && _pane.IsTagView && owner.DataContext is MainViewModel main)
        {
            ownCommands.Add(new ShellContextMenu.OwnCommand(
                "从此标签移除",
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
                pane.ReportStatus($"系统右键菜单失败：{ShellContextMenu.LastError ?? "未知错误"}");
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
