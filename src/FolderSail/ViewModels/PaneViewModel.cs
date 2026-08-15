using FolderSail.Core.Models;
using FolderSail.Core.Navigation;
using FolderSail.Core.Services;
using FolderSail.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class PaneViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly ITagLookup? _tags;
    private readonly IFileSearchService? _search;
    private readonly List<string> _clipboardPaths = [];
    private CancellationTokenSource? _searchCts;
    private bool _clipboardIsCut;
    private bool _switchingTab;
    private string _currentPath = string.Empty;
    private string _addressText = string.Empty;
    private bool _isActive;
    private bool _isAddressEditing;
    private FileItemViewModel? _selectedItem;
    private PaneTabViewModel? _activeTab;

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
        CopySelectedCommand = new RelayCommand(CopySelected, () => SelectedItem != null);
        CutSelectedCommand = new RelayCommand(CutSelected, () => SelectedItem != null);
        PasteCommand = new RelayCommand(Paste);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => SelectedItem != null);
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

    public string ItemSummary
    {
        get
        {
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

    public event EventHandler? ActiveChanged;
    public event EventHandler<string>? StatusMessage;

    public void ReportStatus(string message) => StatusMessage?.Invoke(this, message);

    public void RefreshItems()
    {
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
            StatusMessage?.Invoke(this, "搜索服务不可用");
            return;
        }

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
            return;
        }

        if (!SearchPath.TryParse(CurrentPath, out var current) ||
            !string.Equals(current, query, StringComparison.Ordinal))
        {
            return;
        }

        Items.Clear();

        if (task.IsFaulted)
        {
            var message = task.Exception?.InnerException?.Message ?? "搜索失败";
            StatusMessage?.Invoke(this, message);
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        if (task.IsCanceled || task.Result is null)
        {
            OnPropertyChanged(nameof(ItemSummary));
            return;
        }

        foreach (var item in task.Result)
        {
            Items.Add(new FileItemViewModel(item));
        }

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

    private void CopySelected()
    {
        if (SelectedItem == null)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.Add(SelectedItem.FullPath);
        _clipboardIsCut = false;
        StatusMessage?.Invoke(this, "已复制到剪贴板");
    }

    private void CutSelected()
    {
        if (SelectedItem == null)
        {
            return;
        }

        _clipboardPaths.Clear();
        _clipboardPaths.Add(SelectedItem.FullPath);
        _clipboardIsCut = true;
        StatusMessage?.Invoke(this, "已剪切到剪贴板");
    }

    private void Paste()
    {
        if (_clipboardPaths.Count == 0)
        {
            return;
        }

        var targetDir = ResolveWritableDirectory();

        try
        {
            foreach (var source in _clipboardPaths)
            {
                _fileService.Copy(source, targetDir, _clipboardIsCut);
            }

            if (_clipboardIsCut)
            {
                _clipboardPaths.Clear();
                _clipboardIsCut = false;
            }

            RefreshItems();
            StatusMessage?.Invoke(this, "粘贴完成");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(this, ex.Message);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedItem == null)
        {
            return;
        }

        try
        {
            _fileService.DeleteToRecycleBin([SelectedItem.FullPath]);
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

    public void HandleDrop(string[] paths, bool move)
    {
        var targetDir = ResolveWritableDirectory();

        try
        {
            foreach (var source in paths)
            {
                _fileService.Copy(source, targetDir, move);
            }

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
