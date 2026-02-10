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
}

public static class MonitorHelper
{
    /// <summary>
    /// Get all monitors with their work areas.
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
                monitors.Add(new MonitorData
                {
                    Index = index,
                    DeviceName = mi.szDevice,
                    WorkArea = new WorkArea(
                        mi.rcWork.Left,
                        mi.rcWork.Top,
                        mi.rcWork.Right - mi.rcWork.Left,
                        mi.rcWork.Bottom - mi.rcWork.Top)
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
}
