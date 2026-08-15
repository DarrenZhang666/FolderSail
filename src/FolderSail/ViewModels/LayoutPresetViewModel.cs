using FolderSail.Core.Models;
using FolderSail.Mvvm;

namespace FolderSail.ViewModels;

public sealed class LayoutPresetViewModel : ObservableObject
{
    private bool _isSelected;

    public LayoutPresetViewModel(LayoutMode mode, string name)
    {
        Mode = mode;
        Name = name;
    }

    public LayoutMode Mode { get; }
    public string Name { get; }
    public int PaneCount => Mode.GetDefinition().PaneCount;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
