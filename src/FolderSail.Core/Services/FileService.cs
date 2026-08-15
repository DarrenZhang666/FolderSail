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

        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(destinationDirectory, fileName);

        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, destinationPath, move);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源文件不存在", sourcePath);
        }

        if (move)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
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

    public bool PathExists(string path) =>
        Directory.Exists(path) || File.Exists(path) || path.Equals("ThisPC", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<FileItem> ListDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileItem
            {
                Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name} ({d.VolumeLabel})",
                FullPath = d.Name.TrimEnd('\\'),
                Kind = FileItemKind.Drive,
                Size = d.TotalSize,
                ModifiedUtc = DateTime.UtcNow,
                Extension = string.Empty
            })
            .ToList();
    }

    private static void CopyDirectory(string sourceDir, string destDir, bool move)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)), move: false);
        }

        if (move)
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }
}
