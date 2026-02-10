using Serilog;
using WindowController.Core;
using WindowController.Core.Models;
using WindowController.Win32;

namespace WindowController.App;

/// <summary>
/// Manages sync (minimize/maximize/foreground propagation) across profile groups.
/// </summary>
public class SyncManager : IDisposable
{
    private readonly ProfileStore _store;
    private readonly WindowEnumerator _enumerator;
    private readonly WinEventHookManager _hookManager;
    private readonly ILogger _log;

    // profileName -> set of hwnds
    private Dictionary<string, HashSet<nint>> _syncGroups = new();
    private Dictionary<nint, int> _lastMinMaxByHwnd = new();
    private Dictionary<string, nint> _lastForegroundByProfile = new();
    private Dictionary<string, long> _lastForegroundTickByProfile = new();
    private bool _isPropagating;
    private long _lastRebuildTick;
    private bool _disposed;

    // Debounce rebuild: avoid multiple rapid rebuilds
    private CancellationTokenSource? _rebuildCts;
    private readonly object _rebuildLock = new();

    // WinEvent throttle: skip duplicate events within a short window
    private nint _lastEventHwnd;
    private uint _lastEventType;
    private long _lastEventTick;
    private const long EventThrottleMs = 30;

    public SyncManager(ProfileStore store, WindowEnumerator enumerator,
        WinEventHookManager hookManager, ILogger log)
    {
        _store = store;
        _enumerator = enumerator;
        _hookManager = hookManager;
        _log = log;
        _hookManager.EventReceived += OnWinEvent;
    }

    /// <summary>
    /// Rebuild sync groups using lightweight enumeration (no WMI/UIA).
    /// </summary>
    public void RebuildGroups()
    {
        var candidates = GetCandidatesLightweight();
        var newGroups = new Dictionary<string, HashSet<nint>>();

        foreach (var profile in _store.Data.Profiles)
        {
            if (profile.SyncMinMax == 0) continue;

            var group = new HashSet<nint>();
            foreach (var entry in profile.Windows)
            {
                var match = WindowMatcher.FindBest(entry, candidates, forSync: true);
                if (match != null && NativeMethods.IsWindow(match.Hwnd))
                    group.Add(match.Hwnd);
            }
            if (group.Count > 0)
                newGroups[profile.Name] = group;
        }

        _syncGroups = newGroups;
        _lastRebuildTick = Environment.TickCount64;
    }

    /// <summary>
    /// Schedule a debounced async rebuild (won't block caller).
    /// </summary>
    public void ScheduleRebuild(int delayMs = 100)
    {
        lock (_rebuildLock)
        {
            _rebuildCts?.Cancel();
            _rebuildCts = new CancellationTokenSource();
            var token = _rebuildCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token);
                    if (!token.IsCancellationRequested)
                        RebuildGroups();
                }
                catch (OperationCanceledException) { /* debounced away */ }
                catch (Exception ex)
                {
                    _log.Warning(ex, "ScheduleRebuild failed");
                }
            }, token);
        }
    }

    public void UpdateHooksIfNeeded(bool skipRebuild = false)
    {
        if (_store.Data.Settings.SyncMinMax != 0 && HasAnySyncProfile())
        {
            _hookManager.Install();
            if (!skipRebuild)
                ScheduleRebuild();
        }
        else
        {
            _hookManager.Uninstall();
            _syncGroups.Clear();
            _lastMinMaxByHwnd.Clear();
        }
    }

    private bool HasAnySyncProfile()
        => _store.Data.Profiles.Any(p => p.SyncMinMax != 0);

    private void OnWinEvent(uint eventType, nint hwnd)
    {
        try
        {
            if (_isPropagating) return;
            if (_store.Data.Settings.SyncMinMax == 0) return;

            // Throttle duplicate events for the same hwnd within a short window
            var now = Environment.TickCount64;
            if (hwnd == _lastEventHwnd && eventType == _lastEventType
                && now - _lastEventTick < EventThrottleMs)
                return;
            _lastEventHwnd = hwnd;
            _lastEventType = eventType;
            _lastEventTick = now;

            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                OnForegroundEvent(hwnd);
                return;
            }

            // Min/Max/Restore events
            if (!NativeMethods.IsWindow(hwnd)) return;
            var mm = WindowEnumerator.GetMinMax(hwnd);

            if (_lastMinMaxByHwnd.TryGetValue(hwnd, out var prev) && prev == mm)
                return;
            _lastMinMaxByHwnd[hwnd] = mm;

            var groups = GetGroupsContainingHwnd(hwnd);
            if (groups.Count == 0)
            {
                TryRebuild();
                groups = GetGroupsContainingHwnd(hwnd);
            }
            if (groups.Count == 0) return;

            _isPropagating = true;
            try
            {
                foreach (var (name, group) in groups)
                    PropagateMinMax(name, group, hwnd, mm);
            }
            finally
            {
                _isPropagating = false;
            }
        }
        catch (Exception ex)
        {
            try { _log.Warning(ex, "SyncManager.OnWinEvent error"); }
            catch { /* best effort */ }
        }
    }

    private void OnForegroundEvent(nint hwnd)
    {
        try
        {
            if (!NativeMethods.IsWindow(hwnd)) return;
            if (NativeMethods.IsIconic(hwnd)) return; // minimized, ignore

            var groups = GetGroupsContainingHwnd(hwnd);
            if (groups.Count == 0)
            {
                TryRebuild();
                groups = GetGroupsContainingHwnd(hwnd);
            }
            if (groups.Count == 0) return;

            _isPropagating = true;
            try
            {
                foreach (var (name, group) in groups)
                    PropagateForeground(name, group, hwnd);
            }
            finally
            {
                _isPropagating = false;
            }
        }
        catch (Exception ex)
        {
            try { _log.Warning(ex, "SyncManager.OnForegroundEvent error"); }
            catch { /* best effort */ }
        }
    }

    private void PropagateMinMax(string profileName, HashSet<nint> group, nint sourceHwnd, int mm)
    {
        int count = 0;
        foreach (var target in group)
        {
            if (target == sourceHwnd) continue;
            if (!NativeMethods.IsWindow(target)) continue;

            try
            {
                if (mm == -1)
                {
                    NativeMethods.ShowWindow(target, NativeMethods.SW_MINIMIZE);
                }
                else if (mm == 1)
                {
                    NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);
                    Thread.Sleep(20);
                    NativeMethods.ShowWindow(target, NativeMethods.SW_MAXIMIZE);
                }
                else
                {
                    NativeMethods.ShowWindow(target, NativeMethods.SW_RESTORE);
                }
                _lastMinMaxByHwnd[target] = mm;
                count++;
            }
            catch { /* skip */ }
        }
        if (count > 0)
            _log.Information("Sync propagated within profile '{Name}' to {Count} window(s)", profileName, count);
    }

    private void PropagateForeground(string profileName, HashSet<nint> group, nint sourceHwnd)
    {
        var now = Environment.TickCount64;
        if (_lastForegroundTickByProfile.TryGetValue(profileName, out var lastTick) &&
            now - lastTick < 250 &&
            _lastForegroundByProfile.TryGetValue(profileName, out var lastHwnd) &&
            lastHwnd == sourceHwnd)
            return;

        _lastForegroundTickByProfile[profileName] = now;
        _lastForegroundByProfile[profileName] = sourceHwnd;

        int count = 0;
        foreach (var target in group)
        {
            if (target == sourceHwnd) continue;
            if (!NativeMethods.IsWindow(target)) continue;
            if (NativeMethods.IsIconic(target)) continue;

            try
            {
                NativeMethods.SetWindowPos(target, sourceHwnd, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                count++;
            }
            catch { /* skip */ }
        }
        if (count > 0)
            _log.Information("Foreground sync within profile '{Name}' to {Count} window(s)", profileName, count);
    }

    private List<(string Name, HashSet<nint> Group)> GetGroupsContainingHwnd(nint hwnd)
    {
        var result = new List<(string, HashSet<nint>)>();
        foreach (var (name, group) in _syncGroups)
        {
            if (group.Contains(hwnd))
                result.Add((name, group));
        }
        return result;
    }

    private void TryRebuild()
    {
        if (Environment.TickCount64 - _lastRebuildTick > 2000)
            ScheduleRebuild(50);
    }

    /// <summary>
    /// Lightweight candidate list: no WMI, no UIA — just hwnd/exe/class/title/path.
    /// </summary>
    private List<WindowCandidate> GetCandidatesLightweight()
    {
        var wins = _enumerator.EnumerateWindows(lightweight: true);
        return wins.Select(w => new WindowCandidate
        {
            Hwnd = w.Hwnd,
            Exe = w.Exe,
            Class = w.Class,
            Title = w.Title,
            Path = w.Path,
            Url = "",
            CommandLine = ""
        }).ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hookManager.EventReceived -= OnWinEvent;
        _hookManager.Dispose();
    }
}
