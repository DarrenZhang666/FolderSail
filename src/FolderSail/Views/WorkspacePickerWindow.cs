using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderSail.Core.Models;
using FolderSail.Core.Services;
using FolderSail.Helpers;

namespace FolderSail.Views;

public sealed class WorkspacePickerWindow : Window
{
    private readonly ListBox _list;
    private readonly Button _openButton;

    public WorkspacePickerWindow(IReadOnlyList<WorkspaceEntry> workspaces)
    {
        Title = Loc.Get("Loc.OpenWorkspaceTitle");
        Width = 480;
        MinHeight = 320;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Background = TryBrush("Surface.Card", Brushes.White);
        FontFamily = TryFindResource("Font.Ui") as FontFamily ?? FontFamily;
        FontSize = 13;
        Foreground = TryBrush("Text.Primary", Brushes.Black);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = Loc.Get("Loc.OpenWorkspaceHint"),
            Foreground = TryBrush("Text.Secondary", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _list = new ListBox
        {
            BorderBrush = TryBrush("Line.Soft", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Background = TryBrush("Surface.Subtle", Brushes.WhiteSmoke),
            DisplayMemberPath = nameof(WorkspaceListItem.DisplayText),
            ItemsSource = workspaces.Select(entry => new WorkspaceListItem(entry)).ToList()
        };
        _list.MouseDoubleClick += (_, _) => ConfirmSelection();
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancel = new Button
        {
            Content = Loc.Get("Loc.Cancel"),
            Width = 88,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        if (TryFindResource("GhostButton") is Style ghost)
        {
            cancel.Style = ghost;
        }

        _openButton = new Button
        {
            Content = Loc.Get("Loc.Open"),
            Width = 88,
            Height = 32,
            IsDefault = true,
            IsEnabled = workspaces.Count > 0
        };
        if (TryFindResource("AccentButton") is Style accent)
        {
            _openButton.Style = accent;
        }

        _openButton.Click += (_, _) => ConfirmSelection();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_openButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;

        if (workspaces.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        Loaded += (_, _) => _list.Focus();
    }

    public string? SelectedPath { get; private set; }

    private void ConfirmSelection()
    {
        if (_list.SelectedItem is WorkspaceListItem item)
        {
            SelectedPath = item.Entry.FilePath;
            DialogResult = true;
            Close();
        }
    }

    private Brush TryBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private sealed class WorkspaceListItem(WorkspaceEntry entry)
    {
        public WorkspaceEntry Entry { get; } = entry;

        public string DisplayText =>
            Loc.Format("Loc.WorkspaceListItem", Entry.Name, Entry.SavedAtUtc.ToLocalTime());
    }
}
