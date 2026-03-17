using System.IO;
using System.Text;
using System.Timers;

namespace lgv.Core;

public sealed class FileTailer : IDisposable
{
    private readonly string _filePath;
    private readonly System.Timers.Timer _timer;
    private long _lastPosition;
    private bool _disposed;
    private bool _started;

    public event EventHandler<string>? NewContent;

    public FileTailer(string filePath, int intervalMs = 500)
    {
        _filePath = filePath;
        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        if (_disposed) return;
        if (_started) return;
        _started = true;

        // Read full file content immediately
        ReadAndEmit(fullRead: true);

        // Then start polling for new content
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        ReadAndEmit(fullRead: false);
    }

    private void ReadAndEmit(bool fullRead)
    {
        try
        {
            var fi = new System.IO.FileInfo(_filePath);
            if (!fi.Exists) return;

            // Detect rotation/truncation
            if (fi.Length < _lastPosition)
            {
                _lastPosition = 0;
            }

            if (fullRead)
            {
                _lastPosition = 0;
            }

            if (fi.Length == _lastPosition && !fullRead) return;

            using var fs = new System.IO.FileStream(
                _filePath,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);

            fs.Seek(_lastPosition, System.IO.SeekOrigin.Begin);

            long toRead = fs.Length - _lastPosition;
            if (toRead <= 0) return;

            var newBytes = new byte[toRead];
            int bytesRead = fs.Read(newBytes, 0, newBytes.Length);
            _lastPosition = fs.Position;

            if (bytesRead > 0)
            {
                var text = Encoding.UTF8.GetString(newBytes, 0, bytesRead);
                NewContent?.Invoke(this, text);
            }
        }
        catch
        {
            // File locked, deleted, etc. — try again next tick
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
