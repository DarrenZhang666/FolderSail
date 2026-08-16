using FolderSail.Mvvm;
using FolderSail.Core.Models;
using FolderSail.Helpers;
using System.Windows.Media;

namespace FolderSail.ViewModels;

public sealed class FileItemViewModel : ObservableObject
{
    private ImageSource? _icon;
    private long _size;
    private bool _iconRequested;
    private bool _sizeRequested;
    private bool _isRenaming;
    private string _renameText = string.Empty;

    public FileItemViewModel(FileItem item)
    {
        Item = item;
        _size = item.Kind == FileItemKind.Directory ? -1 : item.Size;
    }

    public FileItem Item { get; }
    public string Name => Item.Name;
    public string FullPath => Item.FullPath;
    public FileItemKind Kind => Item.Kind;
    public long Size => _size < 0 ? 0 : _size;
    public long SizeDisplay => _size;
    public DateTime ModifiedUtc => Item.ModifiedUtc;
    public string Extension => Item.Extension;

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public void EnsureIcon(CancellationToken cancellationToken = default)
    {
        if (_icon != null)
        {
            return;
        }

        var cached = ShellIconHelper.GetCachedIcon(FullPath, Kind != FileItemKind.File);
        if (cached != null)
        {
            Icon = cached;
            return;
        }

        if (_iconRequested)
        {
            return;
        }

        _iconRequested = true;
        ShellIconHelper.RequestIcon(FullPath, Kind != FileItemKind.File, cancellationToken, icon =>
        {
            if (icon != null)
            {
                Icon = icon;
            }
            else
            {
                _iconRequested = false;
            }
        });
    }

    public void EnsureFolderSize(CancellationToken cancellationToken = default)
    {
        if (Kind != FileItemKind.Directory || _size >= 0 || _sizeRequested)
        {
            return;
        }

        _sizeRequested = true;
        FolderSizeHelper.RequestSize(FullPath, cancellationToken, total =>
        {
            _size = total;
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(SizeDisplay));
        });
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
