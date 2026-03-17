using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using WpfColor = System.Windows.Media.Color;

namespace lgv.Highlighting;

/// <summary>
/// Draws line/match background colors at KnownLayer.Background so that
/// the selection highlight (rendered at KnownLayer.Selection) always paints
/// on top, giving consistent selection color on all lines.
/// </summary>
public class LogBackgroundRenderer : IBackgroundRenderer
{
    private readonly List<(PatternRule Rule, Regex Compiled, WpfColor? LineBg, WpfColor? MatchBg)> _rules = [];

    public KnownLayer Layer => KnownLayer.Background;

    public LogBackgroundRenderer(IEnumerable<PatternRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern)) continue;

            var lineBg  = ParseColor(rule.LineBackground);
            var matchBg = ParseColor(rule.MatchBackground);
            if (!lineBg.HasValue && !matchBg.HasValue) continue;

            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _rules.Add((rule, regex, lineBg, matchBg));
            }
            catch { }
        }
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_rules.Count == 0 || textView.Document is null) return;

        textView.EnsureVisualLines();

        foreach (var visualLine in textView.VisualLines)
        {
            var docLine  = visualLine.FirstDocumentLine;
            var lineText = textView.Document.GetText(docLine.Offset, docLine.Length);

            foreach (var (rule, regex, lineBg, matchBg) in _rules)
            {
                if (rule.ApplyToFullLine)
                {
                    if (!lineBg.HasValue || !regex.IsMatch(lineText)) continue;

                    var brush = new SolidColorBrush(lineBg.Value);
                    brush.Freeze();

                    var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
                    builder.AddSegment(textView, docLine);
                    var geo = builder.CreateGeometry();
                    if (geo is not null)
                        dc.DrawGeometry(brush, null, geo);
                }
                else
                {
                    if (!matchBg.HasValue) continue;

                    var brush = new SolidColorBrush(matchBg.Value);
                    brush.Freeze();

                    foreach (System.Text.RegularExpressions.Match m in regex.Matches(lineText))
                    {
                        var seg = new ICSharpCode.AvalonEdit.Document.TextSegment
                        {
                            StartOffset = docLine.Offset + m.Index,
                            Length      = m.Length
                        };

                        var builder = new BackgroundGeometryBuilder
                        {
                            CornerRadius      = 2,
                            AlignToWholePixels = true
                        };
                        builder.AddSegment(textView, seg);
                        var geo = builder.CreateGeometry();
                        if (geo is not null)
                            dc.DrawGeometry(brush, null, geo);
                    }
                }
            }
        }
    }

    private static WpfColor? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return WpfColor.FromRgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            if (hex.Length == 8)
                return WpfColor.FromArgb(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16));
        }
        catch { }
        return null;
    }
}
