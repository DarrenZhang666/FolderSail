using System.Runtime.InteropServices;
using FolderSail.Core.Models;

namespace FolderSail.Core.Services;

public interface IFileSearchService
{
    IReadOnlyList<FileItem> SearchByName(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filename search across the machine. Prefers the Windows Search index
/// (the same catalogue Explorer uses) and falls back to a cancellable
/// directory walk when the index is unavailable or empty.
/// </summary>
public sealed class SearchService : IFileSearchService
{
    public const int MaxResults = 400;

    private static readonly string[] SkipDirectoryNames =
    [
        "$Recycle.Bin",
        "System Volume Information",
        "Windows",
        "WinSxS",
        "Installer",
        "node_modules",
        ".git"
    ];

    private readonly IFileService _files;

    public SearchService(IFileService files)
    {
        _files = files;
    }

    public IReadOnlyList<FileItem> SearchByName(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;

        var tokens = SplitTokens(query);
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var indexed = QueryWindowsSearch(tokens, MaxResults, token);
            if (indexed is not null)
            {
                AddUnique(paths, seen, indexed);
                if (paths.Count == 0)
                {
                    AddUnique(paths, seen, EnumerateByName(tokens, MaxResults, token));
                }
            }
            else
            {
                AddUnique(paths, seen, EnumerateByName(tokens, MaxResults, token));
            }
        }
        catch (OperationCanceledException)
        {
            // Keep whatever was collected before the timeout.
        }

        return _files.ListPaths(paths)
            .OrderByDescending(item => item.ModifiedUtc)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddUnique(List<string> paths, HashSet<string> seen, IEnumerable<string>? candidates)
    {
        if (candidates is null)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (paths.Count >= MaxResults)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
            {
                continue;
            }

            paths.Add(candidate);
        }
    }

    private static string[] SplitTokens(string query) =>
        query.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string>? QueryWindowsSearch(string[] tokens, int limit, CancellationToken token)
    {
        Type? connectionType;
        try
        {
            connectionType = Type.GetTypeFromProgID("ADODB.Connection");
        }
        catch (COMException)
        {
            return null;
        }

        if (connectionType is null)
        {
            return null;
        }

        object? connection = null;
        object? recordset = null;

        try
        {
            connection = Activator.CreateInstance(connectionType);
            if (connection is null)
            {
                return null;
            }

            dynamic conn = connection;
            conn.Open("Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\";");
            recordset = conn.Execute(BuildSql(tokens, limit));
            if (recordset is null)
            {
                return null;
            }

            dynamic rows = recordset;
            var paths = new List<string>();

            while (!rows.EOF)
            {
                token.ThrowIfCancellationRequested();
                object? value = rows.Fields["System.ItemPathDisplay"].Value;
                var path = NormalizeIndexedPath(value);
                if (path is not null)
                {
                    paths.Add(path);
                }

                rows.MoveNext();
            }

            return paths;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            TryCloseCom(recordset);
            TryCloseCom(connection);
        }
    }

    private static string BuildSql(string[] tokens, int limit)
    {
        var clauses = tokens.Select(token => $"System.FileName LIKE '%{EscapeLike(token)}%'");
        return $"SELECT TOP {limit} System.ItemPathDisplay FROM SYSTEMINDEX WHERE SCOPE='file:' AND {string.Join(" AND ", clauses)}";
    }

    private static string EscapeLike(string value) =>
        value.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static string? NormalizeIndexedPath(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(text);
                return uri.IsFile ? uri.LocalPath : text;
            }
            catch (UriFormatException)
            {
                return text;
            }
        }

        return text;
    }

    private static void TryCloseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            comObject.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, comObject, null);
        }
        catch
        {
            // Already closed or not a connection/recordset.
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Ignore RCW teardown failures.
        }
    }

    private static IEnumerable<string> EnumerateByName(string[] tokens, int remaining, CancellationToken token)
    {
        if (remaining <= 0)
        {
            yield break;
        }

        var found = 0;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var root in GetSearchRoots(profile))
        {
            foreach (var path in Walk(root, tokens, profile, token))
            {
                yield return path;
                found++;
                if (found >= remaining)
                {
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<string> GetSearchRoots(string profile)
    {
        if (!string.IsNullOrWhiteSpace(profile) && Directory.Exists(profile))
        {
            yield return profile;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            yield return drive.RootDirectory.FullName;
        }
    }

    private static IEnumerable<string> Walk(string root, string[] tokens, string profile, CancellationToken token)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();

                if (NameMatches(entry, tokens))
                {
                    yield return entry;
                }

                if (!TryGetDirectoryAttributes(entry, out var attributes))
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var name = Path.GetFileName(entry);
                if (ShouldSkipDirectory(name))
                {
                    continue;
                }

                // The user profile is walked first; skip it when scanning the drive root.
                if (!string.IsNullOrEmpty(profile) &&
                    !root.Equals(profile, StringComparison.OrdinalIgnoreCase) &&
                    entry.Equals(profile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                stack.Push(entry);
            }
        }
    }

    private static bool TryGetDirectoryAttributes(string path, out FileAttributes attributes)
    {
        attributes = 0;
        try
        {
            attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool ShouldSkipDirectory(string name)
    {
        foreach (var skip in SkipDirectoryNames)
        {
            if (name.Equals(skip, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(string path, string[] tokens)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
