using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

public class Settings
{
    [JsonPropertyName("syncMinMax")]
    public int SyncMinMax { get; set; }

    [JsonPropertyName("showGuiOnStartup")]
    public int ShowGuiOnStartup { get; set; }

    // ── Monitor mismatch policies ──

    /// <summary>
    /// Warn when the target monitor has a different aspect ratio.
    /// Threshold expressed as absolute difference of w/h ratios (default 0.02 ≈ 2 %).
    /// </summary>
    [JsonPropertyName("aspectRatioWarnThreshold")]
    public double AspectRatioWarnThreshold { get; set; } = 0.02;

    /// <summary>
    /// Warn when the window is placed on a monitor with a different resolution
    /// (even if the aspect ratio is the same, e.g. 1080p → 4K).
    /// </summary>
    [JsonPropertyName("warnOnResolutionMismatch")]
    public bool WarnOnResolutionMismatch { get; set; } = true;

    /// <summary>
    /// Warn when the target monitor cannot be resolved and a fallback is used.
    /// </summary>
    [JsonPropertyName("warnOnMonitorMismatch")]
    public bool WarnOnMonitorMismatch { get; set; } = true;

    // ── Virtual Desktop policies ──

    /// <summary>
    /// Allow applying / moving windows across virtual desktops.
    /// </summary>
    [JsonPropertyName("allowCrossDesktopApply")]
    public bool AllowCrossDesktopApply { get; set; } = true;
}
