using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class MonitorInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// DisplayConfig target device path — the most stable physical monitor identifier.
    /// </summary>
    [JsonPropertyName("devicePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DevicePath { get; set; }

    /// <summary>
    /// Full monitor pixel width (rcMonitor), used for aspect-ratio / resolution warnings.
    /// </summary>
    [JsonPropertyName("pixelWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PixelWidth { get; set; }

    /// <summary>
    /// Full monitor pixel height (rcMonitor), used for aspect-ratio / resolution warnings.
    /// </summary>
    [JsonPropertyName("pixelHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PixelHeight { get; set; }

    /// <summary>
    /// Aspect ratio (width / height). Computed helper for in-memory diagnostics; not serialized.
    /// </summary>
    [JsonIgnore]
    public double AspectRatio => PixelHeight > 0 ? (double)PixelWidth / PixelHeight : 0;
}
