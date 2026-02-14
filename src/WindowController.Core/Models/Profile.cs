using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class Profile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

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

    /// <summary>
    /// Target virtual desktop GUID. When set, windows are moved here on apply.
    /// Set via right-click → "このデスクトップをターゲットに設定".
    /// </summary>
    [JsonPropertyName("targetDesktopId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDesktopId { get; set; }
}
