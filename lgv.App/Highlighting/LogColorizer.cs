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
            var matchFg = ParseColor(rule.MatchForeground);
            if (!matchFg.HasValue) continue;

            if (rule.ApplyToFullLine && !regex.IsMatch(lineText)) continue;

            var fgBrush = new SolidColorBrush(matchFg.Value);
            fgBrush.Freeze();

            foreach (Match m in regex.Matches(lineText))
            {
                int start = line.Offset + m.Index;
                int end   = start + m.Length;
                ChangeLinePart(start, end, el =>
                    el.TextRunProperties.SetForegroundBrush(fgBrush));
            }
        }
    }
}
