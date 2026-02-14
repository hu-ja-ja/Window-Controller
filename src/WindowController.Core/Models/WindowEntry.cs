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

    /// <summary>
    /// Window rect normalised to the owning monitor's work area (0..1).
    /// Used for resolution-independent restore.
    /// </summary>
    [JsonPropertyName("rectNormalized")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NormalizedRect? RectNormalized { get; set; }

    [JsonPropertyName("minMax")]
    public int MinMax { get; set; }

    [JsonPropertyName("snap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Snap? Snap { get; set; }

    [JsonPropertyName("monitor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MonitorInfo? Monitor { get; set; }

    /// <summary>
    /// Virtual Desktop GUID that owned this window at capture time.
    /// </summary>
    [JsonPropertyName("desktopId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesktopId { get; set; }
}
