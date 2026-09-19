using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FolderSail.Core.Models;
using FolderSail.Core.Services;
using FolderSail.Helpers;

namespace FolderSail.Views;

/// <summary>
/// Compact copy/move progress dialog modeled on Windows File Explorer.
/// </summary>
public sealed class CopyProgressWindow : Window
{
    // Explorer copy dialog uses a solid green bar (not the app accent).
    private static readonly SolidColorBrush ExplorerProgressBrush = FreezeBrush(Color.FromRgb(0x06, 0xB0, 0x25));
    private static readonly SolidColorBrush ExplorerTrackBrush = FreezeBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    private readonly TextBlock _percentText;
    private readonly ProgressBar _bar;
    private readonly TextBlock _nameValue;
    private readonly TextBlock _timeValue;
    private readonly TextBlock _itemsValue;
    private readonly TextBlock _speedValue;
    private readonly Button _cancel;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cts = new();
    private long _lastBytes;
    private double _lastSampleSeconds;
    private double _bytesPerSecond;

    public CopyProgressWindow(bool move, int itemCount, string destination)
    {
        var noun = itemCount == 1 ? Loc.Get("Loc.OneItem") : Loc.Format("Loc.ManyItems", itemCount);
        var action = Loc.Format(move ? "Loc.MovingItems" : "Loc.CopyingItems", noun);
        var destName = string.IsNullOrWhiteSpace(destination)
            ? destination
            : (System.IO.Path.GetFileName(destination.TrimEnd('\\', '/')) is { Length: > 0 } name
                ? name
                : destination);

        Title = $"{action} · {Loc.Format("Loc.ToPath", destName)}";
        Width = 384;
        SizeToContent = SizeToContent.Height;
        MinHeight = 0;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = TryBrush("Surface.Card", Brushes.White);
        FontFamily = TryFindResource("Font.Ui") as FontFamily ?? FontFamily;
        FontSize = 12;
        Foreground = TryBrush("Text.Primary", Brushes.Black);

        _bar = new ProgressBar
        {
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = true,
            Foreground = ExplorerProgressBrush,
            Background = ExplorerTrackBrush,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6)
        };

        _percentText = new TextBlock
        {
            Text = Loc.Get("Loc.Preparing"),
            FontSize = 12,
            Foreground = TryBrush("Text.Secondary", Brushes.DimGray),
            Margin = new Thickness(0, 0, 0, 12)
        };

        _nameValue = ValueBlock(Loc.Get("Loc.Preparing"));
        _timeValue = ValueBlock(Loc.Get("Loc.Calculating"));
        _itemsValue = ValueBlock(Loc.Get("Loc.Calculating"));
        _speedValue = ValueBlock(Loc.Get("Loc.Calculating"));

        _cancel = new Button
        {
            Content = Loc.Get("Loc.Cancel"),
            MinWidth = 80,
            Height = 28,
            Padding = new Thickness(14, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            IsCancel = true
        };
        if (TryFindResource("GhostButton") is Style ghost)
        {
            _cancel.Style = ghost;
            _cancel.MinWidth = 80;
            _cancel.Height = 28;
            _cancel.Padding = new Thickness(14, 0, 14, 0);
        }

        _cancel.Click += (_, _) =>
        {
            _cts.Cancel();
            _cancel.IsEnabled = false;
            _cancel.Content = Loc.Get("Loc.Cancelling");
        };

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        root.Children.Add(_bar);
        root.Children.Add(_percentText);
        root.Children.Add(DetailRow(Loc.Get("Loc.Name"), _nameValue));
        root.Children.Add(DetailRow(Loc.Get("Loc.RemainingTime"), _timeValue));
        root.Children.Add(DetailRow(Loc.Get("Loc.RemainingItems"), _itemsValue));
        root.Children.Add(DetailRow(Loc.Get("Loc.Speed"), _speedValue));
        root.Children.Add(_cancel);
        Content = root;

        Closing += (_, _) =>
        {
            if (!_cts.IsCancellationRequested && _bar.Value < 100)
            {
                _cts.Cancel();
            }
        };
    }

    public CancellationToken Token => _cts.Token;

    public void Apply(FileTransferProgress progress)
    {
        var startedCopying = progress.BytesCopied > 0 ||
            (!string.IsNullOrWhiteSpace(progress.CurrentName) &&
             !string.Equals(progress.CurrentName, Loc.Get("Loc.Preparing"), StringComparison.Ordinal));

        if (_bar.IsIndeterminate && startedCopying)
        {
            _bar.IsIndeterminate = false;
        }

        var ratio = progress.TotalBytes <= 0
            ? 0
            : Math.Clamp(progress.BytesCopied / (double)progress.TotalBytes, 0, 1);
        if (!_bar.IsIndeterminate)
        {
            _bar.Value = ratio * 100;
            _percentText.Text = Loc.Format("Loc.PercentDone", $"{ratio * 100:0}");
        }

        if (!string.IsNullOrWhiteSpace(progress.CurrentName))
        {
            _nameValue.Text = progress.CurrentName;
        }

        var elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed > 0.35 && progress.BytesCopied >= _lastBytes)
        {
            var dt = Math.Max(elapsed - _lastSampleSeconds, 0.2);
            var instant = (progress.BytesCopied - _lastBytes) / dt;
            _bytesPerSecond = _bytesPerSecond <= 0 ? instant : (_bytesPerSecond * 0.72) + (instant * 0.28);
            _lastSampleSeconds = elapsed;
            _lastBytes = progress.BytesCopied;
        }

        var remainingBytes = Math.Max(0, progress.TotalBytes - progress.BytesCopied);
        _timeValue.Text = FormatRemaining(remainingBytes);
        var leftFiles = Math.Max(0, progress.TotalFiles - progress.FilesCopied);
        _itemsValue.Text = Loc.Format("Loc.RemainingItemsFmt", leftFiles, FormatSize(remainingBytes));
        _speedValue.Text = _bytesPerSecond < 1024
            ? Loc.Get("Loc.Calculating")
            : Loc.Format("Loc.SpeedFmt", FormatSize((long)_bytesPerSecond));
    }

    public static async Task RunAsync(
        Window? owner,
        IFileService fileService,
        IReadOnlyList<string> sources,
        string destination,
        bool move)
    {
        var dialog = new CopyProgressWindow(move, sources.Count, destination);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        dialog.Show();

        var progress = new Progress<FileTransferProgress>(dialog.Apply);
        try
        {
            await Task.Run(() =>
                    fileService.Transfer(sources, destination, move, progress, dialog.Token),
                dialog.Token).ConfigureAwait(true);

            dialog._bar.IsIndeterminate = false;
            dialog._bar.Value = 100;
            dialog._percentText.Text = Loc.Format("Loc.PercentDone", 100);
            await Task.Delay(160).ConfigureAwait(true);
            dialog.Close();
        }
        catch (OperationCanceledException)
        {
            dialog.Close();
            throw;
        }
        catch
        {
            dialog.Close();
            throw;
        }
        finally
        {
            dialog._cts.Dispose();
        }
    }

    private static TextBlock ValueBlock(string text) =>
        new()
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

    private UIElement DetailRow(string label, TextBlock value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = TryBrush("Text.Secondary", Brushes.DimGray),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(value, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(value);
        return grid;
    }

    private string FormatRemaining(long remainingBytes)
    {
        if (remainingBytes <= 0)
        {
            return Loc.Get("Loc.AlmostDone");
        }

        if (_bytesPerSecond < 1024)
        {
            return Loc.Get("Loc.Calculating");
        }

        var seconds = remainingBytes / _bytesPerSecond;
        if (seconds < 5)
        {
            return Loc.Get("Loc.Under5s");
        }

        if (seconds < 60)
        {
            return Loc.Format("Loc.AboutSeconds", $"{seconds:0}");
        }

        if (seconds < 3600)
        {
            return Loc.Format("Loc.AboutMinutes", $"{seconds / 60:0}");
        }

        return Loc.Format("Loc.AboutHours", $"{seconds / 3600:0.0}");
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var order = 0;
        while (value >= 1024 && order < units.Length - 1)
        {
            value /= 1024;
            order++;
        }

        return order == 0 ? $"{bytes} {units[0]}" : $"{value:0.00} {units[order]}";
    }

    private Brush TryBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
