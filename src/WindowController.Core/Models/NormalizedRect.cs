using System.Text.Json.Serialization;

namespace WindowController.Core.Models;

/// <summary>
/// Window rectangle expressed as fractions (0..1) of the owning monitor's work area.
/// Enables resolution-independent restore across monitors with different pixel dimensions.
/// </summary>
public class NormalizedRect
{
    [JsonPropertyName("xN")]
    public double XN { get; set; }

    [JsonPropertyName("yN")]
    public double YN { get; set; }

    [JsonPropertyName("wN")]
    public double WN { get; set; }

    [JsonPropertyName("hN")]
    public double HN { get; set; }

    /// <summary>
    /// Create a NormalizedRect from absolute pixel values relative to a work area.
    /// </summary>
    public static NormalizedRect FromAbsolute(int x, int y, int w, int h, WorkArea wa)
    {
        if (wa.Width <= 0 || wa.Height <= 0)
            return new NormalizedRect();

        return new NormalizedRect
        {
            XN = (double)(x - wa.Left) / wa.Width,
            YN = (double)(y - wa.Top) / wa.Height,
            WN = (double)w / wa.Width,
            HN = (double)h / wa.Height
        };
    }

    /// <summary>
    /// Convert back to absolute pixel rect using the given work area.
    /// Uses Math.Round to prevent truncation-based drift (e.g. -6.999 → -7, not -6).
    /// </summary>
    public Rect ToAbsolute(WorkArea wa)
    {
        return new Rect
        {
            X = wa.Left + (int)Math.Round(XN * wa.Width),
            Y = wa.Top + (int)Math.Round(YN * wa.Height),
            W = (int)Math.Round(WN * wa.Width),
            H = (int)Math.Round(HN * wa.Height)
        };
    }
}
