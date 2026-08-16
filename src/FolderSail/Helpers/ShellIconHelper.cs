using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FolderSail.Helpers;

public static class ShellIconHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    public static ImageSource? GetIcon(string path, bool isDirectory) =>
        GetCachedIcon(path, isDirectory);

    public static ImageSource? GetCachedIcon(string path, bool isDirectory)
    {
        var key = isDirectory
            ? ":dir:"
            : string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? ":file:"
                : Path.GetExtension(path).ToLowerInvariant();

        return Cache.GetOrAdd(key, _ => LoadIcon(path, isDirectory));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> Cache = new();

    private static ImageSource? LoadIcon(string path, bool isDirectory)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : 0u;
        var probe = isDirectory ? "folder" : "file" + Path.GetExtension(path);
        var result = SHGetFileInfo(probe, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }
}
