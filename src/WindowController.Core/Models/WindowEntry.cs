using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class WindowEntry
{
    [JsonPropertyName("match")]
    public MatchInfo Match { get; set; } = new();

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("rect")]
    public Rect Rect { get; set; } = new();

    [JsonPropertyName("minMax")]
    public int MinMax { get; set; }

    [JsonPropertyName("snap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Snap? Snap { get; set; }

    [JsonPropertyName("monitor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MonitorInfo? Monitor { get; set; }
}
