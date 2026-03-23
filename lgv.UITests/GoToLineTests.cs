using System.IO;
using System.Runtime.CompilerServices;

namespace lgv.UITests;

/// <summary>
/// End-to-end UI tests for the Go To Line feature.
/// Uses a 500-line file with every 5th line tagged "NOISY" (excluded by the filter).
/// Verifies that Ctrl+G accepts raw (original) file line numbers both with and without
/// an active filter.
/// </summary>
public sealed class GoToLineTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string ExePath()
    {
        string thisDir = ThisSourceDir();
        string candidate = Path.GetFullPath(
            Path.Combine(thisDir, "..", "lgv.App", "bin", "x64", "Debug", "net10.0-windows", "win-x64", "lgv.exe"));
        if (!File.Exists(candidate))
        {
            string release = Path.GetFullPath(
                Path.Combine(thisDir, "..", "lgv.App", "bin", "x64", "Release", "net10.0-windows", "win-x64", "lgv.exe"));
            if (File.Exists(release)) return release;
            throw new FileNotFoundException(
                $"lgv.exe not found. Build lgv.App first.\nLast tried: {candidate}");
        }
        return candidate;
    }

    private static string ThisSourceDir([CallerFilePath] string? path = null) =>
        Path.GetDirectoryName(path) ?? ".";

    /// <summary>
    /// Generates a 500-line test file.
    /// Every 5th line (5, 10, 15, …, 500) contains "NOISY" and will be excluded by the filter.
    /// All other lines contain "CONTENT".
    /// </summary>
    private static string Write500LineFile()
    {
        var lines = Enumerable.Range(1, 500)
            .Select(i => i % 5 == 0
                ? $"Line {i:D3} NOISY marker"
                : $"Line {i:D3} CONTENT data")
            .ToArray();
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
            try { File.Delete(f); } catch { /* ignore */ }
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "GoToLine without filter navigates to the requested raw line number")]
    public void NoFilter_GoToLine_NavigatesToCorrectLine()
    {
        var app = StartApp(Write500LineFile());
        app.GetLineNumbers(TimeSpan.FromSeconds(15)); // wait for file to load

        app.GoToLine(250);

        string visible = app.GetVisibleLineNumbers(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(visible), "No visible line numbers after GoToLine(250).");

        var numbers = visible.Split(',').Select(int.Parse).ToArray();
        Assert.Contains(250, numbers);
    }

    [Fact(DisplayName = "GoToLine without filter navigates to line near the end of the file")]
    public void NoFilter_GoToLine_NearEnd_NavigatesCorrectly()
    {
        var app = StartApp(Write500LineFile());
        app.GetLineNumbers(TimeSpan.FromSeconds(15));

        app.GoToLine(490);

        string visible = app.GetVisibleLineNumbers(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(visible));

        var numbers = visible.Split(',').Select(int.Parse).ToArray();
        Assert.Contains(490, numbers);
    }

    [Fact(DisplayName = "GoToLine with filter navigates to the correct original line when target is kept")]
    public void WithFilter_GoToLine_KeptLine_NavigatesCorrectly()
    {
        // Line 249 is not divisible by 5 → kept by the filter.
        var app = StartApp(Write500LineFile());
        string preFilter = app.GetLineNumbers(TimeSpan.FromSeconds(15));

        app.SetGlobalFilter("NOISY"); // exclude every 5th line
        app.WaitUntilLineNumbersChange(preFilter, TimeSpan.FromSeconds(10));

        app.GoToLine(249);

        string visible = app.GetVisibleLineNumbers(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(visible), "No visible line numbers after GoToLine(249) with filter.");

        var numbers = visible.Split(',').Select(int.Parse).ToArray();
        Assert.Contains(249, numbers);
    }

    [Fact(DisplayName = "GoToLine with filter navigates to next kept line when target is filtered out")]
    public void WithFilter_GoToLine_ExcludedLine_NavigatesToNextKeptLine()
    {
        // Line 250 is divisible by 5 → excluded by the filter.
        // The next kept line after 250 is 251.
        var app = StartApp(Write500LineFile());
        string preFilter = app.GetLineNumbers(TimeSpan.FromSeconds(15));

        app.SetGlobalFilter("NOISY");
        app.WaitUntilLineNumbersChange(preFilter, TimeSpan.FromSeconds(10));

        app.GoToLine(250);

        string visible = app.GetVisibleLineNumbers(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(visible), "No visible line numbers after GoToLine(250) with filter.");

        var numbers = visible.Split(',').Select(int.Parse).ToArray();
        // 250 is excluded; navigation should land on 251 (the next kept line)
        Assert.DoesNotContain(250, numbers);
        Assert.Contains(251, numbers);
    }
}
