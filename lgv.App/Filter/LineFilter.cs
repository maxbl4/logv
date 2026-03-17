using System.Text;
using System.Text.RegularExpressions;

namespace lgv.Filter;

public static class LineFilter
{
    public record FilterResult(string FilteredText, int[] LineMap, int ValidPatterns, int InvalidPatterns);

    /// <summary>
    /// Filters lines using multiple regex patterns (one per entry).
    /// Lines starting with # are treated as comments and ignored.
    /// Keeps lines that match NO pattern (always exclude mode).
    /// </summary>
    public static FilterResult Apply(string originalText, IReadOnlyList<string> patterns)
    {
        var activePatterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p) && !p.TrimStart().StartsWith('#'))
            .ToArray();

        if (activePatterns.Length == 0)
            return new FilterResult(originalText, Enumerable.Range(1, originalText.Split('\n').Length).ToArray(), 0, 0);

        int invalid = 0;
        var regexes = new List<Regex>();
        foreach (var p in activePatterns)
        {
            try
            {
                regexes.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch
            {
                invalid++;
            }
        }

        if (regexes.Count == 0)
            return new FilterResult(originalText, [], 0, invalid);

        // Normalize \r\n and lone \r to \n so no \r characters end up
        // embedded as line content in the filtered document (which would
        // shift caret positions and break selection in AvalonEdit).
        var normalized = originalText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var filteredLines = new List<string>(lines.Length);
        var lineMap = new List<int>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool anyMatch = regexes.Any(rx => rx.IsMatch(line));
            bool keep = !anyMatch;

            if (keep)
            {
                filteredLines.Add(line);
                lineMap.Add(i + 1); // 1-based original line number
            }
        }

        var sb = new StringBuilder(filteredLines.Sum(l => l.Length + 1));
        for (int i = 0; i < filteredLines.Count; i++)
        {
            sb.Append(filteredLines[i]);
            if (i < filteredLines.Count - 1)
                sb.Append('\n');
        }

        return new FilterResult(sb.ToString(), [.. lineMap], regexes.Count, invalid);
    }
}
