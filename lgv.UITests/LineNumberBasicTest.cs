using System.IO;
using System.Runtime.CompilerServices;

namespace lgv.UITests;

/// <summary>
/// Basic sanity check: open a multi-line file and verify the line-number margin
/// displays 1, 2, 3 … for every line.
///
/// Run after building lgv.App:
///   dotnet build -c Debug -p:Platform=x64 lgv.App/lgv.csproj
/// </summary>
public sealed class LineNumberBasicTest
{
    private static string ExePath([CallerFilePath] string? src = null)
    {
        string dir = Path.GetDirectoryName(src)!;
        string debug   = Path.GetFullPath(Path.Combine(dir, "..", "lgv.App", "bin", "x64", "Debug",   "net10.0-windows", "lgv.exe"));
        string release = Path.GetFullPath(Path.Combine(dir, "..", "lgv.App", "bin", "x64", "Release", "net10.0-windows", "lgv.exe"));
        if (File.Exists(debug))   return debug;
        if (File.Exists(release)) return release;
        throw new FileNotFoundException($"lgv.exe not found. Build lgv.App first.\nLooked at: {debug}");
    }

    [Fact(DisplayName = "Opens 12-line file → margin shows line numbers 1 through 12")]
    public void OpenFile_MarginShowsAllLineNumbers()
    {
        // 12 lines — intentionally crosses the single-digit boundary so clipping is obvious.
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "line 01\nline 02\nline 03\nline 04\nline 05\nline 06\n" +
                "line 07\nline 08\nline 09\nline 10\nline 11\nline 12\n");

            using var app = AppDriver.Launch(ExePath(), path);

            // Poll until we get a non-empty value (up to 15 s for slow CI machines).
            string raw = app.GetLineNumbers(TimeSpan.FromSeconds(15));

            Assert.False(string.IsNullOrEmpty(raw),
                "LineNumberMargin automation value was empty — element not found or value not exposed.");

            int[] numbers = raw.Split(',').Select(int.Parse).ToArray();

            Assert.True(numbers.Length >= 12,
                $"Expected ≥12 line numbers, got {numbers.Length}: \"{raw}\"");

            for (int i = 0; i < 12; i++)
                Assert.Equal(i + 1, numbers[i]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
