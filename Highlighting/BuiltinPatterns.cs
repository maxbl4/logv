namespace lgv.Highlighting;

public static class BuiltinPatterns
{
    public static List<PatternRule> GetDefaults() =>
    [
        new PatternRule
        {
            Name = "Error",
            Pattern = @"\[(ERR|ERROR|CRIT|FATAL|CRITICAL)\]",
            ApplyToFullLine = true,
            LineBackground = "#3D0000",
            MatchForeground = "#FF6B6B",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Warning",
            Pattern = @"\[(WRN|WARN|WARNING)\]",
            ApplyToFullLine = true,
            LineBackground = "#2D2000",
            MatchForeground = "#FFD166",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Info",
            Pattern = @"\[(INF|INFO)\]",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#6BCFFF",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Debug",
            Pattern = @"\[(DBG|DEBUG|TRACE|VRB|VERBOSE)\]",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#888888",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Timestamp",
            Pattern = @"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#88AACC",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Exception",
            Pattern = @"\b\w+Exception\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#FF9966",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "StackFrame",
            Pattern = @"^\s+at\s+",
            ApplyToFullLine = true,
            LineBackground = "#1A0A00",
            MatchForeground = null,
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Http5xx",
            Pattern = @"\b5\d{2}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#FF4444",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Http4xx",
            Pattern = @"\b4\d{2}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#FFAA44",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Http2xx",
            Pattern = @"\b2\d{2}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#66DD88",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Guid",
            Pattern = @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#AAAAFF",
            MatchBackground = null,
            Enabled = true
        }
    ];
}
