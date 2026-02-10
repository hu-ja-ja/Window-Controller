using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class MonitorInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
