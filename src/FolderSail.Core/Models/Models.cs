namespace FolderSail.Core.Models;

public enum LayoutMode
{
    Single = 1,
    Dual = 2,
    Quad = 4,
    Hex = 6,
    DualRows = 20,
    TripleColumns = 30,
    TripleRows = 31,
    TripleMainLeft = 32,
    TripleMainTop = 33,
    QuadMainLeft = 40,
    QuadColumns = 41
}

public readonly record struct PanePlacement(
    int Row,
    int Column,
    int RowSpan = 1,
    int ColumnSpan = 1);

public sealed record LayoutDefinition(
    IReadOnlyList<double> RowWeights,
    IReadOnlyList<double> ColumnWeights,
    IReadOnlyList<PanePlacement> Placements)
{
    public int PaneCount => Placements.Count;
}

public static class LayoutModeExtensions
{
    public static LayoutDefinition GetDefinition(this LayoutMode mode) => mode switch
    {
        LayoutMode.Single => Define([1], [1], [new(0, 0)]),
        LayoutMode.Dual => Define([1], [1, 1], [new(0, 0), new(0, 1)]),
        LayoutMode.DualRows => Define([1, 1], [1], [new(0, 0), new(1, 0)]),
        LayoutMode.TripleColumns => Define([1], [1, 1, 1], [new(0, 0), new(0, 1), new(0, 2)]),
        LayoutMode.TripleRows => Define([1, 1, 1], [1], [new(0, 0), new(1, 0), new(2, 0)]),
        LayoutMode.TripleMainLeft => Define(
            [1, 1],
            [1.45, 1],
            [new(0, 0, 2, 1), new(0, 1), new(1, 1)]),
        LayoutMode.TripleMainTop => Define(
            [1.25, 1],
            [1, 1],
            [new(0, 0, 1, 2), new(1, 0), new(1, 1)]),
        LayoutMode.Quad => Define(
            [1, 1],
            [1, 1],
            [new(0, 0), new(0, 1), new(1, 0), new(1, 1)]),
        LayoutMode.QuadMainLeft => Define(
            [1, 1, 1],
            [1.5, 1],
            [new(0, 0, 3, 1), new(0, 1), new(1, 1), new(2, 1)]),
        LayoutMode.QuadColumns => Define(
            [1],
            [1, 1, 1, 1],
            [new(0, 0), new(0, 1), new(0, 2), new(0, 3)]),
        LayoutMode.Hex => Define(
            [1, 1],
            [1, 1, 1],
            [new(0, 0), new(0, 1), new(0, 2), new(1, 0), new(1, 1), new(1, 2)]),
        _ => Define([1], [1, 1], [new(0, 0), new(0, 1)])
    };

    private static LayoutDefinition Define(
        double[] rows,
        double[] columns,
        PanePlacement[] placements) =>
        new(rows, columns, placements);
}

public enum FileSortColumn
{
    Name = 0,
    Size = 1,
    Modified = 2,
    Kind = 3
}

public enum FileItemKind
{
    File,
    Directory,
    Drive
}

public sealed class FileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required FileItemKind Kind { get; init; }
    public long Size { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public string Extension { get; init; } = string.Empty;
}

public sealed class DriveItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Label { get; init; }
    public long TotalSize { get; init; }
    public long FreeSize { get; init; }

    public long UsedSize => TotalSize - FreeSize;
    public double UsedRatio => TotalSize > 0 ? (double)UsedSize / TotalSize : 0;
}

public sealed class FavoriteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Path { get; set; }
}

public sealed class FavoriteCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Color { get; set; } = "#3B82F6";
    public List<FavoriteItem> Items { get; set; } = [];
}

/// <summary>
/// The seven colour tags FolderSail ships with, mirroring the fixed palette a
/// Finder sidebar offers rather than free-form user categories.
/// </summary>
public static class TagPalette
{
    public static readonly (string Name, string Color)[] Defaults =
    [
        ("红色", "#D46A66"),
        ("橙色", "#D4925A"),
        ("黄色", "#C9A94A"),
        ("绿色", "#5FA87A"),
        ("蓝色", "#6A96C8"),
        ("紫色", "#9A7FB0"),
        ("灰色", "#8E8E93")
    ];

    /// <summary>
    /// Older, more saturated swatches. Documents still using these get
    /// moved onto the muted sidebar palette without touching custom colours.
    /// </summary>
    public static bool IsRetired(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return true;
        }

        return color.Equals("#FF5F57", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#FF9F0A", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#FFD60A", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#32D74B", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#0A84FF", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#BF5AF2", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#98989D", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#3B82F6", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#EF4444", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#10B981", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#F59E0B", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#8B5CF6", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#EC4899", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#06B6D4", StringComparison.OrdinalIgnoreCase)
            || color.Equals("#64748B", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Lets a pane resolve a virtual tag view without depending on the view models
/// that own the tag collection.
/// </summary>
public interface ITagLookup
{
    IReadOnlyList<string> GetTaggedPaths(Guid tagId);
    string? GetTagName(Guid tagId);
}

public static class TagPath
{
    private const string Prefix = "tag:";

    public static string Create(Guid tagId) => Prefix + tagId.ToString("N");

    public static bool TryParse(string? path, out Guid tagId)
    {
        tagId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Guid.TryParse(path[Prefix.Length..], out tagId);
    }
}

/// <summary>
/// Virtual location that holds a filename search, similar to Everything.
/// </summary>
public static class SearchPath
{
    public const string Prefix = "search:";

    public static string Create(string query) => Prefix + Uri.EscapeDataString(query.Trim());

    public static bool TryParse(string? path, out string query)
    {
        query = string.Empty;

        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = path[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            query = Uri.UnescapeDataString(encoded).Trim();
        }
        catch (UriFormatException)
        {
            query = encoded.Trim();
        }

        return query.Length > 0;
    }
}

public sealed class FavoritesDocument
{
    public List<FavoriteCategory> Categories { get; set; } = [];
}

public sealed class AppSettings
{
    public LayoutMode LayoutMode { get; set; } = LayoutMode.Dual;
    public int ActivePaneIndex { get; set; }
    public List<string> PanePaths { get; set; } = [];
    public double SidebarWidth { get; set; } = 228;
    public bool IsDarkTheme { get; set; }
    public bool ExpandTagChildrenOnStartup { get; set; }
    public List<int> PaneSortColumns { get; set; } = [];
    public List<bool> PaneSortDescending { get; set; } = [];
}

public sealed class FileTransferProgress
{
    public string CurrentName { get; init; } = string.Empty;
    public long BytesCopied { get; init; }
    public long TotalBytes { get; init; }
    public int FilesCopied { get; init; }
    public int TotalFiles { get; init; }
}
