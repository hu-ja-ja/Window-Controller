using WindowController.Core;

namespace WindowController.Core.Tests;

public class SnapCalculatorTests
{
    // Standard work area: 1920x1040 starting at (0, 0)
    private static readonly WorkArea Wa = new(0, 0, 1920, 1040);

    // ────────────────── DetectSnap ──────────────────

    [Fact]
    public void DetectSnap_LeftHalf_ReturnsLeft()
    {
        var result = SnapCalculator.DetectSnap(0, 0, 960, 1040, Wa);
        Assert.Equal("left", result);
    }

    [Fact]
    public void DetectSnap_RightHalf_ReturnsRight()
    {
        var result = SnapCalculator.DetectSnap(960, 0, 960, 1040, Wa);
        Assert.Equal("right", result);
    }

    [Fact]
    public void DetectSnap_TopHalf_ReturnsTop()
    {
        var result = SnapCalculator.DetectSnap(0, 0, 1920, 520, Wa);
        Assert.Equal("top", result);
    }

    [Fact]
    public void DetectSnap_BottomHalf_ReturnsBottom()
    {
        var result = SnapCalculator.DetectSnap(0, 520, 1920, 520, Wa);
        Assert.Equal("bottom", result);
    }

    [Fact]
    public void DetectSnap_TopLeft_Quadrant()
    {
        var result = SnapCalculator.DetectSnap(0, 0, 960, 520, Wa);
        Assert.Equal("topLeft", result);
    }

    [Fact]
    public void DetectSnap_TopRight_Quadrant()
    {
        var result = SnapCalculator.DetectSnap(960, 0, 960, 520, Wa);
        Assert.Equal("topRight", result);
    }

    [Fact]
    public void DetectSnap_BottomLeft_Quadrant()
    {
        var result = SnapCalculator.DetectSnap(0, 520, 960, 520, Wa);
        Assert.Equal("bottomLeft", result);
    }

    [Fact]
    public void DetectSnap_BottomRight_Quadrant()
    {
        var result = SnapCalculator.DetectSnap(960, 520, 960, 520, Wa);
        Assert.Equal("bottomRight", result);
    }

    [Fact]
    public void DetectSnap_WithinTolerance_StillMatches()
    {
        // 20px offset is within default tolerance (25)
        var result = SnapCalculator.DetectSnap(20, 15, 950, 1040, Wa);
        Assert.Equal("left", result);
    }

    [Fact]
    public void DetectSnap_FreeForm_ReturnsNull()
    {
        var result = SnapCalculator.DetectSnap(100, 200, 600, 400, Wa);
        Assert.Null(result);
    }

    [Fact]
    public void DetectSnap_ZeroWorkArea_ReturnsNull()
    {
        var empty = new WorkArea(0, 0, 0, 0);
        var result = SnapCalculator.DetectSnap(0, 0, 960, 520, empty);
        Assert.Null(result);
    }

    // ────────────────── RectFromSnap ──────────────────

    [Theory]
    [InlineData("left", 0, 0, 960, 1040)]
    [InlineData("right", 960, 0, 960, 1040)]
    [InlineData("top", 0, 0, 1920, 520)]
    [InlineData("bottom", 0, 520, 1920, 520)]
    [InlineData("topLeft", 0, 0, 960, 520)]
    [InlineData("topRight", 960, 0, 960, 520)]
    [InlineData("bottomLeft", 0, 520, 960, 520)]
    [InlineData("bottomRight", 960, 520, 960, 520)]
    public void RectFromSnap_ReturnsCorrectRect(string snap, int ex, int ey, int ew, int eh)
    {
        var rect = SnapCalculator.RectFromSnap(Wa, snap);
        Assert.NotNull(rect);
        Assert.Equal(ex, rect.X);
        Assert.Equal(ey, rect.Y);
        Assert.Equal(ew, rect.W);
        Assert.Equal(eh, rect.H);
    }

    [Fact]
    public void RectFromSnap_UnknownType_ReturnsNull()
    {
        var result = SnapCalculator.RectFromSnap(Wa, "diagonal");
        Assert.Null(result);
    }

    [Fact]
    public void RectFromSnap_ZeroWorkArea_ReturnsNull()
    {
        var empty = new WorkArea(0, 0, 0, 0);
        var result = SnapCalculator.RectFromSnap(empty, "left");
        Assert.Null(result);
    }

    // ────────────────── Non-zero origin ──────────────────

    [Fact]
    public void DetectSnap_OffsetOrigin_LeftHalf()
    {
        // Second monitor at (1920, 0)
        var wa2 = new WorkArea(1920, 0, 1920, 1040);
        var result = SnapCalculator.DetectSnap(1920, 0, 960, 1040, wa2);
        Assert.Equal("left", result);
    }

    [Fact]
    public void RectFromSnap_OffsetOrigin_RightHalf()
    {
        var wa2 = new WorkArea(1920, 0, 1920, 1040);
        var rect = SnapCalculator.RectFromSnap(wa2, "right");
        Assert.NotNull(rect);
        Assert.Equal(2880, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(960, rect.W);
        Assert.Equal(1040, rect.H);
    }
}
