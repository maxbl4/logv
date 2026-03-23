using System.IO;
using System.Runtime.CompilerServices;

namespace lgv.UITests;

/// <summary>
/// Verifies that the line-number margin tracks the viewport as the user scrolls:
/// the numbers visible in the margin must always match the actual file line numbers
/// at the current scroll position.
/// </summary>
public sealed class ScrollLineNumberTest
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

    [Fact(DisplayName = "Visible line numbers match actual file lines at top, bottom, and after scrolling back")]
    public void Scroll200LineFile_VisibleNumbersAlwaysMatchActualLines()
    {
        // 200 lines; each line contains its own number so we can cross-check if needed.
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
                Enumerable.Range(1, 200).Select(i => $"Log entry {i:D3}: some content here"));

            using var app = AppDriver.Launch(ExePath(), path);

            // ── 1. Wait for ALL 200 lines to be loaded ───────────────────────────
            string allNums = app.GetLineNumbers(TimeSpan.FromSeconds(15), minLineCount: 200);
            Assert.False(string.IsNullOrEmpty(allNums),
                "File did not fully load within 15 s — check app startup and tailing.");

            // ── 2. Scroll to top; first visible line must be 1 ───────────────────
            // ScrollEditorToTop returns the visible line numbers as drawn by the
            // MappedLineNumberMargin — the same values the user actually sees.
            string topVisible = app.ScrollEditorToTop();

            Assert.False(string.IsNullOrEmpty(topVisible),
                "No visible line numbers after scroll to top.");

            int[] topNums = ParseLineNumbers(topVisible);
            Assert.Equal(1, topNums[0]);
            AssertContiguous(topNums, "top-of-file visible lines");

            // ── 3. Scroll to end; last visible line must be 200 ──────────────────
            string bottomVisible = app.ScrollEditorToEnd();

            Assert.False(string.IsNullOrEmpty(bottomVisible),
                "No visible line numbers after scroll to end.");

            int[] bottomNums = ParseLineNumbers(bottomVisible);

            // File.WriteAllLines appends a trailing newline → AvalonEdit has 201 lines.
            // At the bottom, the last visible line is 200 or 201 (the empty trailing line).
            Assert.True(bottomNums[^1] >= 200,
                $"Expected last visible line ≥ 200 at the bottom, got {bottomNums[^1]}.");
            Assert.True(bottomNums[0] > 1,
                $"Expected to have scrolled past line 1 at the bottom, but first visible is {bottomNums[0]}.");
            AssertContiguous(bottomNums, "bottom-of-file visible lines");

            // ── 4. Scroll back to top; verify we're back at line 1 ───────────────
            string topAgainVisible = app.ScrollEditorToTop();
            int[] topAgainNums = ParseLineNumbers(topAgainVisible);
            Assert.Equal(1, topAgainNums[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static int[] ParseLineNumbers(string raw) =>
        raw.Split(',').Select(int.Parse).ToArray();

    private static void AssertContiguous(int[] nums, string label)
    {
        for (int i = 1; i < nums.Length; i++)
        {
            if (nums[i] != nums[i - 1] + 1)
                throw new Exception(
                    $"Visible {label} are not contiguous: …{nums[i-1]},{nums[i]}… " +
                    $"(full set: {string.Join(",", nums)})");
        }
    }
}
