using FolderSail.Mvvm;
using System.Windows.Input;

namespace FolderSail.ViewModels;

public sealed class BreadcrumbViewModel : ObservableObject
{
    public BreadcrumbViewModel(string label, string path, bool isLast, Action<string> navigate)
    {
        Label = label;
        Path = path;
        IsLast = isLast;
        NavigateCommand = new RelayCommand(() => navigate(path));
    }

    public string Label { get; }
    public string Path { get; }
    public bool IsLast { get; }
    public ICommand NavigateCommand { get; }
}
