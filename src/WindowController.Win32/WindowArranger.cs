using Serilog;
using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.Win32;

/// <summary>
/// Restore window position/size/state.
/// </summary>
public class WindowArranger
{
    private readonly ILogger _log;

    public WindowArranger(ILogger logger)
    {
        _log = logger;
    }

    /// <summary>
    /// Arrange a window according to the saved entry.
    /// </summary>
    public void Arrange(nint hwnd, WindowEntry entry)
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        int x, y, w, h;

        // If snap info is available, recalculate from current monitor's work area
        if (entry.Snap is { Type: var snapType } && !string.IsNullOrEmpty(snapType))
        {
            var wa = GetWorkAreaForEntry(entry);
            if (wa != null)
            {
                var snapRect = SnapCalculator.RectFromSnap(wa, snapType);
                if (snapRect != null)
                {
                    x = snapRect.X;
                    y = snapRect.Y;
                    w = snapRect.W;
                    h = snapRect.H;
                    ApplyRect(hwnd, x, y, w, h, entry.MinMax);
                    return;
                }
            }
        }

        // Fall back to saved rect
        x = entry.Rect.X;
        y = entry.Rect.Y;
        w = entry.Rect.W;
        h = entry.Rect.H;
        ApplyRect(hwnd, x, y, w, h, entry.MinMax);
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
    /// Get work area for the monitor specified in the entry.
    /// </summary>
    private WorkArea? GetWorkAreaForEntry(WindowEntry entry)
    {
        var monitors = MonitorHelper.GetMonitors();
        if (monitors.Count == 0)
            return null;

        // Try by name first
        if (entry.Monitor is { Name: var name } && !string.IsNullOrEmpty(name))
        {
            var byName = monitors.FirstOrDefault(m => m.DeviceName == name);
            if (byName != null)
                return byName.WorkArea;
        }

        // Try by index
        if (entry.Monitor is { Index: var idx } && idx >= 1 && idx <= monitors.Count)
            return monitors[idx - 1].WorkArea;

        // Fallback: primary
        return monitors[0].WorkArea;
    }
}
