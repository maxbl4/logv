using System.Text.RegularExpressions;

namespace lgv.Search;

public static class SearchEngine
{
    public record SearchResult(int Offset, int Length, int Line);

    public static IReadOnlyList<SearchResult> FindAll(
        string text,
        string query,
        bool caseSensitive,
        bool useRegex,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(query))
            return [];

        Regex regex;
        try
        {
            var options = RegexOptions.None;
            if (!caseSensitive) options |= RegexOptions.IgnoreCase;
            var pattern = useRegex ? query : Regex.Escape(query);
            regex = new Regex(pattern, options);
        }
        catch
        {
            return [];
        }

        var results = new List<SearchResult>();

        // Compute line numbers by scanning the text string — safe to call from any thread,
        // unlike TextDocument.GetLineByOffset() which requires the UI thread.
        int lineNumber = 1;
        int prevOffset = 0;

        foreach (Match m in regex.Matches(text))
        {
            ct.ThrowIfCancellationRequested();

            for (int i = prevOffset; i < m.Index; i++)
                if (text[i] == '\n') lineNumber++;

            results.Add(new SearchResult(m.Index, m.Length, lineNumber));
            prevOffset = m.Index;
        }

        return results;
    }
}
