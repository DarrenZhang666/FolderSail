using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FolderSail.Helpers;

/// <summary>
/// Watches a single directory and raises a debounced callback when entries change.
/// </summary>
internal sealed class DirectoryChangeWatcher : IDisposable
{
    private readonly Action _onChanged;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public DirectoryChangeWatcher(Action onChanged, int debounceMs = 350)
    {
        _onChanged = onChanged;
        _debounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(debounceMs, 100, 2000))
        };
        _debounce.Tick += OnDebounceTick;
    }

    public void Watch(string? path)
    {
        StopWatcher();

        if (_disposed || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024
            };
            watcher.Created += OnFsEvent;
            watcher.Deleted += OnFsEvent;
            watcher.Changed += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            StopWatcher();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        StopWatcher();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || _disposed)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            RestartDebounce();
        }
        else
        {
            dispatcher.BeginInvoke(RestartDebounce, DispatcherPriority.Background);
        }
    }

    private void RestartDebounce()
    {
        if (_disposed)
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (!_disposed)
        {
            _onChanged();
        }
    }

    private void StopWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFsEvent;
            _watcher.Deleted -= OnFsEvent;
            _watcher.Changed -= OnFsEvent;
            _watcher.Renamed -= OnFsEvent;
            _watcher.Dispose();
        }
        catch
        {
            // Ignore teardown races on network / removable drives.
        }

        _watcher = null;
    }
}
