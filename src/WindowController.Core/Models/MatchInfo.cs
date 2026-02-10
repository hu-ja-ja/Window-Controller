using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class MatchInfo
{
    [JsonPropertyName("exe")]
    public string Exe { get; set; } = "";

    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("urlKey")]
    public string UrlKey { get; set; } = "";

    [JsonPropertyName("browser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrowserIdentity? Browser { get; set; }
}
