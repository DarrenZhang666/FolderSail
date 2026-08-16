using FolderSail.Core.Models;
using FolderSail.Core.Navigation;
using FolderSail.Core.Services;
using FolderSail.Helpers;
using FolderSail.Mvvm;
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
        OpenSelectedInNewTabCommand = new RelayCommand(OpenSelectedInNewTab, CanOpenSelectedInNewTab);
        OpenKnownFolderCommand = new RelayCommand<string>(OpenKnownFolder);

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
            SelectedItem = null;
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (IsThisPc)
            {
                return "此电脑";
            }

            if (IsTagView)
            {
                return TagName;
            }

            if (IsSearchView)
            {
                return SearchQuery.Length > 16 ? "搜索" : $"搜索 {SearchQuery}";
            }

            var name = Path.GetFileName(CurrentPath.TrimEnd('\\'));
            return string.IsNullOrWhiteSpace(name) ? CurrentPath : name;
        }
    }

    public bool IsTagView => TagPath.TryParse(CurrentPath, out _);

    public bool IsSearchView => SearchPath.TryParse(CurrentPath, out _);

    public bool IsVirtualView => IsThisPc || IsTagView || IsSearchView;

    private string TagName =>
        TagPath.TryParse(CurrentPath, out var tagId) ? _tags?.GetTagName(tagId) ?? "标签" : string.Empty;

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
                    ? "正在搜索…"
                    : $"正在搜索「{SearchQuery}」…";
            }

            var folders = Items.Count(i => i.Kind != FileItemKind.File);
            var files = Items.Count - folders;
            return $"{folders} 个文件夹 · {files} 个文件";
        }
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

    public ObservableCollection<FileItemViewModel> Items { get; } = [];

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
    public ICommand OpenSelectedInNewTabCommand { get; }
    public ICommand OpenKnownFolderCommand { get; }

    public event EventHandler? ActiveChanged;
    public event EventHandler<string>? StatusMessage;
    public event EventHandler? InlineRenameStarted;

    public bool IsInlineRenaming => Items.Any(item => item.IsRenaming);

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
        Items.Clear();

        if (SearchPath.TryParse(CurrentPath, out var query))
        {
            BeginSearch(query);
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        try
        {
            var items = TagPath.TryParse(CurrentPath, out var tagId)
                ? _fileService.ListPaths(_tags?.GetTaggedPaths(tagId) ?? [])
                : _fileService.ListDirectory(CurrentPath);

            foreach (var item in items)
            {
                Items.Add(new FileItemViewModel(item));
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }

        OnPropertyChanged(nameof(ItemSummary));
    }

    private void BeginSearch(string query)
    {
        if (_search is null)
        {
            IsSearching = false;
            StatusMessage?.Invoke(this, "搜索服务不可用");
            return;
        }

        IsSearching = true;
        StatusMessage?.Invoke(this, $"正在搜索「{query}」…");
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

        Items.Clear();

        if (task.IsFaulted)
        {
            IsSearching = false;
            var message = task.Exception?.InnerException?.Message ?? "搜索失败";
            StatusMessage?.Invoke(this, message);
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        if (task.IsCanceled || task.Result is null)
        {
            IsSearching = false;
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        foreach (var item in task.Result)
        {
            Items.Add(new FileItemViewModel(item));
        }

        IsSearching = false;
        OnPropertyChanged(nameof(ItemSummary));
        StatusMessage?.Invoke(
            this,
            task.Result.Count == 0 ? $"未找到「{query}」" : $"找到 {task.Result.Count} 项");
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
            Breadcrumbs.Add(new BreadcrumbViewModel("此电脑", "ThisPC", isLast: true, path => Navigate(path)));
            return;
        }

        if (IsTagView)
        {
            Breadcrumbs.Add(new BreadcrumbViewModel("标签", "ThisPC", isLast: false, path => Navigate(path)));
            Breadcrumbs.Add(new BreadcrumbViewModel(TagName, CurrentPath, isLast: true, path => Navigate(path)));
            return;
        }

        if (IsSearchView)
        {
            Breadcrumbs.Add(new BreadcrumbViewModel("此电脑", "ThisPC", isLast: false, path => Navigate(path)));
            Breadcrumbs.Add(new BreadcrumbViewModel($"搜索 {SearchQuery}", CurrentPath, isLast: true, path => Navigate(path)));
            return;
        }

        Breadcrumbs.Add(new BreadcrumbViewModel("此电脑", "ThisPC", isLast: false, path => Navigate(path)));

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
            StatusMessage?.Invoke(this, $"路径不存在: {path}");
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
            StatusMessage?.Invoke(this, $"路径不存在: {path}");
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
        StatusMessage?.Invoke(this, "已新建标签页");
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

        var duplicate = new PaneTabViewModel(tab.Path, _tags);
        var insertAt = Tabs.IndexOf(tab) + 1;
        Tabs.Insert(insertAt, duplicate);
        ActiveTab = duplicate;
        StatusMessage?.Invoke(this, "已复制标签页");
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

        StatusMessage?.Invoke(this, "已关闭标签页");
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
        StatusMessage?.Invoke(this, "已关闭其他标签页");
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
        StatusMessage?.Invoke(this, "已在新标签页中打开");
    }

    private void AddAndActivateTab(string path)
    {
        var tab = new PaneTabViewModel(path, _tags);
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
        StatusMessage?.Invoke(this, paths.Count == 1 ? "已复制到剪贴板" : $"已复制 {paths.Count} 项");
    }

    private void CutSelected()
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        FileClipboard.SetFiles(paths, cut: true);
        StatusMessage?.Invoke(this, paths.Count == 1 ? "已剪切到剪贴板" : $"已剪切 {paths.Count} 项");
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
        StatusMessage?.Invoke(this, "正在粘贴…");

        try
        {
            await Task.Run(() =>
            {
                foreach (var source in paths)
                {
                    _fileService.Copy(source, targetDir, cut);
                }
            }).ConfigureAwait(true);

            if (cut)
            {
                FileClipboard.ClearCutMark();
            }

            RefreshItems();
            StatusMessage?.Invoke(this, "粘贴完成");
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
            StatusMessage?.Invoke(this, "已移到回收站");
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
            StatusMessage?.Invoke(this, "重命名完成");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    private void NewFolder()
    {
        var targetDir = ResolveWritableDirectory();

        var name = "新建文件夹";
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

            StatusMessage?.Invoke(this, "已创建文件夹");
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
        StatusMessage?.Invoke(this, move ? "正在移动…" : "正在复制…");

        try
        {
            await Task.Run(() =>
            {
                foreach (var source in paths)
                {
                    _fileService.Copy(source, targetDir, move);
                }
            }).ConfigureAwait(true);

            RefreshItems();
            StatusMessage?.Invoke(this, move ? "移动完成" : "复制完成");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    public string GetExportPath() => CurrentPath;
}
