using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FolderSail.Helpers;
using Microsoft.Win32;

namespace FolderSail.Views;

/// <summary>
/// Save-as dialog: workspace name + destination folder (with browse).
/// </summary>
public sealed class WorkspaceSaveAsWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _folderBox;

    public WorkspaceSaveAsWindow(string defaultName, string defaultFolder)
    {
        Title = Loc.Get("Loc.SaveWorkspaceTitle");
        Width = 520;
        MinHeight = 240;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = TryBrush("Surface.Card", Brushes.White);
        FontFamily = TryFindResource("Font.Ui") as FontFamily ?? FontFamily;
        FontSize = 13;
        Foreground = TryBrush("Text.Primary", Brushes.Black);

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = Loc.Get("Loc.WorkspaceName"),
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryBrush("Text.Secondary", Brushes.Gray)
        });

        _nameBox = CreateTextBox(defaultName);
        root.Children.Add(_nameBox);

        root.Children.Add(new TextBlock
        {
            Text = Loc.Get("Loc.WorkspaceFolder"),
            Margin = new Thickness(0, 8, 0, 6),
            Foreground = TryBrush("Text.Secondary", Brushes.Gray)
        });

        var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _folderBox = CreateTextBox(defaultFolder);
        _folderBox.Margin = new Thickness(0);
        Grid.SetColumn(_folderBox, 0);
        folderRow.Children.Add(_folderBox);

        var browse = CreateButton(Loc.Get("Loc.Browse"), isPrimary: false, isCancel: false);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Click += (_, _) => BrowseFolder();
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        root.Children.Add(folderRow);

        root.Children.Add(new TextBlock
        {
            Text = Loc.Get("Loc.WorkspaceSaveAsHint"),
            Foreground = TryBrush("Text.Tertiary", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 16)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = CreateButton(Loc.Get("Loc.Cancel"), isPrimary: false, isCancel: true);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var ok = CreateButton(Loc.Get("Loc.Ok"), isPrimary: true, isCancel: false);
        ok.IsDefault = true;
        ok.Click += (_, _) => Confirm();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            Background = TryBrush("Surface.Card", Brushes.White);
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    public string WorkspaceName { get; private set; } = string.Empty;
    public string FolderPath { get; private set; } = string.Empty;

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.Get("Loc.WorkspaceFolder"),
            Multiselect = false
        };
        var current = _folderBox.Text.Trim();
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _folderBox.Text = dialog.FolderName;
        }
    }

    private void Confirm()
    {
        var name = _nameBox.Text.Trim();
        var folder = _folderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, Loc.Get("Loc.WorkspaceNameRequired"), "FolderSail", MessageBoxButton.OK, MessageBoxImage.Information);
            _nameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, Loc.Get("Loc.WorkspaceFolderRequired"), "FolderSail", MessageBoxButton.OK, MessageBoxImage.Information);
            _folderBox.Focus();
            return;
        }

        WorkspaceName = name;
        FolderPath = folder;
        DialogResult = true;
    }

    private TextBox CreateTextBox(string value)
    {
        var box = new TextBox
        {
            Text = value,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 0, 8, 0)
        };
        if (TryFindResource("AddressBox") is Style addressStyle)
        {
            box.Style = addressStyle;
            box.Height = 30;
            box.Margin = new Thickness(0, 0, 0, 4);
        }

        return box;
    }

    private Button CreateButton(string content, bool isPrimary, bool isCancel)
    {
        var button = new Button
        {
            Content = content,
            Width = 88,
            Height = 32,
            IsCancel = isCancel
        };
        var key = isPrimary ? "PrimaryButton" : "GhostButton";
        if (TryFindResource(key) is Style style)
        {
            button.Style = style;
            button.Width = 88;
            button.Height = 32;
        }

        return button;
    }

    private Brush TryBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}
