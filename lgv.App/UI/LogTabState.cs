using System.Text;
using lgv.Core;

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
    public bool AutoScroll { get; set; }

    // Document state — StringBuilder avoids repeated LOH allocations on every append
    private StringBuilder _originalText = new();
    public string OriginalText
    {
        get => _originalText.ToString();
        set { _originalText.Clear(); _originalText.Append(value); }
    }
    public void AppendOriginalText(string text) => _originalText.Append(text);

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
