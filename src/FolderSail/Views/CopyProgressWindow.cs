using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FolderSail.Core.Models;
using FolderSail.Core.Services;

namespace FolderSail.Views;

/// <summary>
/// Explorer-style copy/move progress window.
/// </summary>
public sealed class CopyProgressWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _destText;
    private readonly ProgressBar _bar;
    private readonly TextBlock _percentText;
    private readonly TextBlock _nameText;
    private readonly TextBlock _timeText;
    private readonly TextBlock _remainText;
    private readonly Button _cancel;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cts = new();
    private long _lastBytes;
    private double _bytesPerSecond;

    public CopyProgressWindow(bool move, int itemCount, string destination)
    {
        Title = move ? "移动项目" : "复制项目";
        Width = 440;
        MinHeight = 220;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = TryBrush("Surface.Card", Brushes.White);
        FontFamily = TryFindResource("Font.Ui") as FontFamily ?? FontFamily;
        FontSize = 13;
        Foreground = TryBrush("Text.Primary", Brushes.Black);

        var noun = itemCount == 1 ? "1 个项目" : $"{itemCount} 个项目";
        _titleText = new TextBlock
        {
            Text = move ? $"正在移动 {noun}" : $"正在复制 {noun}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _destText = new TextBlock
        {
            Text = $"到 {destination}",
            Foreground = TryBrush("Text.Secondary", Brushes.Gray),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 16)
        };

        _bar = new ProgressBar
        {
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = TryBrush("Accent.Base", Brushes.SteelBlue),
            Background = TryBrush("Line.Soft", Brushes.LightGray),
            BorderThickness = new Thickness(0)
        };

        _percentText = new TextBlock
        {
            Text = "0% 完成",
            Margin = new Thickness(0, 8, 0, 18),
            Foreground = TryBrush("Text.Secondary", Brushes.Gray)
        };

        _nameText = Field("名称", "正在准备…");
        _timeText = Field("剩余时间", "正在计算…");
        _remainText = Field("剩余项目", "正在计算…");

        _cancel = new Button
        {
            Content = "取消",
            Width = 88,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            IsCancel = true
        };
        if (TryFindResource("GhostButton") is Style ghost)
        {
            _cancel.Style = ghost;
            _cancel.Width = 88;
            _cancel.Height = 32;
        }

        _cancel.Click += (_, _) =>
        {
            _cts.Cancel();
            _cancel.IsEnabled = false;
            _cancel.Content = "正在取消…";
        };

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(_titleText);
        root.Children.Add(_destText);
        root.Children.Add(_bar);
        root.Children.Add(_percentText);
        root.Children.Add(LabelAbove("名称", _nameText));
        root.Children.Add(LabelAbove("剩余时间", _timeText));
        root.Children.Add(LabelAbove("剩余项目", _remainText));
        root.Children.Add(_cancel);
        Content = root;

        Closing += (_, e) =>
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
        var ratio = progress.TotalBytes <= 0
            ? 0
            : Math.Clamp(progress.BytesCopied / (double)progress.TotalBytes, 0, 1);
        _bar.Value = ratio * 100;
        _percentText.Text = $"{ratio * 100:0}% 完成";
        if (!string.IsNullOrWhiteSpace(progress.CurrentName))
        {
            _nameText.Text = progress.CurrentName;
        }

        var elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed > 0.4 && progress.BytesCopied > _lastBytes)
        {
            var instant = (progress.BytesCopied - _lastBytes) / Math.Max(elapsed - _lastSampleSeconds, 0.2);
            _bytesPerSecond = _bytesPerSecond <= 0 ? instant : (_bytesPerSecond * 0.7) + (instant * 0.3);
            _lastSampleSeconds = elapsed;
            _lastBytes = progress.BytesCopied;
        }

        var remainingBytes = Math.Max(0, progress.TotalBytes - progress.BytesCopied);
        _timeText.Text = FormatRemainingWithSpeed(remainingBytes);
        var leftFiles = Math.Max(0, progress.TotalFiles - progress.FilesCopied);
        _remainText.Text = $"{leftFiles} 个项目 ({FormatSize(remainingBytes)})";
    }

    private double _lastSampleSeconds;

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

            dialog._bar.Value = 100;
            dialog._percentText.Text = "100% 完成";
            await Task.Delay(180).ConfigureAwait(true);
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
    }

    private static TextBlock Field(string _, string value) =>
        new()
        {
            Text = value,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 10)
        };

    private UIElement LabelAbove(string label, TextBlock value)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        block.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = TryBrush("Text.Tertiary", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 2)
        });
        block.Children.Add(value);
        return block;
    }

    private string FormatRemainingWithSpeed(long remainingBytes)
    {
        if (remainingBytes <= 0)
        {
            return "即将完成";
        }

        if (_bytesPerSecond < 1024)
        {
            return "正在计算…";
        }

        var seconds = remainingBytes / _bytesPerSecond;
        if (seconds < 5)
        {
            return "不到 5 秒";
        }

        if (seconds < 60)
        {
            return $"约 {seconds:0} 秒";
        }

        if (seconds < 3600)
        {
            return $"约 {seconds / 60:0} 分钟";
        }

        return $"约 {seconds / 3600:0.0} 小时";
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var order = 0;
        while (value >= 1024 && order < units.Length - 1)
        {
            value /= 1024;
            order++;
        }

        return order == 0 ? $"{bytes} {units[0]}" : $"{value:0.0} {units[order]}";
    }

    private Brush TryBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}
