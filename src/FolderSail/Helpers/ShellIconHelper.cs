using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public static ImageSource? GetIcon(string path, bool isDirectory) =>
        GetCachedIcon(path, isDirectory);

    public static ImageSource? GetCachedIcon(string path, bool isDirectory)
    {
        var key = CacheKey(path, isDirectory);
        return Cache.TryGetValue(key, out var icon) ? icon : null;
    }

    public static void RequestIcon(
        string path,
        bool isDirectory,
        CancellationToken cancellationToken,
        Action<ImageSource?> callback)
    {
        var key = CacheKey(path, isDirectory);
        if (Cache.TryGetValue(key, out var cached))
        {
            callback(cached);
            return;
        }

        _ = Task.Run(() => LoadAndCallback(key, path, isDirectory, cancellationToken, callback), CancellationToken.None);
    }

    private static async Task LoadAndCallback(
        string key,
        string path,
        bool isDirectory,
        CancellationToken cancellationToken,
        Action<ImageSource?> callback)
    {
        try
        {
            await Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            if (Cache.TryGetValue(key, out var existing))
            {
                Dispatch(cancellationToken, () => callback(existing));
                return;
            }

            hIcon = ExtractHIcon(path, isDirectory);
        }
        finally
        {
            Gate.Release();
        }

        if (hIcon == IntPtr.Zero)
        {
            Dispatch(cancellationToken, () => callback(null));
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            DestroyIcon(hIcon);
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (Cache.TryGetValue(key, out var existing))
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        callback(existing);
                    }

                    return;
                }

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                Cache.TryAdd(key, source);
                if (!cancellationToken.IsCancellationRequested)
                {
                    callback(source);
                }
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    callback(null);
                }
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }, DispatcherPriority.Normal);
    }

    private static void Dispatch(CancellationToken cancellationToken, Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                action();
            }
        }, DispatcherPriority.Normal);
    }

    private static string CacheKey(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return ":dir:";
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension)
            ? ":file:"
            : extension.ToLowerInvariant();
    }

    private static IntPtr ExtractHIcon(string path, bool isDirectory)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var probe = isDirectory
            ? @"C:\Windows"
            : string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? "file"
                : "file" + Path.GetExtension(path);

        var result = SHGetFileInfo(probe, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
        {
            return shfi.hIcon;
        }

        if (isDirectory)
        {
            result = SHGetFileInfo(path, FILE_ATTRIBUTE_DIRECTORY, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (result != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                return shfi.hIcon;
            }
        }

        return IntPtr.Zero;
    }
}
