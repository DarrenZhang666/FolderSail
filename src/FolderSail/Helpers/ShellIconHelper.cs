using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

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

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(
        uint flags,
        uint str,
        string pszAssoc,
        string? pszExtra,
        StringBuilder? pszOut,
        ref uint pcchOut);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint SHGFI_TYPENAME = 0x000000400;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    private static readonly ConcurrentDictionary<string, string> TypeCache = new();
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

    public static string GetTypeName(string path, bool isDirectory, bool isDrive = false)
    {
        var key = TypeCacheKey(path, isDirectory, isDrive);
        return TypeCache.GetOrAdd(key, _ => QueryTypeName(path, isDirectory, isDrive));
    }

    public static void ClearTypeCache() => TypeCache.Clear();

    private static string TypeCacheKey(string path, bool isDirectory, bool isDrive) =>
        $"{LanguageManager.Current}|{(isDrive ? ":drive:" : CacheKey(path, isDirectory))}";

    /// <summary>
    /// Prefer the in-app language for type labels. Shell/registry names follow the
    /// OS locale (often Chinese on CN Windows) and would ignore English UI mode.
    /// </summary>
    private static string QueryTypeName(string path, bool isDirectory, bool isDrive)
    {
        if (isDrive)
        {
            return Loc.Get("Loc.Drive");
        }

        if (isDirectory)
        {
            return Loc.Get("Loc.Folder");
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return AlignTypeNameToAppLanguage(
                QueryShellTypeName(path, FILE_ATTRIBUTE_NORMAL, SHGFI_TYPENAME),
                extension: null);
        }

        var ext = extension.ToLowerInvariant();
        if (TryGetLocalizedWellKnownType(ext, out var wellKnown))
        {
            return wellKnown;
        }

        if (LanguageManager.IsEnglish && EnglishTypeNames.TryGetValue(ext, out var englishName))
        {
            return englishName;
        }

        var shellName = QueryFriendlyDocName(extension)
                        ?? QueryRegistryTypeName(extension)
                        ?? QueryShellTypeName("file" + extension, FILE_ATTRIBUTE_NORMAL, SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES)
                        ?? QueryShellTypeName(path, 0, SHGFI_TYPENAME);

        return AlignTypeNameToAppLanguage(shellName, extension);
    }

    private static bool TryGetLocalizedWellKnownType(string extension, out string name)
    {
        switch (extension)
        {
            case ".lnk":
                name = Loc.Get("Loc.Shortcut");
                return true;
            case ".exe":
            case ".com":
            case ".msi":
                name = Loc.Get("Loc.Application");
                return true;
            case ".dll":
            case ".ocx":
            case ".sys":
                name = Loc.Get("Loc.ApplicationExtension");
                return true;
            default:
                name = string.Empty;
                return false;
        }
    }

    private static string AlignTypeNameToAppLanguage(string? shellName, string? extension)
    {
        if (string.IsNullOrWhiteSpace(shellName))
        {
            return FallbackTypedFile(extension);
        }

        var trimmed = shellName.Trim();
        var hasCjk = ContainsCjk(trimmed);

        // English UI on a Chinese OS: drop Shell's Chinese labels.
        if (LanguageManager.IsEnglish && hasCjk)
        {
            return FallbackTypedFile(extension);
        }

        // Chinese UI on an English OS: prefer Loc for generic folder-like leftovers only.
        if (!LanguageManager.IsEnglish && !hasCjk && LooksLikeGenericEnglishType(trimmed))
        {
            return FallbackTypedFile(extension);
        }

        return trimmed;
    }

    private static string FallbackTypedFile(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return Loc.Get("Loc.File");
        }

        var token = extension.TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(token)
            ? Loc.Get("Loc.File")
            : Loc.Format("Loc.TypedFile", token);
    }

    private static bool LooksLikeGenericEnglishType(string name) =>
        name.Equals("File", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Folder", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(" File", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF')
            {
                return true;
            }
        }

        return false;
    }

    // Common Explorer English type names when the OS shell speaks Chinese.
    private static readonly Dictionary<string, string> EnglishTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "Text Document",
        [".rtf"] = "Rich Text Format",
        [".pdf"] = "PDF Document",
        [".doc"] = "Microsoft Word Document",
        [".docx"] = "Microsoft Word Document",
        [".docm"] = "Microsoft Word Macro-Enabled Document",
        [".dotx"] = "Microsoft Word Template",
        [".xls"] = "Microsoft Excel Spreadsheet",
        [".xlsx"] = "Microsoft Excel Spreadsheet",
        [".xlsm"] = "Microsoft Excel Macro-Enabled Spreadsheet",
        [".csv"] = "CSV File",
        [".ppt"] = "Microsoft PowerPoint Presentation",
        [".pptx"] = "Microsoft PowerPoint Presentation",
        [".pptm"] = "Microsoft PowerPoint Macro-Enabled Presentation",
        [".png"] = "PNG File",
        [".jpg"] = "JPEG Image",
        [".jpeg"] = "JPEG Image",
        [".gif"] = "GIF Image",
        [".bmp"] = "Bitmap Image",
        [".webp"] = "WEBP File",
        [".svg"] = "SVG File",
        [".ico"] = "Icon",
        [".mp3"] = "MP3 File",
        [".wav"] = "WAV File",
        [".flac"] = "FLAC File",
        [".mp4"] = "MP4 File",
        [".mkv"] = "MKV File",
        [".avi"] = "AVI File",
        [".mov"] = "MOV File",
        [".zip"] = "Compressed (zipped) Folder",
        [".7z"] = "7Z File",
        [".rar"] = "RAR File",
        [".gz"] = "GZ File",
        [".json"] = "JSON File",
        [".xml"] = "XML File",
        [".html"] = "HTML Document",
        [".htm"] = "HTML Document",
        [".css"] = "Cascading Style Sheet",
        [".js"] = "JavaScript File",
        [".ts"] = "TypeScript File",
        [".cs"] = "C# Source File",
        [".py"] = "Python File",
        [".md"] = "Markdown File",
        [".bat"] = "Windows Batch File",
        [".cmd"] = "Windows Command Script",
        [".ps1"] = "Windows PowerShell Script",
        [".url"] = "Internet Shortcut",
        [".iso"] = "Disc Image File",
    };

    private static string? QueryFriendlyDocName(string extension)
    {
        try
        {
            uint length = 260;
            var buffer = new StringBuilder((int)length);
            var hr = AssocQueryString(0, AssocStrFriendlyDocName, extension, null, buffer, ref length);
            if (hr != 0 || buffer.Length == 0)
            {
                return null;
            }

            var name = buffer.ToString().Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private const uint AssocStrFriendlyDocName = 3;

    private static string? QueryRegistryTypeName(string extension)
    {
        try
        {
            using var extKey = Registry.ClassesRoot.OpenSubKey(extension);
            var progId = extKey?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            using var progKey = Registry.ClassesRoot.OpenSubKey(progId);
            var name = progKey?.GetValue(null) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? QueryShellTypeName(string probe, uint attributes, uint flags)
    {
        if (string.IsNullOrWhiteSpace(probe))
        {
            return null;
        }

        var shfi = new SHFILEINFO();
        var result = SHGetFileInfo(probe, attributes, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        return result != IntPtr.Zero && !string.IsNullOrWhiteSpace(shfi.szTypeName)
            ? shfi.szTypeName.Trim()
            : null;
    }
}
