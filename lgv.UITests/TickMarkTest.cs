using System.IO;
using System.Runtime.CompilerServices;

namespace lgv.UITests;

/// <summary>
/// Verifies that search-result tick marks on the scrollbar are positioned at the correct
/// lines — not all bunched at the top (which was the bug when GetLineByOffset was called
/// from a background thread and silently defaulted every result to line 1).
/// </summary>
public sealed class TickMarkTest
{
    private static string ExePath([CallerFilePath] string? src = null)
    {
        string dir = Path.GetDirectoryName(src)!;
        string debug   = Path.GetFullPath(Path.Combine(dir, "..", "lgv.App", "bin", "x64", "Debug",   "net10.0-windows", "win-x64", "lgv.exe"));
        string release = Path.GetFullPath(Path.Combine(dir, "..", "lgv.App", "bin", "x64", "Release", "net10.0-windows", "win-x64", "lgv.exe"));
        if (File.Exists(debug))   return debug;
        if (File.Exists(release)) return release;
        throw new FileNotFoundException($"lgv.exe not found. Build first.\nLooked at: {debug}");
    }

    [Fact(DisplayName = "Tick marks appear on the correct lines, not all at the top")]
    public void Search_TickMarksMatchMatchedLines()
    {
        // 100-line file; every 10th line contains "MARK", the rest contain "data".
        // Searching for "MARK" should produce ticks at lines 10, 20, …, 100.
        int[] expectedLines = Enumerable.Range(1, 10).Select(i => i * 10).ToArray();

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
                Enumerable.Range(1, 100).Select(i =>
                    i % 10 == 0 ? $"Line {i:D3} MARK" : $"Line {i:D3} data"));

            using var app = AppDriver.Launch(ExePath(), path);

            // Wait for all 100 lines to load.
            string allNums = app.GetLineNumbers(TimeSpan.FromSeconds(15), minLineCount: 100);
            Assert.False(string.IsNullOrEmpty(allNums),
                "File did not fully load within 15 s.");

            // Search for MARK — should match exactly the 10 lines above.
            app.SetSearch("MARK");

            // Poll until tick data is available (DrawTicks runs after the search dispatch).
            string tickRaw = app.GetTickLineNumbers(TimeSpan.FromSeconds(5));

            Assert.False(string.IsNullOrEmpty(tickRaw),
                "No tick data found — TickCanvas AutomationProperties.Name was empty. " +
                "The search may not have run, or DrawTicks did not set the automation value.");

            int[] tickLines = tickRaw.Split(',').Select(int.Parse).ToArray();

            // Verify tick count matches expected match lines.
            Assert.Equal(expectedLines.Length, tickLines.Length);

            // Verify each tick is on the correct line.
            for (int i = 0; i < expectedLines.Length; i++)
            {
                Assert.Equal(expectedLines[i], tickLines[i]);
            }

            // Verify ticks are NOT all on line 1 (the regression symptom).
            Assert.True(tickLines.Distinct().Count() > 1,
                $"All tick marks are on the same line — they are likely all at line 1 " +
                $"(the bug where GetLineByOffset threw on background thread): {tickRaw}");

            // Verify ticks span from near the top to near the bottom of the document
            // (first tick at line 10 out of 100 = 10%, last at line 100 = 100%).
            Assert.Equal(10, tickLines[0]);
            Assert.Equal(100, tickLines[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
