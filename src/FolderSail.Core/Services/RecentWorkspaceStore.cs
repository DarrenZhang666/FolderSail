using System.Text.Json;
using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IRecentWorkspaceStore
{
    IReadOnlyList<WorkspaceEntry> List(int maxCount = 8);
    void RecordOpened(string filePath, string name);
    void SeedIfEmpty(IEnumerable<WorkspaceEntry> workspaces);
}

public sealed class RecentWorkspaceStore : IRecentWorkspaceStore
{
    private const int MaxEntries = 12;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    public RecentWorkspaceStore(string? filePath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "FolderSail");
        Directory.CreateDirectory(folder);
        _filePath = filePath ?? Path.Combine(folder, "recent-workspaces.json");
    }

    public IReadOnlyList<WorkspaceEntry> List(int maxCount = 8)
    {
        var records = LoadRecords();
        var entries = new List<WorkspaceEntry>();
        foreach (var record in records)
        {
            if (!File.Exists(record.FilePath))
            {
                continue;
            }

            entries.Add(new WorkspaceEntry
            {
                Name = string.IsNullOrWhiteSpace(record.Name)
                    ? Path.GetFileNameWithoutExtension(record.FilePath)
                    : record.Name,
                FilePath = record.FilePath,
                SavedAtUtc = record.LastOpenedUtc,
                LastOpenedUtc = record.LastOpenedUtc
            });

            if (entries.Count >= maxCount)
            {
                break;
            }
        }

        return entries;
    }

    public void RecordOpened(string filePath, string name)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        filePath = Path.GetFullPath(filePath);
        var trimmedName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(filePath)
            : name.Trim();

        var records = LoadRecords()
            .Where(record => !string.Equals(record.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        records.Insert(0, new RecentWorkspaceRecord
        {
            FilePath = filePath,
            Name = trimmedName,
            LastOpenedUtc = DateTime.UtcNow
        });

        if (records.Count > MaxEntries)
        {
            records.RemoveRange(MaxEntries, records.Count - MaxEntries);
        }

        SaveRecords(records);
    }

    public void SeedIfEmpty(IEnumerable<WorkspaceEntry> workspaces)
    {
        if (LoadRecords().Count > 0)
        {
            return;
        }

        foreach (var entry in workspaces.Reverse())
        {
            RecordOpened(entry.FilePath, entry.Name);
        }
    }

    private List<RecentWorkspaceRecord> LoadRecords()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<RecentWorkspaceRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveRecords(List<RecentWorkspaceRecord> records)
    {
        var json = JsonSerializer.Serialize(records, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private sealed class RecentWorkspaceRecord
    {
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime LastOpenedUtc { get; set; }
    }
}
