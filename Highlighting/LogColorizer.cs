using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace lgv.Highlighting;

public class LogColorizer : DocumentColorizingTransformer
{
    private readonly List<(PatternRule Rule, Regex Compiled)> _rules = [];

    public LogColorizer(IEnumerable<PatternRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
                continue;
            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
                _rules.Add((rule, regex));
            }
            catch
            {
                // Invalid regex — skip
            }
        }
    }

    private static System.Windows.Media.Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[0..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex[0..2], 16);
                byte r = Convert.ToByte(hex[2..4], 16);
                byte g = Convert.ToByte(hex[4..6], 16);
                byte b = Convert.ToByte(hex[6..8], 16);
                return System.Windows.Media.Color.FromArgb(a, r, g, b);
            }
        }
        catch { }
        return null;
    }

    protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
    {
        var lineText = CurrentContext.Document.GetText(line.Offset, line.Length);

        foreach (var (rule, regex) in _rules)
        {
            var lineBg = ParseColor(rule.LineBackground);
            var matchFg = ParseColor(rule.MatchForeground);
            var matchBg = ParseColor(rule.MatchBackground);

            if (rule.ApplyToFullLine)
            {
                // Check if the line matches at all
                if (!regex.IsMatch(lineText)) continue;

                // Apply line background to the whole line
                if (lineBg.HasValue)
                {
                    var bgBrush = new SolidColorBrush(lineBg.Value);
                    bgBrush.Freeze();
                    ChangeLinePart(line.Offset, line.EndOffset, el =>
                    {
                        el.TextRunProperties.SetBackgroundBrush(bgBrush);
                    });
                }

                // Apply match foreground to each match within the line
                if (matchFg.HasValue)
                {
                    var fgBrush = new SolidColorBrush(matchFg.Value);
                    fgBrush.Freeze();
                    foreach (Match m in regex.Matches(lineText))
                    {
                        int start = line.Offset + m.Index;
                        int end = start + m.Length;
                        ChangeLinePart(start, end, el =>
                        {
                            el.TextRunProperties.SetForegroundBrush(fgBrush);
                        });
                    }
                }
            }
            else
            {
                // Match-only highlighting
                foreach (Match m in regex.Matches(lineText))
                {
                    int start = line.Offset + m.Index;
                    int end = start + m.Length;

                    SolidColorBrush? fg = matchFg.HasValue
                        ? FreezeBrush(new SolidColorBrush(matchFg.Value))
                        : null;
                    SolidColorBrush? bg = matchBg.HasValue
                        ? FreezeBrush(new SolidColorBrush(matchBg.Value))
                        : null;

                    ChangeLinePart(start, end, el =>
                    {
                        if (fg is not null)
                            el.TextRunProperties.SetForegroundBrush(fg);
                        if (bg is not null)
                            el.TextRunProperties.SetBackgroundBrush(bg);
                    });
                }
            }
        }
    }

    private static SolidColorBrush FreezeBrush(SolidColorBrush b) { b.Freeze(); return b; }
}
