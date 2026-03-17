using lgv.Core;
using lgv.Filter;

namespace lgv.UI;

public class LogTabState : IDisposable
{
    public string? FilePath { get; set; }
    public string? DirectoryPath { get; set; }
    public FileTailer? Tailer { get; private set; }
    public DirectoryMonitor? Monitor { get; private set; }

    // View state
    public double ScrollOffset { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public string SearchQuery { get; set; } = "";
    public bool SearchCaseSensitive { get; set; }
    public bool SearchUseRegex { get; set; }
    public int SearchCurrentIndex { get; set; }
    public string FilterQuery { get; set; } = "";
    public FilterMode FilterMode { get; set; } = FilterMode.Include;
    public bool FilterUseRegex { get; set; }
    public bool AutoScroll { get; set; }
    public bool FilterActive { get; set; }

    // Document state
    public string OriginalText { get; set; } = "";

    private bool _disposed;

    public void StartTailing(string filePath, int intervalMs)
    {
        Tailer?.Dispose();
        Tailer = new FileTailer(filePath, intervalMs);
    }

    public void StartDirectoryMonitor(string dirPath)
    {
        Monitor?.Dispose();
        Monitor = new DirectoryMonitor(dirPath);
        Monitor.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Tailer?.Dispose();
        Monitor?.Dispose();
    }
}
