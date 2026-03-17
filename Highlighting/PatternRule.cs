namespace lgv.Highlighting;

public class PatternRule
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public bool ApplyToFullLine { get; set; }
    public string? LineBackground { get; set; }   // hex #RRGGBB or null
    public string? MatchForeground { get; set; }
    public string? MatchBackground { get; set; }
    public bool Enabled { get; set; } = true;
}
