using System.Text.Json;
using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IWorkspaceStore
{
    string WorkspacesDirectory { get; }
    IReadOnlyList<WorkspaceEntry> List();
    WorkspaceDocument Load(string filePath);
    void Save(string filePath, WorkspaceDocument document);
    string ResolvePath(string name);
    bool Exists(string name);
}

public sealed class WorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WorkspaceStore(string? directory = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        WorkspacesDirectory = directory ?? Path.Combine(appData, "FolderSail", "workspaces");
        Directory.CreateDirectory(WorkspacesDirectory);
    }

    public string WorkspacesDirectory { get; }

    public IReadOnlyList<WorkspaceEntry> List()
    {
        if (!Directory.Exists(WorkspacesDirectory))
        {
            return [];
        }

        var entries = new List<WorkspaceEntry>();
        foreach (var file in Directory.EnumerateFiles(WorkspacesDirectory, "*.foldersail"))
        {
            try
            {
                var document = Load(file);
                entries.Add(new WorkspaceEntry
                {
                    Name = string.IsNullOrWhiteSpace(document.Name)
                        ? Path.GetFileNameWithoutExtension(file)
                        : document.Name,
                    FilePath = file,
                    SavedAtUtc = document.SavedAtUtc,
                    LastOpenedUtc = document.SavedAtUtc
                });
            }
            catch
            {
                entries.Add(new WorkspaceEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    SavedAtUtc = File.GetLastWriteTimeUtc(file),
                    LastOpenedUtc = File.GetLastWriteTimeUtc(file)
                });
            }
        }

        return entries
            .OrderByDescending(entry => entry.SavedAtUtc)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WorkspaceDocument Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<WorkspaceDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("工作区文件无效");
    }

    public void Save(string filePath, WorkspaceDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        document.SavedAtUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public string ResolvePath(string name)
    {
        var trimmed = SanitizeName(name);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("名称不能为空", nameof(name));
        }

        return Path.Combine(WorkspacesDirectory, trimmed + ".foldersail");
    }

    public bool Exists(string name) => File.Exists(ResolvePath(name));

    private static string SanitizeName(string name)
    {
        var trimmed = name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        return trimmed;
    }
}
