using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class Profile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("syncMinMax")]
    public int SyncMinMax { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("windows")]
    public List<WindowEntry> Windows { get; set; } = new();
}
