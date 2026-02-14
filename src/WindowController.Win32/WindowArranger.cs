using Serilog;
using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.Win32;

/// <summary>
/// Result of a single window arrange operation (for warning/reporting).
/// </summary>
public class ArrangeResult
{
    public bool Applied { get; init; }
    public MonitorTransformResult? MonitorTransform { get; init; }
}

/// <summary>
/// Restore window position/size/state.
/// Priority: snap → rectNormalized → absolute rect.
/// All paths finish with a work-area clamp to prevent off-screen placement.
/// </summary>
public class WindowArranger
{
    private readonly ILogger _log;
    private readonly Settings _settings;

    public WindowArranger(ILogger logger, Settings settings)
    {
        _log = logger;
        _settings = settings;
    }

    /// <summary>
    /// Arrange a window according to the saved entry.
    /// Returns an ArrangeResult with monitor-transform warnings (if any).
    /// </summary>
    public ArrangeResult Arrange(nint hwnd, WindowEntry entry, MonitorData? forceMonitor = null)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return new ArrangeResult { Applied = false };

        var monitors = MonitorHelper.GetMonitors();

        // --- Resolve target monitor ---
        MonitorData targetMon;
        MonitorTransformResult? transform = null;

        if (forceMonitor != null)
        {
            targetMon = forceMonitor;

            // User explicitly chose this monitor — still evaluate for warnings
            // but downgrade Deny → Warn so the choice is always honoured.
            bool isExactForce = entry.Monitor != null
                && !string.IsNullOrEmpty(entry.Monitor.Name)
                && entry.Monitor.Name == forceMonitor.DeviceName;

            transform = MonitorTransformDecision.Evaluate(
                entry.Monitor,
                forceMonitor.PixelWidth,
                forceMonitor.PixelHeight,
                isExactForce,
                _settings);

            // Downgrade Deny to Warn — user explicitly picked this monitor
            if (transform.Level == MonitorTransformLevel.Deny)
            {
                transform = new MonitorTransformResult
                {
                    Level = MonitorTransformLevel.Warn,
                    Reasons = transform.Reasons
                        .Select(r => new MonitorTransformReason(MonitorTransformLevel.Warn, r.Message))
                        .ToList()
                };
            }
        }
        else
        {
            bool isLegacyProfile = entry.Monitor == null
                                && entry.RectNormalized == null;
            bool isExact;

            if (isLegacyProfile)
            {
                var fromRect = MonitorHelper.GetMonitorForRect(
                    entry.Rect.X, entry.Rect.Y, entry.Rect.W, entry.Rect.H);
                targetMon = fromRect ?? monitors.FirstOrDefault() ?? new MonitorData();
                isExact = fromRect != null;
            }
            else
            {
                (targetMon, isExact) = MonitorHelper.ResolveMonitor(entry.Monitor, monitors);
            }

            transform = MonitorTransformDecision.Evaluate(
                entry.Monitor,
                targetMon.PixelWidth,
                targetMon.PixelHeight,
                isExact,
                _settings);

            if (transform.Level == MonitorTransformLevel.Deny)
            {
                _log.Warning("Arrange denied for {Exe}: {Reasons}",
                    entry.Match.Exe, string.Join("; ", transform.Reasons.Select(r => r.Message)));
                return new ArrangeResult { Applied = false, MonitorTransform = transform };
            }
        }

        int x, y, w, h;
        var wa = targetMon.WorkArea;

        // Priority 1: Snap
        if (entry.Snap is { Type: var snapType } && !string.IsNullOrEmpty(snapType) && wa.Width > 0 && wa.Height > 0)
        {
            var snapRect = SnapCalculator.RectFromSnap(wa, snapType);
            if (snapRect != null)
            {
                x = snapRect.X; y = snapRect.Y; w = snapRect.W; h = snapRect.H;
                ApplyRect(hwnd, x, y, w, h, entry.MinMax);
                return new ArrangeResult { Applied = true, MonitorTransform = transform };
            }
        }

        // Priority 2: Normalized rect
        // Use when: (a) forcing to a different monitor, or (b) resolution differs
        bool useNormalized;
        if (forceMonitor != null)
        {
            useNormalized = true;
        }
        else
        {
            useNormalized = entry.Monitor != null
                && (entry.Monitor.PixelWidth != targetMon.PixelWidth
                 || entry.Monitor.PixelHeight != targetMon.PixelHeight);
        }

        if (useNormalized && wa.Width > 0 && wa.Height > 0)
        {
            NormalizedRect? norm = entry.RectNormalized;

            // If no stored normalized rect, compute on-the-fly from absolute rect
            if (norm == null)
            {
                var origWa = ResolveOriginalWorkArea(entry, monitors);
                if (origWa is { Width: > 0, Height: > 0 })
                {
                    norm = NormalizedRect.FromAbsolute(
                        entry.Rect.X, entry.Rect.Y, entry.Rect.W, entry.Rect.H, origWa);
                }
            }

            if (norm != null)
            {
                var absRect = norm.ToAbsolute(wa);
                x = absRect.X; y = absRect.Y; w = absRect.W; h = absRect.H;
                Clamp(ref x, ref y, ref w, ref h, wa);
                ApplyRect(hwnd, x, y, w, h, entry.MinMax);
                return new ArrangeResult { Applied = true, MonitorTransform = transform };
            }
        }

        // Priority 3: Absolute rect (same-resolution or legacy)
        x = entry.Rect.X; y = entry.Rect.Y; w = entry.Rect.W; h = entry.Rect.H;
        if (wa.Width > 0 && wa.Height > 0)
            Clamp(ref x, ref y, ref w, ref h, wa);
        ApplyRect(hwnd, x, y, w, h, entry.MinMax);
        return new ArrangeResult { Applied = true, MonitorTransform = transform };
    }

    /// <summary>
    /// Resolve the original monitor's work area from the entry.
    /// Used to compute NormalizedRect on-the-fly when forcing a different monitor.
    /// </summary>
    private static WorkArea? ResolveOriginalWorkArea(WindowEntry entry, List<MonitorData> monitors)
    {
        if (entry.Monitor != null)
        {
            var (mon, _) = MonitorHelper.ResolveMonitor(entry.Monitor, monitors);
            if (mon.WorkArea.Width > 0) return mon.WorkArea;
        }
        var fromRect = MonitorHelper.GetMonitorForRect(
            entry.Rect.X, entry.Rect.Y, entry.Rect.W, entry.Rect.H);
        return fromRect?.WorkArea;
    }

    private void ApplyRect(nint hwnd, int x, int y, int w, int h, int targetState)
    {
        try
        {
            // Restore first to allow positioning
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            Thread.Sleep(30);

            // Set position and size
            NativeMethods.SetWindowPos(hwnd, 0, x, y, w, h, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            // Apply final state
            if (targetState == -1)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
            else if (targetState == 1)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "ArrangeWindow failed for hwnd {Hwnd}", hwnd);
        }
    }

    /// <summary>
    /// Clamp rect so that it stays (mostly) within the work area.
    /// Allows a small overflow margin to accommodate DWM invisible borders
    /// (drop shadows) which extend ~7 px beyond the visible window edge
    /// on Windows 10/11.
    /// </summary>
    internal const int DwmFrameMargin = 10;

    private static void Clamp(ref int x, ref int y, ref int w, ref int h, WorkArea wa)
    {
        const int MinVisible = 100;

        // Ensure minimum size
        if (w < MinVisible) w = MinVisible;
        if (h < MinVisible) h = MinVisible;

        // Clamp size to work area + margin (DWM border can exceed work area)
        int maxW = wa.Width + 2 * DwmFrameMargin;
        int maxH = wa.Height + 2 * DwmFrameMargin;
        if (w > maxW) w = maxW;
        if (h > maxH) h = maxH;

        // Clamp position (allow DWM border overflow)
        if (x < wa.Left - DwmFrameMargin) x = wa.Left - DwmFrameMargin;
        if (y < wa.Top - DwmFrameMargin) y = wa.Top - DwmFrameMargin;
        if (x + w > wa.Right + DwmFrameMargin) x = wa.Right + DwmFrameMargin - w;
        if (y + h > wa.Bottom + DwmFrameMargin) y = wa.Bottom + DwmFrameMargin - h;
    }
}
