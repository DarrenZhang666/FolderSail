using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FolderSail.Core.Models;
using FolderSail.Helpers;
using FolderSail.ViewModels;

namespace FolderSail.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? workspacePath)
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        SidebarColumn.Width = new GridLength(viewModel.SidebarWidth);
        Helpers.WindowBackdrop.TryEnable(this);
        Loaded += OnLoaded;
        Helpers.ThemeManager.Changed += OnThemeChanged;
        Closing += OnClosing;

        viewModel.ApplyWindowGeometryRequested += (_, geometry) =>
            MainViewModel.ApplyWindowGeometry(this, geometry);

        if (!string.IsNullOrWhiteSpace(workspacePath) && File.Exists(workspacePath))
        {
            viewModel.LoadWorkspace(workspacePath);
        }

        viewModel.RefreshRecentWorkspaces();
    }

    public static void OpenNewWindow()
    {
        var window = new MainWindow();
        window.Show();
    }

    public static void OpenWorkspaceWindow(string workspacePath)
    {
        var window = new MainWindow(workspacePath);
        window.Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSearchPlaceholder();
        Title = ViewModel.WorkspaceTitle;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.WorkspaceTitle))
            {
                Title = ViewModel.WorkspaceTitle;
            }
        };
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Helpers.ThemeManager.Changed -= OnThemeChanged;
        ViewModel.SidebarWidth = SidebarColumn.ActualWidth;
        ViewModel.SaveOnExit(MainViewModel.CaptureWindowGeometry(this));
    }

    private void OnNewWorkspace(object sender, RoutedEventArgs e) => OpenNewWindow();

    private void OnFileMenuOpened(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRecentWorkspaces();
        RebuildRecentMenuSection();
    }

    private void RebuildRecentMenuSection()
    {
        for (var i = FileMenuRoot.Items.Count - 1; i >= 0; i--)
        {
            if (FileMenuRoot.Items[i] is FrameworkElement element &&
                element.Tag is string tag &&
                tag.StartsWith("recent", StringComparison.Ordinal))
            {
                FileMenuRoot.Items.RemoveAt(i);
            }
        }

        var recents = ViewModel.RecentWorkspaces;
        FileMenuRoot.Items.Add(new Separator { Tag = "recent-sep" });

        if (recents.Count == 0)
        {
            FileMenuRoot.Items.Add(new MenuItem
            {
                Tag = "recent-empty",
                Header = Loc.Get("Loc.WorkspaceRecentEmpty"),
                IsEnabled = false,
                Foreground = (Brush)FindResource("Text.Tertiary")
            });
            return;
        }

        FileMenuRoot.Items.Add(new MenuItem
        {
            Tag = "recent-label",
            Header = Loc.Get("Loc.WorkspaceRecentSection"),
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Text.Secondary")
        });

        foreach (var entry in recents)
        {
            FileMenuRoot.Items.Add(new MenuItem
            {
                Tag = "recent-item",
                Header = Loc.Format("Loc.WorkspaceListItem", entry.Name, entry.LastOpenedUtc.ToLocalTime()),
                Command = ViewModel.OpenRecentWorkspaceCommand,
                CommandParameter = entry.FilePath
            });
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        UpdateSearchChrome(GlobalSearchBox.IsKeyboardFocused);

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject origin)
        {
            return;
        }

        if (IsInsideRenameBox(origin))
        {
            return;
        }

        var insideAddress = IsInsideNamedTextBox(origin, "AddressInput");
        foreach (var pane in vm.Panes)
        {
            if (pane.IsInlineRenaming)
            {
                pane.CommitInlineRename();
            }

            if (pane.IsAddressEditing && !insideAddress)
            {
                pane.GoToAddressCommand.Execute(null);
            }
        }
    }

    private static bool IsInsideRenameBox(DependencyObject origin) =>
        IsInsideNamedTextBox(origin, "RenameBox");

    private static bool IsInsideNamedTextBox(DependencyObject origin, string name)
    {
        for (var current = origin; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox box && box.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N:
                    OpenNewWindow();
                    e.Handled = true;
                    return;
                case Key.O:
                    vm.OpenWorkspaceCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.S:
                    vm.SaveWorkspaceCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        if (vm.ActivePane is not PaneViewModel pane)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key is Key.F or Key.K)
            {
                FocusGlobalSearch();
                e.Handled = true;
                return;
            }

            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.C:
                    if (pane.CopySelectedCommand.CanExecute(null))
                    {
                        pane.CopySelectedCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.X:
                    if (pane.CutSelectedCommand.CanExecute(null))
                    {
                        pane.CutSelectedCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.V:
                    if (pane.PasteCommand.CanExecute(null))
                    {
                        pane.PasteCommand.Execute(null);
                    }

                    e.Handled = true;
                    break;
                case Key.A:
                    // Select-all is handled by the active file list.
                    break;
                case Key.T:
                    pane.NewTabCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.W:
                    pane.CloseTabCommand.Execute(pane.ActiveTab);
                    e.Handled = true;
                    break;
                case Key.L:
                    pane.BeginEditAddressCommand.Execute(null);
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                HandleEscape(vm, pane);
                e.Handled = true;
                break;
            case Key.Delete:
                pane.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2 when pane.SelectedItem != null:
                pane.BeginInlineRename();
                e.Handled = true;
                break;
            case Key.F5:
                pane.RefreshItems();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Match Explorer Esc priority: cancel rename → clear filter → cancel cut dimming → clear selection.
    /// </summary>
    private static void HandleEscape(MainViewModel vm, PaneViewModel pane)
    {
        if (pane.IsInlineRenaming)
        {
            pane.CancelInlineRename();
            return;
        }

        if (pane.HasFilter)
        {
            pane.ClearFilterCommand.Execute(null);
            return;
        }

        if (FileClipboard.CancelCut())
        {
            vm.StatusMessage = Loc.Get("Loc.CutCancelled");
            return;
        }

        pane.ClearSelection();
    }

    private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.SubmitSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.ClearSearch();
            e.Handled = true;
        }
    }

    private void FocusGlobalSearch()
    {
        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();
        UpdateSearchChrome(focused: true);
    }

    private void OnSearchChromeMouseDown(object sender, MouseButtonEventArgs e)
    {
        FocusGlobalSearch();
        e.Handled = true;
    }

    private void OnSearchFocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
        UpdateSearchChrome(GlobalSearchBox.IsKeyboardFocused);

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSearchPlaceholder();

    private void UpdateSearchChrome(bool focused)
    {
        if (focused)
        {
            SearchFocusHalo.BorderBrush = (Brush)FindResource("Accent.FocusHalo");
            SearchChrome.BorderBrush = (Brush)FindResource("Accent.FocusLine");
            SearchChrome.Background = (Brush)FindResource("Surface.Card");
            SearchChrome.BorderThickness = new Thickness(1);
            SearchFocusHalo.BorderThickness = new Thickness(2);
        }
        else
        {
            SearchFocusHalo.BorderBrush = Brushes.Transparent;
            SearchChrome.BorderBrush = (Brush)FindResource("Line.Soft");
            SearchChrome.Background = (Brush)FindResource("Surface.Subtle");
            SearchChrome.BorderThickness = new Thickness(1);
        }

        UpdateSearchPlaceholder();
    }

    private void UpdateSearchPlaceholder()
    {
        var showPlaceholder = !GlobalSearchBox.IsKeyboardFocused &&
                              string.IsNullOrEmpty(GlobalSearchBox.Text);
        SearchPlaceholder.Visibility = showPlaceholder ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSidebarSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SidebarWidth = SidebarColumn.ActualWidth;
        }
    }

    private void OnTagDragEnter(object sender, DragEventArgs e) => UpdateTagDropFeedback(sender, e);

    private void OnTagDragOver(object sender, DragEventArgs e) => UpdateTagDropFeedback(sender, e);

    private void OnTagDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagViewModel tag })
        {
            tag.IsDropTarget = false;
        }
    }

    private void OnTagDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TagViewModel tag })
        {
            return;
        }

        tag.IsDropTarget = false;
        e.Handled = true;

        if (DataContext is not MainViewModel main ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        main.AddDroppedFolders(tag, paths);
    }

    private static void UpdateTagDropFeedback(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TagViewModel tag })
        {
            return;
        }

        var hasFolder = e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
                        paths.Any(Directory.Exists);

        e.Effects = hasFolder ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        tag.IsDropTarget = hasFolder;
    }
}
