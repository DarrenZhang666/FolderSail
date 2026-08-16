namespace FolderSail.Helpers;

public static class FolderSizeHelper
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static void RequestSize(string path, CancellationToken cancellationToken, Action<long> callback)
    {
        _ = Task.Run(() => ComputeAndCallback(path, cancellationToken, callback), cancellationToken);
    }

    private static async Task ComputeAndCallback(string path, CancellationToken cancellationToken, Action<long> callback)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        long total = 0;
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            total = Compute(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            Gate.Release();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                callback(total);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static long Compute(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var directory = new DirectoryInfo(path);
        foreach (var info in directory.EnumerateFileSystemInfos("*", Options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                continue;
            }

            try
            {
                total += info is FileInfo file ? file.Length : 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }
        }

        return total;
    }
}
