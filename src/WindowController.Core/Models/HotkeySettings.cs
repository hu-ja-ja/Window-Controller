using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

/// <summary>
/// Hotkey binding configuration for a profile or the GUI toggle.
/// </summary>
public class HotkeyBinding
{
    /// <summary>
    /// Key code (e.g., "W", "F1", "1", etc.).
    /// Empty string means not bound.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>
    /// Modifier flags (Ctrl, Alt, Shift, Win).
    /// </summary>
    [JsonPropertyName("ctrl")]
    public bool Ctrl { get; set; }

    [JsonPropertyName("alt")]
    public bool Alt { get; set; }

    [JsonPropertyName("shift")]
    public bool Shift { get; set; }

    [JsonPropertyName("win")]
    public bool Win { get; set; }

    /// <summary>
    /// Returns true if the binding is empty (no key assigned).
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(Key);

    /// <summary>
    /// Returns a human-readable representation of the hotkey.
    /// </summary>
    public override string ToString()
    {
        if (IsEmpty) return "なし";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>
    /// Creates a copy of this binding.
    /// </summary>
    public HotkeyBinding Clone() => new()
    {
        Key = Key,
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Win = Win
    };

    /// <summary>
    /// Returns true if this binding equals another (same key and modifiers).
    /// </summary>
    public bool Equals(HotkeyBinding? other)
    {
        if (other is null) return false;
        return Key.Equals(other.Key, StringComparison.OrdinalIgnoreCase) &&
               Ctrl == other.Ctrl &&
               Alt == other.Alt &&
               Shift == other.Shift &&
               Win == other.Win;
    }

    public override bool Equals(object? obj) => obj is HotkeyBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key?.ToUpperInvariant(), Ctrl, Alt, Shift, Win);
}

/// <summary>
/// All hotkey-related settings for the application.
/// </summary>
public class HotkeySettings
{
    /// <summary>
    /// Hotkey to toggle the main GUI. Default: Ctrl+Alt+W (for backward compat).
    /// </summary>
    [JsonPropertyName("showGui")]
    public HotkeyBinding ShowGui { get; set; } = new() { Key = "W", Ctrl = true, Alt = true };

    /// <summary>
    /// Per-profile hotkeys. Key is profile Id, value is the binding.
    /// Empty bindings (or missing keys) mean no hotkey for that profile.
    /// </summary>
    [JsonPropertyName("profiles")]
    public Dictionary<string, HotkeyBinding> Profiles { get; set; } = new();
}
