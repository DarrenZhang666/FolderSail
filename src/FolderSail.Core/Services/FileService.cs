using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IFileService
{
    string GetDefaultPath();
    IReadOnlyList<FileItem> ListDirectory(string path);
    IReadOnlyList<FileItem> ListPaths(IEnumerable<string> paths);
    IReadOnlyList<DriveItem> ListDriveDetails();
    void Copy(string sourcePath, string destinationDirectory, bool move);
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

        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                items.Add(new FileItem
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    Kind = FileItemKind.Directory,
                    Size = 0,
                    ModifiedUtc = info.LastWriteTimeUtc,
                    Extension = string.Empty
                });
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories.
            }
        }

        foreach (var file in Directory.EnumerateFiles(path))
        {
            try
            {
                var info = new FileInfo(file);
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
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible files.
            }
        }

        return items
            .OrderByDescending(i => i.Kind != FileItemKind.File)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // A tagged folder may have been removed or become unreachable.
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

    public void Copy(string sourcePath, string destinationDirectory, bool move)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException($"目标目录不存在: {destinationDirectory}");
        }

        sourcePath = Path.GetFullPath(sourcePath);
        destinationDirectory = Path.GetFullPath(destinationDirectory);

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

            if (isDirectory)
            {
                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }

            return;
        }

        var copyDest = PathsEqual(sourcePath, intended) || Exists(intended)
            ? UniqueDestination(destinationDirectory, fileName, isDirectory)
            : intended;

        if (isDirectory)
        {
            CopyDirectory(sourcePath, copyDest);
            return;
        }

        File.Copy(sourcePath, copyDest, overwrite: false);
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

    /// <summary>
    /// Snapshot children before creating dest so pasting a folder into itself
    /// cannot recurse forever (Explorer-style).
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        sourceDir = Path.GetFullPath(sourceDir);
        destDir = Path.GetFullPath(destDir);

        if (PathsEqual(sourceDir, destDir))
        {
            throw new IOException("无法将文件夹复制到其自身");
        }

        var files = Directory.GetFiles(sourceDir);
        var dirs = Directory.GetDirectories(sourceDir)
            .Where(dir => !PathsEqual(dir, destDir) && !IsUnder(dir, destDir))
            .ToArray();

        Directory.CreateDirectory(destDir);

        foreach (var file in files)
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (PathsEqual(file, destFile))
            {
                continue;
            }

            File.Copy(file, destFile, overwrite: false);
        }

        foreach (var subDir in dirs)
        {
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
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
