using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace FolderSail.Helpers;

/// <summary>
/// File copy/cut via the Windows clipboard so FolderSail and Explorer share the same paste buffer.
/// </summary>
internal static class FileClipboard
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        var existing = paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existing.Count == 0)
        {
            return;
        }

        var files = new StringCollection();
        foreach (var path in existing)
        {
            files.Add(path);
        }

        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffect, DropEffectStream(cut ? DropEffectMove : DropEffectCopy));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception)
        {
            Clipboard.SetFileDropList(files);
        }
    }

    public static (IReadOnlyList<string> Paths, bool Cut) GetFiles()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return ([], false);
            }

            var list = Clipboard.GetFileDropList();
            var paths = list.Cast<string?>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();

            var cut = false;
            if (Clipboard.GetData(PreferredDropEffect) is MemoryStream stream)
            {
                var bytes = stream.ToArray();
                if (bytes.Length >= 4)
                {
                    cut = BitConverter.ToInt32(bytes, 0) == DropEffectMove;
                }
            }

            return (paths, cut);
        }
        catch (Exception)
        {
            return ([], false);
        }
    }

    public static bool HasFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void ClearCutMark()
    {
        var (paths, cut) = GetFiles();
        if (!cut || paths.Count == 0)
        {
            return;
        }

        SetFiles(paths, cut: false);
    }

    private static MemoryStream DropEffectStream(int effect)
    {
        var stream = new MemoryStream(4);
        stream.Write(BitConverter.GetBytes(effect));
        stream.Position = 0;
        return stream;
    }
}
