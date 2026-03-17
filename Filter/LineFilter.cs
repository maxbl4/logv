using System.Text;
using System.Text.RegularExpressions;

namespace lgv.Filter;

public enum FilterMode { Include, Exclude }

public static class LineFilter
{
    public record FilterResult(string FilteredText, int[] LineMap);

    public static FilterResult Apply(string originalText, string pattern, FilterMode mode, bool useRegex)
    {
        if (string.IsNullOrEmpty(pattern))
            return new FilterResult(originalText, []);

        Regex regex;
        try
        {
            var regexPattern = useRegex ? pattern : Regex.Escape(pattern);
            regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch
        {
            return new FilterResult(originalText, []);
        }

        var lines = originalText.Split('\n');
        var filteredLines = new List<string>();
        var lineMap = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool matches = regex.IsMatch(line);
            bool keep = mode == FilterMode.Include ? matches : !matches;

            if (keep)
            {
                filteredLines.Add(line);
                lineMap.Add(i + 1); // 1-based original line number
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < filteredLines.Count; i++)
        {
            sb.Append(filteredLines[i]);
            if (i < filteredLines.Count - 1)
                sb.Append('\n');
        }

        return new FilterResult(sb.ToString(), [.. lineMap]);
    }
}
