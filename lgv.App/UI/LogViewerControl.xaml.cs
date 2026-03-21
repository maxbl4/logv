using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using lgv.Core;
using lgv.Filter;
using lgv.Highlighting;
using lgv.Search;
using LogBgRenderer = lgv.Highlighting.LogBackgroundRenderer;

// Avoid ambiguity between WPF and WinForms
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSystemColors = System.Windows.SystemColors;

namespace lgv.UI;

public partial class LogViewerControl : WpfUserControl
{
    public AppSettings Settings { get; set; } = new();

    private LogTabState _state = new();
    private readonly SearchMarkerRenderer _searchRenderer = new();
    private readonly MappedLineNumberMargin _lineNumberMargin = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _filterCts;
    private readonly DispatcherTimer _searchDebounce;
    private string[] _activeFilterPatterns = [];
    private LogColorizer? _colorizer;
    private LogBgRenderer? _bgRenderer;

    // Events for MainWindow status bar
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? FilePathChanged;

    public LogTabState State => _state;

    public LogViewerControl()
    {
        InitializeComponent();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RunSearch(); };

        Editor.TextArea.TextView.BackgroundRenderers.Add(_searchRenderer);

        Editor.KeyDown += Editor_KeyDown;
        Editor.TextArea.KeyDown += Editor_KeyDown;

        Editor.TextChanged += (_, _) => UpdateStatusBar();
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatusBar();

        SetupEditor();

        Loaded += (_, _) => SetupColorizer();
    }

    private void SetupEditor()
    {
        Editor.IsReadOnly = true;
        Editor.ShowLineNumbers = false;
        Editor.WordWrap = false;
        Editor.Background = WpfSystemColors.WindowBrush;
        Editor.Foreground = WpfSystemColors.WindowTextBrush;
        Editor.SyntaxHighlighting = null;

        _lineNumberMargin.SetValue(System.Windows.Controls.Control.ForegroundProperty, WpfSystemColors.GrayTextBrush);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_lineNumberMargin, "LineNumberMargin");
        Editor.TextArea.LeftMargins.Add(_lineNumberMargin);

        // Tag the internal ScrollViewer so UI tests can scroll via ScrollPattern reliably.
        Loaded += (_, _) =>
        {
            var sv = FindScrollViewer(Editor);
            if (sv is not null)
                System.Windows.Automation.AutomationProperties.SetAutomationId(sv, "EditorScrollViewer");
        };
    }

    private void SetupColorizer()
    {
        // Remove old foreground colorizer
        var toRemove = Editor.TextArea.TextView.LineTransformers
            .OfType<LogColorizer>().ToList();
        foreach (var c in toRemove)
            Editor.TextArea.TextView.LineTransformers.Remove(c);

        // Remove old background renderer
        var bgToRemove = Editor.TextArea.TextView.BackgroundRenderers
            .OfType<LogBgRenderer>().ToList();
        foreach (var r in bgToRemove)
            Editor.TextArea.TextView.BackgroundRenderers.Remove(r);

        if (Settings.HighlightingEnabled && Settings.Patterns.Count > 0)
        {
            _colorizer  = new LogColorizer(Settings.Patterns);
            _bgRenderer = new LogBgRenderer(Settings.Patterns);

            Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
            // Insert before search renderer so line backgrounds sit underneath search ticks
            Editor.TextArea.TextView.BackgroundRenderers.Insert(0, _bgRenderer);
        }

        Editor.FontFamily = new System.Windows.Media.FontFamily(Settings.FontFamily);
        Editor.FontSize = Settings.FontSize;
    }

    public void RefreshColorizer()
    {
        SetupColorizer();
        Editor.TextArea.TextView.Redraw();
    }

    public void LoadFile(string path)
    {
        _state.Dispose();
        _state = new LogTabState { FilePath = path };

        FilePathChanged?.Invoke(this, path);

        _state.StartTailing(path, Settings.PollIntervalMs);
        _state.Tailer!.NewContent += OnNewContent;
        _state.Tailer.Start();
    }

    public void LoadDirectory(string dir)
    {
        _state.Dispose();
        _state = new LogTabState { DirectoryPath = dir };

        _state.StartDirectoryMonitor(dir);
        var newestFile = _state.Monitor!.GetNewestFile();

        if (newestFile is not null)
        {
            _state.FilePath = newestFile;
            _state.StartTailing(newestFile, Settings.PollIntervalMs);
            _state.Tailer!.NewContent += OnNewContent;
            _state.Tailer.Start();
            FilePathChanged?.Invoke(this, newestFile);
        }
        else
        {
            SetEditorText($"[Directory monitor active: {dir}]\n[No files found yet]");
            StatusChanged?.Invoke(this, "Watching directory...");
        }
    }

    public void RestoreState(TabState saved)
    {
        if (!string.IsNullOrEmpty(saved.SearchQuery))
        {
            SearchBox.Text = saved.SearchQuery;
            SearchCaseChk.IsChecked = saved.SearchCaseSensitive;
            SearchRegexChk.IsChecked = saved.SearchUseRegex;
        }

        _state.AutoScroll = saved.AutoScroll;
        _state.ScrollOffset = saved.ScrollOffset;

        Dispatcher.InvokeAsync(() =>
        {
            if (saved.ScrollOffset > 0)
            {
                var scrollViewer = FindScrollViewer(Editor);
                scrollViewer?.ScrollToVerticalOffset(saved.ScrollOffset);
            }
        }, DispatcherPriority.Loaded);
    }

    public TabState SaveState()
    {
        var scrollViewer = FindScrollViewer(Editor);
        return new TabState
        {
            FilePath = _state.FilePath,
            DirectoryPath = _state.DirectoryPath,
            ScrollOffset = scrollViewer?.VerticalOffset ?? 0,
            SearchQuery = SearchBox.Text,
            SearchCaseSensitive = SearchCaseChk.IsChecked == true,
            SearchUseRegex = SearchRegexChk.IsChecked == true,
            AutoScroll = _state.AutoScroll,
            WatchNewFiles = _state.DirectoryPath is not null
        };
    }

    private void OnNewContent(object? sender, string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            AppendContent(text);
        }, DispatcherPriority.Background);
    }

    private void AppendContent(string newText)
    {
        _state.AppendOriginalText(newText);

        if (_activeFilterPatterns.Length > 0)
        {
            // Re-run filter to incorporate new content
            RunGlobalFilter(_activeFilterPatterns);
            return;
        }

        var scrollViewer = FindScrollViewer(Editor);
        double savedOffset = scrollViewer?.VerticalOffset ?? 0;
        int savedSelStart = Editor.SelectionStart;
        int savedSelLen = Editor.SelectionLength;

        Editor.Document.Insert(Editor.Document.TextLength, newText);

        if (_state.AutoScroll)
        {
            Editor.ScrollToEnd();
        }
        else
        {
            scrollViewer?.ScrollToVerticalOffset(savedOffset);
            Editor.Select(savedSelStart, savedSelLen);
        }

        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        DrawTicks();
        UpdateStatusBar();
    }

    private void SetEditorText(string text)
    {
        _state.OriginalText = text;
        Editor.Document.Text = text;
        UpdateStatusBar();
    }

    // Called by MainWindow to apply/update the global filter
    public void ApplyGlobalFilter(string[] patterns)
    {
        _activeFilterPatterns = patterns;
        RunGlobalFilter(patterns);
    }

    private void RunGlobalFilter(string[] patterns)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var ct = _filterCts.Token;
        var originalText = _state.OriginalText;

        if (patterns.Length == 0)
        {
            var scrollViewer = FindScrollViewer(Editor);
            double savedOffset = scrollViewer?.VerticalOffset ?? 0;
            Editor.Document.Text = originalText;
            _lineNumberMargin.SetLineMap(null);
            if (!_state.AutoScroll)
                scrollViewer?.ScrollToVerticalOffset(savedOffset);
            UpdateStatusBar();
            return;
        }

        Task.Run(() =>
        {
            LineFilter.FilterResult result;
            try
            {
                result = LineFilter.Apply(originalText, patterns);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { return; }

            Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;

                var scrollViewer = FindScrollViewer(Editor);
                double savedOffset = scrollViewer?.VerticalOffset ?? 0;
                Editor.Document.Text = result.FilteredText;
                _lineNumberMargin.SetLineMap(result.LineMap);

                if (_state.AutoScroll)
                    Editor.ScrollToEnd();
                else
                    scrollViewer?.ScrollToVerticalOffset(savedOffset);

                UpdateStatusBar();
            });
        }, ct);
    }

    public void ToggleSearch()
    {
        if (SearchBar.Visibility == Visibility.Visible)
        {
            SearchBar.Visibility = Visibility.Collapsed;
            ClearSearchHighlights();
        }
        else
        {
            SearchBar.Visibility = Visibility.Visible;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    public void SetAutoScroll(bool value)
    {
        _state.AutoScroll = value;
        if (value)
            Editor.ScrollToEnd();
    }

    private void RunSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var query = SearchBox.Text;
        var caseSensitive = SearchCaseChk.IsChecked == true;
        var useRegex = SearchRegexChk.IsChecked == true;
        var text = Editor.Document.Text;
        var doc = Editor.Document;

        if (string.IsNullOrEmpty(query))
        {
            ClearSearchHighlights();
            return;
        }

        Task.Run(() =>
        {
            IReadOnlyList<SearchEngine.SearchResult> results;
            try
            {
                results = SearchEngine.FindAll(text, doc, query, caseSensitive, useRegex, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested) return;

                int currentIdx = _state.SearchCurrentIndex < results.Count
                    ? _state.SearchCurrentIndex : 0;

                _searchRenderer.Update(results, currentIdx);
                Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);

                string countText = results.Count == 0
                    ? "No matches"
                    : $"{(currentIdx + 1)} of {results.Count}";
                SearchCountLabel.Content = countText;

                DrawTicks();
            });
        }, ct);
    }

    private void ClearSearchHighlights()
    {
        _searchRenderer.Update([], -1);
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        SearchCountLabel.Content = "0 of 0";
        TickCanvas.Children.Clear();
    }

    public void NavigateToMatch(int index)
    {
        var results = _searchRenderer.Results;
        if (results.Count == 0) return;

        index = ((index % results.Count) + results.Count) % results.Count;
        _state.SearchCurrentIndex = index;

        _searchRenderer.Update(results, index);
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);

        SearchCountLabel.Content = $"{index + 1} of {results.Count}";

        var result = results[index];
        Editor.Select(result.Offset, result.Length);
        Editor.ScrollTo(result.Line, 0);

        var docLine = Editor.Document.GetLineByOffset(result.Offset);
        Editor.TextArea.Caret.Offset = result.Offset;
        Editor.ScrollToLine(docLine.LineNumber);
    }

    public void NavigateNext() => NavigateToMatch(_state.SearchCurrentIndex + 1);
    public void NavigatePrev() => NavigateToMatch(_state.SearchCurrentIndex - 1);

    public void ShowGoToLineDialog()
    {
        var dialog = new GoToLineDialog(Editor.Document.LineCount)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            Editor.ScrollToLine(dialog.LineNumber);
            var line = Editor.Document.GetLineByNumber(dialog.LineNumber);
            Editor.TextArea.Caret.Offset = line.Offset;
        }
    }

    private void DrawTicks()
    {
        TickCanvas.Children.Clear();
        var results = _searchRenderer.Results;
        if (results.Count == 0) return;

        int totalLines = Editor.Document.LineCount;
        if (totalLines == 0) return;

        double trackHeight = TickCanvas.ActualHeight;
        if (trackHeight <= 0) trackHeight = ActualHeight;
        if (trackHeight <= 0) return;

        var goldBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00));
        goldBrush.Freeze();

        foreach (var result in results)
        {
            double proportion = (double)(result.Line - 1) / Math.Max(1, totalLines - 1);
            double top = proportion * trackHeight;

            var rect = new WpfRectangle
            {
                Width = TickCanvas.Width > 0 ? TickCanvas.Width : 17,
                Height = 2,
                Fill = goldBrush
            };

            Canvas.SetTop(rect, top);
            Canvas.SetLeft(rect, 0);
            TickCanvas.Children.Add(rect);
        }
    }

    private void UpdateStatusBar()
    {
        var caret = Editor.TextArea.Caret;
        int totalLines = Editor.Document.LineCount;
        string status = $"Line {caret.Line}, Col {caret.Column} / {totalLines} lines";
        StatusChanged?.Invoke(this, status);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? parent)
    {
        if (parent is null) return null;
        if (parent is ScrollViewer sv) return sv;

        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            var result = FindScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }

    // ---- Event Handlers ----

    private void Editor_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                NavigatePrev();
            else
                NavigateNext();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            ToggleSearch();
        }
        else if (e.Key == Key.G && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            ShowGoToLineDialog();
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            ReloadFile();
        }
    }

    public void ReloadFile()
    {
        if (_state.FilePath is null) return;
        _state.Tailer?.Dispose();
        _state.OriginalText = "";
        Editor.Document.Text = "";
        _state.StartTailing(_state.FilePath, Settings.PollIntervalMs);
        _state.Tailer!.NewContent += OnNewContent;
        _state.Tailer.Start();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
        _state.SearchCurrentIndex = 0;
    }

    private void SearchBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                NavigatePrev();
            else
                NavigateNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBar.Visibility = Visibility.Collapsed;
            ClearSearchHighlights();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                NavigatePrev();
            else
                NavigateNext();
            e.Handled = true;
        }
    }

    private void SearchOptions_Changed(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void ScrollToTopBtn_Click(object sender, RoutedEventArgs e)
    {
        Editor.ScrollToHome();
        ReportVisibleLinesAfterLayout(sender as System.Windows.Controls.Button);
    }

    private void ScrollToEndBtn_Click(object sender, RoutedEventArgs e)
    {
        Editor.ScrollToEnd();
        ReportVisibleLinesAfterLayout(sender as System.Windows.Controls.Button);
    }

    /// <summary>
    /// Queues a non-blocking <see cref="DispatcherPriority.Loaded"/> callback that toggles
    /// the button's HelpText to signal the scroll has completed.  By the time this Loaded
    /// callback runs, the Render pass has already executed, so the
    /// <see cref="MappedLineNumberMargin"/> has re-rendered and its own HelpText already
    /// reflects the new visible line numbers.  Tests read the actual displayed numbers from
    /// the margin's HelpText, not from a separate AvalonEdit API.
    /// </summary>
    private void ReportVisibleLinesAfterLayout(System.Windows.Controls.Button? btn)
    {
        if (btn is null) return;
        Dispatcher.InvokeAsync(() =>
        {
            // Refresh the margin's HelpText with the correct mapped line numbers for the
            // current scroll position.  EnsureVisualLines() is safe here (Loaded priority,
            // outside the rendering pipeline) and returns lines at the actual scroll offset.
            _lineNumberMargin.RefreshVisibleLinesHelpText();

            // Toggle the button HelpText last so the test can use it as a "scroll done"
            // signal: by the time the toggle is visible, the margin HelpText is already
            // up-to-date and ready to read.
            string cur = System.Windows.Automation.AutomationProperties.GetHelpText(btn);
            string toggle = cur.StartsWith("done") ? "ready" : "done";
            System.Windows.Automation.AutomationProperties.SetHelpText(btn, toggle);
        }, DispatcherPriority.Loaded);
    }

    private void SearchCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = Visibility.Collapsed;
        ClearSearchHighlights();
    }

    private void SearchNextBtn_Click(object sender, RoutedEventArgs e) => NavigateNext();
    private void SearchPrevBtn_Click(object sender, RoutedEventArgs e) => NavigatePrev();
}
