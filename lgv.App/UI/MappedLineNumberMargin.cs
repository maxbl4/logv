using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSize = System.Windows.Size;
using WpfPoint = System.Windows.Point;
using WpfDrawingContext = System.Windows.Media.DrawingContext;
using WpfTypeface = System.Windows.Media.Typeface;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFormattedText = System.Windows.Media.FormattedText;
using WpfControl = System.Windows.Controls.Control;
using WpfFontStyle = System.Windows.FontStyle;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace lgv.UI;

/// <summary>
/// Line-number margin that shows original source line numbers when a filter is active.
/// _lineMap[i] = original 1-based line number for filtered document line (i+1).
/// Pass null to revert to sequential numbering.
/// </summary>
public sealed class MappedLineNumberMargin : AbstractMargin
{
    private int[]? _lineMap;

    // Track the TextDocument we've subscribed to so we can unsubscribe when it changes.
    private TextDocument? _currentDocument;

    // Cached on the UI thread so UIAutomation can read it from any thread safely.
    private volatile string _automationValue = "";

    // Last set of visible line numbers written in OnRender — always reflects what was drawn.
    private string _lastVisibleHelpText = "";

    // Snapshot computed at Loaded priority (after layout has committed the scroll offset).
    // OnRender uses this when non-null, then clears it.
    private record struct LineEntry(int Display, double Y);
    private LineEntry[]? _snapshot;

    public void SetLineMap(int[]? lineMap)
    {
        _lineMap = lineMap;
        RefreshAutomationValue();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.DocumentChanged -= OnDocumentObjectChanged;
            oldTextView.ScrollOffsetChanged -= OnScrollOffsetChanged;
        }

        UnsubscribeDocument();

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView is not null)
        {
            newTextView.DocumentChanged += OnDocumentObjectChanged;
            newTextView.ScrollOffsetChanged += OnScrollOffsetChanged;
            SubscribeDocument(newTextView.Document);
        }

        RefreshAutomationValue();
        InvalidateMeasure();
    }

    // When the scroll offset changes, queue a Loaded-priority callback to capture the
    // correct visual lines.  EnsureVisualLines() is not safe inside OnRender (it calls
    // UpdateLayout() which runs a nested layout at the wrong offset), but it works
    // correctly at Loaded priority — after the Render-priority layout pass has finished.
    private void OnScrollOffsetChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(CaptureSnapshot, DispatcherPriority.Loaded);
    }

    // Fires when the TextDocument *object* is replaced on the TextView.
    private void OnDocumentObjectChanged(object? sender, EventArgs e)
    {
        UnsubscribeDocument();
        if (sender is TextView tv)
            SubscribeDocument(tv.Document);

        RefreshAutomationValue();
        InvalidateMeasure();
    }

    // Fires on every insert / delete inside the TextDocument.
    private void OnDocumentContentChanged(object? sender, DocumentChangeEventArgs e)
    {
        RefreshAutomationValue();
        InvalidateMeasure();
    }

    private void SubscribeDocument(TextDocument? doc)
    {
        _currentDocument = doc;
        if (doc is not null)
            doc.Changed += OnDocumentContentChanged;
    }

    private void UnsubscribeDocument()
    {
        if (_currentDocument is not null)
        {
            _currentDocument.Changed -= OnDocumentContentChanged;
            _currentDocument = null;
        }
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        int maxLine = _lineMap is { Length: > 0 }
            ? _lineMap[^1]
            : Math.Max(1, TextView?.Document?.LineCount ?? 1);

        var ft = MakeText(new string('9', maxLine.ToString().Length));
        return new WpfSize(ft.Width + 6, 0);
    }

    protected override void OnRender(WpfDrawingContext dc)
    {
        if (TextView is null || Document is null) return;

        var foreground = (WpfBrush?)GetValue(WpfControl.ForegroundProperty) ?? WpfBrushes.Gray;
        double width = RenderSize.Width;

        LineEntry[] entries;

        if (_snapshot is not null)
        {
            // Use the snapshot captured at Loaded priority where EnsureVisualLines() is
            // safe.  Clear it so subsequent normal repaints use VisualLines directly.
            entries = _snapshot;
            _snapshot = null;
        }
        else
        {
            // Normal repaint (e.g. document change, resize): VisualLines are valid here
            // because AvalonEdit's own measure/arrange ran before this OnRender.
            // Do NOT call EnsureVisualLines() — see OnScrollOffsetChanged for why.
            var vls = TextView.VisualLines;
            entries = new LineEntry[vls.Count];
            for (int i = 0; i < vls.Count; i++)
            {
                var vl = vls[i];
                int docLine = vl.FirstDocumentLine.LineNumber;
                int display = (_lineMap is not null && docLine <= _lineMap.Length)
                    ? _lineMap[docLine - 1]
                    : docLine;
                double y = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop)
                           - TextView.VerticalOffset;
                entries[i] = new LineEntry(display, y);
            }
        }

        var visibleNums = new System.Text.StringBuilder();
        foreach (var (display, y) in entries)
        {
            var ft = MakeText(display.ToString(), foreground);
            dc.DrawText(ft, new WpfPoint(width - ft.Width - 2, y));

            if (visibleNums.Length > 0) visibleNums.Append(',');
            visibleNums.Append(display);
        }

        // HelpText always mirrors what was just drawn so tests read the true display.
        string visibleStr = visibleNums.ToString();
        if (visibleStr != _lastVisibleHelpText)
        {
            _lastVisibleHelpText = visibleStr;
            AutomationProperties.SetHelpText(this, visibleStr);
        }
    }

    /// <summary>
    /// Called at <see cref="DispatcherPriority.Loaded"/> (both automatically on every scroll
    /// and explicitly by <see cref="RefreshVisibleLinesHelpText"/>).  Calls
    /// <see cref="TextView.EnsureVisualLines"/> — safe here because the Render-priority
    /// layout pass has already committed the new scroll offset — then stores a snapshot for
    /// the next <see cref="OnRender"/> and schedules a repaint.
    /// </summary>
    private void CaptureSnapshot()
    {
        if (TextView is null) return;
        TextView.EnsureVisualLines();

        var vls = TextView.VisualLines;
        var snapshot = new LineEntry[vls.Count];
        for (int i = 0; i < vls.Count; i++)
        {
            var vl = vls[i];
            int docLine = vl.FirstDocumentLine.LineNumber;
            int display = (_lineMap is not null && docLine <= _lineMap.Length)
                ? _lineMap[docLine - 1]
                : docLine;
            double y = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop)
                       - TextView.VerticalOffset;
            snapshot[i] = new LineEntry(display, y);
        }

        _snapshot = snapshot;
        InvalidateVisual();
    }

    /// <summary>
    /// Forces an immediate snapshot refresh.  Called by the scroll-test automation buttons
    /// at <see cref="DispatcherPriority.Loaded"/> so the margin HelpText is updated before
    /// the button's own HelpText is toggled (giving the test a reliable read point).
    /// </summary>
    public void RefreshVisibleLinesHelpText() => CaptureSnapshot();

    private WpfFormattedText MakeText(string s, WpfBrush? foreground = null)
    {
        var source = (DependencyObject?)TextView ?? this;
        return new WpfFormattedText(
            s,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new WpfTypeface(
                (WpfFontFamily)source.GetValue(TextBlock.FontFamilyProperty),
                (WpfFontStyle)source.GetValue(TextBlock.FontStyleProperty),
                (FontWeight)source.GetValue(TextBlock.FontWeightProperty),
                (FontStretch)source.GetValue(TextBlock.FontStretchProperty)),
            (double)source.GetValue(TextBlock.FontSizeProperty),
            foreground ?? WpfBrushes.Black,
            GetDpi());
    }

    private double GetDpi() =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

    private void RefreshAutomationValue()
    {
        if (_lineMap is { Length: > 0 })
            _automationValue = string.Join(",", _lineMap);
        else
        {
            int count = Document?.LineCount ?? 0;
            _automationValue = count == 0 ? "" : string.Join(",", Enumerable.Range(1, count));
        }
        AutomationProperties.SetName(this, _automationValue);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);
}
