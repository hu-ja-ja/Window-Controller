using Serilog;

namespace WindowController.Win32;

/// <summary>
/// WinEvent hook for monitoring minimize/maximize/foreground events.
/// </summary>
public class WinEventHookManager : IDisposable
{
    private readonly ILogger _log;
    private readonly List<nint> _hookHandles = new();
    private NativeMethods.WinEventProc? _callback; // prevent GC collection
    private bool _disposed;

    public event Action<uint, nint>? EventReceived;

    public WinEventHookManager(ILogger logger)
    {
        _log = logger;
    }

    public void Install()
    {
        if (_hookHandles.Count > 0) return;

        _callback = OnWinEvent;
        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;

        // Minimize start/end
        var h1 = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            0, _callback, 0, 0, flags);
        if (h1 != 0) _hookHandles.Add(h1);
        else _log.Warning("SetWinEventHook(minimize) failed");

        // State change
        var h2 = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_STATECHANGE,
            NativeMethods.EVENT_OBJECT_STATECHANGE,
            0, _callback, 0, 0, flags);
        if (h2 != 0) _hookHandles.Add(h2);
        else _log.Warning("SetWinEventHook(statechange) failed");

        // Foreground
        var h3 = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            0, _callback, 0, 0, flags);
        if (h3 != 0) _hookHandles.Add(h3);
        else _log.Warning("SetWinEventHook(foreground) failed");

        _log.Information("WinEvent hooks installed ({Count} hooks)", _hookHandles.Count);
    }

    public void Uninstall()
    {
        foreach (var h in _hookHandles)
        {
            try { NativeMethods.UnhookWinEvent(h); }
            catch { /* best effort */ }
        }
        _hookHandles.Clear();
        _callback = null;
        _log.Information("WinEvent hooks removed");
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0 || hwnd == 0) return;
        EventReceived?.Invoke(eventType, hwnd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Uninstall();
    }
}
