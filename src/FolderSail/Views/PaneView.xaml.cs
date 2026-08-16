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

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => ApplyActiveVisual();
        Unloaded += (_, _) => FolderSail.Helpers.ThemeManager.Changed -= OnThemeChanged;
        FolderSail.Helpers.ThemeManager.Changed += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyActiveVisual();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_pane != null)
        {
            _pane.PropertyChanged -= OnPanePropertyChanged;
        }

        _pane = e.NewValue as PaneViewModel;

        if (_pane != null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
        }

        ApplyActiveVisual();
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PaneViewModel.IsActive):
                ApplyActiveVisual();
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

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
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
            sender is not FrameworkElement { DataContext: FileItemViewModel item })
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
        var data = new DataObject(DataFormats.FileDrop, new[] { item.FullPath });
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
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
            var shown = ShellContextMenu.Show(owner, targetPath, ownCommands, extended, folderBackground);
            if (!shown)
            {
                pane.ReportStatus($"系统右键菜单失败：{ShellContextMenu.LastError ?? "未知错误"}");
            }

            pane.RefreshItems();
        }, DispatcherPriority.ApplicationIdle);
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
