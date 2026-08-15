using FolderSail.Core.Models;
using FolderSail.Core.Services;

namespace FolderSail.Core.Navigation;

public sealed class NavigationHistory
{
    /// <summary>Virtual root that stands for the drive list rather than a real directory.</summary>
    public const string ThisPcToken = "ThisPC";

    private readonly List<string> _entries = [];
    private int _index = -1;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    public string? Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;

    public void Navigate(string path)
    {
        var normalized = Normalize(path);
        if (Current == normalized)
        {
            return;
        }

        if (_index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        _entries.Add(normalized);
        _index = _entries.Count - 1;
    }

    public string? GoBack()
    {
        if (!CanGoBack)
        {
            return null;
        }

        _index--;
        return Current;
    }

    public string? GoForward()
    {
        if (!CanGoForward)
        {
            return null;
        }

        _index++;
        return Current;
    }

    public void Reset(string path)
    {
        _entries.Clear();
        _index = -1;
        Navigate(path);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals(ThisPcToken, StringComparison.OrdinalIgnoreCase))
        {
            return ThisPcToken;
        }

        if (TagPath.TryParse(path, out var tagId))
        {
            return TagPath.Create(tagId);
        }

        if (SearchPath.TryParse(path, out var query))
        {
            return SearchPath.Create(query);
        }

        var full = FileService.ExpandDriveRoot(path);
        full = Path.GetFullPath(full);

        // Keep the trailing separator on drive roots so "D:\" never collapses to the
        // process-relative "D:" form.
        return Path.GetPathRoot(full) == full
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
