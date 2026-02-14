using WindowController.Core.Models;

namespace WindowController.Core;

/// <summary>
/// Decision result for monitor transform checks.
/// </summary>
public enum MonitorTransformLevel
{
    /// <summary>No issues — apply normally.</summary>
    Allow,
    /// <summary>Differences detected — apply but warn the user.</summary>
    Warn,
    /// <summary>Restore is not possible.</summary>
    Deny
}

/// <summary>
/// Detail of a single monitor transform warning/deny reason.
/// </summary>
public record MonitorTransformReason(MonitorTransformLevel Level, string Message);

/// <summary>
/// Aggregate result of monitor transform evaluation.
/// </summary>
public class MonitorTransformResult
{
    public MonitorTransformLevel Level { get; init; } = MonitorTransformLevel.Allow;
    public List<MonitorTransformReason> Reasons { get; init; } = new();

    public static MonitorTransformResult Ok() => new();
}

/// <summary>
/// Pure-function evaluator that compares a saved MonitorInfo against a target monitor
/// and produces Allow / Warn / Deny with reasons.
/// </summary>
public static class MonitorTransformDecision
{
    /// <summary>
    /// Evaluate whether restoring onto <paramref name="target"/> from <paramref name="saved"/> is safe.
    /// </summary>
    public static MonitorTransformResult Evaluate(
        MonitorInfo? saved,
        int targetPixelWidth,
        int targetPixelHeight,
        bool isExactMonitorMatch,
        Settings settings)
    {
        // If the target monitor has no valid size, deny.
        if (targetPixelWidth <= 0 || targetPixelHeight <= 0)
        {
            return new MonitorTransformResult
            {
                Level = MonitorTransformLevel.Deny,
                Reasons = { new(MonitorTransformLevel.Deny, "ターゲットモニタの情報を取得できません") }
            };
        }

        // If saved monitor info is missing or invalid, we only know the absolute rect — warn.
        if (saved == null || saved.PixelWidth <= 0 || saved.PixelHeight <= 0)
        {
            if (!settings.WarnOnMonitorMismatch)
                return MonitorTransformResult.Ok();

            return new MonitorTransformResult
            {
                Level = MonitorTransformLevel.Warn,
                Reasons = { new(MonitorTransformLevel.Warn, "保存時モニタ情報なし — 絶対座標で復元します") }
            };
        }

        // Same physical monitor — no warnings needed.
        if (isExactMonitorMatch
            && saved.PixelWidth == targetPixelWidth
            && saved.PixelHeight == targetPixelHeight)
        {
            return MonitorTransformResult.Ok();
        }

        var reasons = new List<MonitorTransformReason>();

        // Aspect ratio check
        double savedAR = (double)saved.PixelWidth / saved.PixelHeight;
        double targetAR = (double)targetPixelWidth / targetPixelHeight;
        double arDiff = Math.Abs(savedAR - targetAR);

        if (arDiff > settings.AspectRatioWarnThreshold)
        {
            reasons.Add(new(MonitorTransformLevel.Warn,
                $"アスペクト比が異なります: 保存={savedAR:F3}, 適用先={targetAR:F3} (差={arDiff:F3})"));
        }

        // Resolution check (same aspect but different pixel count → default warn)
        if (settings.WarnOnResolutionMismatch
            && (saved.PixelWidth != targetPixelWidth || saved.PixelHeight != targetPixelHeight))
        {
            // "Same model multiple monitors" case: exact match means no warning.
            // That is already handled by the isExactMonitorMatch + resolution equality check above.
            // If we get here, resolution differs.
            reasons.Add(new(MonitorTransformLevel.Warn,
                $"解像度が異なります: 保存={saved.PixelWidth}x{saved.PixelHeight}, 適用先={targetPixelWidth}x{targetPixelHeight}"));
        }

        // Monitor fallback (name/index mismatch)
        if (!isExactMonitorMatch && settings.WarnOnMonitorMismatch)
        {
            reasons.Add(new(MonitorTransformLevel.Warn,
                $"別のモニタに配置します (保存={saved.Name} #{saved.Index})"));
        }

        if (reasons.Count == 0)
            return MonitorTransformResult.Ok();

        var maxLevel = reasons.Max(r => r.Level);
        return new MonitorTransformResult
        {
            Level = maxLevel,
            Reasons = reasons
        };
    }
}
