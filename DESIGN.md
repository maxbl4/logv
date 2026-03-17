# LGV — Log Viewer
## Plan & Design Document

---

## 1. Overview

LGV is a fast, portable Windows desktop application for viewing and monitoring structured plain-text log files. It is designed for developers and operators who need to tail high-throughput logs, spot errors at a glance, and navigate large files without friction.

---

## 2. Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Language | C# 14 / .NET 10 | First-class WPF support, excellent string/IO libs, fast startup with NativeAOT option; current LTS release (support until Nov 2028) |
| UI Framework | WPF | Native Windows rendering, rich control templating, hardware-accelerated text |
| Text Engine | AvalonEdit 6.x | Purpose-built code/log editor: syntax highlighting, large-file support, keyboard nav, TextMarkerService for search markers |
| Settings | System.Text.Json | Zero-dependency JSON serialization, single settings file |
| File Watching | System.IO.FileSystemWatcher | OS-level change notifications for directory monitoring |
| Build/Publish | `dotnet publish` single-file | Self-contained, single `.exe`, no install required |

### Why .NET 10 over .NET 8?
.NET 10 is the current LTS release (support until November 2028). .NET 8's LTS window closes November 2026. No component in this stack requires .NET 8 — AvalonEdit targets .NET Standard 2.0+ and WPF is fully supported on .NET 10. .NET 10 also ships improved JIT, faster startup, and C# 14.

### Why not Rust/egui or C++/Qt?
AvalonEdit already solves the hardest problems (search marker overlay on scrollbar, syntax highlighting engine, efficient text rendering, keyboard navigation). Re-implementing those in Rust or C++ would take significantly longer for no user-visible benefit at this file-size range.

### Single-exe publish command
```
dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true
  -p:EnableCompressionInSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
```
Expected output size: ~25–35 MB. (.NET 10 brings modest size and startup improvements over .NET 8.)

---

## 3. Requirements Summary

| # | Requirement | Notes |
|---|---|---|
| R1 | Windows-only build and target | WPF is Windows-only by design |
| R2 | Portable single `.exe` | No installer, no registry, settings next to exe |
| R3 | Remember and reopen last open files | Persisted per-tab state (path, scroll pos, search, filter) |
| R4 | Open directory → auto-open newest file | FileSystemWatcher on directory; toggleable per-session |
| R5 | Auto-add tab when newer file appears | Added in background; tab header blinks briefly |
| R6 | Text viewer with keyboard nav and copy | AvalonEdit in read-only mode; standard editor keys |
| R7 | Syntax highlighting by pattern | Built-in defaults; toggleable; defined in code as data |
| R8 | Search all occurrences | Highlighted in text + tick marks on scrollbar (Chrome-style) |
| R9 | Line filtering | Hide non-matching lines; toggle; original doc preserved |
| R10 | Background file tailing | New bytes appended without moving view/selection |
| R11 | Auto-scroll to bottom option | Toggle button in toolbar; off by default |
| R12 | Multiple tabs | Each tab is independent; preserves scroll/search/filter state |

---

## 4. Project Structure

```
lgv/
├── lgv.csproj
├── App.xaml / App.xaml.cs               # Startup, settings load/save, unhandled exception handler
├── Core/
│   ├── AppSettings.cs                   # Serializable settings model
│   ├── SettingsStore.cs                 # Load/save JSON next to exe
│   ├── FileTailer.cs                    # Efficient tail: tracks byte offset, reads only new bytes
│   ├── FileWatcher.cs                   # Wraps FileSystemWatcher for single-file changes
│   └── DirectoryMonitor.cs             # Watches dir, detects newest file, emits NewFileDetected event
├── Highlighting/
│   ├── BuiltinPatterns.cs               # Static list of default PatternRule objects
│   ├── PatternRule.cs                   # Model: regex, color, scope (line/match), enabled flag
│   └── LogHighlightingDefinition.cs    # Converts PatternRules → AvalonEdit IHighlightingDefinition
├── Search/
│   ├── SearchEngine.cs                  # Finds all matches in document, returns offset list
│   └── TextMarkerService.cs            # AvalonEdit extension: colored background spans
├── Filter/
│   └── LineFilter.cs                    # Builds filtered document, maps filtered↔original line numbers
├── UI/
│   ├── MainWindow.xaml/.cs             # Root: TabControl, menu, global toolbar
│   ├── LogTabControl.xaml/.cs          # Custom TabControl with blink animation
│   ├── LogTabItem.cs                    # Tab state: path, tailer, watcher, view state
│   ├── LogViewerControl.xaml/.cs       # AvalonEdit host + scrollbar overlay + search bar
│   ├── SearchBar.xaml/.cs              # Inline search/filter toolbar (Ctrl+F to show)
│   └── ScrollbarTickOverlay.xaml/.cs   # Canvas drawn over scrollbar showing search hit positions
└── Converters/                          # WPF value converters (bool→visibility, etc.)
```

---

## 5. Core Subsystems

### 5.1 File Tailing (`FileTailer`)

- Maintains a `long _lastPosition` byte offset into the file.
- On each tick (configurable interval, default **500 ms**), opens file with `FileShare.ReadWrite | FileShare.Delete`, seeks to `_lastPosition`, reads all new bytes, closes handle.
- Emits `NewContent(string text)` event on the UI thread (via `Dispatcher.InvokeAsync`).
- Handles log rotation: if `FileInfo.Length < _lastPosition`, treat as truncation/rotation and reset position to 0.
- Timer is paused while the app window is minimized to reduce CPU use.

```
Poll interval: 500ms default (user-configurable: 250ms / 500ms / 1000ms / 2000ms)
```

### 5.2 Directory Monitor (`DirectoryMonitor`)

- Takes a directory path.
- On activation: scans for newest file by `LastWriteTime`, opens it in a new tab.
- Uses `FileSystemWatcher` (filter `*.*`) to detect `Created` and `Renamed` events.
- When a file newer than the currently tracked newest is detected:
  - Emits `NewFileDetected(string path)` event.
  - The existing tab is **not** affected; a new tab is opened in the background.
- Feature is toggled via a toolbar button (state persisted in settings).

### 5.3 Highlighting Engine

#### PatternRule model
```csharp
record PatternRule(
    string Name,
    string Pattern,          // regex
    Color LineBackground,    // null = match-only highlight
    Color MatchForeground,
    Color MatchBackground,
    bool ApplyToFullLine,    // true = color the entire line
    bool EnabledByDefault
);
```

#### Built-in patterns (defined in `BuiltinPatterns.cs`)

| Name | Pattern | Scope | Color |
|---|---|---|---|
| Error | `\[(ERR\|ERROR\|CRIT\|FATAL\|CRITICAL)\]` | Full line | Background: #3D0000, Foreground: #FF6B6B |
| Warning | `\[(WRN\|WARN\|WARNING)\]` | Full line | Background: #2D2000, Foreground: #FFD166 |
| Info | `\[(INF\|INFO)\]` | Match only | Foreground: #6BCFFF |
| Debug | `\[(DBG\|DEBUG\|TRACE\|VRB\|VERBOSE)\]` | Match only | Foreground: #888888 |
| Timestamp | `^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}` | Match only | Foreground: #88AACC |
| Exception type | `\b\w+Exception\b` | Match only | Foreground: #FF9966 |
| Stack frame | `^\s+at\s+` | Full line | Background: #1A0A00 |
| HTTP 5xx | `\b5\d{2}\b` | Match only | Foreground: #FF4444 |
| HTTP 4xx | `\b4\d{2}\b` | Match only | Foreground: #FFAA44 |
| HTTP 2xx | `\b2\d{2}\b` | Match only | Foreground: #66DD88 |
| GUID | `\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-...\b` | Match only | Foreground: #AAAAFF |

All patterns are stored in `lgv.settings.json` (see §8). `BuiltinPatterns.cs` contains the **default list only** — it is written to the JSON file the first time the app runs (or if the `patterns` key is missing). After that, the JSON file is the authoritative source. Users can add, remove, or edit patterns by editing the file directly; the app reloads on next startup.

### 5.4 Search (`SearchEngine` + `ScrollbarTickOverlay`)

**Flow:**
1. User opens search bar (`Ctrl+F`). Input field appears below toolbar.
2. On each keystroke (debounced 150ms), `SearchEngine.FindAll(document, query, options)` runs on a background thread.
3. Returns `IReadOnlyList<(int offset, int length)>` of all matches.
4. `TextMarkerService` creates highlight markers for all matches in AvalonEdit (yellow background by default; current match: orange).
5. `ScrollbarTickOverlay` receives the same list + document line count → draws proportionally-positioned tick marks as a `Canvas` overlaid on the vertical scrollbar track.
6. `Ctrl+G` / `F3` / `Enter` moves to next match; `Shift+F3` previous. Current match is centered in view.
7. Search is case-insensitive by default; toggle for case-sensitive and regex mode.
8. Match count shown as `"N of M"` in search bar.

**Scrollbar overlay implementation:**
- Template the `ScrollBar` in AvalonEdit's `ScrollViewer` to inject an `AdornerDecorator`.
- Or simpler: place a transparent `Canvas` as an overlay using a `Grid` with shared rows, positioned to match the scrollbar track bounds.
- Each tick is a 2px-tall `Rectangle` at `Canvas.Top = (matchLine / totalLines) * trackHeight`.
- Color: gold for normal hits, red for error-line hits (if the match is on a highlighted error line).

### 5.5 Line Filtering (`LineFilter`)

**Design decision:** filtering operates on a *derived document* to preserve AvalonEdit's native rendering performance. The original document is always kept in memory.

**Flow:**
1. User types in the filter box (separate from search; `Ctrl+Shift+F` or filter icon).
2. `LineFilter.Apply(originalText, pattern)` runs on a background thread:
   - Iterates lines, keeps only those matching the pattern (regex or plain text).
   - Builds a new string and a `int[] lineMap` (filteredLine → originalLine).
3. AvalonEdit document is replaced with the filtered string.
4. A "Filter active" indicator shows in the status bar with match count.
5. Turning filter off restores the original document and scroll position.
6. Tailing while filter is active: new content is appended to the original buffer; filter is re-applied on the background thread if new lines pass the filter; otherwise only the buffer grows.

**Filter mode options:** include matching lines (default) | exclude matching lines.

### 5.6 Background Tailing & View Stability

When `FileTailer` emits new content:
1. Save `ScrollViewer.VerticalOffset` and `TextEditor.SelectionStart/Length`.
2. Append text to the document using `document.Insert(document.TextLength, newText)`.
3. Restore saved offset and selection (AvalonEdit resets scroll on document change).
4. If **Auto-scroll** is enabled: instead scroll to end after append.

This ensures the view does not jump when new lines arrive.

---

## 6. Tab System

### Tab State (`LogTabItem`)
Each tab holds:
```csharp
class LogTabItem {
    string FilePath;
    string? WatchedDirectory;    // set if opened via directory monitor
    FileTailer Tailer;
    FileWatcher Watcher;
    double ScrollOffset;
    int SelectionStart, SelectionLength;
    string SearchQuery;
    SearchOptions SearchOptions;
    string FilterQuery;
    FilterMode FilterMode;
    bool AutoScroll;
    TextDocument OriginalDocument;   // always full content
    TextDocument? FilteredDocument;  // null when filter inactive
    int[] FilterLineMap;
}
```

### New Tab Blink Animation
When a new tab is added by the directory monitor:
1. Tab is inserted at the end, not selected.
2. A `ColorAnimation` runs on the tab header's background:
   - From: `#3A5A8A` (accent blue) → To: `Transparent`
   - Duration: 1.2 seconds, `EasingFunction: QuadraticEase Out`
   - Repeats: 3 times (`RepeatBehavior="3x"`)
3. A subtle "NEW" badge overlay fades out after the animation completes.

---

## 7. UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ Menu: File  View  Settings                                   │
├─────────────────────────────────────────────────────────────┤
│ [Open File] [Open Dir] [Watch Dir: ON] [AutoScroll: OFF]    │  ← Toolbar
│ [Highlight: ON] [Poll: 500ms]                               │
├──────────┬──────────┬──────────┬──────────┐                 │
│ app.log  │server.log│ ●NEW●    │    +     │                 │  ← Tabs
├──────────┴──────────┴──────────┴──────────┴─────────────────┤
│                                                    │▲│       │
│  2024-01-15 10:23:01 [INF] Service started         │ │       │
│  2024-01-15 10:23:02 [ERR] Connection failed       │█│ ← scrollbar
│    at Database.Connect() line 42                   │ │  with tick
│  2024-01-15 10:23:03 [WRN] Retry attempt 1/3       │▓│  marks
│  ...                                               │ │       │
│                                                    │▼│       │
├─────────────────────────────────────────────────────────────┤
│ [x] Filter: [__________] [Include▼] [Regex]  42 lines shown │  ← Filter bar
│ [x] Search: [__________] [Aa] [.*]   5 of 23 matches  [∧][∨]│  ← Search bar
├─────────────────────────────────────────────────────────────┤
│ app.log  │  Line 1,204 / 3,847  │  Tailing: active  │ UTF-8 │  ← Status bar
└─────────────────────────────────────────────────────────────┘
```

**Keyboard shortcuts:**
| Key | Action |
|---|---|
| `Ctrl+O` | Open file |
| `Ctrl+Shift+O` | Open directory |
| `Ctrl+W` | Close current tab |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Ctrl+F` | Toggle search bar |
| `Ctrl+Shift+F` | Toggle filter bar |
| `F3` / `Shift+F3` | Next / previous search hit |
| `Ctrl+G` | Go to line |
| `Ctrl+End` | Jump to end of file |
| `Ctrl+Home` | Jump to start of file |
| `Ctrl+C` | Copy selection |
| `Ctrl+A` | Select all |
| `F5` | Force re-read file from disk |
| `Ctrl+D` | Toggle directory watch mode |
| `Ctrl+T` | Toggle auto-scroll |

---

## 8. Settings & Persistence

Settings file: `lgv.settings.json` placed in the **same directory as the exe**. This is the single source of truth for all user preferences, open session state, and highlighting patterns. Users can edit it directly in any text editor.

`SettingsStore` resolves the path as `Path.Combine(AppContext.BaseDirectory, "lgv.settings.json")`.

On first run (file absent or `patterns` key missing): built-in defaults from `BuiltinPatterns.cs` are written into the file. Subsequent runs load from file only.

```json
{
  "lastOpenTabs": [
    {
      "filePath": "C:\\logs\\app.log",
      "scrollOffset": 4820.5,
      "searchQuery": "",
      "filterQuery": "ERROR",
      "filterMode": "Include",
      "autoScroll": false
    },
    {
      "directoryPath": "C:\\logs\\",
      "watchNewFiles": true,
      "filePath": "C:\\logs\\app-2024-01-15.log",
      "scrollOffset": 0,
      "searchQuery": "",
      "filterQuery": "",
      "filterMode": "Include",
      "autoScroll": true
    }
  ],
  "lastActiveTabIndex": 0,
  "watchDirectoryEnabled": true,
  "pollIntervalMs": 500,
  "highlightingEnabled": true,
  "theme": "Dark",
  "fontSize": 13,
  "fontFamily": "Consolas",
  "patterns": [
    {
      "name": "Error",
      "pattern": "\\[(ERR|ERROR|CRIT|FATAL|CRITICAL)\\]",
      "applyToFullLine": true,
      "lineBackground": "#3D0000",
      "matchForeground": "#FF6B6B",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Warning",
      "pattern": "\\[(WRN|WARN|WARNING)\\]",
      "applyToFullLine": true,
      "lineBackground": "#2D2000",
      "matchForeground": "#FFD166",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Info",
      "pattern": "\\[(INF|INFO)\\]",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#6BCFFF",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Debug",
      "pattern": "\\[(DBG|DEBUG|TRACE|VRB|VERBOSE)\\]",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#888888",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Timestamp",
      "pattern": "^\\d{4}-\\d{2}-\\d{2}[T ]\\d{2}:\\d{2}:\\d{2}",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#88AACC",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Exception",
      "pattern": "\\b\\w+Exception\\b",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#FF9966",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "StackFrame",
      "pattern": "^\\s+at\\s+",
      "applyToFullLine": true,
      "lineBackground": "#1A0A00",
      "matchForeground": null,
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Http5xx",
      "pattern": "\\b5\\d{2}\\b",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#FF4444",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Http4xx",
      "pattern": "\\b4\\d{2}\\b",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#FFAA44",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Http2xx",
      "pattern": "\\b2\\d{2}\\b",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#66DD88",
      "matchBackground": null,
      "enabled": true
    },
    {
      "name": "Guid",
      "pattern": "\\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\b",
      "applyToFullLine": false,
      "lineBackground": null,
      "matchForeground": "#AAAAFF",
      "matchBackground": null,
      "enabled": true
    }
  ]
}
```

**Startup behavior:**
- Restore tabs in saved order; re-open files; seek to last scroll position; restore search/filter state.
- If a tab's `filePath` no longer exists: show a placeholder tab with a "File not found" message and a Retry button.
- If a tab has `directoryPath`: reattach the directory monitor on that path.
- Save settings on app exit and whenever a tab is closed.

---

## 9. Performance Considerations

| Scenario | Strategy |
|---|---|
| Large file initial load (50 MB) | Load on background thread; show progress in tab header; do not block UI |
| High-frequency tailing (50 MB/min) | Read only new bytes; batch appends if multiple ticks fired during heavy load; collapse to single document update per UI frame |
| Search on large document | Run on `Task.Run`; cancel previous search if query changes before it completes (`CancellationToken`) |
| Filter rebuild | Same background pattern with cancellation |
| Scrollbar tick rendering | Computed once per search result set; cached until document or results change |
| Regex compilation | Pre-compile all highlighting regexes at startup with `RegexOptions.Compiled` |
| Memory | Each tab: max ~100 MB for 50 MB file (string + document model). For 5 tabs: ~500 MB worst case. Acceptable on modern dev machines. |

---

## 10. Implementation Phases

### Phase 1 — Shell & Core Viewer
- Project setup, single-exe publish pipeline
- Main window, tab control
- Open file, display in AvalonEdit (read-only)
- Keyboard navigation, copy

### Phase 2 — Tailing & File Watching
- `FileTailer` with byte-offset tracking
- View stability on append
- Auto-scroll toggle
- `FileWatcher` for single-file change detection

### Phase 3 — Highlighting
- `PatternRule` model and `BuiltinPatterns`
- AvalonEdit custom highlighting definition
- Toggle highlighting on/off per pattern

### Phase 4 — Search
- Search bar UI (Ctrl+F)
- `SearchEngine` and `TextMarkerService`
- `ScrollbarTickOverlay`
- Next/previous navigation, match counter

### Phase 5 — Filter
- Filter bar UI
- `LineFilter` with derived document and line map
- Include/exclude modes
- Tailing while filter is active

### Phase 6 — Directory Monitor & Tab UX
- `DirectoryMonitor`
- Background tab open with blink animation
- Settings persistence (open/restore tabs)

### Phase 7 — Polish
- Status bar (line/col, tail status, encoding)
- Go-to-line dialog
- Settings dialog (font, poll interval, theme)
- Edge cases: file rotation, file deleted mid-tail, very long lines

---

## 11. Dependencies (NuGet)

| Package | Version | Purpose |
|---|---|---|
| `AvalonEditB` or `AvalonEdit` | 6.x | Core text editor/viewer |
| `System.Text.Json` | (in-box .NET 8) | Settings serialization |

No other third-party dependencies. `FileSystemWatcher` and all other features come from the BCL.

> Note: `AvalonEditB` is a maintained WPF-focused fork with additional bugfixes; either works.

---

## 12. Out of Scope (v1)

- Log file parsing into structured columns/table view
- Highlighting pattern editor UI (patterns are in `lgv.settings.json`, editable directly as JSON)
- Remote/SSH file viewing
- Log aggregation from multiple files into one view
- Plugins or scripting
