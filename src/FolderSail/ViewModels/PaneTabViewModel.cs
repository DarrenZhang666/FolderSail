using FolderSail.Core.Models;
using FolderSail.Core.Navigation;
using FolderSail.Helpers;
using FolderSail.Mvvm;

namespace FolderSail.ViewModels;

public sealed class PaneTabViewModel : ObservableObject
{
    private readonly ITagLookup? _tags;
    private string _path;
    private string _title;
    private bool _isActive;
    private FileSortColumn _sortColumn = FileSortColumn.Name;
    private bool _sortDescending;
    private bool _foldersFirst = true;

    public PaneTabViewModel(string path, ITagLookup? tags = null)
    {
        _tags = tags;
        _path = path;
        _title = GetTitle(path);
        History.Reset(path);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public NavigationHistory History { get; } = new();

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
            {
                Title = GetTitle(value);
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public FileSortColumn SortColumn
    {
        get => _sortColumn;
        set => SetProperty(ref _sortColumn, value);
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set => SetProperty(ref _sortDescending, value);
    }

    public bool FoldersFirst
    {
        get => _foldersFirst;
        set => SetProperty(ref _foldersFirst, value);
    }

    public void RefreshTitle() => Title = GetTitle(Path);

    private string GetTitle(string path)
    {
        if (path.Equals(NavigationHistory.ThisPcToken, StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Get("Loc.ThisPc");
        }

        if (TagPath.TryParse(path, out var tagId))
        {
            return _tags?.GetTagName(tagId) ?? Loc.Get("Loc.Tag");
        }

        if (SearchPath.TryParse(path, out var query))
        {
            return query.Length > 12 ? Loc.Get("Loc.Search") : Loc.Format("Loc.SearchPrefix", query);
        }

        var normalized = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
