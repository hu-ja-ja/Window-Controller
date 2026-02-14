using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

/// <summary>
/// Application-level settings stored separately from profiles.json
/// to avoid the chicken-and-egg problem of storing the profiles path
/// inside profiles.json itself.
/// Persisted to config/appsettings.json next to the executable.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Absolute path to profiles.json.
    /// Empty/null means use the default location (config/profiles.json next to exe).
    /// </summary>
    [JsonPropertyName("profilesPath")]
    public string ProfilesPath { get; set; } = "";

    /// <summary>
    /// Hotkey configurations (GUI toggle + per-profile apply).
    /// </summary>
    [JsonPropertyName("hotkeys")]
    public HotkeySettings Hotkeys { get; set; } = new();
}
