using System.IO;

namespace lgv.Core;

public sealed class FileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public event EventHandler? Changed;

    public FileWatcher(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var file = Path.GetFileName(filePath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = false
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public void Start()
    {
        if (_disposed) return;
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_disposed) return;
        _watcher.EnableRaisingEvents = false;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
