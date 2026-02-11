using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Text;
using Serilog;
using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.Win32;

/// <summary>
/// Information about a running top-level window.
/// </summary>
public class WindowInfo
{
    public nint Hwnd { get; init; }
    public string Title { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Class { get; init; } = "";
    public string Path { get; init; } = "";
    public string Url { get; init; } = "";
    public string BrowserProfile { get; init; } = "";
    public string CommandLine { get; init; } = "";
    public int MinMax { get; init; }
    public Core.Models.Rect Rect { get; init; } = new();
}

/// <summary>
/// Enumerate and manipulate windows using Win32 APIs.
/// </summary>
public class WindowEnumerator
{
    private readonly ILogger _log;
    private readonly Func<nint, string, string>? _urlGetter;

    // WMI command-line cache: PID → (commandLine, tickWhenCached)
    private readonly ConcurrentDictionary<uint, (string CmdLine, long Tick)> _cmdLineCache = new();
    private const long CmdLineCacheTtlMs = 60_000; // 1 minute

    public WindowEnumerator(ILogger logger, Func<nint, string, string>? urlGetter = null)
    {
        _log = logger;
        _urlGetter = urlGetter;
    }

    /// <summary>
    /// Enumerate all visible top-level windows.
    /// <param name="lightweight">If true, skip expensive WMI command-line and UIA URL retrieval.
    /// Use for sync matching where only exe/class/title/path are needed.</param>
    /// </summary>
    public List<WindowInfo> EnumerateWindows(bool lightweight = false)
    {
        var results = new List<WindowInfo>();
        var hwnds = new List<nint>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            hwnds.Add(hwnd);
            return true;
        }, 0);

        foreach (var hwnd in hwnds)
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hwnd))
                    continue;

                var title = GetWindowTitle(hwnd);
                if (string.IsNullOrEmpty(title))
                    continue;

                var style = (uint)NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_STYLE);
                var exStyle = (uint)NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);

                if ((style & NativeMethods.WS_VISIBLE) == 0)
                    continue;
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                    continue;

                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                var exe = GetProcessName(pid);
                var cls = GetClassName(hwnd);
                var path = GetProcessPath(pid);

                var cmdLine = "";
                if (!lightweight)
                {
                    cmdLine = GetCommandLineCached(pid);
                }

                var url = "";
                if (!lightweight)
                {
                    try
                    {
                        if (_urlGetter != null)
                            url = _urlGetter(hwnd, exe);
                    }
                    catch (Exception ex) { _log.Debug(ex, "URL retrieval failed for hwnd {Hwnd}", hwnd); }
                }

                var browserProfile = "";
                var exeLower = exe.ToLowerInvariant();
                if (BrowserIdentifier.IsBrowser(exeLower))
                {
                    var ident = BrowserIdentifier.ExtractIdentity(exeLower, cmdLine);
                    if (ident != null)
                    {
                        browserProfile = ident.ProfileDirectory
                            ?? ident.ProfileName
                            ?? ident.ProfileDir
                            ?? "";
                    }
                }

                var minMax = GetMinMax(hwnd);
                var rect = GetWindowRect(hwnd);

                results.Add(new WindowInfo
                {
                    Hwnd = hwnd,
                    Title = title,
                    Exe = exe,
                    Class = cls,
                    Path = path,
                    Url = url,
                    BrowserProfile = browserProfile,
                    CommandLine = cmdLine,
                    MinMax = minMax,
                    Rect = rect
                });
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "EnumerateWindows item failed for hwnd {Hwnd}", hwnd);
            }
        }

        return results;
    }

    /// <summary>
    /// Get the min/max state: -1=minimized, 0=normal, 1=maximized.
    /// </summary>
    public static int GetMinMax(nint hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) return -1;
        if (NativeMethods.IsZoomed(hwnd)) return 1;
        return 0;
    }

    public static string GetWindowTitle(nint hwnd)
    {
        var len = NativeMethods.GetWindowTextLengthW(hwnd);

        if (len > 0)
        {
            var sb = new StringBuilder(len + 1);
            var copied = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
            if (copied <= 0) return "";
            return sb.ToString(0, copied);
        }

        // Some windows return 0 length from GetWindowTextLengthW but still have text.
        // Best-effort fallback with a reasonable cap.
        {
            const int fallbackCap = 2048;
            var sb = new StringBuilder(fallbackCap);
            var copied = NativeMethods.GetWindowTextW(hwnd, sb, sb.Capacity);
            if (copied <= 0) return "";
            return sb.ToString(0, copied);
        }
    }

    public static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        var len = NativeMethods.GetClassNameW(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : "";
    }

    public static Core.Models.Rect GetWindowRect(nint hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var r);
        return new Core.Models.Rect
        {
            X = r.Left,
            Y = r.Top,
            W = r.Right - r.Left,
            H = r.Bottom - r.Top
        };
    }

    public static string GetProcessName(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName + ".exe";
        }
        catch { return ""; } // Expected for protected/exited processes
    }

    public static string GetProcessPath(uint pid)
    {
        var hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0) return "";
        try
        {
            var buf = new char[1024];
            uint size = (uint)buf.Length;
            if (NativeMethods.QueryFullProcessImageNameW(hProc, 0, buf, ref size))
                return new string(buf, 0, (int)size);
            return "";
        }
        finally
        {
            NativeMethods.CloseHandle(hProc);
        }
    }

    public static string GetCommandLine(uint pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"Select CommandLine from Win32_Process where ProcessId={pid}");
            foreach (var obj in searcher.Get())
            {
                var cl = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cl))
                    return cl;
            }
        }
        catch { /* WMI may fail for protected/system processes — expected */ }
        return "";
    }

    /// <summary>
    /// Get command line with caching to avoid repeated expensive WMI calls.
    /// </summary>
    public string GetCommandLineCached(uint pid)
    {
        var now = Environment.TickCount64;

        if (_cmdLineCache.TryGetValue(pid, out var cached) && now - cached.Tick < CmdLineCacheTtlMs)
            return cached.CmdLine;

        // Only query WMI for browser processes to avoid unnecessary overhead
        var exeName = GetProcessName(pid).ToLowerInvariant();
        if (!BrowserIdentifier.IsBrowser(exeName))
        {
            _cmdLineCache[pid] = ("", now);
            return "";
        }

        var cmdLine = GetCommandLine(pid);
        _cmdLineCache[pid] = (cmdLine, now);

        // Prune stale entries periodically
        if (_cmdLineCache.Count > 200)
        {
            var staleKeys = _cmdLineCache
                .Where(kv => now - kv.Value.Tick > CmdLineCacheTtlMs * 2)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in staleKeys)
                _cmdLineCache.TryRemove(key, out _);
        }

        return cmdLine;
    }
}
