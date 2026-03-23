using System.IO;
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
        {
            int lineCount = 1 + originalText.AsSpan().Count('\n');
            return new FilterResult(originalText, [.. Enumerable.Range(1, lineCount)], 0, 0);
        }

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

        // Use StringReader to iterate lines without allocating a normalized copy of the
        // entire text. StringReader.ReadLine() handles \r\n, \r, and \n transparently.
        var filteredLines = new List<string>();
        var lineMap = new List<int>();
        using var reader = new StringReader(originalText);
        int lineNumber = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            bool anyMatch = regexes.Any(rx => rx.IsMatch(line));
            if (!anyMatch)
            {
                filteredLines.Add(line);
                lineMap.Add(lineNumber);
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
