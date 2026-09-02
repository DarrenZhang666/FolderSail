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

    private static readonly HashSet<string> CutPaths = new(StringComparer.OrdinalIgnoreCase);

    public static event EventHandler? CutStateChanged;

    public static bool HasCutItems => CutPaths.Count > 0;

    public static bool IsCut(string path) =>
        !string.IsNullOrWhiteSpace(path) && CutPaths.Contains(path);

    /// <summary>
    /// Explorer Esc: keep paths on the clipboard but clear the move (cut) flag,
    /// so dimming disappears and a later paste becomes copy.
    /// </summary>
    public static bool CancelCut()
    {
        if (!HasCutItems)
        {
            return false;
        }

        ClearCutMark();
        return true;
    }

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
        var written = false;
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                var data = CreateDataObject(files, cut);
                Clipboard.SetDataObject(data, copy: false);
                written = true;
                break;
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
                written = true;
                break;
            }
        }

        if (!written)
        {
            return;
        }

        RememberCutState(existing, cut);
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

            // Prefer local cut mark when clipboard drop-effect is missing (fallback write).
            if (!cut && paths.Count > 0 && paths.All(IsCut))
            {
                cut = true;
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
            ClearCutState();
            return;
        }

        SetFiles(paths, cut: false);
    }

    public static void ClearCutState()
    {
        if (CutPaths.Count == 0)
        {
            return;
        }

        CutPaths.Clear();
        CutStateChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void RememberCutState(IReadOnlyList<string> paths, bool cut)
    {
        CutPaths.Clear();
        if (cut)
        {
            foreach (var path in paths)
            {
                CutPaths.Add(path);
            }
        }

        CutStateChanged?.Invoke(null, EventArgs.Empty);
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
