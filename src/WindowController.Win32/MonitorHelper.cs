using WindowController.Core;

namespace WindowController.Win32;

/// <summary>
/// Monitor information helper.
/// </summary>
public class MonitorData
{
    public int Index { get; init; }
    public string DeviceName { get; init; } = "";
    public WorkArea WorkArea { get; init; } = new(0, 0, 0, 0);

    /// <summary>Full monitor pixel width (rcMonitor).</summary>
    public int PixelWidth { get; init; }

    /// <summary>Full monitor pixel height (rcMonitor).</summary>
    public int PixelHeight { get; init; }

    /// <summary>Full monitor bounds (rcMonitor) as a WorkArea for convenience.</summary>
    public WorkArea MonitorRect { get; init; } = new(0, 0, 0, 0);

    /// <summary>Aspect ratio (width / height). 0 if height is 0.</summary>
    public double AspectRatio => PixelHeight > 0 ? (double)PixelWidth / PixelHeight : 0;
}

public static class MonitorHelper
{
    /// <summary>
    /// Get all monitors with their work areas and pixel dimensions.
    /// </summary>
    public static List<MonitorData> GetMonitors()
    {
        var monitors = new List<MonitorData>();
        int index = 0;

        NativeMethods.EnumDisplayMonitors(0, 0, (nint hMonitor, nint hdcMonitor, ref NativeMethods.RECT lprcMonitor, nint dwData) =>
        {
            index++;
            var mi = new NativeMethods.MONITORINFOEX();
            mi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

            if (NativeMethods.GetMonitorInfoW(hMonitor, ref mi))
            {
                var monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
                var monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                monitors.Add(new MonitorData
                {
                    Index = index,
                    DeviceName = mi.szDevice,
                    WorkArea = new WorkArea(
                        mi.rcWork.Left,
                        mi.rcWork.Top,
                        mi.rcWork.Right - mi.rcWork.Left,
                        mi.rcWork.Bottom - mi.rcWork.Top),
                    PixelWidth = monW,
                    PixelHeight = monH,
                    MonitorRect = new WorkArea(
                        mi.rcMonitor.Left,
                        mi.rcMonitor.Top,
                        monW, monH)
                });
            }
            return true;
        }, 0);

        return monitors;
    }

    /// <summary>
    /// Find the monitor that contains the center of the given rect.
    /// </summary>
    public static MonitorData? GetMonitorForRect(int x, int y, int w, int h)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0) return null;

        int cx = x + w / 2;
        int cy = y + h / 2;

        foreach (var m in monitors)
        {
            var wa = m.WorkArea;
            if (cx >= wa.Left && cx < wa.Right && cy >= wa.Top && cy < wa.Bottom)
                return m;
        }

        // Fallback to first
        return monitors[0];
    }

    /// <summary>
    /// Resolve a saved MonitorInfo to a current MonitorData using best-match priority:
    /// devicePath → name → index → primary.
    /// </summary>
    public static (MonitorData Monitor, bool IsExactMatch) ResolveMonitor(
        Core.Models.MonitorInfo? saved, List<MonitorData>? monitors = null)
    {
        monitors ??= GetMonitors();
        if (monitors.Count == 0)
            return (new MonitorData(), false);

        if (saved == null)
            return (monitors[0], false);

        // Priority 1: devicePath (not yet populated — future-proofing)
        // Priority 2: name
        if (!string.IsNullOrEmpty(saved.Name))
        {
            var byName = monitors.FirstOrDefault(m => m.DeviceName == saved.Name);
            if (byName != null)
                return (byName, true);
        }

        // Priority 3: index
        if (saved.Index >= 1 && saved.Index <= monitors.Count)
            return (monitors[saved.Index - 1], false);

        // Fallback: primary
        return (monitors[0], false);
    }
}
