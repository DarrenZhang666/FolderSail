using FolderSail.Core.Models;
using FolderSail.Core.Navigation;
using FolderSail.Core.Services;
using FolderSail.Helpers;
using FolderSail.Mvvm;
using FolderSail.Views;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class PaneViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly ITagLookup? _tags;
    private readonly IFileSearchService? _search;
    private CancellationTokenSource? _searchCts;
    private bool _switchingTab;
    private bool _isPasting;
    private string _currentPath = string.Empty;
    private string _addressText = string.Empty;
    private bool _isActive;
    private bool _isAddressEditing;
    private FileItemViewModel? _selectedItem;
    private PaneTabViewModel? _activeTab;
    private bool _isSearching;
    private string _filterText = string.Empty;
    private bool _filterOpen;
    private double _sizeColumnWidth = 72;
    private double _modifiedColumnWidth = 56;
    private double _kindColumnWidth = 96;
    private int _filterEpoch;
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource _iconCts = new();

    public PaneViewModel(
        int index,
        IFileService fileService,
        string initialPath,
        ITagLookup? tags = null,
        IFileSearchService? search = null)
    {
        Index = index;
        _fileService = fileService;
        _tags = tags;
        _search = search;

        GoBackCommand = new RelayCommand(GoBack, () => ActiveTab?.History.CanGoBack == true);
        GoForwardCommand = new RelayCommand(GoForward, () => ActiveTab?.History.CanGoForward == true);
        GoUpCommand = new RelayCommand(GoUp);
        GoToThisPcCommand = new RelayCommand(GoToThisPc);
        GoToAddressCommand = new RelayCommand(GoToAddress);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedItem != null);
        CopySelectedCommand = new RelayCommand(CopySelected, HasSelection);
        CutSelectedCommand = new RelayCommand(CutSelected, HasSelection);
        PasteCommand = new RelayCommand(Paste, () => !_isPasting && FileClipboard.HasFiles());
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, HasSelection);
        RenameSelectedCommand = new RelayCommand(RenameSelected);
        NewFolderCommand = new RelayCommand(NewFolder);
        RefreshCommand = new RelayCommand(RefreshItems);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer);
        BeginEditAddressCommand = new RelayCommand(() => IsAddressEditing = true);
        CancelEditAddressCommand = new RelayCommand(() =>
        {
            AddressText = CurrentPath;
            IsAddressEditing = false;
        });
        NewTabCommand = new RelayCommand(NewTab);
        ActivateTabCommand = new RelayCommand<PaneTabViewModel>(ActivateTab);
        CloseTabCommand = new RelayCommand<PaneTabViewModel>(CloseTab);
        DuplicateTabCommand = new RelayCommand<PaneTabViewModel>(DuplicateTab);
        CloseOtherTabsCommand = new RelayCommand<PaneTabViewModel>(CloseOtherTabs);
        CloseAllTabsCommand = new RelayCommand(CloseAllTabs);
        OpenSelectedInNewTabCommand = new RelayCommand(OpenSelectedInNewTab, CanOpenSelectedInNewTab);
        OpenKnownFolderCommand = new RelayCommand<string>(OpenKnownFolder);
        SortByCommand = new RelayCommand<FileSortColumn>(SortBy);
        SortAscendingCommand = new RelayCommand(() => SetSortDirection(descending: false));
        SortDescendingCommand = new RelayCommand(() => SetSortDirection(descending: true));
        ClearFilterCommand = new RelayCommand(ClearFilter);
        OpenFilterCommand = new RelayCommand(OpenFilter);

        var initialTab = new PaneTabViewModel(initialPath, _tags);
        Tabs.Add(initialTab);
        ActiveTab = initialTab;
        RebuildBreadcrumbs();
    }

    public int Index { get; }

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (!SetProperty(ref _currentPath, value))
            {
                return;
            }

            AddressText = value;
            if (!_switchingTab && ActiveTab != null)
            {
                ActiveTab.Path = value;
            }
            RebuildBreadcrumbs();
            RefreshItems();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(IsTagView));
            OnPropertyChanged(nameof(IsSearchView));
        }
    }

    public string AddressText
    {
        get => _addressText;
        set => SetProperty(ref _addressText, value);
    }

    public bool IsAddressEditing
    {
        get => _isAddressEditing;
        set => SetProperty(ref _isAddressEditing, value);
    }

    public ObservableCollection<BreadcrumbViewModel> Breadcrumbs { get; } = [];
    public ObservableCollection<PaneTabViewModel> Tabs { get; } = [];

    public PaneTabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (!SetProperty(ref _activeTab, value) || value == null)
            {
                return;
            }

            foreach (var tab in Tabs)
            {
                tab.IsActive = ReferenceEquals(tab, value);
            }

            _switchingTab = true;
            CurrentPath = value.Path;
            _switchingTab = false;
            IsAddressEditing = false;
            FilterText = string.Empty;
            _filterOpen = false;
            OnPropertyChanged(nameof(ShowFilterChrome));
            SelectedItem = null;
            OnPropertyChanged(nameof(SortColumn));
            OnPropertyChanged(nameof(SortDescending));
            OnPropertyChanged(nameof(FoldersFirst));
            NotifySortHeaders();
            RebuildVisible();
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (IsThisPc)
            {
                return Loc.Get("Loc.ThisPc");
            }

            if (IsTagView)
            {
                return TagName;
            }

            if (IsSearchView)
            {
                return SearchQuery.Length > 16 ? Loc.Get("Loc.Search") : Loc.Format("Loc.SearchPrefix", SearchQuery);
            }

            var name = Path.GetFileName(CurrentPath.TrimEnd('\\'));
            return string.IsNullOrWhiteSpace(name) ? CurrentPath : name;
        }
    }

    public bool IsTagView => TagPath.TryParse(CurrentPath, out _);

    public bool IsSearchView => SearchPath.TryParse(CurrentPath, out _);

    public bool IsVirtualView => IsThisPc || IsTagView || IsSearchView;

    private string TagName =>
        TagPath.TryParse(CurrentPath, out var tagId) ? _tags?.GetTagName(tagId) ?? Loc.Get("Loc.Tag") : string.Empty;

    private string SearchQuery =>
        SearchPath.TryParse(CurrentPath, out var query) ? query : string.Empty;

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(ItemSummary));
            }
        }
    }

    public string ItemSummary
    {
        get
        {
            if (IsSearching)
            {
                return SearchQuery.Length == 0
                    ? Loc.Get("Loc.Searching")
                    : Loc.Format("Loc.SearchingQuery", SearchQuery);
            }

            var folders = Items.Count(i => i.Kind != FileItemKind.File);
            var files = Items.Count - folders;
            if (HasFilter)
            {
                return Loc.Format("Loc.FilterCount", VisibleItems.Count, Items.Count);
            }

            return Loc.Format("Loc.ItemCounts", folders, files);
        }
    }

    public void RefreshLocalization()
    {
        RebuildBreadcrumbs();
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ItemSummary));
        foreach (var tab in Tabs)
        {
            tab.RefreshTitle();
        }

        ReplaceVisible(VisibleItems.ToList());
    }

    public bool IsThisPc => CurrentPath.Equals("ThisPC", StringComparison.OrdinalIgnoreCase);

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!SetProperty(ref _isActive, value))
            {
                return;
            }

            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public FileItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public List<FileItemViewModel> SelectedItems { get; } = [];

    public List<FileItemViewModel> Items { get; private set; } = [];

    public ObservableCollection<FileItemViewModel> VisibleItems { get; private set; } = [];

    public CancellationToken IconLoadToken => _iconCts.Token;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasFilter));
            OnPropertyChanged(nameof(ShowFilterChrome));
            var epoch = Interlocked.Increment(ref _filterEpoch);
            _ = Task.Delay(80).ContinueWith(_ =>
            {
                if (epoch != _filterEpoch)
                {
                    return;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.Invoke(() =>
                {
                    if (epoch == _filterEpoch)
                    {
                        RebuildVisible();
                    }
                });
            });
        }
    }

    public bool HasFilter => !string.IsNullOrWhiteSpace(_filterText);

    public bool ShowFilterChrome => HasFilter || _filterOpen;

    public FileSortColumn SortColumn => ActiveTab?.SortColumn ?? FileSortColumn.Name;

    public bool SortDescending => ActiveTab?.SortDescending ?? false;

    public bool FoldersFirst
    {
        get => ActiveTab?.FoldersFirst ?? true;
        set
        {
            if (ActiveTab == null || ActiveTab.FoldersFirst == value)
            {
                return;
            }

            ActiveTab.FoldersFirst = value;
            OnPropertyChanged();
            RebuildVisible();
            ViewStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double SizeColumnWidth
    {
        get => _sizeColumnWidth;
        private set => SetProperty(ref _sizeColumnWidth, value);
    }

    public double ModifiedColumnWidth
    {
        get => _modifiedColumnWidth;
        private set => SetProperty(ref _modifiedColumnWidth, value);
    }

    public double KindColumnWidth
    {
        get => _kindColumnWidth;
        private set => SetProperty(ref _kindColumnWidth, value);
    }

    public IReadOnlyList<double> GetColumnWidths() =>
        [SizeColumnWidth, ModifiedColumnWidth, KindColumnWidth];

    public void ApplyColumnWidths(IReadOnlyList<double>? widths)
    {
        if (widths is not { Count: >= 3 })
        {
            return;
        }

        SizeColumnWidth = ClampColumnWidth(widths[0], 40, 320);
        ModifiedColumnWidth = ClampColumnWidth(widths[1], 40, 220);
        KindColumnWidth = widths[2] <= 48
            ? 96
            : ClampColumnWidth(widths[2], 48, 280);
    }

    public void PreviewColumnWidths(double size, double modified, double kind) =>
        ApplyColumnWidths([size, modified, kind]);

    public void CommitColumnWidths(double size, double modified, double kind)
    {
        ApplyColumnWidths([size, modified, kind]);
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double ClampColumnWidth(double width, double min, double max)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
        {
            return min;
        }

        return Math.Clamp(width, min, max);
    }

    public string NameSortGlyph => SortGlyph(FileSortColumn.Name);
    public string SizeSortGlyph => SortGlyph(FileSortColumn.Size);
    public string ModifiedSortGlyph => SortGlyph(FileSortColumn.Modified);
    public string KindSortGlyph => SortGlyph(FileSortColumn.Kind);

    public event EventHandler? ViewStateChanged;

    public bool CanGoBack => ActiveTab?.History.CanGoBack == true;
    public bool CanGoForward => ActiveTab?.History.CanGoForward == true;

    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand GoToThisPcCommand { get; }
    public ICommand GoToAddressCommand { get; }
    public ICommand OpenSelectedCommand { get; }
    public ICommand CopySelectedCommand { get; }
    public ICommand CutSelectedCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand RenameSelectedCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenInExplorerCommand { get; }
    public ICommand BeginEditAddressCommand { get; }
    public ICommand CancelEditAddressCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand ActivateTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand DuplicateTabCommand { get; }
    public ICommand CloseOtherTabsCommand { get; }
    public ICommand CloseAllTabsCommand { get; }
    public ICommand OpenSelectedInNewTabCommand { get; }
    public ICommand OpenKnownFolderCommand { get; }
    public ICommand SortByCommand { get; }
    public ICommand SortAscendingCommand { get; }
    public ICommand SortDescendingCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand OpenFilterCommand { get; }

    public event EventHandler? ActiveChanged;
    public event EventHandler<string>? StatusMessage;
    public event EventHandler? InlineRenameStarted;
    public event EventHandler? FilterFocusRequested;

    public bool IsInlineRenaming => Items.Any(item => item.IsRenaming);

    public void OpenFilter()
    {
        _filterOpen = true;
        OnPropertyChanged(nameof(ShowFilterChrome));
        FilterFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void CloseFilterIfEmpty()
    {
        if (HasFilter)
        {
            return;
        }

        _filterOpen = false;
        OnPropertyChanged(nameof(ShowFilterChrome));
    }

    private void ClearFilter()
    {
        FilterText = string.Empty;
        _filterOpen = false;
        OnPropertyChanged(nameof(ShowFilterChrome));
    }

    public void ReportStatus(string message) => StatusMessage?.Invoke(this, message);

    public void BeginInlineRename()
    {
        var item = SelectedItem;
        if (item == null || item.Kind == FileItemKind.Drive)
        {
            return;
        }

        foreach (var row in Items)
        {
            row.IsRenaming = false;
        }

        item.RenameText = item.Name;
        item.IsRenaming = true;
        InlineRenameStarted?.Invoke(this, EventArgs.Empty);
    }

    public void CommitInlineRename()
    {
        var item = Items.FirstOrDefault(row => row.IsRenaming);
        if (item == null)
        {
            return;
        }

        var newName = item.RenameText.Trim();
        item.IsRenaming = false;

        if (string.IsNullOrWhiteSpace(newName) ||
            newName.Equals(item.Name, StringComparison.Ordinal))
        {
            return;
        }

        SelectedItem = item;
        RenameSelected(newName);
    }

    public void CancelInlineRename()
    {
        foreach (var row in Items.Where(item => item.IsRenaming))
        {
            row.RenameText = row.Name;
            row.IsRenaming = false;
        }
    }

    public void RefreshItems()
    {
        CancelInlineRename();
        CancelSearch();
        CancelListing();
        ResetIconLoads();
        Items = [];
        ReplaceVisible([]);

        if (SearchPath.TryParse(CurrentPath, out var query))
        {
            BeginSearch(query);
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        var path = CurrentPath;
        var listCts = new CancellationTokenSource();
        _listCts = listCts;
        var token = listCts.Token;
        var fileService = _fileService;
        var isTag = TagPath.TryParse(path, out var tagId);
        var tagged = isTag ? _tags?.GetTaggedPaths(tagId) ?? [] : [];

        _ = Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return (IReadOnlyList<FileItem>)[];
                }

                return isTag
                    ? fileService.ListPaths(tagged)
                    : fileService.ListDirectory(path);
            }, token)
            .ContinueWith(task =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.Invoke(() => ApplyDirectoryListing(path, token, task));
            }, TaskScheduler.Default);
    }

    private void ApplyDirectoryListing(
        string path,
        CancellationToken token,
        Task<IReadOnlyList<FileItem>> task)
    {
        if (token.IsCancellationRequested || !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (task.IsFaulted)
        {
            StatusMessage?.Invoke(this, task.Exception?.InnerException?.Message ?? Loc.Get("Loc.CannotReadDir"));
            Items = [];
            ReplaceVisible([]);
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        if (task.IsCanceled || task.Result is null)
        {
            return;
        }

        Items = task.Result.Select(item => new FileItemViewModel(item)).ToList();
        RebuildVisible();
        OnPropertyChanged(nameof(ItemSummary));
    }

    private void BeginSearch(string query)
    {
        if (_search is null)
        {
            IsSearching = false;
            StatusMessage?.Invoke(this, Loc.Get("Loc.SearchUnavailable"));
            return;
        }

        IsSearching = true;
        StatusMessage?.Invoke(this, Loc.Format("Loc.SearchingQuery", query));
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;
        var search = _search;

        _ = Task.Run(() => search.SearchByName(query, token), token)
            .ContinueWith(task =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    return;
                }

                dispatcher.Invoke(() => ApplySearchResults(query, token, task));
            }, TaskScheduler.Default);
    }

    private void ApplySearchResults(
        string query,
        CancellationToken token,
        Task<IReadOnlyList<FileItem>> task)
    {
        if (token.IsCancellationRequested)
        {
            IsSearching = false;
            return;
        }

        if (!SearchPath.TryParse(CurrentPath, out var current) ||
            !string.Equals(current, query, StringComparison.Ordinal))
        {
            IsSearching = false;
            return;
        }

        Items = [];

        if (task.IsFaulted)
        {
            IsSearching = false;
            var message = task.Exception?.InnerException?.Message ?? Loc.Get("Loc.SearchFailed");
            StatusMessage?.Invoke(this, message);
            RebuildVisible();
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        if (task.IsCanceled || task.Result is null)
        {
            IsSearching = false;
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        Items = task.Result.Select(item => new FileItemViewModel(item)).ToList();

        IsSearching = false;
        RebuildVisible();
        OnPropertyChanged(nameof(ItemSummary));
        StatusMessage?.Invoke(
            this,
            task.Result.Count == 0 ? Loc.Format("Loc.NoResults", query) : Loc.Format("Loc.FoundItems", task.Result.Count));
    }

    public void ApplySort(FileSortColumn column, bool descending, bool foldersFirst = true)
    {
        if (ActiveTab == null)
        {
            return;
        }

        ActiveTab.SortColumn = column;
        ActiveTab.SortDescending = descending;
        ActiveTab.FoldersFirst = foldersFirst;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(FoldersFirst));
        NotifySortHeaders();
        RebuildVisible();
    }

    private void SetSortDirection(bool descending)
    {
        if (ActiveTab == null)
        {
            return;
        }

        ActiveTab.SortDescending = descending;
        OnPropertyChanged(nameof(SortDescending));
        NotifySortHeaders();
        RebuildVisible();
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SortBy(FileSortColumn column)
    {
        if (ActiveTab == null)
        {
            return;
        }

        if (ActiveTab.SortColumn == column)
        {
            ActiveTab.SortDescending = !ActiveTab.SortDescending;
        }
        else
        {
            ActiveTab.SortColumn = column;
            ActiveTab.SortDescending = column is FileSortColumn.Size or FileSortColumn.Modified;
        }

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        NotifySortHeaders();
        RebuildVisible();
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(ModifiedSortGlyph));
        OnPropertyChanged(nameof(KindSortGlyph));
    }

    private string SortGlyph(FileSortColumn column)
    {
        if (SortColumn != column)
        {
            return string.Empty;
        }

        return SortDescending ? "\uE70D" : "\uE70E";
    }

    private void RebuildVisible()
    {
        var selected = SelectedItem;
        var sorted = SortItems(Items);
        var filter = _filterText.Trim();
        if (filter.Length > 0)
        {
            sorted = sorted.Where(item =>
                item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceVisible(sorted.ToList());

        if (selected != null && VisibleItems.Contains(selected))
        {
            SelectedItem = selected;
        }
        else if (VisibleItems.Count > 0 && selected != null)
        {
            SelectedItem = null;
        }

        OnPropertyChanged(nameof(ItemSummary));
    }

    private void ReplaceVisible(IReadOnlyList<FileItemViewModel> items)
    {
        VisibleItems = new ObservableCollection<FileItemViewModel>(items);
        OnPropertyChanged(nameof(VisibleItems));
    }

    private IEnumerable<FileItemViewModel> SortItems(IEnumerable<FileItemViewModel> source)
    {
        IOrderedEnumerable<FileItemViewModel> ordered = FoldersFirst
            ? source.OrderByDescending(item => item.Kind != FileItemKind.File)
            : source.OrderBy(_ => 0);
        var descending = SortDescending;
        return SortColumn switch
        {
            FileSortColumn.Size => descending
                ? ordered.ThenByDescending(item => item.Size)
                : ordered.ThenBy(item => item.Size),
            FileSortColumn.Modified => descending
                ? ordered.ThenByDescending(item => item.ModifiedUtc)
                : ordered.ThenBy(item => item.ModifiedUtc),
            FileSortColumn.Kind => descending
                ? ordered.ThenByDescending(item => item.TypeName, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(item => item.TypeName, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? ordered.ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void CancelListing()
    {
        if (_listCts is null)
        {
            return;
        }

        try
        {
            _listCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _listCts.Dispose();
        _listCts = null;
    }

    private void ResetIconLoads()
    {
        try
        {
            _iconCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _iconCts.Dispose();
        _iconCts = new CancellationTokenSource();
    }

    private void CancelSearch()
    {
        if (_searchCts is null)
        {
            return;
        }

        try
        {
            _searchCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        _searchCts.Dispose();
        _searchCts = null;
        IsSearching = false;
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();

        if (IsThisPc)
        {
            Breadcrumbs.Add(new BreadcrumbViewModel(Loc.Get("Loc.ThisPc"), "ThisPC", isLast: true, path => Navigate(path)));
            return;
        }

        if (IsTagView)
        {
            Breadcrumbs.Add(new BreadcrumbViewModel(Loc.Get("Loc.Tags"), "ThisPC", isLast: false, path => Navigate(path)));
            Breadcrumbs.Add(new BreadcrumbViewModel(TagName, CurrentPath, isLast: true, path => Navigate(path)));
            return;
        }

        if (IsSearchView)
        {
            Breadcrumbs.Add(new BreadcrumbViewModel(Loc.Get("Loc.ThisPc"), "ThisPC", isLast: false, path => Navigate(path)));
            Breadcrumbs.Add(new BreadcrumbViewModel(Loc.Format("Loc.SearchPrefix", SearchQuery), CurrentPath, isLast: true, path => Navigate(path)));
            return;
        }

        Breadcrumbs.Add(new BreadcrumbViewModel(Loc.Get("Loc.ThisPc"), "ThisPC", isLast: false, path => Navigate(path)));

        var segments = CurrentPath.TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var accumulated = string.Empty;

        for (var i = 0; i < segments.Length; i++)
        {
            accumulated = i == 0 ? segments[i] + "\\" : Path.Combine(accumulated, segments[i]);
            var label = i == 0 ? segments[i] : segments[i];
            var target = accumulated;
            Breadcrumbs.Add(new BreadcrumbViewModel(label, target, i == segments.Length - 1, path => Navigate(path)));
        }
    }

    public void Navigate(string path, bool addToHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "ThisPC";
        }

        if (path.Equals("ThisPC", StringComparison.OrdinalIgnoreCase))
        {
            CurrentPath = "ThisPC";
            if (addToHistory && ActiveTab != null)
            {
                ActiveTab.History.Navigate("ThisPC");
            }

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            return;
        }

        if (TagPath.TryParse(path, out _))
        {
            CurrentPath = path;
            if (addToHistory && ActiveTab != null)
            {
                ActiveTab.History.Navigate(path);
            }

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            return;
        }

        if (SearchPath.TryParse(path, out var query))
        {
            CurrentPath = SearchPath.Create(query);
            if (addToHistory && ActiveTab != null)
            {
                ActiveTab.History.Navigate(CurrentPath);
            }

            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            return;
        }

        path = FileService.ExpandDriveRoot(path);

        if (!_fileService.PathExists(path))
        {
            StatusMessage?.Invoke(this, Loc.Format("Loc.PathMissing", path));
            return;
        }

        if (File.Exists(path))
        {
            _fileService.OpenWithDefaultApp(path);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        CurrentPath = fullPath;
        if (addToHistory && ActiveTab != null)
        {
            ActiveTab.History.Navigate(fullPath);
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    private void GoBack()
    {
        var path = ActiveTab?.History.GoBack();
        if (path != null)
        {
            Navigate(path, addToHistory: false);
        }
    }

    private void GoForward()
    {
        var path = ActiveTab?.History.GoForward();
        if (path != null)
        {
            Navigate(path, addToHistory: false);
        }
    }

    private void GoUp()
    {
        if (CurrentPath.Equals("ThisPC", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsTagView || IsSearchView)
        {
            Navigate("ThisPC");
            return;
        }

        var parent = Directory.GetParent(CurrentPath)?.FullName;
        Navigate(parent ?? "ThisPC");
    }

    private void GoToThisPc() => Navigate("ThisPC");

    private void OpenKnownFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (folder.Equals("ThisPC", StringComparison.OrdinalIgnoreCase))
        {
            Navigate("ThisPC");
            return;
        }

        var path = folder switch
        {
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => folder
        };

        if (!Directory.Exists(path))
        {
            StatusMessage?.Invoke(this, Loc.Format("Loc.PathMissing", path));
            return;
        }

        Navigate(path);
    }

    private void GoToAddress()
    {
        Navigate(AddressText.Trim());
        IsAddressEditing = false;
    }

    private void OpenInExplorer()
    {
        if (IsVirtualView)
        {
            return;
        }

        try
        {
            _fileService.OpenInExplorer(CurrentPath);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    private void OpenSelected()
    {
        if (SelectedItem != null)
        {
            Navigate(SelectedItem.FullPath);
        }
    }

    private void NewTab()
    {
        AddAndActivateTab(NavigationHistory.ThisPcToken);
        StatusMessage?.Invoke(this, Loc.Get("Loc.TabCreated"));
    }

    private void ActivateTab(PaneTabViewModel? tab)
    {
        if (tab != null && Tabs.Contains(tab))
        {
            ActiveTab = tab;
        }
    }

    private void DuplicateTab(PaneTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab == null)
        {
            return;
        }

        var duplicate = new PaneTabViewModel(tab.Path, _tags)
        {
            SortColumn = tab.SortColumn,
            SortDescending = tab.SortDescending,
            FoldersFirst = tab.FoldersFirst
        };
        var insertAt = Tabs.IndexOf(tab) + 1;
        Tabs.Insert(insertAt, duplicate);
        ActiveTab = duplicate;
        StatusMessage?.Invoke(this, Loc.Get("Loc.TabDuplicated"));
    }

    private void CloseTab(PaneTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab == null)
        {
            return;
        }

        if (Tabs.Count == 1)
        {
            // A pane always keeps one usable tab; closing the last one resets it.
            Navigate(NavigationHistory.ThisPcToken);
            return;
        }

        var closingIndex = Tabs.IndexOf(tab);
        var wasActive = ReferenceEquals(tab, ActiveTab);
        Tabs.Remove(tab);

        if (wasActive)
        {
            ActiveTab = Tabs[Math.Clamp(closingIndex, 0, Tabs.Count - 1)];
        }

        StatusMessage?.Invoke(this, Loc.Get("Loc.TabClosed"));
    }

    private void CloseOtherTabs(PaneTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab == null)
        {
            return;
        }

        foreach (var other in Tabs.Where(candidate => !ReferenceEquals(candidate, tab)).ToList())
        {
            Tabs.Remove(other);
        }

        ActiveTab = tab;
        StatusMessage?.Invoke(this, Loc.Get("Loc.OtherTabsClosed"));
    }

    private void CloseAllTabs()
    {
        Tabs.Clear();
        var remaining = new PaneTabViewModel(NavigationHistory.ThisPcToken, _tags);
        Tabs.Add(remaining);
        ActiveTab = remaining;
        StatusMessage?.Invoke(this, Loc.Get("Loc.AllTabsClosed"));
    }

    private bool CanOpenSelectedInNewTab() =>
        SelectedItem?.Kind is FileItemKind.Directory or FileItemKind.Drive;

    private void OpenSelectedInNewTab()
    {
        if (!CanOpenSelectedInNewTab() || SelectedItem == null)
        {
            return;
        }

        AddAndActivateTab(SelectedItem.FullPath);
        StatusMessage?.Invoke(this, Loc.Get("Loc.OpenedInNewTab"));
    }

    private void AddAndActivateTab(string path)
    {
        var tab = new PaneTabViewModel(path, _tags);
        if (ActiveTab != null)
        {
            tab.SortColumn = ActiveTab.SortColumn;
            tab.SortDescending = ActiveTab.SortDescending;
            tab.FoldersFirst = ActiveTab.FoldersFirst;
        }

        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public IReadOnlyList<string> GetSelectedPathsForDrag(string fallback)
    {
        var paths = GetSelectedPaths();
        if (paths.Count > 0 && paths.Exists(path => path.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
        {
            return paths;
        }

        return [fallback];
    }

    public void SetSelectedItems(IEnumerable<FileItemViewModel> items)
    {
        SelectedItems.Clear();
        SelectedItems.AddRange(items);
        CommandManager.InvalidateRequerySuggested();
    }

    private bool HasSelection() => SelectedItems.Count > 0 || SelectedItem != null;

    private List<string> GetSelectedPaths()
    {
        if (SelectedItems.Count > 0)
        {
            return SelectedItems.Select(item => item.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return SelectedItem == null ? [] : [SelectedItem.FullPath];
    }

    private void CopySelected()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        FileClipboard.SetFiles(paths, cut: false);
        StatusMessage?.Invoke(this, paths.Count == 1 ? Loc.Get("Loc.CopiedOne") : Loc.Format("Loc.CopiedMany", paths.Count));
    }

    private void CutSelected()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        FileClipboard.SetFiles(paths, cut: true);
        StatusMessage?.Invoke(this, paths.Count == 1 ? Loc.Get("Loc.CutOne") : Loc.Format("Loc.CutMany", paths.Count));
    }

    private async void Paste()
    {
        if (_isPasting)
        {
            return;
        }

        var (paths, cut) = FileClipboard.GetFiles();
        if (paths.Count == 0)
        {
            return;
        }

        var targetDir = ResolveWritableDirectory();
        _isPasting = true;
        CommandManager.InvalidateRequerySuggested();
        StatusMessage?.Invoke(this, cut ? Loc.Get("Loc.Moving") : Loc.Get("Loc.Copying"));

        try
        {
            await CopyProgressWindow.RunAsync(
                System.Windows.Application.Current?.MainWindow,
                _fileService,
                paths,
                targetDir,
                cut).ConfigureAwait(true);

            if (cut)
            {
                FileClipboard.ClearCutMark();
            }

            RefreshItems();
            StatusMessage?.Invoke(this, cut ? Loc.Get("Loc.MoveDone") : Loc.Get("Loc.PasteDone"));
        }
        catch (OperationCanceledException)
        {
            RefreshItems();
            StatusMessage?.Invoke(this, Loc.Get("Loc.Cancelled"));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
        finally
        {
            _isPasting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void DeleteSelected()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            _fileService.DeleteToRecycleBin(paths);
            RefreshItems();
            StatusMessage?.Invoke(this, Loc.Get("Loc.RecycleBin"));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    private void RenameSelected(object? parameter)
    {
        if (SelectedItem == null || parameter is not string newName || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        try
        {
            _fileService.Rename(SelectedItem.FullPath, newName.Trim());
            RefreshItems();
            StatusMessage?.Invoke(this, Loc.Get("Loc.Renamed"));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    private void NewFolder()
    {
        var targetDir = ResolveWritableDirectory();

        var name = Loc.Get("Loc.NewFolder");
        var candidate = name;
        var counter = 1;
        while (Directory.Exists(Path.Combine(targetDir, candidate)))
        {
            candidate = $"{name} ({counter++})";
        }

        try
        {
            _fileService.CreateDirectory(targetDir, candidate);
            if (CurrentPath.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                RefreshItems();
            }

            StatusMessage?.Invoke(this, Loc.Get("Loc.FolderCreated"));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Virtual locations (This PC, tag views) cannot receive files, so they fall
    /// back to the default folder.
    /// </summary>
    private string ResolveWritableDirectory() =>
        IsVirtualView
            ? _fileService.GetDefaultPath()
            : CurrentPath;

    public async void HandleDrop(string[] paths, bool move)
    {
        var targetDir = ResolveWritableDirectory();
        StatusMessage?.Invoke(this, move ? Loc.Get("Loc.Moving") : Loc.Get("Loc.Copying"));

        try
        {
            await CopyProgressWindow.RunAsync(
                System.Windows.Application.Current?.MainWindow,
                _fileService,
                paths,
                targetDir,
                move).ConfigureAwait(true);

            RefreshItems();
            StatusMessage?.Invoke(this, move ? Loc.Get("Loc.MoveDone") : Loc.Get("Loc.CopyDone"));
        }
        catch (OperationCanceledException)
        {
            RefreshItems();
            StatusMessage?.Invoke(this, Loc.Get("Loc.Cancelled"));
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    public string GetExportPath() => CurrentPath;
}
