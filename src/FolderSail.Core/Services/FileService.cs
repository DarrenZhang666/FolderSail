using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IFileService
{
    string GetDefaultPath();
    IReadOnlyList<FileItem> ListDirectory(string path);
    IReadOnlyList<FileItem> ListPaths(IEnumerable<string> paths);
    IReadOnlyList<DriveItem> ListDriveDetails();
    void Copy(string sourcePath, string destinationDirectory, bool move);
    void Transfer(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        bool move,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken);
    void DeleteToRecycleBin(IEnumerable<string> paths);
    void Rename(string path, string newName);
    void CreateDirectory(string parentPath, string folderName);
    void OpenWithDefaultApp(string path);
    void OpenInExplorer(string path);
    bool PathExists(string path);
}

public sealed class FileService : IFileService
{
    public string GetDefaultPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public IReadOnlyList<FileItem> ListDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ListDrives();
        }

        if (path.Equals("ThisPC", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("此电脑", StringComparison.OrdinalIgnoreCase))
        {
            return ListDrives();
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"路径不存在: {path}");
        }

        var items = new List<FileItem>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                try
                {
                    var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                    items.Add(new FileItem
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        Kind = isDirectory ? FileItemKind.Directory : FileItemKind.File,
                        Size = isDirectory ? 0 : info is FileInfo file ? file.Length : 0,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Extension = isDirectory ? string.Empty : info.Extension
                    });
                }
                catch (Exception ex) when (
                    ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                }
            }
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }

        return items;
    }

    public IReadOnlyList<FileItem> ListPaths(IEnumerable<string> paths)
    {
        var items = new List<FileItem>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    items.Add(new FileItem
                    {
                        Name = info.Name.Length > 0 ? info.Name : info.FullName,
                        FullPath = info.FullName,
                        Kind = FileItemKind.Directory,
                        Size = 0,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Extension = string.Empty
                    });
                }
                else if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    items.Add(new FileItem
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        Kind = FileItemKind.File,
                        Size = info.Length,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Extension = info.Extension
                    });
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }
        }

        return items
            .OrderByDescending(i => i.Kind != FileItemKind.File)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<DriveItem> ListDriveDetails()
    {
        var drives = new List<DriveItem>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            try
            {
                drives.Add(new DriveItem
                {
                    Name = drive.Name.TrimEnd('\\'),
                    Path = drive.Name,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel,
                    TotalSize = drive.TotalSize,
                    FreeSize = drive.AvailableFreeSpace
                });
            }
            catch (IOException)
            {
                // Skip drives that report errors while querying size.
            }
        }

        return drives;
    }

    public void Copy(string sourcePath, string destinationDirectory, bool move) =>
        Transfer([sourcePath], destinationDirectory, move, progress: null, CancellationToken.None);

    public void Transfer(
        IReadOnlyList<string> sourcePaths,
        string destinationDirectory,
        bool move,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException($"目标目录不存在: {destinationDirectory}");
        }

        destinationDirectory = Path.GetFullPath(destinationDirectory);
        var sources = sourcePaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(Path.GetFullPath)
            .ToList();

        var state = new TransferState
        {
            TotalBytes = 1,
            TotalFiles = 1
        };
        Report(progress, state, "正在准备…");

        var (totalBytes, totalFiles) = Measure(sources, progress, state, cancellationToken);
        state.TotalBytes = Math.Max(totalBytes, 1);
        state.TotalFiles = Math.Max(totalFiles, 1);
        Report(progress, state, "正在准备…");

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyOne(source, destinationDirectory, move, state, progress, cancellationToken);
        }
    }

    private void CopyOne(
        string sourcePath,
        string destinationDirectory,
        bool move,
        TransferState state,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var isDirectory = Directory.Exists(sourcePath);
        if (!isDirectory && !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源文件不存在", sourcePath);
        }

        var fileName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("无法复制该路径");
        }

        var intended = Path.Combine(destinationDirectory, fileName);

        if (move)
        {
            if (PathsEqual(sourcePath, intended))
            {
                return;
            }

            if (isDirectory && IsUnder(intended, sourcePath))
            {
                throw new InvalidOperationException("无法将文件夹移动到其自身中");
            }

            var destinationPath = Exists(intended)
                ? UniqueDestination(destinationDirectory, fileName, isDirectory)
                : intended;

            if (SameVolume(sourcePath, destinationPath))
            {
                var (extraBytes, extraFiles) = Measure(
                    [sourcePath],
                    progress: null,
                    state: null,
                    cancellationToken);
                if (isDirectory)
                {
                    Directory.Move(sourcePath, destinationPath);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                state.BytesCopied += Math.Max(extraBytes, 0);
                state.FilesCopied += Math.Max(extraFiles, 0);
                state.CurrentName = fileName;
                Report(progress, state, fileName);
                return;
            }

            if (isDirectory)
            {
                CopyDirectory(sourcePath, destinationPath, state, progress, cancellationToken);
                Directory.Delete(sourcePath, recursive: true);
            }
            else
            {
                CopyFile(sourcePath, destinationPath, state, progress, cancellationToken);
                File.Delete(sourcePath);
            }

            return;
        }

        var copyDest = PathsEqual(sourcePath, intended) || Exists(intended)
            ? UniqueDestination(destinationDirectory, fileName, isDirectory)
            : intended;

        if (isDirectory)
        {
            CopyDirectory(sourcePath, copyDest, state, progress, cancellationToken);
            return;
        }

        CopyFile(sourcePath, copyDest, state, progress, cancellationToken);
    }

    public void DeleteToRecycleBin(IEnumerable<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0)
        {
            return;
        }

        RecycleBinHelper.SendToRecycleBin(existing);
    }

    public void Rename(string path, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("名称不能为空", nameof(newName));
        }

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("无法获取父目录");

        var destination = Path.Combine(parent, newName);

        if (Directory.Exists(path))
        {
            Directory.Move(path, destination);
        }
        else if (File.Exists(path))
        {
            File.Move(path, destination);
        }
        else
        {
            throw new FileNotFoundException("路径不存在", path);
        }
    }

    public void CreateDirectory(string parentPath, string folderName)
    {
        var path = Path.Combine(parentPath, folderName);
        Directory.CreateDirectory(path);
    }

    public void OpenWithDefaultApp(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    public void OpenInExplorer(string path)
    {
        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", path);
            return;
        }

        if (File.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }

    public bool PathExists(string path)
    {
        if (path.Equals("ThisPC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        path = ExpandDriveRoot(path);
        return Directory.Exists(path) || File.Exists(path);
    }

    /// <summary>
    /// Win32 treats "D:" as "current directory on D:", not the drive root.
    /// Always expand that form to "D:\" so clicking a disk never jumps into
    /// whatever folder the process was launched from.
    /// </summary>
    public static string ExpandDriveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return path + "\\";
        }

        return path;
    }

    private static IReadOnlyList<FileItem> ListDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileItem
            {
                Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name} ({d.VolumeLabel})",
                FullPath = d.Name,
                Kind = FileItemKind.Drive,
                Size = d.TotalSize,
                ModifiedUtc = DateTime.UtcNow,
                Extension = string.Empty
            })
            .ToList();
    }

    private static void CopyDirectory(
        string sourceDir,
        string destDir,
        TransferState state,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        sourceDir = Path.GetFullPath(sourceDir);
        destDir = Path.GetFullPath(destDir);

        if (PathsEqual(sourceDir, destDir))
        {
            throw new IOException("无法将文件夹复制到其自身");
        }

        // Iterative copy avoids StackOverflowException on deep trees / junction cycles.
        var stack = new Stack<(string Source, string Destination)>();
        stack.Push((sourceDir, destDir));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (currentSource, currentDest) = stack.Pop();
            currentSource = Path.GetFullPath(currentSource);
            currentDest = Path.GetFullPath(currentDest);

            if (PathsEqual(currentSource, currentDest))
            {
                continue;
            }

            Directory.CreateDirectory(currentDest);

            string[] files;
            try
            {
                files = Directory.GetFiles(currentSource);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destFile = Path.Combine(currentDest, Path.GetFileName(file));
                if (PathsEqual(file, destFile))
                {
                    continue;
                }

                CopyFile(file, destFile, state, progress, cancellationToken);
            }

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(currentSource);
            }
            catch
            {
                continue;
            }

            foreach (var subDir in dirs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(subDir))
                {
                    // Skip junctions/symlinks so a link back to an ancestor cannot
                    // recurse forever and crash the process.
                    continue;
                }

                if (PathsEqual(subDir, currentDest) || IsUnder(subDir, currentDest))
                {
                    continue;
                }

                stack.Push((subDir, Path.Combine(currentDest, Path.GetFileName(subDir))));
            }
        }
    }

    private static void CopyFile(
        string sourcePath,
        string destinationPath,
        TransferState state,
        IProgress<FileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        state.CurrentName = Path.GetFileName(sourcePath);
        Report(progress, state, state.CurrentName);

        // 1 MB buffer keeps throughput high for multi-GB files without loading the file into memory.
        const int bufferSize = 1024 * 1024;
        try
        {
            using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.SequentialScan);
            using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.SequentialScan);
            var buffer = new byte[bufferSize];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                state.BytesCopied += read;
                Report(progress, state, state.CurrentName);
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(destinationPath);
            throw;
        }

        state.FilesCopied++;
        Report(progress, state, state.CurrentName);
    }

    private static (long Bytes, int Files) Measure(
        IEnumerable<string> paths,
        IProgress<FileTransferProgress>? progress,
        TransferState? state,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        var files = 0;
        var stack = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            stack.Push(path);
        }

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = stack.Pop();

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (!visited.Add(full))
            {
                continue;
            }

            try
            {
                if (File.Exists(full))
                {
                    bytes += new FileInfo(full).Length;
                    files++;
                    ReportMeasure(progress, state, bytes, files);
                    continue;
                }

                if (!Directory.Exists(full))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(full))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        bytes += new FileInfo(file).Length;
                        files++;
                    }
                    catch
                    {
                    }
                }

                ReportMeasure(progress, state, bytes, files);

                foreach (var dir in Directory.EnumerateDirectories(full))
                {
                    if (IsReparsePoint(dir))
                    {
                        continue;
                    }

                    stack.Push(dir);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return (bytes, files);
    }

    private static void ReportMeasure(
        IProgress<FileTransferProgress>? progress,
        TransferState? state,
        long bytes,
        int files)
    {
        if (state is null)
        {
            return;
        }

        state.TotalBytes = Math.Max(bytes, 1);
        state.TotalFiles = Math.Max(files, 1);
        Report(progress, state, "正在准备…");
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Report(IProgress<FileTransferProgress>? progress, TransferState state, string name)
    {
        var now = Environment.TickCount64;
        var done = state.BytesCopied >= state.TotalBytes;
        if (!done && now - state.LastReportTick < 50)
        {
            return;
        }

        state.LastReportTick = now;
        progress?.Report(new FileTransferProgress
        {
            CurrentName = name,
            BytesCopied = state.BytesCopied,
            TotalBytes = state.TotalBytes,
            FilesCopied = state.FilesCopied,
            TotalFiles = state.TotalFiles
        });
    }

    private static bool SameVolume(string left, string right)
    {
        var a = Path.GetPathRoot(Path.GetFullPath(left));
        var b = Path.GetPathRoot(Path.GetFullPath(right));
        return !string.IsNullOrEmpty(a) &&
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class TransferState
    {
        public long BytesCopied;
        public long TotalBytes = 1;
        public int FilesCopied;
        public int TotalFiles = 1;
        public string CurrentName = string.Empty;
        public long LastReportTick;
    }

    private static string UniqueDestination(string directory, string originalName, bool isFolder)
    {
        string baseName;
        string extension;
        if (isFolder)
        {
            baseName = originalName;
            extension = string.Empty;
        }
        else
        {
            baseName = Path.GetFileNameWithoutExtension(originalName);
            extension = Path.GetExtension(originalName);
        }

        var candidate = $"{baseName} - 副本{extension}";
        var n = 2;
        while (Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName} - 副本 ({n++}){extension}";
        }

        return Path.Combine(directory, candidate);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool PathsEqual(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd('\\', '/');
        var b = Path.GetFullPath(right).TrimEnd('\\', '/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnder(string path, string parent)
    {
        var prefix = Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
