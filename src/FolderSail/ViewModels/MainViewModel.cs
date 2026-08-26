using FolderSail.Core.Models;
using FolderSail.Core.Services;
using FolderSail.Helpers;
using FolderSail.Mvvm;
using FolderSail.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class MainViewModel : ObservableObject, ITagLookup
{
    private readonly IFileService _fileService;
    private readonly IFileSearchService _searchService;
    private readonly IFavoriteStore _favoriteStore;
    private readonly ISettingsStore _settingsStore;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRecentWorkspaceStore _recentStore;
    private readonly FavoritesDocument _favoritesDocument;
    private readonly List<PaneViewModel> _allPanes = [];
    private LayoutMode _layoutMode = LayoutMode.Dual;
    private int _activePaneIndex;
    private string _statusMessage = string.Empty;
    private double _sidebarWidth = 228;
    private string _searchText = string.Empty;
    private int _searchEpoch;
    private bool _isDarkTheme;
    private bool _expandTagChildrenOnStartup;
    private readonly List<int> _paneSortColumns;
    private readonly List<bool> _paneSortDescending;
    private readonly List<bool> _paneFoldersFirst;
    private readonly List<List<double>> _paneColumnWidths;
    private readonly Dictionary<string, List<PaneBounds>> _layoutPaneBounds;
    private string? _currentWorkspacePath;
    private string _workspaceName = string.Empty;

    public const double SidebarMinWidth = 156;
    public const double SidebarMaxWidth = 520;
    public const double SidebarDefaultWidth = 228;

    public MainViewModel()
        : this(new FileService(), new FavoriteStore(), new SettingsStore())
    {
    }

    public MainViewModel(IFileService fileService, IFavoriteStore favoriteStore, ISettingsStore settingsStore)
        : this(fileService, new SearchService(fileService), favoriteStore, settingsStore, new WorkspaceStore(), new RecentWorkspaceStore())
    {
    }

    public MainViewModel(
        IFileService fileService,
        IFileSearchService searchService,
        IFavoriteStore favoriteStore,
        ISettingsStore settingsStore)
        : this(fileService, searchService, favoriteStore, settingsStore, new WorkspaceStore(), new RecentWorkspaceStore())
    {
    }

    public MainViewModel(
        IFileService fileService,
        IFileSearchService searchService,
        IFavoriteStore favoriteStore,
        ISettingsStore settingsStore,
        IWorkspaceStore workspaceStore,
        IRecentWorkspaceStore recentStore)
    {
        _fileService = fileService;
        _searchService = searchService;
        _favoriteStore = favoriteStore;
        _settingsStore = settingsStore;
        _workspaceStore = workspaceStore;
        _recentStore = recentStore;
        _favoritesDocument = _favoriteStore.Load();
        var settings = _settingsStore.Load();

        var tagActions = new TagActions(
            OpenTag,
            OpenTaggedItem,
            RemoveTaggedItem,
            RenameTag,
            ClearTag,
            AddCurrentPathToTag);
        foreach (var category in _favoritesDocument.Categories)
        {
            Tags.Add(new TagViewModel(category, tagActions, PersistFavorites));
        }

        _layoutMode = settings.LayoutMode;
        _sidebarWidth = ClampSidebarWidth(settings.SidebarWidth);
        _isDarkTheme = settings.IsDarkTheme;
        _expandTagChildrenOnStartup = settings.ExpandTagChildrenOnStartup;
        _paneSortColumns = settings.PaneSortColumns;
        _paneSortDescending = settings.PaneSortDescending;
        _paneFoldersFirst = settings.PaneFoldersFirst;
        _paneColumnWidths = settings.PaneColumnWidths;
        _layoutPaneBounds = settings.LayoutPaneBounds ?? [];
        ApplyTagChildExpansion(_expandTagChildrenOnStartup);
        ApplyLayout(settings.ActivePaneIndex, settings.PanePaths);
        UpdateLayoutPresetSelection();
        LoadDrives();

        SetLayoutCommand = new RelayCommand<LayoutMode>(SetLayout);
        ActivatePaneCommand = new RelayCommand<int>(ActivatePane);
        OpenDriveCommand = new RelayCommand<DriveItemViewModel>(OpenDrive);
        OpenThisPcCommand = new RelayCommand(() => ActivePane?.Navigate("ThisPC"));
        OpenKnownFolderCommand = new RelayCommand<string>(OpenKnownFolder);
        SearchCommand = new RelayCommand(SubmitSearch, () => HasSearchText);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => HasSearchText);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        OpenWorkspaceCommand = new RelayCommand(OpenWorkspace);
        SaveWorkspaceCommand = new RelayCommand(SaveWorkspace);
        SaveWorkspaceAsCommand = new RelayCommand(SaveWorkspaceAs);
        OpenRecentWorkspaceCommand = new RelayCommand<string?>(OpenRecentWorkspace);
        LanguageManager.Changed += (_, _) => RefreshLocalization();
        _statusMessage = Loc.Get("Loc.Ready");
        UpdateWorkspaceTitle();
        _recentStore.SeedIfEmpty(_workspaceStore.List());
        RefreshRecentWorkspaces();
    }

    public ObservableCollection<PaneViewModel> Panes { get; } = [];
    public ObservableCollection<TagViewModel> Tags { get; } = [];
    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ObservableCollection<LayoutPresetViewModel> LayoutPresets { get; } =
    [
        new(LayoutMode.Single, "Loc.Layout.Single"),
        new(LayoutMode.Dual, "Loc.Layout.Dual"),
        new(LayoutMode.DualRows, "Loc.Layout.DualRows"),
        new(LayoutMode.TripleColumns, "Loc.Layout.TripleColumns"),
        new(LayoutMode.TripleRows, "Loc.Layout.TripleRows"),
        new(LayoutMode.TripleMainLeft, "Loc.Layout.TripleMainLeft"),
        new(LayoutMode.TripleMainTop, "Loc.Layout.TripleMainTop"),
        new(LayoutMode.Quad, "Loc.Layout.Quad"),
        new(LayoutMode.QuadMainLeft, "Loc.Layout.QuadMainLeft"),
        new(LayoutMode.QuadColumns, "Loc.Layout.QuadColumns"),
        new(LayoutMode.Hex, "Loc.Layout.Hex")
    ];

    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        set
        {
            if (!SetProperty(ref _layoutMode, value))
            {
                return;
            }

            UpdateLayoutPresetSelection();
            ApplyLayout(ActivePaneIndex, savedPaths: null);
            SaveSettings();
            StatusMessage = Loc.Format("Loc.LayoutSwitched", LayoutPresets.First(p => p.Mode == value).Name);
        }
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        set
        {
            var changed = SetProperty(ref _activePaneIndex, value);
            for (var i = 0; i < _allPanes.Count; i++)
            {
                _allPanes[i].IsActive = i == value && i < Panes.Count;
            }

            if (changed)
            {
                SaveSettings();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSearchText));
            ScheduleLiveSearch();
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (!SetProperty(ref _isDarkTheme, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ThemeToggleTip));
            FolderSail.Helpers.ThemeManager.Apply(value);
            SaveSettings();
        }
    }

    public string ThemeToggleTip =>
        Loc.Get(IsDarkTheme ? "Loc.ThemeToLight" : "Loc.ThemeToDark");

    public string LanguageToggleTip =>
        Loc.Get(LanguageManager.IsEnglish ? "Loc.LanguageToChinese" : "Loc.LanguageToEnglish");

    public bool ExpandTagChildrenOnStartup
    {
        get => _expandTagChildrenOnStartup;
        set
        {
            if (!SetProperty(ref _expandTagChildrenOnStartup, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ExpandTagChildrenOnStartupTip));
            ApplyTagChildExpansion(value);
            SaveSettings();
        }
    }

    public string ExpandTagChildrenOnStartupTip =>
        Loc.Get(ExpandTagChildrenOnStartup ? "Loc.ExpandTagsOn" : "Loc.ExpandTagsOff");

    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            var clamped = ClampSidebarWidth(value);
            if (SetProperty(ref _sidebarWidth, clamped))
            {
                SaveSettings();
            }
        }
    }

    public PaneViewModel? ActivePane =>
        ActivePaneIndex >= 0 && ActivePaneIndex < Panes.Count ? Panes[ActivePaneIndex] : null;

    public ICommand SetLayoutCommand { get; }
    public ICommand ActivatePaneCommand { get; }
    public ICommand OpenDriveCommand { get; }
    public ICommand OpenThisPcCommand { get; }
    public ICommand OpenKnownFolderCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand OpenWorkspaceCommand { get; }
    public ICommand SaveWorkspaceCommand { get; }
    public ICommand SaveWorkspaceAsCommand { get; }
    public ICommand OpenRecentWorkspaceCommand { get; }

    public ObservableCollection<WorkspaceEntry> RecentWorkspaces { get; } = [];

    public string WorkspaceTitle =>
        string.IsNullOrWhiteSpace(_workspaceName)
            ? "FolderSail"
            : $"FolderSail - {_workspaceName}";

    public string? CurrentWorkspacePath => _currentWorkspacePath;

    public IReadOnlyList<WorkspaceEntry> ListWorkspaces() => _workspaceStore.List();

    public void LoadWorkspace(string filePath, WindowGeometry? windowGeometry = null, bool applyGeometry = true)
    {
        var document = _workspaceStore.Load(filePath);
        ApplyWorkspaceDocument(
            document,
            filePath,
            applyGeometry ? windowGeometry ?? document.Window : null);
        RecordWorkspaceAccess(filePath, _workspaceName);
        StatusMessage = Loc.Format("Loc.WorkspaceOpened", _workspaceName);
    }

    public void ResetToDefaultSession()
    {
        var settings = _settingsStore.Load();
        _currentWorkspacePath = null;
        SetWorkspaceName(string.Empty);
        ReplaceLayoutPaneBounds(settings.LayoutPaneBounds);
        SidebarWidth = ClampSidebarWidth(settings.SidebarWidth);
        _layoutMode = settings.LayoutMode;
        OnPropertyChanged(nameof(LayoutMode));
        UpdateLayoutPresetSelection();
        RebuildPanes(settings.ActivePaneIndex, settings.PanePaths, null);
        StatusMessage = Loc.Get("Loc.WorkspaceNewWindow");
    }

    public bool SaveWorkspace(Window? owner, WindowGeometry? geometry)
    {
        if (string.IsNullOrWhiteSpace(_currentWorkspacePath))
        {
            return SaveWorkspaceAs(owner, geometry);
        }

        return SaveWorkspaceToPath(_currentWorkspacePath, WorkspaceNameFromPath(_currentWorkspacePath, _workspaceName), geometry);
    }

    public bool SaveWorkspaceAs(Window? owner, WindowGeometry? geometry)
    {
        var defaultName = string.IsNullOrWhiteSpace(_workspaceName)
            ? "foldersail_workspace"
            : _workspaceName;
        var name = PromptForText(Loc.Get("Loc.SaveWorkspaceTitle"), Loc.Get("Loc.WorkspaceName"), defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        var targetPath = _workspaceStore.ResolvePath(trimmed);
        if (_workspaceStore.Exists(trimmed) &&
            (_currentWorkspacePath == null ||
             !string.Equals(targetPath, _currentWorkspacePath, StringComparison.OrdinalIgnoreCase)))
        {
            var overwrite = MessageBox.Show(
                Loc.Format("Loc.WorkspaceOverwrite", trimmed),
                "FolderSail",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        return SaveWorkspaceToPath(targetPath, trimmed, geometry);
    }

    public void SaveOnExit(WindowGeometry? geometry)
    {
        if (!string.IsNullOrWhiteSpace(_currentWorkspacePath))
        {
            try
            {
                SaveWorkspaceToPath(_currentWorkspacePath, _workspaceName, geometry, notify: false);
            }
            catch
            {
                // Best effort on exit.
            }
        }

        SaveSettings();
        PersistFavorites();
    }

    public void SubmitSearch()
    {
        Interlocked.Increment(ref _searchEpoch);
        var query = SearchText.Trim();
        if (query.Length == 0 || ActivePane is null)
        {
            return;
        }

        ActivePane.Navigate(SearchPath.Create(query));
    }

    public void ClearSearch()
    {
        Interlocked.Increment(ref _searchEpoch);
        SearchText = string.Empty;
    }

    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    private void ToggleLanguage()
    {
        LanguageManager.Apply(LanguageManager.IsEnglish ? LanguageManager.Chinese : LanguageManager.English);
        SaveSettings();
    }

    private void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ThemeToggleTip));
        OnPropertyChanged(nameof(LanguageToggleTip));
        OnPropertyChanged(nameof(ExpandTagChildrenOnStartupTip));
        foreach (var preset in LayoutPresets)
        {
            preset.RefreshName();
        }

        foreach (var drive in Drives)
        {
            drive.RefreshLocalization();
        }

        foreach (var tag in Tags)
        {
            tag.RefreshLocalization();
        }

        foreach (var pane in _allPanes)
        {
            pane.RefreshLocalization();
        }
    }

    private async void ScheduleLiveSearch()
    {
        var epoch = Interlocked.Increment(ref _searchEpoch);
        var query = SearchText.Trim();
        if (query.Length < 2)
        {
            return;
        }

        try
        {
            await Task.Delay(380);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (epoch != _searchEpoch)
        {
            return;
        }

        SubmitSearch();
    }

    private void SetLayout(LayoutMode mode) => LayoutMode = mode;

    private void ActivatePane(int index)
    {
        if (index >= 0 && index < Panes.Count)
        {
            ActivePaneIndex = index;
        }
    }

    private void LoadDrives()
    {
        Drives.Clear();
        try
        {
            foreach (var drive in _fileService.ListDriveDetails())
            {
                Drives.Add(new DriveItemViewModel(drive));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void OpenDrive(DriveItemViewModel? drive)
    {
        if (drive != null)
        {
            ActivePane?.Navigate(drive.Path);
        }
    }

    private void OpenKnownFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
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
            StatusMessage = Loc.Format("Loc.PathMissing", path);
            return;
        }

        ActivePane?.Navigate(path);
    }

    private void OpenTag(TagViewModel tag)
    {
        if (ActivePane == null)
        {
            return;
        }

        ActivePane.Navigate(tag.ViewPath);
        UpdateTagSelection(tag);
        StatusMessage = tag.HasItems
            ? Loc.Format("Loc.TagHasItems", tag.Name, tag.ItemCount)
            : Loc.Format("Loc.TagEmptyHint", tag.Name);
    }

    private void OpenTaggedItem(TagViewModel tag, TaggedItemViewModel item)
    {
        if (!_fileService.PathExists(item.Path))
        {
            StatusMessage = Loc.Format("Loc.PathMissing", item.Path);
            return;
        }

        ActivePane?.Navigate(item.Path);
        UpdateTagSelection(tag);
        StatusMessage = Loc.Format("Loc.OpenedPath", item.Path);
    }

    private void RemoveTaggedItem(TagViewModel tag, TaggedItemViewModel item)
    {
        tag.RemoveItem(item);
        PersistFavorites();
        RefreshTagViews();
        StatusMessage = Loc.Format("Loc.RemovedFromTag", tag.Name);
    }

    private void RenameTag(TagViewModel tag)
    {
        var newName = PromptForText(Loc.Get("Loc.RenameTag"), Loc.Get("Loc.TagName"), tag.Name);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        tag.Name = newName.Trim();
        PersistFavorites();
        RefreshTagViews();
    }

    private void ClearTag(TagViewModel tag)
    {
        if (!tag.HasItems)
        {
            return;
        }

        var result = MessageBox.Show(
            Loc.Format("Loc.ConfirmClearTag", tag.Name, tag.ItemCount),
            "FolderSail",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        tag.Clear();
        PersistFavorites();
        RefreshTagViews();
        StatusMessage = Loc.Format("Loc.ClearedTag", tag.Name);
    }

    private void AddCurrentPathToTag(TagViewModel tag)
    {
        if (ActivePane == null)
        {
            return;
        }

        var path = ActivePane.CurrentPath;
        if (ActivePane.IsVirtualView)
        {
            StatusMessage = Loc.Get("Loc.CannotTagHere");
            return;
        }

        AddDroppedFolders(tag, [path]);
    }

    public void AddDroppedFolders(TagViewModel tag, IEnumerable<string> paths)
    {
        var added = 0;
        var ignored = 0;

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (tag.TryAddFolder(path))
            {
                added++;
            }
            else
            {
                ignored++;
            }
        }

        if (added > 0)
        {
            PersistFavorites();
            RefreshTagViews();
            StatusMessage = Loc.Format("Loc.TaggedFolders", added, tag.Name);
        }
        else if (ignored > 0)
        {
            StatusMessage = Loc.Get("Loc.TagNotAdded");
        }
    }

    /// <summary>Removes a folder from a tag, used by the tag view's context menu.</summary>
    public void RemoveTaggedPath(string path)
    {
        var removed = Tags.Count(tag => tag.RemovePath(path));
        if (removed == 0)
        {
            return;
        }

        PersistFavorites();
        RefreshTagViews();
        StatusMessage = Loc.Get("Loc.TagRemoved");
    }

    /// <summary>Any pane currently showing a tag needs to re-read the tag contents.</summary>
    private void RefreshTagViews()
    {
        foreach (var pane in _allPanes.Where(pane => pane.IsTagView))
        {
            pane.RefreshItems();
        }
    }

    private void UpdateTagSelection(TagViewModel? selected)
    {
        foreach (var tag in Tags)
        {
            tag.IsSelected = ReferenceEquals(tag, selected);
        }
    }

    public IReadOnlyList<string> GetTaggedPaths(Guid tagId) =>
        Tags.FirstOrDefault(tag => tag.Id == tagId)?.Paths ?? [];

    public string? GetTagName(Guid tagId) =>
        Tags.FirstOrDefault(tag => tag.Id == tagId)?.Name;

    private void OpenWorkspace()
    {
        var workspaces = _workspaceStore.List();
        var picker = new WorkspacePickerWindow(workspaces);
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
        if (owner is { IsVisible: true })
        {
            picker.Owner = owner;
        }

        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedPath))
        {
            return;
        }

        LoadWorkspace(picker.SelectedPath, applyGeometry: false);
    }

    private void OpenRecentWorkspace(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        LoadWorkspace(filePath, applyGeometry: false);
    }

    public void RefreshRecentWorkspaces()
    {
        RecentWorkspaces.Clear();
        foreach (var entry in _recentStore.List())
        {
            RecentWorkspaces.Add(entry);
        }
    }

    private void RecordWorkspaceAccess(string filePath, string name)
    {
        _recentStore.RecordOpened(filePath, name);
        RefreshRecentWorkspaces();
    }

    private void SaveWorkspace()
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
        if (!SaveWorkspace(owner, CaptureWindowGeometry(owner)))
        {
            return;
        }
    }

    private void SaveWorkspaceAs()
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
        SaveWorkspaceAs(owner, CaptureWindowGeometry(owner));
    }

    private bool SaveWorkspaceToPath(string filePath, string name, WindowGeometry? geometry, bool notify = true)
    {
        var document = CaptureWorkspace(name, geometry);
        _workspaceStore.Save(filePath, document);
        _currentWorkspacePath = filePath;
        SetWorkspaceName(name);
        if (notify)
        {
            StatusMessage = Loc.Format("Loc.WorkspaceSaved", name);
        }

        RecordWorkspaceAccess(filePath, name);
        return true;
    }

    private WorkspaceDocument CaptureWorkspace(string name, WindowGeometry? geometry)
    {
        return new WorkspaceDocument
        {
            Name = name,
            LayoutMode = LayoutMode,
            ActivePaneIndex = ActivePaneIndex,
            SidebarWidth = SidebarWidth,
            LayoutPaneBounds = _layoutPaneBounds.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(bounds => bounds.Clone()).ToList()),
            Panes = _allPanes.Select(pane => pane.CaptureSessionState()).ToList(),
            Window = geometry
        };
    }

    private void ApplyWorkspaceDocument(WorkspaceDocument document, string filePath, WindowGeometry? windowGeometry)
    {
        _currentWorkspacePath = filePath;
        SetWorkspaceName(string.IsNullOrWhiteSpace(document.Name)
            ? WorkspaceNameFromPath(filePath, string.Empty)
            : document.Name);
        ReplaceLayoutPaneBounds(document.LayoutPaneBounds);
        SidebarWidth = ClampSidebarWidth(document.SidebarWidth);
        _layoutMode = document.LayoutMode;
        OnPropertyChanged(nameof(LayoutMode));
        UpdateLayoutPresetSelection();

        var panePaths = document.Panes
            .Select(pane => pane.Tabs.ElementAtOrDefault(Math.Clamp(pane.ActiveTabIndex, 0, Math.Max(0, pane.Tabs.Count - 1)))?.Path
                ?? pane.Tabs.FirstOrDefault()?.Path
                ?? "ThisPC")
            .ToList();

        RebuildPanes(document.ActivePaneIndex, panePaths, document.Panes);

        if (windowGeometry != null)
        {
            ApplyWindowGeometryRequested?.Invoke(this, windowGeometry);
        }
    }

    public event EventHandler<WindowGeometry>? ApplyWindowGeometryRequested;

    private void RebuildPanes(int activeIndex, IList<string>? savedPaths, IList<PaneSessionState>? paneStates)
    {
        _allPanes.Clear();
        Panes.Clear();

        var visibleCount = LayoutMode.GetDefinition().PaneCount;
        var poolTarget = Math.Max(visibleCount, Math.Max(savedPaths?.Count ?? 0, paneStates?.Count ?? 0));

        for (var i = 0; i < poolTarget; i++)
        {
            string path;
            if (savedPaths != null && i < savedPaths.Count && !string.IsNullOrWhiteSpace(savedPaths[i]))
            {
                path = savedPaths[i];
            }
            else if (paneStates != null && i < paneStates.Count && paneStates[i].Tabs.Count > 0)
            {
                path = paneStates[i].Tabs[Math.Clamp(paneStates[i].ActiveTabIndex, 0, paneStates[i].Tabs.Count - 1)].Path;
            }
            else
            {
                path = i == 0 ? _fileService.GetDefaultPath() : "ThisPC";
            }

            var pane = CreatePane(i, path);
            if (paneStates != null && i < paneStates.Count)
            {
                pane.ApplySessionState(paneStates[i]);
            }

            _allPanes.Add(pane);
        }

        for (var i = 0; i < visibleCount; i++)
        {
            Panes.Add(_allPanes[i]);
        }

        ActivePaneIndex = Math.Clamp(activeIndex, 0, Panes.Count - 1);
        for (var i = 0; i < _allPanes.Count; i++)
        {
            _allPanes[i].IsActive = i == ActivePaneIndex && i < Panes.Count;
        }
    }

    private PaneViewModel CreatePane(int index, string path)
    {
        var pane = new PaneViewModel(index, _fileService, path, this, _searchService);
        pane.StatusMessage += (_, message) => StatusMessage = message;
        pane.ViewStateChanged += (_, _) =>
        {
            SaveSettings();
            if (!string.IsNullOrWhiteSpace(_currentWorkspacePath))
            {
                // Keep named workspaces fresh while editing.
            }
        };

        if (index < _paneSortColumns.Count)
        {
            var column = (FileSortColumn)Math.Clamp(_paneSortColumns[index], 0, 3);
            var descending = index < _paneSortDescending.Count && _paneSortDescending[index];
            var foldersFirst = index >= _paneFoldersFirst.Count || _paneFoldersFirst[index];
            pane.ApplySort(column, descending, foldersFirst);
        }

        if (index < _paneColumnWidths.Count)
        {
            pane.ApplyColumnWidths(_paneColumnWidths[index]);
        }

        return pane;
    }

    private void UpdateWorkspaceTitle() =>
        OnPropertyChanged(nameof(WorkspaceTitle));

    private void SetWorkspaceName(string name)
    {
        _workspaceName = name;
        UpdateWorkspaceTitle();
    }

    private static string WorkspaceNameFromPath(string filePath, string fallback) =>
        string.IsNullOrWhiteSpace(fallback)
            ? Path.GetFileNameWithoutExtension(filePath)
            : fallback;

    public static WindowGeometry? CaptureWindowGeometry(Window? window)
    {
        if (window == null)
        {
            return null;
        }

        return new WindowGeometry
        {
            Left = window.RestoreBounds.Left,
            Top = window.RestoreBounds.Top,
            Width = window.RestoreBounds.Width,
            Height = window.RestoreBounds.Height,
            IsMaximized = window.WindowState == WindowState.Maximized
        };
    }

    public static void ApplyWindowGeometry(Window window, WindowGeometry geometry)
    {
        if (geometry.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        window.WindowState = WindowState.Normal;
        if (geometry.Width >= window.MinWidth && geometry.Height >= window.MinHeight)
        {
            window.Width = geometry.Width;
            window.Height = geometry.Height;
        }

        window.Left = geometry.Left;
        window.Top = geometry.Top;
    }

    private void ApplyLayout(int activeIndex, IList<string>? savedPaths)
    {
        var count = LayoutMode.GetDefinition().PaneCount;
        var defaultPath = _fileService.GetDefaultPath();
        var poolTarget = Math.Max(count, savedPaths?.Count ?? 0);

        while (_allPanes.Count < poolTarget)
        {
            var i = _allPanes.Count;
            string path;

            if (savedPaths != null && i < savedPaths.Count && !string.IsNullOrWhiteSpace(savedPaths[i]))
            {
                path = savedPaths[i];
            }
            else
            {
                path = i == 0 ? defaultPath : "ThisPC";
            }

            _allPanes.Add(CreatePane(i, path));
        }

        Panes.Clear();
        for (var i = 0; i < count; i++)
        {
            Panes.Add(_allPanes[i]);
        }

        ActivePaneIndex = Math.Clamp(activeIndex, 0, Panes.Count - 1);
        for (var i = 0; i < _allPanes.Count; i++)
        {
            _allPanes[i].IsActive = i == ActivePaneIndex && i < Panes.Count;
        }
    }

    private void UpdateLayoutPresetSelection()
    {
        foreach (var preset in LayoutPresets)
        {
            preset.IsSelected = preset.Mode == LayoutMode;
        }
    }

    private void PersistFavorites() => _favoriteStore.Save(_favoritesDocument);

    private void SaveSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            LayoutMode = LayoutMode,
            ActivePaneIndex = ActivePaneIndex,
            PanePaths = _allPanes.Select(p => p.GetExportPath()).ToList(),
            SidebarWidth = SidebarWidth,
            IsDarkTheme = IsDarkTheme,
            Language = LanguageManager.Current,
            ExpandTagChildrenOnStartup = ExpandTagChildrenOnStartup,
            PaneSortColumns = _allPanes.Select(p => (int)p.SortColumn).ToList(),
            PaneSortDescending = _allPanes.Select(p => p.SortDescending).ToList(),
            PaneFoldersFirst = _allPanes.Select(p => p.FoldersFirst).ToList(),
            PaneColumnWidths = _allPanes.Select(p => p.GetColumnWidths().ToList()).ToList(),
            LayoutPaneBounds = _layoutPaneBounds
        });
    }

    public IReadOnlyList<PaneBounds>? GetLayoutPaneBounds(LayoutMode mode) =>
        _layoutPaneBounds.TryGetValue(LayoutKey(mode), out var bounds) ? bounds : null;

    public void SaveLayoutPaneBounds(LayoutMode mode, IReadOnlyList<PaneBounds> bounds)
    {
        _layoutPaneBounds[LayoutKey(mode)] = bounds.Select(item => item.Clone()).ToList();
        SaveSettings();
    }

    private static string LayoutKey(LayoutMode mode) => ((int)mode).ToString();

    private void ReplaceLayoutPaneBounds(Dictionary<string, List<PaneBounds>>? source)
    {
        _layoutPaneBounds.Clear();
        if (source == null)
        {
            return;
        }

        foreach (var pair in source)
        {
            _layoutPaneBounds[pair.Key] = pair.Value.Select(bounds => bounds.Clone()).ToList();
        }
    }

    private void ApplyTagChildExpansion(bool expanded)
    {
        foreach (var tag in Tags)
        {
            tag.IsExpanded = expanded && tag.HasItems;
        }
    }

    private static double ClampSidebarWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return SidebarDefaultWidth;
        }

        return Math.Clamp(width, SidebarMinWidth, SidebarMaxWidth);
    }

    private static string? PromptForText(string title, string label, string defaultValue)
    {
        var window = new Views.InputDialogWindow(title, label, defaultValue);
        if (Application.Current.MainWindow is Window owner && owner.IsVisible)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.InputText : null;
    }
}
