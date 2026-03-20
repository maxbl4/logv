using System.Globalization;
using System.Windows;
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

    public void SetLineMap(int[]? lineMap)
    {
        _lineMap = lineMap;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
            oldTextView.DocumentChanged -= OnDocumentChanged;

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView is not null)
            newTextView.DocumentChanged += OnDocumentChanged;

        InvalidateMeasure();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => InvalidateMeasure();

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

        TextView.EnsureVisualLines();

        foreach (var vl in TextView.VisualLines)
        {
            int docLine = vl.FirstDocumentLine.LineNumber; // 1-based in filtered doc
            int display = (_lineMap is not null && docLine <= _lineMap.Length)
                ? _lineMap[docLine - 1]
                : docLine;

            double y = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop)
                       - TextView.VerticalOffset;

            var ft = MakeText(display.ToString(), foreground);
            dc.DrawText(ft, new WpfPoint(width - ft.Width - 2, y));
        }
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
}
