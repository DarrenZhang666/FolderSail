using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
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
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMs = 40;

    public static void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        // Only keep path strings on the clipboard (CF_HDROP). Never read file contents.
        // Skip Exists checks that can hang on unresponsive network volumes — the paste
        // side already filters missing paths.
        var existing = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
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

        // copy: false = delayed rendering (Explorer-style). copy: true calls OleFlushClipboard
        // and blocks the UI while every clipboard listener inspects the drop list — some of
        // them open/hash the referenced files, which freezes or kills the app on large files.
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                var data = CreateDataObject(files, cut);
                Clipboard.SetDataObject(data, copy: false);
                return;
            }
            catch (COMException) when (attempt < ClipboardRetryCount - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
            catch (Exception) when (attempt < ClipboardRetryCount - 1)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
            catch (Exception)
            {
                TrySetFileDropList(files);
                return;
            }
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
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                var bytes = new byte[4];
                var read = stream.Read(bytes, 0, bytes.Length);
                if (read >= 4)
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

    private static DataObject CreateDataObject(StringCollection files, bool cut)
    {
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffect, DropEffectStream(cut ? DropEffectMove : DropEffectCopy));
        return data;
    }

    private static void TrySetFileDropList(StringCollection files)
    {
        try
        {
            Clipboard.SetFileDropList(files);
        }
        catch (Exception)
        {
            // Clipboard locked by another process — give up quietly.
        }
    }

    private static MemoryStream DropEffectStream(int effect)
    {
        var bytes = BitConverter.GetBytes(effect);
        return new MemoryStream(bytes);
    }
}
