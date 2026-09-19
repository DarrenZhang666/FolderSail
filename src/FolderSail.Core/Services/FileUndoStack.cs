namespace FolderSail.Core.Services;

public enum FileUndoKind
{
    Delete,
    Rename,
    Move
}

public sealed class FileUndoEntry
{
    public required FileUndoKind Kind { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public IReadOnlyList<RecycledItem>? RecycledItems { get; init; }
}

/// <summary>
/// App-local undo for file operations performed in FolderSail (Explorer-like Ctrl+Z).
/// </summary>
public static class FileUndoStack
{
    private const int MaxDepth = 40;
    private static readonly List<FileUndoEntry> Entries = [];
    private static readonly object Gate = new();

    public static event EventHandler? Changed;

    public static bool CanUndo
    {
        get
        {
            lock (Gate)
            {
                return Entries.Count > 0;
            }
        }
    }

    public static void PushDelete(IReadOnlyList<RecycledItem> recycled)
    {
        if (recycled.Count == 0)
        {
            return;
        }

        Push(new FileUndoEntry
        {
            Kind = FileUndoKind.Delete,
            Paths = recycled.Select(r => r.OriginalPath).ToList(),
            RecycledItems = recycled.ToList()
        });
    }

    public static void PushDelete(IReadOnlyList<string> originalPaths)
    {
        var paths = Normalize(originalPaths);
        if (paths.Count == 0)
        {
            return;
        }

        Push(new FileUndoEntry
        {
            Kind = FileUndoKind.Delete,
            Paths = paths,
            RecycledItems = paths.Select(p => new RecycledItem(p, null)).ToList()
        });
    }

    public static void PushRename(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return;
        }

        Push(new FileUndoEntry
        {
            Kind = FileUndoKind.Rename,
            Paths = [Path.GetFullPath(oldPath), Path.GetFullPath(newPath)]
        });
    }

    public static void PushMove(string fromPath, string toPath)
    {
        if (string.IsNullOrWhiteSpace(fromPath) || string.IsNullOrWhiteSpace(toPath))
        {
            return;
        }

        Push(new FileUndoEntry
        {
            Kind = FileUndoKind.Move,
            Paths = [Path.GetFullPath(fromPath), Path.GetFullPath(toPath)]
        });
    }

    public static bool TryUndo(out FileUndoKind kind, out int affectedCount, out string? error)
    {
        kind = default;
        affectedCount = 0;
        error = null;

        FileUndoEntry entry;
        lock (Gate)
        {
            if (Entries.Count == 0)
            {
                return false;
            }

            entry = Entries[^1];
            Entries.RemoveAt(Entries.Count - 1);
        }

        NotifyChanged();
        kind = entry.Kind;
        try
        {
            affectedCount = entry.Kind switch
            {
                FileUndoKind.Delete => UndoDelete(entry),
                FileUndoKind.Rename => UndoRenameOrMove(entry.Paths[0], entry.Paths[1]),
                FileUndoKind.Move => UndoRenameOrMove(entry.Paths[0], entry.Paths[1]),
                _ => 0
            };

            if (affectedCount == 0)
            {
                // Put it back so the user can retry / see a clear status.
                lock (Gate)
                {
                    Entries.Add(entry);
                }

                NotifyChanged();
                error = "nothing";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                Entries.Add(entry);
            }

            NotifyChanged();
            error = ex.Message;
            return false;
        }
    }

    private static void Push(FileUndoEntry entry)
    {
        lock (Gate)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxDepth)
            {
                Entries.RemoveAt(0);
            }
        }

        NotifyChanged();
    }

    private static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);

    private static List<string> Normalize(IEnumerable<string> paths) =>
        paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int UndoDelete(FileUndoEntry entry)
    {
        if (entry.RecycledItems is { Count: > 0 } recycled)
        {
            return RecycleBinHelper.Restore(recycled);
        }

        return RecycleBinHelper.RestoreByOriginalPaths(entry.Paths);
    }

    private static int UndoRenameOrMove(string oldPath, string newPath)
    {
        if (!File.Exists(newPath) && !Directory.Exists(newPath))
        {
            return 0;
        }

        if (File.Exists(oldPath) || Directory.Exists(oldPath))
        {
            throw new IOException("无法撤销：原路径已被占用。");
        }

        var parent = Path.GetDirectoryName(oldPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(newPath))
        {
            Directory.Move(newPath, oldPath);
        }
        else
        {
            File.Move(newPath, oldPath);
        }

        return 1;
    }
}
