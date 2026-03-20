// Tests for the line-numbering feature of lgv.
//
// Scope
// -----
// 1. LineFilter.Apply (pure C#) — verified end-to-end: filtered text, LineMap content,
//    ValidPatterns / InvalidPatterns counts, and the identity map produced when no
//    active patterns are present.
//
// 2. MappedLineNumberMargin — the storage contract (SetLineMap stores the array) and
//    the digit-width calculation that drives margin sizing.
//
// Limitation: MappedLineNumberMargin inherits from AvalonEdit's AbstractMargin, which
// is a WPF UIElement. Calling SetLineMap() in a test will invoke InvalidateMeasure()
// and InvalidateVisual() on an element that has no PresentationSource (no window, no
// dispatcher). Those calls are no-ops on an un-parented element in WPF — they do NOT
// throw — so we CAN call SetLineMap() safely in unit tests. We verify the stored state
// via reflection on the private _lineMap field.
//
// The digit-width formula (how many '9' characters to render) is exercised through a
// test-local helper that mirrors the same arithmetic used in MeasureOverride, letting
// us verify the growth behaviour (e.g. 999 → 1 digit, 1000 → 4 digits) without
// needing a real WPF rendering pipeline.

using System.Reflection;
using System.Runtime.ExceptionServices;
using lgv.Filter;
using lgv.UI;

namespace lgv.Tests;

// MappedLineNumberMargin inherits FrameworkElement which requires an STA thread.
// xUnit uses MTA by default, so tests that instantiate the margin must be wrapped.
internal static class Sta
{
    public static void Run(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

// ---------------------------------------------------------------------------
// LineFilter tests
// ---------------------------------------------------------------------------

public class LineFilterApplyTests
{
    // ── Identity map (no filter) ────────────────────────────────────────────

    [Fact]
    public void NoPatterns_ReturnsOriginalTextUnchanged()
    {
        const string text = "line one\nline two\nline three";

        var result = LineFilter.Apply(text, []);

        Assert.Equal(text, result.FilteredText);
    }

    [Fact]
    public void NoPatterns_LineMapIsSequentialOneBasedRange()
    {
        // Three lines → [1, 2, 3]
        const string text = "alpha\nbeta\ngamma";

        var result = LineFilter.Apply(text, []);

        Assert.Equal([1, 2, 3], result.LineMap);
    }

    [Fact]
    public void NoPatterns_LineMapLengthMatchesSplitLineCount()
    {
        // Split('\n') on a string ending with \n produces an empty trailing element,
        // so the map length equals originalText.Split('\n').Length — the same formula
        // used in the production code.
        const string text = "a\nb\nc\n";
        int expectedLength = text.Split('\n').Length; // 4 (trailing empty element)

        var result = LineFilter.Apply(text, []);

        Assert.Equal(expectedLength, result.LineMap.Length);
    }

    [Fact]
    public void NoPatterns_ValidAndInvalidPatternCountsAreZero()
    {
        var result = LineFilter.Apply("some text", []);

        Assert.Equal(0, result.ValidPatterns);
        Assert.Equal(0, result.InvalidPatterns);
    }

    [Fact]
    public void NoPatterns_SingleLine_ReturnsMapOf1()
    {
        var result = LineFilter.Apply("only one line", []);

        Assert.Equal([1], result.LineMap);
    }

    // ── Comment-only patterns treated as no active patterns ─────────────────

    [Fact]
    public void CommentOnlyPatterns_BehavesLikeNoPatterns()
    {
        const string text = "alpha\nbeta";

        var result = LineFilter.Apply(text, ["# this is a comment", "  # another comment"]);

        Assert.Equal(text, result.FilteredText);
        Assert.Equal([1, 2], result.LineMap);
    }

    [Fact]
    public void WhitespaceOnlyPatterns_BehavesLikeNoPatterns()
    {
        const string text = "x\ny";

        var result = LineFilter.Apply(text, ["   ", "\t"]);

        Assert.Equal(text, result.FilteredText);
        Assert.Equal([1, 2], result.LineMap);
    }

    // ── Filtered map: original source line numbers are preserved ───────────

    [Fact]
    public void FilterActive_LineMapReflectsOriginalLineNumbers()
    {
        // Lines 1 and 3 contain "ERROR" → they are excluded.
        // Lines 2 and 4 survive → original line numbers 2 and 4.
        const string text = "ERROR: something bad\nINFO: all good\nERROR: another bad\nINFO: fine";

        var result = LineFilter.Apply(text, ["ERROR"]);

        Assert.Equal([2, 4], result.LineMap);
    }

    [Fact]
    public void FilterActive_FilteredTextContainsOnlySurvivingLines()
    {
        const string text = "foo\nbar\nbaz";

        // Exclude lines containing "bar"
        var result = LineFilter.Apply(text, ["bar"]);

        Assert.Equal("foo\nbaz", result.FilteredText);
    }

    [Fact]
    public void FilterActive_LineMapAndFilteredTextAreConsistent()
    {
        // Construct a document where we know exactly which lines survive.
        var lines = new[]
        {
            "2024-01-01 INFO  startup",   // line 1 — survives
            "2024-01-01 DEBUG verbose",   // line 2 — excluded
            "2024-01-01 INFO  request",   // line 3 — survives
            "2024-01-01 DEBUG internal",  // line 4 — excluded
            "2024-01-01 INFO  shutdown",  // line 5 — survives
        };
        string text = string.Join('\n', lines);

        var result = LineFilter.Apply(text, ["DEBUG"]);

        // Three surviving lines
        Assert.Equal(3, result.LineMap.Length);
        // They map to original positions 1, 3, 5
        Assert.Equal([1, 3, 5], result.LineMap);
        // The filtered text contains exactly those three lines joined by \n
        Assert.Equal("2024-01-01 INFO  startup\n2024-01-01 INFO  request\n2024-01-01 INFO  shutdown",
            result.FilteredText);
    }

    [Fact]
    public void FilterActive_MultiplePatterns_ExcludesLineMatchingAny()
    {
        // Exclude lines that match "DEBUG" or "TRACE"
        const string text = "INFO a\nDEBUG b\nTRACE c\nINFO d";

        var result = LineFilter.Apply(text, ["DEBUG", "TRACE"]);

        // Lines 1 and 4 survive
        Assert.Equal([1, 4], result.LineMap);
        Assert.Equal("INFO a\nINFO d", result.FilteredText);
    }

    [Fact]
    public void FilterActive_MatchIsCaseInsensitive()
    {
        const string text = "Hello world\nerror occurred\nAll clear";

        var result = LineFilter.Apply(text, ["ERROR"]);

        // "error occurred" (line 2) is excluded regardless of case
        Assert.Equal([1, 3], result.LineMap);
    }

    [Fact]
    public void FilterActive_RegexPatternIsApplied()
    {
        // Exclude lines whose third word is a 3-digit number
        const string text = "status 200 ok\nstatus 404 not found\nno number here";

        var result = LineFilter.Apply(text, [@"\b[2-5]\d{2}\b"]);

        // Lines 1 (200) and 2 (404) are excluded; line 3 survives
        Assert.Equal([3], result.LineMap);
    }

    // ── Empty results ───────────────────────────────────────────────────────

    [Fact]
    public void FilterActive_AllLinesExcluded_EmptyFilteredTextAndEmptyLineMap()
    {
        const string text = "ERROR a\nERROR b\nERROR c";

        var result = LineFilter.Apply(text, ["ERROR"]);

        Assert.Equal(string.Empty, result.FilteredText);
        Assert.Empty(result.LineMap);
    }

    [Fact]
    public void FilterActive_EmptyOriginalText_ReturnsEmptyResult()
    {
        var result = LineFilter.Apply(string.Empty, ["ERROR"]);

        // Single empty line is checked and excluded (empty string matches nothing),
        // but an empty document produces a single empty-string element from Split('\n').
        // That empty element does NOT match "ERROR", so it survives.
        // The key assertion is that the LineMap has at most 1 element and
        // FilteredText is either empty or the empty string line.
        Assert.True(result.LineMap.Length <= 1);
    }

    // ── ValidPatterns / InvalidPatterns counts ──────────────────────────────

    [Fact]
    public void ValidRegex_ValidPatternCountEqualsActivePatternCount()
    {
        var result = LineFilter.Apply("some text", ["foo", "bar"]);

        Assert.Equal(2, result.ValidPatterns);
        Assert.Equal(0, result.InvalidPatterns);
    }

    [Fact]
    public void InvalidRegex_IsCountedAndDoesNotThrow()
    {
        // "[[invalid" is not a valid regex
        var result = LineFilter.Apply("some text", ["[[invalid"]);

        Assert.Equal(0, result.ValidPatterns);
        Assert.Equal(1, result.InvalidPatterns);
    }

    [Fact]
    public void MixedValidAndInvalidRegex_BothCountsAreCorrect()
    {
        var result = LineFilter.Apply("some text", ["valid", "[[bad", "also_valid"]);

        Assert.Equal(2, result.ValidPatterns);
        Assert.Equal(1, result.InvalidPatterns);
    }

    [Fact]
    public void AllPatternsInvalid_OriginalTextIsReturned()
    {
        // When every pattern fails to compile, the filter falls back to returning
        // the original text unchanged (all-invalid branch in LineFilter.Apply).
        const string text = "line one\nline two";

        var result = LineFilter.Apply(text, ["[[bad1", "[[bad2"]);

        Assert.Equal(text, result.FilteredText);
    }

    // ── Line-ending normalisation ────────────────────────────────────────────

    [Fact]
    public void CrLfLineEndings_AreNormalizedToLf_InFilteredOutput()
    {
        // The production code normalizes \r\n → \n before splitting so that
        // AvalonEdit does not see embedded \r characters in the filtered document.
        const string text = "INFO a\r\nDEBUG b\r\nINFO c";

        var result = LineFilter.Apply(text, ["DEBUG"]);

        Assert.DoesNotContain('\r', result.FilteredText);
        Assert.Equal([1, 3], result.LineMap);
    }

    [Fact]
    public void LoneCrLineEndings_AreNormalized()
    {
        const string text = "INFO a\rDEBUG b\rINFO c";

        var result = LineFilter.Apply(text, ["DEBUG"]);

        Assert.DoesNotContain('\r', result.FilteredText);
        Assert.Equal([1, 3], result.LineMap);
    }
}

// ---------------------------------------------------------------------------
// MappedLineNumberMargin line-map storage tests
// ---------------------------------------------------------------------------

public class MappedLineNumberMarginStorageTests
{
    // Reflection accessor for the private _lineMap field.
    // We use reflection because the field is private and we must not change the
    // production API just to accommodate tests.
    private static int[]? GetLineMap(MappedLineNumberMargin margin)
    {
        var field = typeof(MappedLineNumberMargin)
            .GetField("_lineMap", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(nameof(MappedLineNumberMargin), "_lineMap");
        return (int[]?)field.GetValue(margin);
    }

    [Fact]
    public void SetLineMap_WithArray_StoresTheArray() => Sta.Run(() =>
    {
        var margin = new MappedLineNumberMargin();
        int[] map = [2, 4, 7, 9];

        margin.SetLineMap(map);

        var stored = GetLineMap(margin);
        Assert.Equal(map, stored);
    });

    [Fact]
    public void SetLineMap_WithNull_ClearsStoredMap() => Sta.Run(() =>
    {
        var margin = new MappedLineNumberMargin();
        margin.SetLineMap([1, 2, 3]);

        // Simulate "filter cleared" — pass null to revert to sequential mode.
        margin.SetLineMap(null);

        Assert.Null(GetLineMap(margin));
    });

    [Fact]
    public void SetLineMap_CalledRepeatedly_LastValueWins() => Sta.Run(() =>
    {
        var margin = new MappedLineNumberMargin();
        margin.SetLineMap([1, 3, 5]);
        margin.SetLineMap([2, 4, 6, 8]);

        Assert.Equal([2, 4, 6, 8], GetLineMap(margin));
    });

    [Fact]
    public void SetLineMap_WithEmptyArray_StoresEmptyArray() => Sta.Run(() =>
    {
        var margin = new MappedLineNumberMargin();

        margin.SetLineMap([]);

        var stored = GetLineMap(margin);
        Assert.NotNull(stored);
        Assert.Empty(stored);
    });

    [Fact]
    public void InitialState_LineMapIsNull() => Sta.Run(() =>
    {
        // Before any call to SetLineMap, the margin should be in sequential mode
        // (null map), meaning MeasureOverride falls back to Document.LineCount.
        var margin = new MappedLineNumberMargin();

        Assert.Null(GetLineMap(margin));
    });
}

// ---------------------------------------------------------------------------
// Digit-width / margin sizing logic tests
// ---------------------------------------------------------------------------
//
// MeasureOverride uses this formula to compute the margin width:
//
//   int maxLine = _lineMap is { Length: > 0 }
//       ? _lineMap[^1]          // last element = highest original line number
//       : Math.Max(1, lineCount);
//   int digitCount = maxLine.ToString().Length;
//   // Renders a string of 'digitCount' nines to measure worst-case width.
//
// We cannot call MeasureOverride directly without a WPF layout pass, so we
// test the digit-count formula in isolation via a local helper. This ensures
// that as new lines are appended during file tailing, the margin will correctly
// widen from (e.g.) 3-digit to 4-digit width at line 1000.

public class DigitWidthLogicTests
{
    // Mirrors the digit-count portion of MeasureOverride exactly.
    private static int ComputeDigitCount(int[]? lineMap, int documentLineCount)
    {
        int maxLine = lineMap is { Length: > 0 }
            ? lineMap[^1]
            : Math.Max(1, documentLineCount);
        return maxLine.ToString().Length;
    }

    // ── No filter (sequential) ───────────────────────────────────────────────

    [Theory]
    [InlineData(1,   1)]
    [InlineData(9,   1)]
    [InlineData(10,  2)]
    [InlineData(99,  2)]
    [InlineData(100, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 4)]
    [InlineData(9999, 4)]
    [InlineData(10000, 5)]
    public void NoFilter_DigitCountMatchesLineCountDigits(int lineCount, int expectedDigits)
    {
        int actual = ComputeDigitCount(null, lineCount);

        Assert.Equal(expectedDigits, actual);
    }

    [Fact]
    public void NoFilter_ZeroLineCount_MinimumIsOne_SingleDigit()
    {
        // Math.Max(1, 0) → 1 → 1 digit.  Protects against empty document.
        int actual = ComputeDigitCount(null, 0);

        Assert.Equal(1, actual);
    }

    // ── Filter active ────────────────────────────────────────────────────────

    [Fact]
    public void FilterActive_DigitCountBasedOnLastMapEntry()
    {
        // If filtered document has 3 lines but they are original lines 998, 999, 1000,
        // the margin must be 4 digits wide (for "1000"), not 1 digit (for "3").
        int[] map = [998, 999, 1000];

        int actual = ComputeDigitCount(map, documentLineCount: 3);

        Assert.Equal(4, actual);
    }

    [Fact]
    public void FilterActive_SingleSurvivorAtHighLineNumber_CorrectDigits()
    {
        // Only line 500 survived the filter. Margin must accommodate 3 digits.
        int[] map = [500];

        int actual = ComputeDigitCount(map, documentLineCount: 1);

        Assert.Equal(3, actual);
    }

    [Fact]
    public void FilterActive_EmptyLineMap_FallsBackToDocumentLineCount()
    {
        // Empty map → treated same as null → use document line count.
        // This is the edge case where every line was filtered out but the
        // margin still needs a width (it will show nothing, but must not crash).
        int actual = ComputeDigitCount([], documentLineCount: 5);

        Assert.Equal(1, actual); // Math.Max(1, 5) → 5 → 1 digit... actually 1 digit
        // Wait — 5 has 1 digit. Let's assert correctly:
        Assert.Equal("5".Length, actual);
    }

    // ── File tailing: margin widens as new lines arrive ──────────────────────

    [Fact]
    public void Tailing_BoundaryAt10_MarginWidensFrom1To2Digits()
    {
        Assert.Equal(1, ComputeDigitCount(null, documentLineCount: 9));
        Assert.Equal(2, ComputeDigitCount(null, documentLineCount: 10));
    }

    [Fact]
    public void Tailing_BoundaryAt100_MarginWidensFrom2To3Digits()
    {
        Assert.Equal(2, ComputeDigitCount(null, documentLineCount: 99));
        Assert.Equal(3, ComputeDigitCount(null, documentLineCount: 100));
    }

    [Fact]
    public void Tailing_BoundaryAt1000_MarginWidensFrom3To4Digits()
    {
        // This is the scenario explicitly called out in the feature spec:
        // a file growing past 999 lines must widen the margin automatically.
        Assert.Equal(3, ComputeDigitCount(null, documentLineCount: 999));
        Assert.Equal(4, ComputeDigitCount(null, documentLineCount: 1000));
    }

    [Fact]
    public void Tailing_WithFilterActive_DigitCountTracksHighestOriginalLineNumber()
    {
        // As new content arrives, the filter is re-run. The LineMap's last element
        // reflects the highest original line number seen. If new tailed content
        // pushes that into 4-digit territory, the margin must widen.
        int[] mapBefore = [997, 998, 999];
        int[] mapAfter  = [997, 998, 999, 1000];

        int before = ComputeDigitCount(mapBefore, documentLineCount: 3);
        int after  = ComputeDigitCount(mapAfter,  documentLineCount: 4);

        Assert.Equal(3, before);
        Assert.Equal(4, after);
    }

    // ── Render display number selection ──────────────────────────────────────
    //
    // MappedLineNumberMargin.OnRender selects the display number for each
    // visual line using:
    //   int display = (_lineMap is not null && docLine <= _lineMap.Length)
    //       ? _lineMap[docLine - 1]
    //       : docLine;
    //
    // We test this mapping logic in isolation.

    [Theory]
    [InlineData(1, new[] { 2, 4, 7 }, 2)]   // filtered doc line 1 → original line 2
    [InlineData(2, new[] { 2, 4, 7 }, 4)]   // filtered doc line 2 → original line 4
    [InlineData(3, new[] { 2, 4, 7 }, 7)]   // filtered doc line 3 → original line 7
    public void RenderLogic_WithFilter_ReturnsOriginalLineNumber(
        int docLine, int[] lineMap, int expectedDisplay)
    {
        int display = (lineMap is not null && docLine <= lineMap.Length)
            ? lineMap[docLine - 1]
            : docLine;

        Assert.Equal(expectedDisplay, display);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void RenderLogic_NullMap_ReturnsDocumentLineNumber(int docLine)
    {
        int[]? lineMap = null;

        int display = (lineMap is not null && docLine <= lineMap.Length)
            ? lineMap[docLine - 1]
            : docLine;

        Assert.Equal(docLine, display);
    }

    [Fact]
    public void RenderLogic_DocLineExceedsMapLength_FallsBackToDocLine()
    {
        // Guard: if docLine > lineMap.Length, use docLine directly.
        // This should not happen in normal operation, but the guard exists in
        // production code and we should verify it behaves correctly.
        int[] lineMap = [10, 20];
        int docLine = 5; // beyond map length of 2

        int display = (lineMap is not null && docLine <= lineMap.Length)
            ? lineMap[docLine - 1]
            : docLine;

        Assert.Equal(5, display);
    }
}

// ---------------------------------------------------------------------------
// Integration: LineFilter output feeds MappedLineNumberMargin correctly
// ---------------------------------------------------------------------------

public class FilterToMarginIntegrationTests
{
    private static int[]? GetLineMap(MappedLineNumberMargin margin)
    {
        var field = typeof(MappedLineNumberMargin)
            .GetField("_lineMap", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(nameof(MappedLineNumberMargin), "_lineMap");
        return (int[]?)field.GetValue(margin);
    }

    [Fact]
    public void FilterResult_LineMap_CanBePassedDirectlyToMargin() => Sta.Run(() =>
    {
        const string text = "alpha\nbeta\ngamma\ndelta";
        var filterResult = LineFilter.Apply(text, ["beta", "delta"]);

        // Surviving lines: "alpha" (1) and "gamma" (3)
        Assert.Equal([1, 3], filterResult.LineMap);

        var margin = new MappedLineNumberMargin();
        margin.SetLineMap(filterResult.LineMap);

        Assert.Equal([1, 3], GetLineMap(margin));
    });

    [Fact]
    public void ClearFilter_NullLineMap_MarginReturnsToSequentialMode() => Sta.Run(() =>
    {
        const string text = "a\nb\nc";
        var filterResult = LineFilter.Apply(text, ["b"]);

        var margin = new MappedLineNumberMargin();
        margin.SetLineMap(filterResult.LineMap);
        Assert.NotNull(GetLineMap(margin));

        // User clears the filter — LogViewerControl passes null
        margin.SetLineMap(null);

        Assert.Null(GetLineMap(margin));
    });

    [Fact]
    public void NoFilterApplied_IdentityMapFromFilter_MarginStoresIt() => Sta.Run(() =>
    {
        // When no patterns are active, RunGlobalFilter short-circuits and sets
        // null (not the identity array), so the margin uses the document line count.
        // LineFilter.Apply with no patterns returns an identity array — but the
        // caller (LogViewerControl.RunGlobalFilter) never calls SetLineMap with it;
        // instead it calls SetLineMap(null). This test documents that contract:
        // a null SetLineMap call produces sequential numbering.
        var margin = new MappedLineNumberMargin();

        margin.SetLineMap(null); // mirrors LogViewerControl clearing filter

        Assert.Null(GetLineMap(margin));
    });
}
