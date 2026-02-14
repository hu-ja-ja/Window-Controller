using WindowController.Core.Models;

namespace WindowController.Core.Tests;

public class NormalizedRectTests
{
    // Standard 1920×1080 work area starting at (0, 0), taskbar 40px
    private static readonly WorkArea Wa1080 = new(0, 0, 1920, 1040);
    // 4K work area with same aspect ratio, taskbar 40px scaled
    private static readonly WorkArea Wa4K = new(0, 0, 3840, 2120);
    // Secondary monitor offset
    private static readonly WorkArea WaSecondary = new(1920, 0, 1920, 1040);

    // ────────────────── FromAbsolute ──────────────────

    [Fact]
    public void FromAbsolute_FullWorkArea_Returns_1_1()
    {
        var norm = NormalizedRect.FromAbsolute(0, 0, 1920, 1040, Wa1080);

        Assert.Equal(0.0, norm.XN, 6);
        Assert.Equal(0.0, norm.YN, 6);
        Assert.Equal(1.0, norm.WN, 6);
        Assert.Equal(1.0, norm.HN, 6);
    }

    [Fact]
    public void FromAbsolute_LeftHalf_Returns_0_5_Width()
    {
        var norm = NormalizedRect.FromAbsolute(0, 0, 960, 1040, Wa1080);

        Assert.Equal(0.0, norm.XN, 6);
        Assert.Equal(0.0, norm.YN, 6);
        Assert.Equal(0.5, norm.WN, 6);
        Assert.Equal(1.0, norm.HN, 6);
    }

    [Fact]
    public void FromAbsolute_RightHalf_Returns_0_5_X()
    {
        var norm = NormalizedRect.FromAbsolute(960, 0, 960, 1040, Wa1080);

        Assert.Equal(0.5, norm.XN, 6);
        Assert.Equal(0.0, norm.YN, 6);
        Assert.Equal(0.5, norm.WN, 6);
        Assert.Equal(1.0, norm.HN, 6);
    }

    [Fact]
    public void FromAbsolute_SecondaryMonitor_UsesRelativeCoordinates()
    {
        // Window at left half of secondary monitor
        var norm = NormalizedRect.FromAbsolute(1920, 0, 960, 1040, WaSecondary);

        Assert.Equal(0.0, norm.XN, 6);
        Assert.Equal(0.0, norm.YN, 6);
        Assert.Equal(0.5, norm.WN, 6);
        Assert.Equal(1.0, norm.HN, 6);
    }

    [Fact]
    public void FromAbsolute_ZeroSizeWorkArea_ReturnsZeros()
    {
        var zeroWa = new WorkArea(0, 0, 0, 0);
        var norm = NormalizedRect.FromAbsolute(100, 200, 400, 300, zeroWa);

        Assert.Equal(0.0, norm.XN);
        Assert.Equal(0.0, norm.YN);
        Assert.Equal(0.0, norm.WN);
        Assert.Equal(0.0, norm.HN);
    }

    // ────────────────── ToAbsolute ──────────────────

    [Fact]
    public void ToAbsolute_FullWorkArea_ReturnsFullRect()
    {
        var norm = new NormalizedRect { XN = 0, YN = 0, WN = 1, HN = 1 };
        var rect = norm.ToAbsolute(Wa1080);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(1920, rect.W);
        Assert.Equal(1040, rect.H);
    }

    [Fact]
    public void ToAbsolute_SecondaryMonitor_OffsetsCorrectly()
    {
        var norm = new NormalizedRect { XN = 0.5, YN = 0, WN = 0.5, HN = 1 };
        var rect = norm.ToAbsolute(WaSecondary);

        Assert.Equal(1920 + 960, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(960, rect.W);
        Assert.Equal(1040, rect.H);
    }

    // ────────────────── Round-trip ──────────────────

    [Fact]
    public void RoundTrip_SameWorkArea_ReturnsOriginal()
    {
        int origX = 200, origY = 50, origW = 800, origH = 600;
        var norm = NormalizedRect.FromAbsolute(origX, origY, origW, origH, Wa1080);
        var restored = norm.ToAbsolute(Wa1080);

        Assert.Equal(origX, restored.X);
        Assert.Equal(origY, restored.Y);
        Assert.Equal(origW, restored.W);
        Assert.Equal(origH, restored.H);
    }

    [Fact]
    public void RoundTrip_1080p_To_4K_ScalesProportionally()
    {
        // Window at left-half on 1080p
        var norm = NormalizedRect.FromAbsolute(0, 0, 960, 1040, Wa1080);
        var restored = norm.ToAbsolute(Wa4K);

        // Should be left-half on 4K
        Assert.Equal(0, restored.X);
        Assert.Equal(0, restored.Y);
        Assert.Equal(1920, restored.W);  // 3840 * 0.5
        Assert.Equal(2120, restored.H);  // full height of 4K work area
    }

    [Fact]
    public void RoundTrip_4K_To_1080p_ScalesDown()
    {
        // Window using right 25% of 4K work area
        var norm = NormalizedRect.FromAbsolute(2880, 0, 960, 2120, Wa4K);
        var restored = norm.ToAbsolute(Wa1080);

        // xN = 2880/3840 = 0.75, wN = 960/3840 = 0.25
        Assert.Equal(0.75, norm.XN, 6);
        Assert.Equal(0.25, norm.WN, 6);
        Assert.Equal(1440, restored.X);  // 1920 * 0.75
        Assert.Equal(480, restored.W);   // 1920 * 0.25
    }

    // ────────────────── Drop-shadow / negative overflow ──────────────────
    // Windows 10/11 invisible borders cause xN < 0 and wN > 1

    [Fact]
    public void RoundTrip_NegativeOverflow_DropShadow_PreservedExactly()
    {
        // Simulates a window on a portrait secondary monitor at (-1080, 30, 1080, 1890)
        // with 7px drop-shadow overflow: x=-1087, w=1094
        var waPortrait = new WorkArea(-1080, 30, 1080, 1890);

        var norm = NormalizedRect.FromAbsolute(-1087, 30, 1094, 192, waPortrait);

        // xN should be negative, wN should exceed 1
        Assert.True(norm.XN < 0);
        Assert.True(norm.WN > 1);

        // Round-trip back to same work area should be pixel-perfect
        var restored = norm.ToAbsolute(waPortrait);
        Assert.Equal(-1087, restored.X);
        Assert.Equal(30, restored.Y);
        Assert.Equal(1094, restored.W);
        Assert.Equal(192, restored.H);
    }

    [Fact]
    public void RoundTrip_NegativeOverflow_AnotherWindow_PreservedExactly()
    {
        // Another window on the same portrait monitor: x=-1087, y=1119, w=1094, h=808
        var waPortrait = new WorkArea(-1080, 30, 1080, 1890);

        var norm = NormalizedRect.FromAbsolute(-1087, 1119, 1094, 808, waPortrait);
        var restored = norm.ToAbsolute(waPortrait);

        Assert.Equal(-1087, restored.X);
        Assert.Equal(1119, restored.Y);
        Assert.Equal(1094, restored.W);
        Assert.Equal(808, restored.H);
    }

    [Fact]
    public void ToAbsolute_UsesRounding_NotTruncation()
    {
        // xN * width = -6.999... should round to -7, not truncate to -6
        var wa = new WorkArea(-1080, 30, 1080, 1890);
        var norm = new NormalizedRect
        {
            XN = -7.0 / 1080,  // = -0.00648148...
            YN = 0,
            WN = 1094.0 / 1080, // = 1.01296296...
            HN = 192.0 / 1890   // = 0.10158730...
        };

        var restored = norm.ToAbsolute(wa);

        // With Math.Round these should be exact
        Assert.Equal(-1080 + (-7), restored.X);  // -1087
        Assert.Equal(1094, restored.W);
        Assert.Equal(192, restored.H);
    }
}
