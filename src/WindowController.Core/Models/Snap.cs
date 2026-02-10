using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class Snap
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}
