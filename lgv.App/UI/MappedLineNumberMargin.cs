using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
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

    // Snapshot captured by RefreshVisibleLinesHelpText (called at Loaded priority, outside
    // the render pipeline).  Non-null means OnRender must use this snapshot rather than
    // calling EnsureVisualLines() directly (which yields stale results inside OnRender).
    private record struct LineEntry(int Display, double Y);
    private LineEntry[]? _forcedSnapshot;

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
            oldTextView.DocumentChanged -= OnDocumentObjectChanged;

        UnsubscribeDocument();

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView is not null)
        {
            newTextView.DocumentChanged += OnDocumentObjectChanged;
            SubscribeDocument(newTextView.Document);
        }

        RefreshAutomationValue();
        InvalidateMeasure();
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
    // This is the fix for the width-never-updates bug: previously we only listened to
    // DocumentChanged (document object replaced), but LogViewerControl always mutates
    // the same TextDocument instance, so DocumentChanged never fired after startup.
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

        // Use '9' repeated — widest digit — so the margin never clips numbers.
        // Read font from TextView so we match the editor's actual font (e.g. Consolas).
        var ft = MakeText(new string('9', maxLine.ToString().Length));
        return new WpfSize(ft.Width + 6, 0);
    }

    protected override void OnRender(WpfDrawingContext dc)
    {
        if (TextView is null || Document is null) return;

        var foreground = (WpfBrush?)GetValue(WpfControl.ForegroundProperty) ?? WpfBrushes.Gray;
        double width = RenderSize.Width;

        LineEntry[] entries;

        if (_forcedSnapshot is not null)
        {
            // A forced snapshot was prepared by RefreshVisibleLinesHelpText (called at
            // Loaded priority, outside the render pipeline, where EnsureVisualLines works
            // correctly).  Use it and clear it so normal rendering takes over afterwards.
            entries = _forcedSnapshot;
            _forcedSnapshot = null;
        }
        else
        {
            // Normal render path: VisualLines are valid here because AvalonEdit's own
            // measure/arrange pass ran before this OnRender call.  Do NOT call
            // EnsureVisualLines() here — it calls UpdateLayout() which triggers a nested
            // layout at the wrong scroll offset, causing the margin to always show line
            // 1..N regardless of scroll position.
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

        // HelpText always mirrors what was just drawn — the test reads from here so it
        // sees the same numbers the user sees.
        string visibleStr = visibleNums.ToString();
        if (visibleStr != _lastVisibleHelpText)
        {
            _lastVisibleHelpText = visibleStr;
            AutomationProperties.SetHelpText(this, visibleStr);
        }
    }

    /// <summary>
    /// Captures the currently visible lines via <see cref="TextView.EnsureVisualLines"/>
    /// (safe at <see cref="System.Windows.Threading.DispatcherPriority.Loaded"/>, outside
    /// the rendering pipeline), stores them as a forced snapshot, then calls
    /// <see cref="InvalidateVisual"/> so the next <see cref="OnRender"/> uses the snapshot
    /// instead of the stale <see cref="TextView.VisualLines"/>.
    /// </summary>
    public void RefreshVisibleLinesHelpText()
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

        _forcedSnapshot = snapshot;
        InvalidateVisual(); // triggers OnRender which draws the snapshot and updates HelpText
    }

    // Read font from TextView so we use the editor's font, not the margin's inherited default.
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

    // Always called on the UI thread (from event handlers and SetLineMap).
    // Writes to AutomationProperties.Name so tests can read it via el.Current.Name —
    // a standard UIA property that works reliably across process boundaries without
    // needing a custom IValueProvider pattern (which isn't reliably proxied for
    // FrameworkElement subclasses in out-of-process UIA).
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

    // A minimal peer is required so the element appears in the UIA tree at all;
    // the actual data is exposed via Name (all lines) and HelpText (visible lines).
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);
}
