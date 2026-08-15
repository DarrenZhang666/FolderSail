using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FolderSail.ViewModels;

namespace FolderSail.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        SidebarColumn.Width = new GridLength(viewModel.SidebarWidth);
        Helpers.WindowBackdrop.TryEnable(this);
        Loaded += (_, _) => UpdateSearchPlaceholder();
        Closing += (_, _) =>
        {
            viewModel.SidebarWidth = SidebarColumn.ActualWidth;
            viewModel.SaveOnExit();
        };
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.ActivePane is not PaneViewModel pane)
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
                    pane.CopySelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.X:
                    pane.CutSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.V:
                    pane.PasteCommand.Execute(null);
                    e.Handled = true;
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
            case Key.Delete:
                pane.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2 when pane.SelectedItem != null:
            {
                var dialog = new InputDialogWindow("重命名", "新名称:", pane.SelectedItem.Name)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true)
                {
                    pane.RenameSelectedCommand.Execute(dialog.InputText);
                }

                e.Handled = true;
                break;
            }
            case Key.F5:
                pane.RefreshItems();
                e.Handled = true;
                break;
        }
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
