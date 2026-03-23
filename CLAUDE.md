# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Rules

After every code change, run all tests and ensure they pass before considering the task done:
```bash
dotnet build lgv.App/lgv.csproj -c Debug && dotnet test lgv.Tests && dotnet test lgv.UITests
```

## Build & Test

```bash
# Build (default Debug x64, self-contained)
dotnet build

# Run unit tests
dotnet test lgv.Tests

# Run a single unit test by name pattern
dotnet test lgv.Tests -k "NoPatterns_ReturnsOriginalTextUnchanged"

# UI tests require the app to be built first
dotnet build -c Debug lgv.App/lgv.csproj
dotnet test lgv.UITests

# Run a single UI test by class name
dotnet test lgv.UITests -k "LineNumberDisplayTests"

# Release publish (bundles into single file)
dotnet publish -c Release lgv.App/lgv.csproj
```

All projects target `net10.0-windows`. The app project has `RuntimeIdentifier=win-x64` and `SelfContained=true` set in the csproj, so no `-r` or `--self-contained` flags are needed at the command line. `PublishSingleFile=true` is also set in the csproj and takes effect during `dotnet publish`.

`dotnet build` output lands at `lgv.App/bin/x64/Debug/net10.0-windows/win-x64/lgv.exe`. UI tests probe that path (falling back to Release).

## Architecture

LGV is a WPF log viewer. The single runtime dependency is **AvalonEdit** (ICSharpCode) for the editor surface. Settings persist to `lgv.settings.json` next to the exe.

### Data flow

**Tailing:**
`FileTailer` (500 ms timer) reads new bytes → decodes UTF-8 → fires `NewContent` event → `LogViewerControl.OnNewContent` marshals to UI thread → appends to `LogTabState._originalText` (a `StringBuilder`) → appends to `Editor.Document`.

**Filter (active):**
When `_activeFilterPatterns` is set, new content triggers a 1-second debounce timer instead of an immediate re-filter. When the timer fires, `LineFilter.Apply` runs on a background `Task` with a `CancellationToken` (a new filter cancels the previous one). Result: a `FilterResult` containing `FilteredText` + `int[] LineMap`. The line map drives `MappedLineNumberMargin`, which renders original source line numbers instead of sequential ones.

**Search:**
Typing in the search box triggers a 150 ms debounce, then `SearchEngine.FindAll` runs on a background task. Results drive `SearchMarkerRenderer` (AvalonEdit background renderer) and `DrawTicks` (scrollbar tick marks drawn as a single frozen `StreamGeometry` on a `Path`).

### Key design constraints

- **Exclude mode only**: the filter keeps lines that match *no* pattern.
- **Read-only editor**: `Editor.IsReadOnly = true`; undo stack is disabled (`UndoStack.SizeLimit = 0`) since logs are append-only.
- **MappedLineNumberMargin** requires STA. Unit tests that instantiate it must use the `Sta.Run()` helper defined in `lgv.Tests`.
- **UI tests** use Windows UIAutomation against the live exe process. They delete `lgv.settings.json` before launch for a clean state. They are not headless-friendly.

### Automation IDs used by UI tests

`LineNumberMargin`, `EditorScrollViewer`, `TickCanvas`, `SearchBox`, `GlobalFilterBox`, `ScrollToTopBtn`, `ScrollToEndBtn`, `ShowSearchBarBtn`

The margin writes comma-separated visible line numbers to `AutomationProperties.HelpText` on every render; tests poll this to detect scroll completion.

### Project layout

| Project | Purpose |
|---|---|
| `lgv.App/` | Main WPF app. Subdirs: `Core/` (FileTailer, DirectoryMonitor, settings), `Filter/`, `Search/`, `Highlighting/`, `UI/` |
| `lgv.Tests/` | xUnit unit tests (LineFilter logic, MappedLineNumberMargin sizing) |
| `lgv.UITests/` | xUnit + UIAutomation end-to-end tests; `AppDriver.cs` manages the process |
| `lgv.TestGen/` | Standalone tool used to generate test fixture data |
