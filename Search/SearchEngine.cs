using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;

namespace lgv.Search;

public static class SearchEngine
{
    public record SearchResult(int Offset, int Length, int Line);

    public static IReadOnlyList<SearchResult> FindAll(
        string text,
        TextDocument document,
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

        foreach (Match m in regex.Matches(text))
        {
            ct.ThrowIfCancellationRequested();

            int lineNumber;
            try
            {
                lineNumber = document.GetLineByOffset(m.Index).LineNumber;
            }
            catch
            {
                lineNumber = 1;
            }

            results.Add(new SearchResult(m.Index, m.Length, lineNumber));
        }

        return results;
    }
}
