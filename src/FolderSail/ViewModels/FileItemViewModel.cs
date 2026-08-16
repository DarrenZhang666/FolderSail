using FolderSail.Mvvm;
using FolderSail.Core.Models;
using FolderSail.Helpers;
using System.Windows.Media;

namespace FolderSail.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private ImageSource? _icon;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    public FileItemViewModel(FileItem item)
    {
        Item = item;
    }

    public FileItem Item { get; }
    public string Name => Item.Name;
    public string FullPath => Item.FullPath;
    public FileItemKind Kind => Item.Kind;
    public long Size => Item.Size;
    public DateTime ModifiedUtc => Item.ModifiedUtc;
    public string Extension => Item.Extension;

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public void EnsureIcon()
    {
        if (_icon != null)
        {
            return;
        }

        Icon = ShellIconHelper.GetCachedIcon(FullPath, Kind != FileItemKind.File);
    }

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
