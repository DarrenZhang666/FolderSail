using System.Text.Json;
using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IFavoriteStore
{
    FavoritesDocument Load();
    void Save(FavoritesDocument document);
}

public sealed class FavoriteStore : IFavoriteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public FavoriteStore(string? filePath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "FolderSail");
        Directory.CreateDirectory(folder);
        _filePath = filePath ?? Path.Combine(folder, "favorites.json");
    }

    public FavoritesDocument Load()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<FavoritesDocument>(json, JsonOptions) ?? CreateDefault();

            if (Migrate(document))
            {
                Save(document);
            }

            return document;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(FavoritesDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Brings older documents (free-form categories) onto the fixed colour tag
    /// palette, keeping any folders the user had already saved.
    /// </summary>
    private static bool Migrate(FavoritesDocument document)
    {
        var palette = TagPalette.Defaults;
        var expected = palette.Select(tag => tag.Name).ToHashSet(StringComparer.Ordinal);
        var legacy = document.Categories.Where(category => !expected.Contains(category.Name)).ToList();

        if (legacy.Count == 0 && document.Categories.Count == palette.Length)
        {
            return false;
        }

        var tags = palette
            .Select(tag => document.Categories.FirstOrDefault(category => category.Name == tag.Name)
                           ?? new FavoriteCategory { Name = tag.Name, Items = [] })
            .ToList();

        for (var i = 0; i < tags.Count; i++)
        {
            tags[i].Color = palette[i].Color;
        }

        // Park orphaned folders on the blue tag so nothing silently disappears.
        var fallback = tags[4];
        foreach (var item in legacy.SelectMany(category => category.Items))
        {
            if (!fallback.Items.Any(existing =>
                    string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                fallback.Items.Add(item);
            }
        }

        document.Categories = tags;
        return true;
    }

    private static FavoritesDocument CreateDefault() => new()
    {
        Categories = TagPalette.Defaults
            .Select(tag => new FavoriteCategory
            {
                Name = tag.Name,
                Color = tag.Color,
                Items = []
            })
            .ToList()
    };
}
