namespace lgv.Core;
using lgv.Highlighting;

public class TabState
{
    public string? FilePath { get; set; }
    public string? DirectoryPath { get; set; }   // set if this is a directory tab
    public bool WatchNewFiles { get; set; }
    public double ScrollOffset { get; set; }
    public string SearchQuery { get; set; } = "";
    public bool SearchCaseSensitive { get; set; }
    public bool SearchUseRegex { get; set; }
    public bool AutoScroll { get; set; }

    // Directory tab children (file sub-tabs)
    public List<TabState>? ChildTabs { get; set; }
    public int ActiveChildTabIndex { get; set; }
}

public class AppSettings
{
    public List<TabState> LastOpenTabs { get; set; } = [];
    public int LastActiveTabIndex { get; set; }
    public bool WatchDirectoryEnabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 500;
    public bool HighlightingEnabled { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = "Consolas";
    public List<PatternRule> Patterns { get; set; } = [];
    public string GlobalFilterPatterns { get; set; } = "";
}
