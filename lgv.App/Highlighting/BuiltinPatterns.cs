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
            LineBackground = "#FFEBEE",
            MatchForeground = "#B71C1C",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Warning",
            Pattern = @"\[(WRN|WARN|WARNING)\]",
            ApplyToFullLine = true,
            LineBackground = "#FFF8E1",
            MatchForeground = "#E65100",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Info",
            Pattern = @"\[(INF|INFO)\]",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#1565C0",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Debug",
            Pattern = @"\[(DBG|DEBUG|TRACE|VRB|VERBOSE)\]",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#757575",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Timestamp",
            Pattern = @"^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#37474F",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Exception",
            Pattern = @"\b\w+Exception\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#BF360C",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "StackFrame",
            Pattern = @"^\s+at\s+",
            ApplyToFullLine = true,
            LineBackground = "#FFF3E0",
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
            MatchForeground = "#B71C1C",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Http4xx",
            Pattern = @"\b4\d{2}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#E65100",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Http2xx",
            Pattern = @"\b2\d{2}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#1B5E20",
            MatchBackground = null,
            Enabled = true
        },
        new PatternRule
        {
            Name = "Guid",
            Pattern = @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            ApplyToFullLine = false,
            LineBackground = null,
            MatchForeground = "#4527A0",
            MatchBackground = null,
            Enabled = true
        }
    ];
}
