using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class Settings
{
    [JsonPropertyName("syncMinMax")]
    public int SyncMinMax { get; set; }

    [JsonPropertyName("showGuiOnStartup")]
    public int ShowGuiOnStartup { get; set; }
}
