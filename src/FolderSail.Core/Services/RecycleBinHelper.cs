using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace FolderSail.Core.Services;

public sealed record RecycledItem(string OriginalPath, string? RecyclePayloadPath);

internal static class RecycleBinHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public FileOperationFunc wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public FileOperationFlags fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [Flags]
    private enum FileOperationFlags : ushort
    {
        Silent = 0x0004,
        NoConfirmation = 0x0010,
        AllowUndo = 0x0040,
        NoErrorUI = 0x0400
    }

    private enum FileOperationFunc : uint
    {
        Delete = 0x0003
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static IReadOnlyList<RecycledItem> SendToRecycleBin(IEnumerable<string> paths)
    {
        var existing = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existing.Count == 0)
        {
            return [];
        }

        var joined = string.Join('\0', existing) + '\0' + '\0';
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FileOperationFunc.Delete,
            pFrom = joined,
            fFlags = FileOperationFlags.AllowUndo | FileOperationFlags.NoConfirmation | FileOperationFlags.NoErrorUI
        };

        var result = SHFileOperation(ref fileOp);
        if (result != 0)
        {
            throw new IOException($"删除到回收站失败，错误码: {result}");
        }

        return existing
            .Select(original => new RecycledItem(original, FindRecyclePayload(original)))
            .ToList();
    }

    public static int Restore(IReadOnlyList<RecycledItem> items)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var remaining = items
            .Select(i => i.OriginalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var restored = 0;

        // 1) Direct move from tracked $R payload (most reliable for our own deletes).
        foreach (var item in items)
        {
            var original = Path.GetFullPath(item.OriginalPath);
            if (!remaining.Contains(original))
            {
                continue;
            }

            if (File.Exists(original) || Directory.Exists(original))
            {
                remaining.Remove(original);
                restored++;
                continue;
            }

            var payload = item.RecyclePayloadPath;
            if (string.IsNullOrWhiteSpace(payload) ||
                (!File.Exists(payload) && !Directory.Exists(payload)))
            {
                continue;
            }

            if (TryMovePayloadHome(payload, original))
            {
                remaining.Remove(original);
                restored++;
            }
        }

        if (remaining.Count == 0)
        {
            return restored;
        }

        // 2) Shell.Application "undelete" / "restore".
        restored += RestoreViaShell(remaining);

        if (remaining.Count == 0)
        {
            return restored;
        }

        // 3) Scan $I metadata as last resort.
        restored += RestoreViaInfoFiles(remaining);
        return restored;
    }

    /// <summary>Legacy entry used by older call sites.</summary>
    public static int RestoreByOriginalPaths(IEnumerable<string> originalPaths) =>
        Restore(originalPaths.Select(p => new RecycledItem(Path.GetFullPath(p), FindRecyclePayload(p))).ToList());

    private static bool TryMovePayloadHome(string payloadPath, string originalPath)
    {
        try
        {
            var parent = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (Directory.Exists(payloadPath))
            {
                Directory.Move(payloadPath, originalPath);
            }
            else
            {
                File.Move(payloadPath, originalPath);
            }

            TryDeleteInfoSibling(payloadPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteInfoSibling(string payloadPath)
    {
        try
        {
            var name = Path.GetFileName(payloadPath);
            if (name.Length < 3 || !name.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var info = Path.Combine(Path.GetDirectoryName(payloadPath)!, "$I" + name[2..]);
            if (File.Exists(info))
            {
                File.Delete(info);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static int RestoreViaShell(HashSet<string> remaining)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return 0;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic recycler = shell.NameSpace(10);
            if (recycler is null)
            {
                return 0;
            }

            var snapshot = new List<dynamic>();
            foreach (dynamic item in recycler.Items())
            {
                snapshot.Add(item);
            }

            var restored = 0;
            foreach (var item in snapshot)
            {
                if (remaining.Count == 0)
                {
                    break;
                }

                if (!TryResolveShellOriginal(item, recycler, remaining, out string original))
                {
                    continue;
                }

                if (InvokeRestoreVerb(item))
                {
                    remaining.Remove(original);
                    restored++;
                }
            }

            return restored;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryResolveShellOriginal(
        dynamic item,
        dynamic recycler,
        HashSet<string> remaining,
        out string original)
    {
        original = string.Empty;
        try
        {
            string? name = item.Name as string;
            string? deletedFrom =
                TryExtendedProperty(item, "System.Recycle.DeletedFrom") ??
                TryExtendedProperty(item, "DeletedFrom");

            if (!string.IsNullOrWhiteSpace(deletedFrom) && !string.IsNullOrWhiteSpace(name))
            {
                var candidate = Path.GetFullPath(Path.Combine(deletedFrom, name));
                if (remaining.Contains(candidate))
                {
                    original = candidate;
                    return true;
                }
            }

            // Match by file name when unique among pending restores.
            if (!string.IsNullOrWhiteSpace(name))
            {
                var matches = remaining
                    .Where(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 1)
                {
                    original = matches[0];
                    return true;
                }
            }

            // Some shells expose original location via details column 1.
            try
            {
                string? detail = recycler.GetDetailsOf(item, 1) as string;
                if (!string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(name))
                {
                    var candidate = Path.GetFullPath(Path.Combine(detail.Trim(), name));
                    if (remaining.Contains(candidate))
                    {
                        original = candidate;
                        return true;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string? TryExtendedProperty(dynamic item, string property)
    {
        try
        {
            return item.ExtendedProperty(property) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool InvokeRestoreVerb(dynamic item)
    {
        foreach (var verb in new[] { "undelete", "restore", "还原", "撤销删除" })
        {
            try
            {
                item.InvokeVerb(verb);
                return true;
            }
            catch
            {
                // try next
            }
        }

        return false;
    }

    private static int RestoreViaInfoFiles(HashSet<string> remaining)
    {
        var restored = 0;
        foreach (var group in remaining.ToList().GroupBy(p => Path.GetPathRoot(p) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(group.Key) || remaining.Count == 0)
            {
                continue;
            }

            foreach (var recycleDir in EnumerateRecycleDirectories(group.Key))
            {
                if (remaining.Count == 0)
                {
                    break;
                }

                IEnumerable<string> infoFiles;
                try
                {
                    infoFiles = Directory.EnumerateFiles(recycleDir, "$I*").ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var infoFile in infoFiles)
                {
                    if (!TryReadOriginalPath(infoFile, out var original) ||
                        !remaining.Contains(original))
                    {
                        continue;
                    }

                    var infoName = Path.GetFileName(infoFile);
                    if (infoName.Length < 3)
                    {
                        continue;
                    }

                    var payloadPath = Path.Combine(recycleDir, "$R" + infoName[2..]);
                    if (TryMovePayloadHome(payloadPath, original))
                    {
                        remaining.Remove(original);
                        restored++;
                    }

                    if (remaining.Count == 0)
                    {
                        break;
                    }
                }
            }
        }

        return restored;
    }

    private static string? FindRecyclePayload(string originalPath)
    {
        try
        {
            var full = Path.GetFullPath(originalPath);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            foreach (var recycleDir in EnumerateRecycleDirectories(root))
            {
                IEnumerable<string> infoFiles;
                try
                {
                    infoFiles = Directory.EnumerateFiles(recycleDir, "$I*");
                }
                catch
                {
                    continue;
                }

                foreach (var infoFile in infoFiles)
                {
                    if (!TryReadOriginalPath(infoFile, out var original) ||
                        !original.Equals(full, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var infoName = Path.GetFileName(infoFile);
                    if (infoName.Length < 3)
                    {
                        continue;
                    }

                    var payload = Path.Combine(recycleDir, "$R" + infoName[2..]);
                    if (File.Exists(payload) || Directory.Exists(payload))
                    {
                        return payload;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRecycleDirectories(string driveRoot)
    {
        var recycleRoot = Path.Combine(driveRoot, "$Recycle.Bin");
        if (!Directory.Exists(recycleRoot))
        {
            yield break;
        }

        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (!string.IsNullOrEmpty(sid))
        {
            var preferred = Path.Combine(recycleRoot, sid);
            if (Directory.Exists(preferred))
            {
                yield return preferred;
            }
        }

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(recycleRoot).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            if (!string.IsNullOrEmpty(sid) &&
                dir.EndsWith(sid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return dir;
        }
    }

    private static bool TryReadOriginalPath(string infoFile, out string originalPath)
    {
        originalPath = string.Empty;
        try
        {
            using var stream = new FileStream(infoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < 28)
            {
                return false;
            }

            using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: false);
            var version = reader.ReadInt64();
            _ = reader.ReadInt64(); // size
            _ = reader.ReadInt64(); // delete FILETIME

            if (version == 1)
            {
                var chars = new char[(stream.Length - stream.Position) / 2];
                var read = reader.Read(chars, 0, chars.Length);
                var text = new string(chars, 0, read).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                originalPath = Path.GetFullPath(text);
                return true;
            }

            if (version >= 2)
            {
                var charCount = reader.ReadInt32();
                if (charCount <= 0 || charCount > 0x8000)
                {
                    return false;
                }

                var chars = reader.ReadChars(Math.Min(charCount, (int)((stream.Length - stream.Position) / 2)));
                var text = new string(chars).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                originalPath = Path.GetFullPath(text);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
