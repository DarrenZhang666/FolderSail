using FolderSail.Mvvm;
using FolderSail.Core.Models;
using FolderSail.Helpers;

namespace FolderSail.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileItem item)
    {
        Item = item;
        Icon = ShellIconHelper.GetIcon(item.FullPath, item.Kind != FileItemKind.File);
    }

    public FileItem Item { get; }
    public string Name => Item.Name;
    public string FullPath => Item.FullPath;
    public FileItemKind Kind => Item.Kind;
    public long Size => Item.Size;
    public DateTime ModifiedUtc => Item.ModifiedUtc;
    public string Extension => Item.Extension;
    public System.Windows.Media.ImageSource? Icon { get; }
}
