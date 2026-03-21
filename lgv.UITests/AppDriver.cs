using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace lgv.UITests;

/// <summary>
/// Launches and drives the LGV application via UIAutomation.
/// Dispose to kill the process when done.
/// </summary>
internal sealed class AppDriver : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP   = 0x04;

    private readonly Process _process;
    private readonly AutomationElement _window;

    private AppDriver(Process process, AutomationElement window)
    {
        _process = process;
        _window = window;
    }

    /// <summary>
    /// Launches lgv.exe with the given file path and waits for the main window to appear.
    /// </summary>
    public static AppDriver Launch(string exePath, string? filePath = null)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
        };
        if (filePath is not null)
            psi.Arguments = $"\"{filePath}\"";

        // Clear persisted session so RestoreSession doesn't reopen previous test's tabs
        // and contaminate line-number readings with extra documents.
        string settingsPath = Path.Combine(Path.GetDirectoryName(exePath)!, "lgv.settings.json");
        try { File.Delete(settingsPath); } catch { /* ignore if missing */ }

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process.");

        // Wait for the main window to appear (up to 15 seconds).
        // AutomationElement.FromHandle can throw COMException transiently while UIA
        // is initialising — retry until the window is stable.
        AutomationElement? window = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(200);
                if (process.HasExited) throw new InvalidOperationException("Process exited unexpectedly.");

                process.Refresh();
                if (process.MainWindowHandle == IntPtr.Zero) continue;

                try
                {
                    window = AutomationElement.FromHandle(process.MainWindowHandle);
                    if (window is not null) break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // UIA not ready yet — keep polling.
                }
            }
        }
        catch
        {
            // If anything goes wrong during launch we must not leave the process running.
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
            throw;
        }

        if (window is null)
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
            throw new TimeoutException("LGV main window did not appear within 15 seconds.");
        }

        return new AppDriver(process, window);
    }

    /// <summary>
    /// Returns the comma-separated document line numbers from the margin.
    /// Polls until the value is non-empty (and has at least <paramref name="minLineCount"/> entries)
    /// or the timeout expires.
    /// </summary>
    public string GetLineNumbers(TimeSpan? timeout = null, int minLineCount = 1)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            string val = ReadLineNumberMarginValue();
            if (!string.IsNullOrEmpty(val) && CountEntries(val) >= minLineCount)
                return val;
            Thread.Sleep(200);
        }
        return ReadLineNumberMarginValue();
    }

    /// <summary>
    /// Polls until the margin's document line-number value differs from
    /// <paramref name="previousValue"/>. Use this after applying a filter to wait
    /// for the filtered result instead of getting the stale pre-filter value.
    /// </summary>
    public string WaitUntilLineNumbersChange(string previousValue, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            string val = ReadLineNumberMarginValue();
            if (!string.IsNullOrEmpty(val) && val != previousValue)
                return val;
            Thread.Sleep(200);
        }
        return ReadLineNumberMarginValue();
    }

    private static int CountEntries(string csv) =>
        string.IsNullOrEmpty(csv) ? 0 : csv.Count(c => c == ',') + 1;

    private string ReadLineNumberMarginValue()
    {
        var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "LineNumberMargin");
        var el = _window.FindFirst(TreeScope.Descendants, cond);
        // The margin exposes all document line numbers via AutomationProperties.Name —
        // a standard UIA property that is reliably accessible cross-process without
        // needing a custom IValueProvider pattern.
        return el?.Current.Name ?? "";
    }

    private AutomationElement? FindMargin() =>
        _window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "LineNumberMargin"));

    /// <summary>
    /// Returns the comma-separated line numbers that are currently visible in the viewport.
    /// Updated by the margin on every repaint; reflects the scroll position.
    /// Polls until the value stabilises (same result twice in a row) or the timeout expires.
    /// </summary>
    public string GetVisibleLineNumbers(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        string prev = "";
        while (DateTime.UtcNow < deadline)
        {
            string cur = FindMargin()?.Current.HelpText ?? "";
            if (!string.IsNullOrEmpty(cur) && cur == prev)
                return cur;   // stable for two consecutive reads
            prev = cur;
            Thread.Sleep(200);
        }
        return prev;
    }

    /// <summary>
    /// Scrolls the editor to the top and returns the visible line numbers as drawn by the
    /// <c>MappedLineNumberMargin</c> — the same values the user actually sees on screen.
    /// </summary>
    public string ScrollEditorToTop() => InvokeScrollButton("ScrollToTopBtn");

    /// <summary>
    /// Scrolls the editor to the bottom and returns the visible line numbers as drawn by the
    /// <c>MappedLineNumberMargin</c> — the same values the user actually sees on screen.
    /// </summary>
    public string ScrollEditorToEnd() => InvokeScrollButton("ScrollToEndBtn");

    /// <summary>
    /// Invokes the named automation button, waits for the app to toggle its HelpText
    /// (confirming the scroll and the subsequent Render pass have completed), then reads
    /// the visible line numbers from the <see cref="MappedLineNumberMargin"/>'s own HelpText —
    /// the same values the margin actually painted on screen.
    /// </summary>
    private string InvokeScrollButton(string automationId)
    {
        var el = _window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId))
            ?? throw new InvalidOperationException(
                $"Automation element '{automationId}' not found in UIA tree.");

        if (!el.TryGetCurrentPattern(InvokePattern.Pattern, out object? p) || p is not InvokePattern ip)
            throw new InvalidOperationException(
                $"Element '{automationId}' does not support InvokePattern.");

        string prevHelp = el.Current.HelpText;
        ip.Invoke();

        // Wait up to 5 s for the Loaded callback to toggle HelpText.
        // By then, the Render pass has already run and the margin's OnRender has updated
        // its own HelpText to reflect the new scroll position.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (el.Current.HelpText != prevHelp) break;
            Thread.Sleep(50);
        }

        if (el.Current.HelpText == prevHelp)
            throw new InvalidOperationException(
                $"Scroll handler for '{automationId}' did not complete within 5 s (HelpText stayed '{prevHelp}').");

        // Read the visible line numbers that the MappedLineNumberMargin actually drew.
        return GetVisibleLineNumbers(TimeSpan.FromSeconds(3));
    }


    private void FocusEditor()
    {
        // Bring the window to the foreground, then physically click in its centre so the
        // AvalonEdit TextArea (which doesn't support UIA SetFocus) receives keyboard focus.
        SetForegroundWindow(_process.MainWindowHandle);
        Thread.Sleep(100);

        // Find the editor element for its on-screen bounds; fall back to the window centre.
        var editorEl = _window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "Editor"));
        var rect = editorEl is not null
            ? editorEl.Current.BoundingRectangle
            : _window.Current.BoundingRectangle;

        int cx = (int)(rect.Left + rect.Width  / 2);
        int cy = (int)(rect.Top  + rect.Height / 2);
        SetCursorPos(cx, cy);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, IntPtr.Zero);
        Thread.Sleep(200);
    }

    /// <summary>
    /// Types text into the global filter box (AutomationId="GlobalFilterBox").
    /// Opens the filter panel first if it is not visible.
    /// </summary>
    public void SetGlobalFilter(string pattern)
    {
        var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "GlobalFilterBox");

        // The filter panel is collapsed by default; invoke the hidden automation button to show it.
        var el = _window.FindFirst(TreeScope.Descendants, cond);
        if (el is null)
        {
            var showBtn = _window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "ShowFilterPanelBtn"))
                ?? throw new InvalidOperationException(
                    "ShowFilterPanelBtn automation element not found.");

            if (showBtn.TryGetCurrentPattern(InvokePattern.Pattern, out object? btnP)
                && btnP is InvokePattern btnIp)
                btnIp.Invoke();

            // Wait up to 2 s for the panel to appear.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                el = _window.FindFirst(TreeScope.Descendants, cond);
                if (el is not null) break;
                Thread.Sleep(100);
            }

            if (el is null)
                throw new InvalidOperationException(
                    "GlobalFilterBox not found even after invoking ShowFilterPanelBtn.");
        }

        el.SetFocus();
        Thread.Sleep(100);
        SetForegroundWindow(_process.MainWindowHandle);
        Thread.Sleep(50);

        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
            && patternObj is ValuePattern vp)
        {
            vp.SetValue(pattern);
        }
        else
        {
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(50);
            System.Windows.Forms.SendKeys.SendWait(pattern);
        }

        // Give the debounced filter time to apply
        Thread.Sleep(600);
    }

    /// <summary>
    /// Clears the global filter box.
    /// </summary>
    public void ClearGlobalFilter() => SetGlobalFilter("");

    /// <summary>
    /// Opens the in-editor search bar and types the given query into the search box.
    /// Waits for the debounced search to complete before returning.
    /// </summary>
    public void SetSearch(string query)
    {
        var searchBoxCond = new PropertyCondition(AutomationElement.AutomationIdProperty, "SearchBox");

        var el = _window.FindFirst(TreeScope.Descendants, searchBoxCond);
        if (el is null)
        {
            var showBtn = _window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "ShowSearchBarBtn"))
                ?? throw new InvalidOperationException("ShowSearchBarBtn automation element not found.");

            if (showBtn.TryGetCurrentPattern(InvokePattern.Pattern, out object? btnP)
                && btnP is InvokePattern btnIp)
                btnIp.Invoke();

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                el = _window.FindFirst(TreeScope.Descendants, searchBoxCond);
                if (el is not null) break;
                Thread.Sleep(100);
            }

            if (el is null)
                throw new InvalidOperationException(
                    "SearchBox not found even after invoking ShowSearchBarBtn.");
        }

        el.SetFocus();
        Thread.Sleep(100);

        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
            && patternObj is ValuePattern vp)
        {
            vp.SetValue(query);
        }
        else
        {
            System.Windows.Forms.SendKeys.SendWait("^a");
            Thread.Sleep(50);
            System.Windows.Forms.SendKeys.SendWait(query);
        }

        // Give the debounced search time to run
        Thread.Sleep(600);
    }

    /// <summary>
    /// Returns the comma-separated line numbers of the tick marks currently drawn on the
    /// scrollbar (one entry per unique matched line), as set by DrawTicks().
    /// Polls until non-empty or the timeout expires.
    /// </summary>
    public string GetTickLineNumbers(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var el = _window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "TickCanvas"));
            string val = el?.Current.Name ?? "";
            if (!string.IsNullOrEmpty(val))
                return val;
            Thread.Sleep(200);
        }
        return _window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "TickCanvas"))
            ?.Current.Name ?? "";
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch { /* ignore */ }
        _process.Dispose();
    }
}
