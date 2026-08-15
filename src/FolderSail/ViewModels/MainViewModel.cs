using FolderSail.Core.Models;
using FolderSail.Core.Services;
using FolderSail.Mvvm;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class MainViewModel : ObservableObject, ITagLookup
{
    private readonly IFileService _fileService;
    private readonly IFavoriteStore _favoriteStore;
    private readonly ISettingsStore _settingsStore;
    private readonly FavoritesDocument _favoritesDocument;
    private readonly List<PaneViewModel> _allPanes = [];
    private LayoutMode _layoutMode = LayoutMode.Dual;
    private int _activePaneIndex;
    private string _statusMessage = "就绪";
    private double _sidebarWidth = 228;

    public const double SidebarMinWidth = 156;
    public const double SidebarMaxWidth = 520;
    public const double SidebarDefaultWidth = 228;

    public MainViewModel()
        : this(new FileService(), new FavoriteStore(), new SettingsStore())
    {
    }

    public MainViewModel(IFileService fileService, IFavoriteStore favoriteStore, ISettingsStore settingsStore)
    {
        _fileService = fileService;
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
        ApplyLayout(settings.ActivePaneIndex, settings.PanePaths);
        UpdateLayoutPresetSelection();
        LoadDrives();

        SetLayoutCommand = new RelayCommand<LayoutMode>(SetLayout);
        ActivatePaneCommand = new RelayCommand<int>(ActivatePane);
        OpenDriveCommand = new RelayCommand<DriveItemViewModel>(OpenDrive);
        OpenThisPcCommand = new RelayCommand(() => ActivePane?.Navigate("ThisPC"));
        OpenKnownFolderCommand = new RelayCommand<string>(OpenKnownFolder);
    }

    public ObservableCollection<PaneViewModel> Panes { get; } = [];
    public ObservableCollection<TagViewModel> Tags { get; } = [];
    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ObservableCollection<LayoutPresetViewModel> LayoutPresets { get; } =
    [
        new(LayoutMode.Single, "单窗格"),
        new(LayoutMode.Dual, "左右双栏"),
        new(LayoutMode.DualRows, "上下双栏"),
        new(LayoutMode.TripleColumns, "三栏"),
        new(LayoutMode.TripleRows, "三行"),
        new(LayoutMode.TripleMainLeft, "左主右双"),
        new(LayoutMode.TripleMainTop, "上主下双"),
        new(LayoutMode.Quad, "四宫格"),
        new(LayoutMode.QuadMainLeft, "左主右三"),
        new(LayoutMode.QuadColumns, "四栏"),
        new(LayoutMode.Hex, "六宫格")
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
            StatusMessage = $"已切换为 {LayoutPresets.First(p => p.Mode == value).Name}";
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
            StatusMessage = $"路径不存在: {path}";
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
            ? $"「{tag.Name}」共 {tag.ItemCount} 项"
            : $"「{tag.Name}」还没有内容，把文件夹拖到标签上即可";
    }

    private void OpenTaggedItem(TagViewModel tag, TaggedItemViewModel item)
    {
        if (!_fileService.PathExists(item.Path))
        {
            StatusMessage = $"路径不存在: {item.Path}";
            return;
        }

        ActivePane?.Navigate(item.Path);
        UpdateTagSelection(tag);
        StatusMessage = $"已打开 {item.Path}";
    }

    private void RemoveTaggedItem(TagViewModel tag, TaggedItemViewModel item)
    {
        tag.RemoveItem(item);
        PersistFavorites();
        RefreshTagViews();
        StatusMessage = $"已从「{tag.Name}」移除（本地文件未删除）";
    }

    private void RenameTag(TagViewModel tag)
    {
        var newName = PromptForText("重命名标签", "标签名称:", tag.Name);
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
            $"确定清空标签「{tag.Name}」中的 {tag.ItemCount} 项吗？（不会删除文件）",
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
        StatusMessage = $"已清空「{tag.Name}」";
    }

    private void AddCurrentPathToTag(TagViewModel tag)
    {
        if (ActivePane == null)
        {
            return;
        }

        var path = ActivePane.CurrentPath;
        if (path.Equals("ThisPC", StringComparison.OrdinalIgnoreCase) || ActivePane.IsTagView)
        {
            StatusMessage = "当前位置无法添加标签";
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
            StatusMessage = $"已为 {added} 个文件夹加上「{tag.Name}」标签";
        }
        else if (ignored > 0)
        {
            StatusMessage = "未添加：只接受文件夹，且同一标签不会重复记录";
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
        StatusMessage = "已移除标签";
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

            var pane = new PaneViewModel(i, _fileService, path, this);
            pane.StatusMessage += (_, message) => StatusMessage = message;
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
            SidebarWidth = SidebarWidth
        });
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
