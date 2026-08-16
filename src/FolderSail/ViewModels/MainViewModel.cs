using FolderSail.Core.Models;
using FolderSail.Core.Services;
using FolderSail.Helpers;
using FolderSail.Mvvm;
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

    public const double SidebarMinWidth = 156;
    public const double SidebarMaxWidth = 520;
    public const double SidebarDefaultWidth = 228;

    public MainViewModel()
        : this(new FileService(), new FavoriteStore(), new SettingsStore())
    {
    }

    public MainViewModel(IFileService fileService, IFavoriteStore favoriteStore, ISettingsStore settingsStore)
        : this(fileService, new SearchService(fileService), favoriteStore, settingsStore)
    {
    }

    public MainViewModel(
        IFileService fileService,
        IFileSearchService searchService,
        IFavoriteStore favoriteStore,
        ISettingsStore settingsStore)
    {
        _fileService = fileService;
        _searchService = searchService;
        _favoriteStore = favoriteStore;
        _settingsStore = settingsStore;
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
        LanguageManager.Changed += (_, _) => RefreshLocalization();
        _statusMessage = Loc.Get("Loc.Ready");
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

    public void SaveOnExit()
    {
        SaveSettings();
        PersistFavorites();
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

            var pane = new PaneViewModel(i, _fileService, path, this, _searchService);
            pane.StatusMessage += (_, message) => StatusMessage = message;
            pane.ViewStateChanged += (_, _) => SaveSettings();
            if (i < _paneSortColumns.Count)
            {
                var column = (FileSortColumn)Math.Clamp(_paneSortColumns[i], 0, 3);
                var descending = i < _paneSortDescending.Count && _paneSortDescending[i];
                var foldersFirst = i >= _paneFoldersFirst.Count || _paneFoldersFirst[i];
                pane.ApplySort(column, descending, foldersFirst);
            }

            if (i < _paneColumnWidths.Count)
            {
                pane.ApplyColumnWidths(_paneColumnWidths[i]);
            }

            _allPanes.Add(pane);
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
            PaneColumnWidths = _allPanes.Select(p => p.GetColumnWidths().ToList()).ToList()
        });
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
