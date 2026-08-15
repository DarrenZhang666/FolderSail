using FolderSail.Core.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FolderSail.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter?.ToString() == "Invert")
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long size || size <= 0)
        {
            return string.Empty;
        }

        var order = 0;
        double len = size;
        while (len >= 1024 && order < Units.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.#} {Units[order]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LayoutModeToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LayoutMode mode || parameter is null)
        {
            return false;
        }

        return int.TryParse(parameter.ToString(), out var expected) && (int)mode == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null && int.TryParse(parameter.ToString(), out var expected))
        {
            return (LayoutMode)expected;
        }

        return Binding.DoNothing;
    }
}

/// <summary>
/// Turns a UTC timestamp into a compact age label such as "今天", "3 天", "5 月".
/// </summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc || utc == default)
        {
            return string.Empty;
        }

        var span = DateTime.UtcNow - utc;

        if (span.TotalMinutes < 60)
        {
            return "刚刚";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} 时";
        }

        if (span.TotalDays < 31)
        {
            return $"{(int)span.TotalDays} 天";
        }

        if (span.TotalDays < 365)
        {
            return $"{(int)(span.TotalDays / 30)} 月";
        }

        return $"{(int)(span.TotalDays / 365)} 年";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps file age onto the FolderSail "tide" palette so recency is scannable at a glance.
/// Pass "Foreground" as the converter parameter to get the paired text colour.
/// </summary>
public sealed class RelativeTimeBrushConverter : IValueConverter
{
    private enum Tier
    {
        Fresh,
        Recent,
        Monthly,
        Older
    }

    private static readonly Dictionary<Tier, SolidColorBrush> Backgrounds = new()
    {
        [Tier.Fresh] = Freeze("#E4F6FA"),
        [Tier.Recent] = Freeze("#E7F6EC"),
        [Tier.Monthly] = Freeze("#FCF2E2"),
        [Tier.Older] = Freeze("#F2F4F6")
    };

    private static readonly Dictionary<Tier, SolidColorBrush> Foregrounds = new()
    {
        [Tier.Fresh] = Freeze("#0B6E86"),
        [Tier.Recent] = Freeze("#1B6B3A"),
        [Tier.Monthly] = Freeze("#8A5313"),
        [Tier.Older] = Freeze("#616B78")
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tier = Classify(value);
        var palette = parameter?.ToString() == "Foreground" ? Foregrounds : Backgrounds;
        return palette[tier];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Tier Classify(object? value)
    {
        if (value is not DateTime utc || utc == default)
        {
            return Tier.Older;
        }

        return (DateTime.UtcNow - utc).TotalDays switch
        {
            < 2 => Tier.Fresh,
            < 14 => Tier.Recent,
            < 90 => Tier.Monthly,
            _ => Tier.Older
        };
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Renders the compact kind marker shown at the end of each row.
/// </summary>
public sealed class ItemKindLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            FileItemKind.Directory => "文件夹",
            FileItemKind.Drive => "驱动器",
            _ => "文件"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RatioToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double ratio || values[1] is not double available)
        {
            return 0d;
        }

        if (double.IsNaN(available) || available <= 0)
        {
            return 0d;
        }

        return Math.Max(2d, available * Math.Clamp(ratio, 0, 1));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Transparent;
    public Brush FalseBrush { get; set; } = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
