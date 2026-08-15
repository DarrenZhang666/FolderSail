using FolderSail.Core.Models;
using FolderSail.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class TaggedItemViewModel : ObservableObject
{
    private string _name;

    public TaggedItemViewModel(FavoriteItem item)
    {
        Model = item;
        _name = item.Name;
    }

    public FavoriteItem Model { get; }

    public Guid Id => Model.Id;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Model.Name = value;
            }
        }
    }

    public string Path => Model.Path;
}

/// <summary>
/// Actions a tag row offers, supplied by the owning view model so the sidebar's
/// context menu does not have to reach back out through the visual tree.
/// </summary>
public sealed record TagActions(
    Action<TagViewModel> Open,
    Action<TagViewModel, TaggedItemViewModel> OpenItem,
    Action<TagViewModel, TaggedItemViewModel> RemoveItem,
    Action<TagViewModel> Rename,
    Action<TagViewModel> Clear,
    Action<TagViewModel> TagCurrentLocation);

/// <summary>
/// One colour tag in the sidebar. Tags come from a fixed palette; users tag
/// folders by dropping them here rather than by creating categories.
/// </summary>
public sealed class TagViewModel : ObservableObject
{
    private readonly Action? _changed;
    private string _name;
    private string _color;
    private bool _isSelected;
    private bool _isDropTarget;
    private bool _isExpanded;

    public TagViewModel(FavoriteCategory category, TagActions actions, Action? changed = null)
    {
        Model = category;
        _changed = changed;
        _name = category.Name;
        _color = string.IsNullOrWhiteSpace(category.Color) ? "#6A96C8" : category.Color;
        category.Color = _color;

        SetColorCommand = new RelayCommand<string>(SetColor);
        OpenCommand = new RelayCommand(() => actions.Open(this));
        OpenItemCommand = new RelayCommand<TaggedItemViewModel>(
            item =>
            {
                if (item != null)
                {
                    actions.OpenItem(this, item);
                }
            });
        RemoveItemCommand = new RelayCommand<TaggedItemViewModel>(
            item =>
            {
                if (item != null)
                {
                    actions.RemoveItem(this, item);
                }
            });
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        RenameCommand = new RelayCommand(() => actions.Rename(this));
        ClearCommand = new RelayCommand(() => actions.Clear(this), () => HasItems);
        TagCurrentLocationCommand = new RelayCommand(() => actions.TagCurrentLocation(this));

        foreach (var item in category.Items)
        {
            Items.Add(new TaggedItemViewModel(item));
        }
    }

    public FavoriteCategory Model { get; }

    public Guid Id => Model.Id;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Model.Name = value;
            }
        }
    }

    public ObservableCollection<TaggedItemViewModel> Items { get; } = [];
    public int ItemCount => Items.Count;
    public bool HasItems => Items.Count > 0;

    public ICommand SetColorCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand TagCurrentLocationCommand { get; }

    /// <summary>Virtual path a pane navigates to in order to list this tag.</summary>
    public string ViewPath => TagPath.Create(Id);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string Color
    {
        get => _color;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SetProperty(ref _color, value))
            {
                return;
            }

            Model.Color = value;
            _changed?.Invoke();
        }
    }

    public IReadOnlyList<string> Paths => Items.Select(item => item.Path).ToList();

    public void AddItem(string name, string path)
    {
        if (ContainsPath(path))
        {
            return;
        }

        var item = new FavoriteItem { Name = name, Path = path };
        Model.Items.Add(item);
        Items.Add(new TaggedItemViewModel(item));
        NotifyItemsChanged();
    }

    public bool TryAddFolder(string path)
    {
        if (!Directory.Exists(path) || ContainsPath(path))
        {
            return false;
        }

        var trimmed = path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        AddItem(string.IsNullOrWhiteSpace(name) ? path : name, Path.GetFullPath(path));
        return true;
    }

    public void RemoveItem(TaggedItemViewModel item)
    {
        Model.Items.Remove(item.Model);
        Items.Remove(item);
        NotifyItemsChanged();
    }

    public bool RemovePath(string path)
    {
        var match = Items.FirstOrDefault(item =>
            item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            return false;
        }

        RemoveItem(match);
        return true;
    }

    public void Clear()
    {
        if (Items.Count == 0)
        {
            return;
        }

        Model.Items.Clear();
        Items.Clear();
        NotifyItemsChanged();
    }

    private void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Paths));
        _changed?.Invoke();
    }

    private bool ContainsPath(string path)
    {
        var normalized = Normalize(path);

        return Items.Any(item => Normalize(item.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void SetColor(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            Color = color;
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }
}
