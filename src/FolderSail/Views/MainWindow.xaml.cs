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
        Closing += (_, _) =>
        {
            viewModel.SidebarWidth = SidebarColumn.ActualWidth;
            viewModel.SaveOnExit();
        };
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.ActivePane is not PaneViewModel pane)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
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
