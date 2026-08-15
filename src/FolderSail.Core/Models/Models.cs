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
        ("红色", "#FF5F57"),
        ("橙色", "#FF9F0A"),
        ("黄色", "#FFD60A"),
        ("绿色", "#32D74B"),
        ("蓝色", "#0A84FF"),
        ("紫色", "#BF5AF2"),
        ("灰色", "#98989D")
    ];
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
}
