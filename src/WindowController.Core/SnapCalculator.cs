namespace WindowController.Core;

/// <summary>
/// Work area rectangle for snap calculations.
/// </summary>
public record WorkArea(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// Snap detection and rect calculation from work area.
/// </summary>
public static class SnapCalculator
{
    private const int Tolerance = 25;
    private const int SizeTolerance = 35;

    private static bool IsNear(int a, int b) => Math.Abs(a - b) <= Tolerance;
    private static bool IsNearSize(int a, int b) => Math.Abs(a - b) <= SizeTolerance;

    /// <summary>
    /// Detect snap type from window rect and work area.
    /// </summary>
    public static string? DetectSnap(int x, int y, int w, int h, WorkArea wa)
    {
        if (wa.Width <= 0 || wa.Height <= 0)
            return null;

        int hw = wa.Width / 2;
        int hh = wa.Height / 2;

        // Left half
        if (IsNear(x, wa.Left) && IsNear(y, wa.Top) && IsNearSize(w, hw) && IsNearSize(h, wa.Height))
            return "left";
        // Right half
        if (IsNear(x + w, wa.Right) && IsNear(y, wa.Top) && IsNearSize(w, hw) && IsNearSize(h, wa.Height))
            return "right";
        // Top half
        if (IsNear(x, wa.Left) && IsNear(y, wa.Top) && IsNearSize(w, wa.Width) && IsNearSize(h, hh))
            return "top";
        // Bottom half
        if (IsNear(x, wa.Left) && IsNear(y + h, wa.Bottom) && IsNearSize(w, wa.Width) && IsNearSize(h, hh))
            return "bottom";
        // Quadrants
        if (IsNear(x, wa.Left) && IsNear(y, wa.Top) && IsNearSize(w, hw) && IsNearSize(h, hh))
            return "topLeft";
        if (IsNear(x + w, wa.Right) && IsNear(y, wa.Top) && IsNearSize(w, hw) && IsNearSize(h, hh))
            return "topRight";
        if (IsNear(x, wa.Left) && IsNear(y + h, wa.Bottom) && IsNearSize(w, hw) && IsNearSize(h, hh))
            return "bottomLeft";
        if (IsNear(x + w, wa.Right) && IsNear(y + h, wa.Bottom) && IsNearSize(w, hw) && IsNearSize(h, hh))
            return "bottomRight";

        return null;
    }

    /// <summary>
    /// Calculate rect from snap type and work area.
    /// </summary>
    public static Models.Rect? RectFromSnap(WorkArea wa, string snapType)
    {
        if (wa.Width <= 0 || wa.Height <= 0)
            return null;

        int hw = wa.Width / 2;
        int hh = wa.Height / 2;

        return snapType switch
        {
            "left" => new Models.Rect { X = wa.Left, Y = wa.Top, W = hw, H = wa.Height },
            "right" => new Models.Rect { X = wa.Left + (wa.Width - hw), Y = wa.Top, W = hw, H = wa.Height },
            "top" => new Models.Rect { X = wa.Left, Y = wa.Top, W = wa.Width, H = hh },
            "bottom" => new Models.Rect { X = wa.Left, Y = wa.Top + (wa.Height - hh), W = wa.Width, H = hh },
            "topLeft" => new Models.Rect { X = wa.Left, Y = wa.Top, W = hw, H = hh },
            "topRight" => new Models.Rect { X = wa.Left + (wa.Width - hw), Y = wa.Top, W = hw, H = hh },
            "bottomLeft" => new Models.Rect { X = wa.Left, Y = wa.Top + (wa.Height - hh), W = hw, H = hh },
            "bottomRight" => new Models.Rect { X = wa.Left + (wa.Width - hw), Y = wa.Top + (wa.Height - hh), W = hw, H = hh },
            _ => null
        };
    }
}
