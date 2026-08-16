using FolderSail.Core.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FolderSail.Helpers;

public enum PreviewKind
{
    Empty,
    Folder,
    Image,
    Text,
    Generic
}

public sealed class FilePreview
{
    public PreviewKind Kind { get; init; } = PreviewKind.Empty;
    public ImageSource? Image { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}

public static class PreviewService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".jfif"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".cfg",
        ".cs", ".js", ".ts", ".css", ".html", ".htm", ".py", ".ps1", ".bat"
    };

    private const int MaxTextBytes = 64 * 1024;
    private const int DecodeWidth = 360;

    public static FilePreview Load(FileItem? item, CancellationToken cancellationToken = default)
    {
        if (item == null)
        {
            return new FilePreview { Hint = Loc.Get("Loc.SelectToPreview") };
        }

        var meta = FormatMeta(item);
        if (item.Kind != FileItemKind.File)
        {
            return new FilePreview
            {
                Kind = PreviewKind.Folder,
                Title = item.Name,
                Meta = meta,
                Hint = Loc.Get("Loc.PreviewFolder")
            };
        }

        var ext = item.Extension;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new FilePreview
            {
                Kind = PreviewKind.Generic,
                Title = item.Name,
                Meta = meta,
                Hint = Loc.Get("Loc.PreviewPdf")
            };
        }

        if (ImageExtensions.Contains(ext))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = TryLoadImage(item.FullPath);
            if (image != null)
            {
                return new FilePreview
                {
                    Kind = PreviewKind.Image,
                    Image = image,
                    Title = item.Name,
                    Meta = meta
                };
            }
        }

        if (TextExtensions.Contains(ext))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = TryLoadText(item.FullPath);
            if (text != null)
            {
                return new FilePreview
                {
                    Kind = PreviewKind.Text,
                    Text = text,
                    Title = item.Name,
                    Meta = meta
                };
            }
        }

        return new FilePreview
        {
            Kind = PreviewKind.Generic,
            Title = item.Name,
            Meta = meta,
            Hint = Loc.Get("Loc.PreviewGeneric")
        };
    }

    private static string FormatMeta(FileItem item)
    {
        var time = item.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        if (item.Kind != FileItemKind.File || item.Size <= 0)
        {
            return time;
        }

        return $"{FormatSize(item.Size)}  ·  {time}";
    }

    private static string FormatSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        var order = 0;
        while (value >= 1024 && order < units.Length - 1)
        {
            value /= 1024;
            order++;
        }

        return order == 0 ? $"{size} B" : $"{value:0.0} {units[order]}";
    }

    private static ImageSource? TryLoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = DecodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryLoadText(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, MaxTextBytes);
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            var text = DecodeText(buffer.AsSpan(0, read));
            if (stream.Length > MaxTextBytes)
            {
                text += "\n\n…";
            }

            return text;
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return System.Text.Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return System.Text.Encoding.Unicode.GetString(bytes[2..]);
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
