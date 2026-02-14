using WindowController.Core.Models;

namespace WindowController.Core.Tests;

public class MonitorTransformDecisionTests
{
    private static Settings DefaultSettings => new()
    {
        AspectRatioWarnThreshold = 0.02,
        WarnOnResolutionMismatch = true,
        WarnOnMonitorMismatch = true,
        AllowCrossDesktopApply = true
    };

    private static MonitorInfo MakeSaved(int w = 1920, int h = 1080, string name = "\\\\.\\DISPLAY1", int index = 0)
        => new() { PixelWidth = w, PixelHeight = h, Name = name, Index = index };

    // ────────── Allow: exact same monitor + resolution ──────────

    [Fact]
    public void ExactMatch_SameResolution_Allow()
    {
        var saved = MakeSaved();
        var result = MonitorTransformDecision.Evaluate(saved, 1920, 1080, isExactMonitorMatch: true, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Allow, result.Level);
        Assert.Empty(result.Reasons);
    }

    // ────────── Deny: invalid target monitor ──────────

    [Fact]
    public void InvalidTarget_ZeroSize_Deny()
    {
        var saved = MakeSaved();
        var result = MonitorTransformDecision.Evaluate(saved, 0, 0, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Deny, result.Level);
        Assert.Single(result.Reasons);
    }

    [Fact]
    public void InvalidTarget_Negative_Deny()
    {
        var saved = MakeSaved();
        var result = MonitorTransformDecision.Evaluate(saved, -1, 1080, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Deny, result.Level);
    }

    // ────────── Warn: no saved monitor info ──────────

    [Fact]
    public void SavedNull_Warn()
    {
        var result = MonitorTransformDecision.Evaluate(null, 1920, 1080, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
        Assert.Single(result.Reasons);
        Assert.Contains("保存時モニタ情報なし", result.Reasons[0].Message);
    }

    [Fact]
    public void SavedNoPixels_Warn()
    {
        var saved = new MonitorInfo { Index = 0, Name = "DISPLAY1" };
        var result = MonitorTransformDecision.Evaluate(saved, 1920, 1080, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
    }

    [Fact]
    public void SavedNull_WarnDisabled_Allow()
    {
        var settings = DefaultSettings;
        settings.WarnOnMonitorMismatch = false;

        var result = MonitorTransformDecision.Evaluate(null, 1920, 1080, isExactMonitorMatch: false, settings);

        Assert.Equal(MonitorTransformLevel.Allow, result.Level);
    }

    // ────────── Warn: same ratio, different resolution (1080p → 4K) ──────────

    [Fact]
    public void SameRatio_DifferentResolution_Warn()
    {
        var saved = MakeSaved(1920, 1080);
        var result = MonitorTransformDecision.Evaluate(saved, 3840, 2160, isExactMonitorMatch: true, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
        Assert.Contains(result.Reasons, r => r.Message.Contains("解像度"));
    }

    [Fact]
    public void SameRatio_DifferentResolution_WarnDisabled_Allow()
    {
        var settings = DefaultSettings;
        settings.WarnOnResolutionMismatch = false;

        var saved = MakeSaved(1920, 1080);
        var result = MonitorTransformDecision.Evaluate(saved, 3840, 2160, isExactMonitorMatch: true, settings);

        Assert.Equal(MonitorTransformLevel.Allow, result.Level);
    }

    // ────────── Warn: different aspect ratio ──────────

    [Fact]
    public void DifferentAspectRatio_Warn()
    {
        // 16:9 saved → 21:9 target
        var saved = MakeSaved(1920, 1080);
        var result = MonitorTransformDecision.Evaluate(saved, 2560, 1080, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
        Assert.Contains(result.Reasons, r => r.Message.Contains("アスペクト比"));
    }

    [Fact]
    public void DifferentAspectRatio_WithinThreshold_NoAspectWarning()
    {
        // Tiny aspect ratio difference within 2% threshold
        var saved = MakeSaved(1920, 1080); // AR = 1.778
        // Target: 1921/1080 ≈ 1.779 difference < 0.02
        var result = MonitorTransformDecision.Evaluate(saved, 1921, 1080, isExactMonitorMatch: true, DefaultSettings);

        // Should warn about resolution mismatch but NOT aspect ratio
        Assert.DoesNotContain(result.Reasons, r => r.Message.Contains("アスペクト比"));
    }

    // ────────── Warn: monitor fallback ──────────

    [Fact]
    public void MonitorFallback_SameResolution_Warn()
    {
        var saved = MakeSaved(1920, 1080);
        var result = MonitorTransformDecision.Evaluate(saved, 1920, 1080, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
        Assert.Contains(result.Reasons, r => r.Message.Contains("別のモニタ"));
    }

    [Fact]
    public void MonitorFallback_WarnDisabled_Allow()
    {
        var settings = DefaultSettings;
        settings.WarnOnMonitorMismatch = false;

        var saved = MakeSaved(1920, 1080);
        var result = MonitorTransformDecision.Evaluate(saved, 1920, 1080, isExactMonitorMatch: false, settings);

        Assert.Equal(MonitorTransformLevel.Allow, result.Level);
    }

    // ────────── Multiple warnings combined ──────────

    [Fact]
    public void DifferentAspect_DifferentResolution_DifferentMonitor_MultipleWarnings()
    {
        var saved = MakeSaved(1920, 1080); // 16:9
        var result = MonitorTransformDecision.Evaluate(saved, 2560, 1440, isExactMonitorMatch: false, DefaultSettings);

        Assert.Equal(MonitorTransformLevel.Warn, result.Level);
        // Should have both resolution warning and monitor fallback warning
        Assert.Contains(result.Reasons, r => r.Message.Contains("解像度"));
        Assert.Contains(result.Reasons, r => r.Message.Contains("別のモニタ"));
    }

    // ────────── Conversion table from plan ──────────

    [Theory]
    [InlineData(1920, 1080, 1920, 1080, true, MonitorTransformLevel.Allow)]   // Same monitor, same resolution
    [InlineData(1920, 1080, 3840, 2160, true, MonitorTransformLevel.Warn)]    // Same monitor, 1080→4K
    [InlineData(1920, 1080, 2560, 1080, false, MonitorTransformLevel.Warn)]   // Different monitor, different AR
    [InlineData(1920, 1080, 1920, 1080, false, MonitorTransformLevel.Warn)]   // Different monitor, same res
    public void ConversionTable(int savedW, int savedH, int targetW, int targetH, bool exact, MonitorTransformLevel expected)
    {
        var saved = MakeSaved(savedW, savedH);
        var result = MonitorTransformDecision.Evaluate(saved, targetW, targetH, exact, DefaultSettings);

        Assert.Equal(expected, result.Level);
    }
}
