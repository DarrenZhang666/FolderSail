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

    private bool _isRenaming;
    private string _renameText = string.Empty;

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    public int RenameSelectLength
    {
        get
        {
            if (Kind != FileItemKind.File)
            {
                return RenameText.Length;
            }

            var lastDot = RenameText.LastIndexOf('.');
            return lastDot <= 0 ? RenameText.Length : lastDot;
        }
    }
}
