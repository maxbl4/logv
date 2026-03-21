using System.IO;
using System.Runtime.CompilerServices;

namespace lgv.UITests;

/// <summary>
/// End-to-end UI tests that launch the real LGV executable, interact with it via UIAutomation,
/// and verify line numbers displayed in the margin.
///
/// Prerequisites:
///   - Build lgv.App in Debug|x64 before running: dotnet build -c Debug lgv.App/lgv.csproj
///   - Tests run on Windows with a live desktop session (not headless).
/// </summary>
public sealed class LineNumberDisplayTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string ExePath()
    {
        // Resolve relative to this source file so it works from any working directory.
        string thisDir = ThisSourceDir();
        // MSBuild places the output under bin\<Platform>\<Configuration>\<TFM>\ when Platform is set.
        string candidate = Path.GetFullPath(
            Path.Combine(thisDir, "..", "lgv.App", "bin", "x64", "Debug", "net10.0-windows", "lgv.exe"));

        if (!File.Exists(candidate))
        {
            string release = Path.GetFullPath(
                Path.Combine(thisDir, "..", "lgv.App", "bin", "x64", "Release", "net10.0-windows", "lgv.exe"));
            if (File.Exists(release)) return release;

            throw new FileNotFoundException(
                $"lgv.exe not found. Build lgv.App first (dotnet build -c Debug -p:Platform=x64).\nLast tried: {candidate}");
        }

        return candidate;
    }

    private static string ThisSourceDir([CallerFilePath] string? path = null) =>
        Path.GetDirectoryName(path) ?? ".";

    /// <summary>Creates a temporary log file with the given lines and returns its path.</summary>
    private static string WriteTestFile(params string[] lines)
    {
        string path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }

    private readonly List<string> _tempFiles = new();
    private AppDriver? _driver;

    private AppDriver StartApp(string filePath)
    {
        _tempFiles.Add(filePath);
        _driver = AppDriver.Launch(ExePath(), filePath);
        return _driver;
    }

    public void Dispose()
    {
        _driver?.Dispose();
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Sequential line numbers shown when file is loaded without filter")]
    public void NoFilter_ShowsSequentialLineNumbers()
    {
        string file = WriteTestFile(
            "alpha line",
            "beta line",
            "gamma line",
            "delta line",
            "epsilon line");

        var app = StartApp(file);

        string lineNumbers = app.GetLineNumbers(TimeSpan.FromSeconds(12));

        Assert.False(string.IsNullOrEmpty(lineNumbers),
            "Line number margin returned empty value — check app startup and AutomationPeer.");

        var numbers = lineNumbers.Split(',').Select(int.Parse).ToArray();

        // The file has 5 lines; expect 1..5 (file may have trailing newline, so ≥5)
        Assert.True(numbers.Length >= 5,
            $"Expected at least 5 line numbers, got: {lineNumbers}");

        // Numbers must be sequential starting at 1
        for (int i = 0; i < 5; i++)
            Assert.Equal(i + 1, numbers[i]);
    }

    [Fact(DisplayName = "Original line numbers shown after filter is applied (exclusion mode)")]
    public void WithFilter_ShowsOriginalLineNumbers()
    {
        // The global filter is an EXCLUSION filter: it hides lines that match.
        // Lines 2 and 4 contain "NOISE" and will be hidden.
        // The margin must show the original line numbers of the KEPT lines (1, 3, 5).
        // File.WriteAllLines appends a trailing newline, giving AvalonEdit a 6th empty line.
        string file = WriteTestFile(
            "INFO  startup",   // line 1  — kept
            "NOISE junk",      // line 2  — excluded by filter
            "INFO  heartbeat", // line 3  — kept
            "NOISE garbage",   // line 4  — excluded by filter
            "INFO  shutdown"); // line 5  — kept
                               // line 6  — empty (trailing newline), kept

        var app = StartApp(file);

        string preFilter = app.GetLineNumbers(TimeSpan.FromSeconds(12));

        // Apply exclusion filter, then wait for the value to change from the pre-filter state.
        app.SetGlobalFilter("NOISE");

        string lineNumbers = app.WaitUntilLineNumbersChange(preFilter, TimeSpan.FromSeconds(8));

        Assert.False(string.IsNullOrEmpty(lineNumbers),
            "Line number margin returned empty value after filter.");

        var numbers = lineNumbers.Split(',').Select(int.Parse).ToArray();

        // Kept lines are 1, 3, 5 (plus the trailing empty line 6).
        // The margin must show original line numbers, not sequential filtered-doc positions.
        Assert.Equal(1, numbers[0]);
        Assert.Equal(3, numbers[1]);
        Assert.Equal(5, numbers[2]);
    }

    [Fact(DisplayName = "Sequential line numbers restored after filter is cleared")]
    public void ClearFilter_RestoresSequentialLineNumbers()
    {
        string file = WriteTestFile(
            "INFO  startup",
            "ERROR disk full",
            "INFO  heartbeat",
            "ERROR network timeout",
            "INFO  shutdown");

        var app = StartApp(file);

        // Load, filter, then clear
        string preFilter = app.GetLineNumbers(TimeSpan.FromSeconds(12));
        app.SetGlobalFilter("ERROR");
        string filtered = app.WaitUntilLineNumbersChange(preFilter, TimeSpan.FromSeconds(8));
        app.ClearGlobalFilter();

        string lineNumbers = app.WaitUntilLineNumbersChange(filtered, TimeSpan.FromSeconds(8));

        Assert.False(string.IsNullOrEmpty(lineNumbers), "Line number margin empty after clearing filter.");

        var numbers = lineNumbers.Split(',').Select(int.Parse).ToArray();
        Assert.True(numbers.Length >= 5, $"Expected ≥5 line numbers after clear, got: {lineNumbers}");
        for (int i = 0; i < 5; i++)
            Assert.Equal(i + 1, numbers[i]);
    }

    [Fact(DisplayName = "Single line excluded by filter — margin skips that line number")]
    public void SingleMatchFilter_SkipsExcludedLineNumber()
    {
        // Exclusion filter "SPECIAL" hides only line 4.
        // The margin must show 1, 2, 3, 5, 6, 7 (skipping 4) — original file positions.
        // File.WriteAllLines with 6 strings → 7 document lines (trailing empty line).
        string file = WriteTestFile(
            "INFO  a",            // line 1
            "INFO  b",            // line 2
            "INFO  c",            // line 3
            "SPECIAL event here", // line 4  — excluded
            "INFO  e",            // line 5
            "INFO  f");           // line 6
                                  // line 7  — empty (trailing newline)

        var app = StartApp(file);
        string preFilter = app.GetLineNumbers(TimeSpan.FromSeconds(12));

        app.SetGlobalFilter("SPECIAL");

        string lineNumbers = app.WaitUntilLineNumbersChange(preFilter, TimeSpan.FromSeconds(8));
        Assert.False(string.IsNullOrEmpty(lineNumbers));

        var numbers = lineNumbers.Split(',').Select(int.Parse).ToArray();

        // Line 4 must be absent; lines 1,2,3,5,6 must be present (7 is the empty trailing line).
        Assert.DoesNotContain(4, numbers);
        Assert.Contains(1, numbers);
        Assert.Contains(3, numbers);
        Assert.Contains(5, numbers);
        // Numbers must jump from 3 to 5 (gap proves original numbering is preserved)
        int idx3 = Array.IndexOf(numbers, 3);
        int idx5 = Array.IndexOf(numbers, 5);
        Assert.True(idx5 == idx3 + 1,
            $"Expected 5 to immediately follow 3, but got: {lineNumbers}");
    }
}
