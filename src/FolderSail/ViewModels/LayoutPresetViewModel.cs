using FolderSail.Core.Models;
using FolderSail.Helpers;
using FolderSail.Mvvm;

namespace FolderSail.ViewModels;

public sealed class LayoutPresetViewModel : ObservableObject
{
    private readonly string _nameKey;
    private bool _isSelected;

    public LayoutPresetViewModel(LayoutMode mode, string nameKey)
    {
        Mode = mode;
        _nameKey = nameKey;
    }

    public LayoutMode Mode { get; }
    public string Name => Loc.Get(_nameKey);
    public int PaneCount => Mode.GetDefinition().PaneCount;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshName() => OnPropertyChanged(nameof(Name));
}
