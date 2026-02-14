using System.Diagnostics;
using System.IO;
using Serilog;
using WindowController.Browser;
using WindowController.Core;
using WindowController.Core.Models;
using WindowController.Win32;

namespace WindowController.App;

/// <summary>
/// Result of a profile apply operation.
/// </summary>
public record ApplyResult(int Applied, int Total, List<string> Failures, List<string> Warnings)
{
    public bool Success => Failures.Count == 0;

    public string ToStatusMessage(string profileName)
    {
        var msg = $"{profileName} を適用: {Applied}/{Total}";
        if (Failures.Count > 0)
            msg += $"（失敗 {Failures.Count}件: {string.Join(", ", Failures.Take(3))}）";
        if (Warnings.Count > 0)
            msg += $"（警告 {Warnings.Count}件）";
        return msg;
    }
}

/// <summary>
/// Encapsulates profile application logic so that both UI and hotkeys can invoke it.
/// </summary>
public class ProfileApplier
{
    private readonly ProfileStore _store;
    private readonly WindowEnumerator _enumerator;
    private readonly WindowArranger _arranger;
    private readonly SyncManager _syncManager;
    private readonly VirtualDesktopService _vdService;
    private readonly ILogger _log;

    public ProfileApplier(ProfileStore store, WindowEnumerator enumerator,
        WindowArranger arranger, SyncManager syncManager,
        VirtualDesktopService vdService, ILogger log)
    {
        _store = store;
        _enumerator = enumerator;
        _arranger = arranger;
        _syncManager = syncManager;
        _vdService = vdService;
        _log = log;
    }

    /// <summary>
    /// Apply a profile by Id.
    /// </summary>
    public async Task<ApplyResult> ApplyByIdAsync(string profileId, bool launchMissing,
        nint appHwnd = 0, MonitorData? targetMonitor = null, Guid? targetDesktopId = null)
    {
        var profile = _store.FindById(profileId);
        if (profile == null)
            return new ApplyResult(0, 0, new List<string> { "プロファイルが見つかりません" }, new());

        return await ApplyProfileAsync(profile, launchMissing, appHwnd, targetMonitor, targetDesktopId);
    }

    /// <summary>
    /// Apply a profile by name.
    /// </summary>
    public async Task<ApplyResult> ApplyByNameAsync(string profileName, bool launchMissing,
        nint appHwnd = 0, MonitorData? targetMonitor = null, Guid? targetDesktopId = null)
    {
        var profile = _store.FindByName(profileName);
        if (profile == null)
            return new ApplyResult(0, 0, new List<string> { "プロファイルが見つかりません" }, new());

        return await ApplyProfileAsync(profile, launchMissing, appHwnd, targetMonitor, targetDesktopId);
    }

    private async Task<ApplyResult> ApplyProfileAsync(Profile profile, bool launchMissing,
        nint appHwnd, MonitorData? targetMonitor = null, Guid? targetDesktopId = null)
    {
        var settings = _store.Data.Settings;
        var candidates = GetCandidates();
        int applied = 0;
        var failures = new List<string>();
        var warnings = new List<string>();

        foreach (var entry in profile.Windows)
        {
            try
            {
                var match = WindowMatcher.FindBest(entry, candidates);
                nint hwnd = match?.Hwnd ?? 0;

                if ((hwnd == 0 || !NativeMethods.IsWindow(hwnd)) && launchMissing)
                {
                    hwnd = await LaunchAndWaitAsync(entry, candidates);
                }

                if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
                {
                    failures.Add($"{entry.Match.Exe} | {entry.Match.Title} : 見つかりません");
                    continue;
                }

                // --- Virtual Desktop handling (整備中のため無効化) ---
                // クロスデスクトップ適用を許可しない場合は、別デスクトップ上のウィンドウをスキップする。
                // ※デスクトップ移動/切り替えは実行しない。
                var onCurrent = _vdService.IsWindowOnCurrentDesktop(hwnd);
                if (onCurrent == false && !settings.AllowCrossDesktopApply)
                {
                    warnings.Add($"{entry.Match.Exe} | {entry.Match.Title} : 別デスクトップ — スキップ");
                    continue;
                }

                // --- Arrange (with monitor transform warnings) ---
                var result = _arranger.Arrange(hwnd, entry, targetMonitor);
                if (!result.Applied)
                {
                    var reason = result.MonitorTransform?.Reasons.FirstOrDefault()?.Message ?? "配置失敗";
                    failures.Add($"{entry.Match.Exe} | {entry.Match.Title} : {reason}");
                    continue;
                }

                // Collect monitor warnings
                if (result.MonitorTransform is { Level: MonitorTransformLevel.Warn } mt)
                {
                    foreach (var r in mt.Reasons)
                        warnings.Add($"{entry.Match.Exe} : {r.Message}");
                }

                applied++;
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Match.Exe} | {entry.Match.Title} : {ex.Message}");
                _log.Warning(ex, "ApplyProfile item failed");
            }
        }

        _syncManager.ScheduleRebuild();
        return new ApplyResult(applied, profile.Windows.Count, failures, warnings);
    }

    private async Task<nint> LaunchAndWaitAsync(WindowEntry entry, List<WindowCandidate> existingCandidates)
    {
        var exe = entry.Match.Exe;
        var url = entry.Match.Url;
        var path = entry.Path;

        // Collect existing hwnds for this exe
        var beforeHwnds = new HashSet<nint>(
            existingCandidates.Where(c => c.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)).Select(c => c.Hwnd));

        try
        {
            var startPath = !string.IsNullOrEmpty(path) && File.Exists(path) ? path : exe;

            var psi = new ProcessStartInfo(startPath);
            if (!string.IsNullOrEmpty(url))
            {
                // Only allow http/https/file URLs as arguments
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    psi.Arguments = $"\"{url}\"";
                }
                else
                {
                    _log.Warning("Launch skipped URL argument with unsupported scheme: {Url}", url);
                }
            }
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Launch failed for {Exe}", exe);
            return 0;
        }

        // Wait for new window
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 12000)
        {
            await Task.Delay(300);
            var wins = _enumerator.EnumerateWindows();
            foreach (var w in wins)
            {
                if (w.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase) && !beforeHwnds.Contains(w.Hwnd))
                    return w.Hwnd;
            }
        }

        // Last resort: try matching again
        var newCandidates = GetCandidates();
        var match = WindowMatcher.FindBest(entry, newCandidates);
        return match?.Hwnd ?? 0;
    }

    private List<WindowCandidate> GetCandidates()
    {
        var wins = _enumerator.EnumerateWindows();
        return wins.Select(w => new WindowCandidate
        {
            Hwnd = w.Hwnd,
            Exe = w.Exe,
            Class = w.Class,
            Title = w.Title,
            Path = w.Path,
            Url = w.Url,
            CommandLine = w.CommandLine
        }).ToList();
    }
}
