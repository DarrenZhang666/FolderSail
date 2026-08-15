using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FolderSail.Views;

public class InputDialogWindow : Window
{
    private readonly TextBox _input;

    public InputDialogWindow(string title, string label, string defaultValue)
    {
        Title = title;
        Width = 420;
        MinHeight = 160;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.White;

        if (TryFindResource("Font.Ui") is FontFamily uiFont)
        {
            FontFamily = uiFont;
        }

        FontSize = 12;
        Foreground = TryBrush("Text.Primary", Brushes.Black);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
            Foreground = TryBrush("Text.Secondary", Brushes.Gray)
        };
        Grid.SetRow(caption, 0);
        root.Children.Add(caption);

        _input = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 18),
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0)
        };

        if (TryFindResource("AddressBox") is Style addressStyle)
        {
            _input.Style = addressStyle;
            _input.Height = 30;
        }

        Grid.SetRow(_input, 1);
        root.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "取消",
            Width = 78,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        if (TryFindResource("GhostButton") is Style ghost)
        {
            cancel.Style = ghost;
            cancel.Width = 78;
            cancel.Height = 30;
        }
        cancel.Click += (_, _) => DialogResult = false;

        var ok = new Button
        {
            Content = "确定",
            Width = 86,
            Height = 30,
            IsDefault = true
        };
        if (TryFindResource("PrimaryButton") is Style primary)
        {
            ok.Style = primary;
            ok.Width = 86;
            ok.Height = 30;
        }
        else
        {
            ok.Foreground = Brushes.White;
            ok.Background = TryBrush("Accent.Base", Brushes.SteelBlue);
            ok.BorderThickness = new Thickness(0);
            ok.Cursor = System.Windows.Input.Cursors.Hand;
            ok.FontSize = 12;
        }
        ok.Click += (_, _) =>
        {
            InputText = _input.Text;
            DialogResult = true;
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;

        Loaded += (_, _) =>
        {
            // Prefer an opaque card colour once Application resources are available.
            Background = TryBrush("Surface.Card", Brushes.White);
            if (Owner is null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Activate();
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string InputText { get; private set; } = string.Empty;

    private Brush TryBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}
