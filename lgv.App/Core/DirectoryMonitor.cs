using System.IO;

namespace lgv.Core;

public sealed class DirectoryMonitor : IDisposable
{
    private readonly string _directoryPath;
    private FileSystemWatcher? _watcher;
    private DateTime _newestTimestamp = DateTime.MinValue;
    private bool _disposed;

    public event EventHandler<string>? NewFileDetected;
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;

    public DirectoryMonitor(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public string? GetNewestFile()
    {
        try
        {
            var di = new DirectoryInfo(_directoryPath);
            if (!di.Exists) return null;

            var newest = di.GetFiles("*.*")
                           .OrderByDescending(f => f.LastWriteTime)
                           .FirstOrDefault();

            if (newest is not null)
            {
                _newestTimestamp = newest.LastWriteTime;
                return newest.FullName;
            }
        }
        catch
        {
            // Directory not accessible
        }
        return null;
    }

    public void Start()
    {
        if (_disposed) return;

        // Establish the current newest timestamp
        GetNewestFile();

        _watcher = new FileSystemWatcher(_directoryPath, "*.*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnRenamedEvent;
    }

    public void Stop()
    {
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = false;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        CheckForNewerFile(e.FullPath);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        // Always treat a rename as a new file — renaming doesn't change LastWriteTime
        // so the timestamp check would silently skip it.
        try
        {
            var fi = new FileInfo(e.FullPath);
            if (fi.Exists)
                _newestTimestamp = fi.LastWriteTime;
        }
        catch { }

        FileRenamed?.Invoke(this, (e.OldFullPath, e.FullPath));
    }

    private void CheckForNewerFile(string fullPath)
    {
        try
        {
            var fi = new FileInfo(fullPath);
            if (!fi.Exists) return;

            if (fi.LastWriteTime > _newestTimestamp)
            {
                _newestTimestamp = fi.LastWriteTime;
                NewFileDetected?.Invoke(this, fullPath);
            }
        }
        catch
        {
            // File inaccessible
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
