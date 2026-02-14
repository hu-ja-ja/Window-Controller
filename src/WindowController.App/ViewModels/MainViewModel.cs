using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;
using WindowController.Browser;
using WindowController.Core;
using WindowController.Core.Models;
using WindowController.Win32;

namespace WindowController.App.ViewModels;

public partial class WindowItem : ObservableObject
{
    [ObservableProperty] private bool _isChecked;
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

public partial class ProfileItem : ObservableObject
{
    [ObservableProperty] private bool _syncMinMax;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _targetDesktopLabel = "";
    public string Id { get; init; } = "";
    public int WindowCount { get; init; }
}

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly WindowEnumerator _enumerator;
    private readonly WindowArranger _arranger;
    private readonly BrowserUrlRetriever _urlRetriever;
    private readonly SyncManager _syncManager;
    private readonly VirtualDesktopService _vdService;
    private readonly ProfileApplier _profileApplier;
    private readonly ILogger _log;
    private bool _isUpdatingProfileName;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private ProfileItem? _selectedProfile;

    public ObservableCollection<WindowItem> Windows { get; } = new();
    public ObservableCollection<ProfileItem> Profiles { get; } = new();

    public MainViewModel(ProfileStore store, WindowEnumerator enumerator,
        WindowArranger arranger, BrowserUrlRetriever urlRetriever,
        SyncManager syncManager, VirtualDesktopService vdService,
        ProfileApplier profileApplier,
        AppSettingsStore appSettings, ILogger log)
    {
        _store = store;
        _enumerator = enumerator;
        _arranger = arranger;
        _urlRetriever = urlRetriever;
        _syncManager = syncManager;
        _vdService = vdService;
        _profileApplier = profileApplier;
        _log = log;
    }

    [RelayCommand]
    private async Task RefreshWindowsAsync()
    {
        try
        {
            StatusText = "ウィンドウ一覧を取得中…";
            var wins = await Task.Run(() => _enumerator.EnumerateWindows());
            Windows.Clear();
            foreach (var w in wins)
            {
                Windows.Add(new WindowItem
                {
                    Hwnd = w.Hwnd,
                    Title = w.Title,
                    Exe = w.Exe,
                    Class = w.Class,
                    Path = w.Path,
                    Url = w.Url,
                    BrowserProfile = w.BrowserProfile,
                    CommandLine = w.CommandLine,
                    MinMax = w.MinMax,
                    Rect = w.Rect
                });
            }
            StatusText = $"ウィンドウ一覧を更新しました（{wins.Count}件）";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "RefreshWindows failed");
            StatusText = $"更新に失敗しました: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var name = ProfileName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            StatusText = "プロファイル名を入力してください。";
            return;
        }

        var checkedWindows = Windows.Where(w => w.IsChecked).ToList();
        if (checkedWindows.Count == 0)
        {
            StatusText = "チェックされたウィンドウがありません。";
            return;
        }

        try
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");
            var existing = _store.FindByName(name);

            var windowEntries = new List<WindowEntry>();
            foreach (var w in checkedWindows)
            {
                var entry = CaptureWindowEntry(w);
                if (entry != null)
                    windowEntries.Add(entry);
            }

            if (windowEntries.Count == 0)
            {
                StatusText = "保存できるウィンドウがありません。";
                return;
            }

            var profile = existing ?? new Profile
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = name,
                CreatedAt = now,
                SyncMinMax = 0
            };

            profile.Windows = windowEntries;
            profile.UpdatedAt = now;
            if (existing == null)
                profile.CreatedAt = now;

            _store.SaveProfile(profile);
            ReloadProfiles();
            _syncManager.ScheduleRebuild();
            StatusText = $"保存しました: {name}";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "SaveProfile failed");
            StatusText = $"保存に失敗しました: {ex.Message}";
        }
    }

    private WindowEntry? CaptureWindowEntry(WindowItem w)
    {
        try
        {
            var cls = ClassMatcher.NormalizeForMatch(w.Class);
            var url = "";
            try
            {
                url = _urlRetriever.TryGetUrl(w.Hwnd, w.Exe);
            }
            catch (Exception ex) { _log.Debug(ex, "URL retrieval failed for hwnd {Hwnd}", w.Hwnd); }

            var urlKey = UrlNormalizer.Normalize(url);

            BrowserIdentity? browser = null;
            var exeLower = w.Exe.ToLowerInvariant();
            if (BrowserIdentifier.IsBrowser(exeLower) && !string.IsNullOrEmpty(w.CommandLine))
            {
                browser = BrowserIdentifier.ExtractIdentity(exeLower, w.CommandLine);
            }

            // Get current rect and minmax from live window
            var rect = WindowEnumerator.GetWindowRect(w.Hwnd);
            var minMax = WindowEnumerator.GetMinMax(w.Hwnd);

            // --- Always resolve owning monitor ---
            Snap? snap = null;
            Core.Models.MonitorInfo? monitor = null;
            NormalizedRect? rectNormalized = null;

            var mon = MonitorHelper.GetMonitorForRect(rect.X, rect.Y, rect.W, rect.H);
            if (mon != null)
            {
                monitor = new Core.Models.MonitorInfo
                {
                    Index = mon.Index,
                    Name = mon.DeviceName,
                    PixelWidth = mon.PixelWidth,
                    PixelHeight = mon.PixelHeight
                };

                // Normalized rect (work-area relative)
                rectNormalized = NormalizedRect.FromAbsolute(
                    rect.X, rect.Y, rect.W, rect.H, mon.WorkArea);

                // Snap detection (normal state only)
                if (minMax == 0)
                {
                    var snapType = SnapCalculator.DetectSnap(
                        rect.X, rect.Y, rect.W, rect.H, mon.WorkArea);
                    if (!string.IsNullOrEmpty(snapType))
                        snap = new Snap { Type = snapType };
                }
            }

            // --- Virtual Desktop Id ---
            string? desktopId = null;
            var dId = _vdService.GetWindowDesktopId(w.Hwnd);
            if (dId.HasValue && dId.Value != Guid.Empty)
                desktopId = dId.Value.ToString("D");

            return new WindowEntry
            {
                Match = new MatchInfo
                {
                    Exe = w.Exe,
                    Class = cls,
                    Title = WindowEnumerator.GetWindowTitle(w.Hwnd),
                    Url = url,
                    UrlKey = urlKey,
                    Browser = browser
                },
                Path = w.Path,
                Rect = rect,
                RectNormalized = rectNormalized,
                MinMax = minMax,
                Snap = snap,
                Monitor = monitor,
                DesktopId = desktopId
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "CaptureWindowEntry failed for {Exe}", w.Exe);
            return null;
        }
    }

    [RelayCommand]
    private async Task ApplyProfile()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }
        await DoApplyAsync(SelectedProfile.Id, false);
    }

    [RelayCommand]
    private async Task LaunchAndApplyProfile()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }
        await DoApplyAsync(SelectedProfile.Id, true);
    }

    private async Task DoApplyAsync(string profileId, bool launchMissing)
    {
        try
        {
            var profile = _store.FindById(profileId);
            if (profile == null)
            {
                StatusText = "プロファイルが見つかりません";
                return;
            }

            var appHwnd = GetMainWindowHandle();

            var result = await _profileApplier.ApplyByIdAsync(profileId, launchMissing, appHwnd);
            StatusText = result.ToStatusMessage(profile.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ApplyProfile failed");
            StatusText = $"適用に失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyToMonitor()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }

        var monitors = MonitorHelper.GetMonitors();
        if (monitors.Count == 0)
        {
            StatusText = "モニターが見つかりません。";
            return;
        }

        var overlays = new List<MonitorOverlayWindow>();
        MonitorData? selectedMonitor = null;

        try
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                var name = m.DeviceName;
                if (name.StartsWith("\\\\.\\"))
                    name = name.Substring(4);
                var overlay = new MonitorOverlayWindow(
                    i + 1, $"{name}\n{m.PixelWidth}\u00d7{m.PixelHeight}",
                    m.MonitorRect.Left, m.MonitorRect.Top,
                    m.MonitorRect.Width, m.MonitorRect.Height);
                overlay.Show();
                overlays.Add(overlay);
            }

            var picker = new MonitorPickerWindow(monitors);
            if (Application.Current.MainWindow is { } owner)
                picker.Owner = owner;
            if (picker.ShowDialog() == true)
                selectedMonitor = picker.SelectedMonitor;
        }
        finally
        {
            foreach (var o in overlays)
                o.Close();
        }

        if (selectedMonitor == null) return;

        // --- 事前警告チェック ---
        var profile = _store.FindById(SelectedProfile.Id);
        if (profile == null)
        {
            StatusText = "プロファイルが見つかりません";
            return;
        }

        var settings = _store.Data.Settings;
        var preWarnings = new List<string>();
        foreach (var entry in profile.Windows)
        {
            bool isExact = entry.Monitor != null
                && !string.IsNullOrEmpty(entry.Monitor.Name)
                && entry.Monitor.Name == selectedMonitor.DeviceName;

            var result = MonitorTransformDecision.Evaluate(
                entry.Monitor,
                selectedMonitor.PixelWidth,
                selectedMonitor.PixelHeight,
                isExact,
                settings);

            if (result.Level >= MonitorTransformLevel.Warn)
            {
                var exeName = entry.Match?.Exe ?? "不明";
                foreach (var r in result.Reasons)
                    preWarnings.Add($"{exeName}: {r.Message}");
            }
        }

        // 重複排除 (同一解像度警告など)
        preWarnings = preWarnings.Distinct().ToList();

        if (preWarnings.Count > 0)
        {
            var monName = selectedMonitor.DeviceName;
            if (monName.StartsWith("\\\\.\\"))
                monName = monName.Substring(4);
            var desc = $"配置先: {monName} ({selectedMonitor.PixelWidth}\u00d7{selectedMonitor.PixelHeight})";

            var dlg = new MonitorWarningDialog(desc, preWarnings);
            if (Application.Current.MainWindow is { } warnOwner)
                dlg.Owner = warnOwner;
            if (dlg.ShowDialog() != true)
            {
                StatusText = "配置をキャンセルしました。";
                return;
            }
        }

        // --- 適用 ---
        try
        {
            var appHwnd = GetMainWindowHandle();

            var result = await _profileApplier.ApplyByIdAsync(
                SelectedProfile.Id, false, appHwnd, selectedMonitor);
            StatusText = result.ToStatusMessage(profile.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ApplyToMonitor failed");
            StatusText = $"適用に失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task ApplyToDesktopAndMonitor()
    {
        // 仮想デスクトップ関連は整備中のため、現時点では何もしない。
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }

        var name = SelectedProfile.Name;

        var result = MessageBox.Show(
            $"プロファイル『{name}』を削除しますか？",
            "Window-Controller",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            StatusText = "削除をキャンセルしました。";
            return;
        }

        if (_store.DeleteProfileById(SelectedProfile.Id))
        {
            ReloadProfiles();
            _syncManager.ScheduleRebuild();
            StatusText = $"削除しました: {name}";
        }
        else
        {
            StatusText = $"削除できませんでした: {name}";
        }
    }

    [RelayCommand]
    private void SetTargetDesktop()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }

        var appHwnd = GetMainWindowHandle();

        var desktopId = _vdService.GetCurrentDesktopId(appHwnd);
        if (!desktopId.HasValue || desktopId.Value == Guid.Empty)
        {
            StatusText = "現在のデスクトップIDを取得できませんでした。";
            return;
        }

        var profile = _store.FindById(SelectedProfile.Id);
        if (profile == null) return;

        profile.TargetDesktopId = desktopId.Value.ToString("D");
        _store.SaveProfile(profile);

        SelectedProfile.TargetDesktopLabel = FormatDesktopLabel(profile.TargetDesktopId);
        StatusText = $"ターゲットデスクトップを設定しました: {profile.Name}";
    }

    [RelayCommand]
    private void ClearTargetDesktop()
    {
        if (SelectedProfile == null)
        {
            StatusText = "プロファイルを選択してください。";
            return;
        }

        var profile = _store.FindById(SelectedProfile.Id);
        if (profile == null) return;

        profile.TargetDesktopId = null;
        _store.SaveProfile(profile);

        SelectedProfile.TargetDesktopLabel = "";
        StatusText = $"ターゲットデスクトップを解除しました: {profile.Name}";
    }

    private static string FormatDesktopLabel(string? desktopId)
        => desktopId ?? "";

    private static nint GetMainWindowHandle()
    {
        if (Application.Current.MainWindow is not { } mainWindow)
            return 0;

        var helper = new WindowInteropHelper(mainWindow);
        return helper.Handle;
    }

    public void ReloadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _store.Data.Profiles)
        {
            var item = new ProfileItem
            {
                Id = p.Id,
                Name = p.Name,
                SyncMinMax = p.SyncMinMax != 0,
                WindowCount = p.Windows.Count,
                TargetDesktopLabel = FormatDesktopLabel(p.TargetDesktopId)
            };
            item.PropertyChanged += (s, e) =>
            {
                if (s is not ProfileItem pi) return;
                if (e.PropertyName == nameof(ProfileItem.SyncMinMax))
                    OnProfileSyncChanged(pi);
                else if (e.PropertyName == nameof(ProfileItem.Name))
                    OnProfileNameChanged(pi);
            };
            Profiles.Add(item);
        }
    }

    private void OnProfileNameChanged(ProfileItem pi)
    {
        // Prevent re-entrancy when we programmatically update pi.Name
        if (_isUpdatingProfileName)
            return;

        // Get the current profile from the store
        var current = _store.FindById(pi.Id);
        if (current == null)
        {
            StatusText = "プロファイルが見つかりません。";
            return;
        }

        // If the name has not actually changed compared to the store, ignore.
        if (string.Equals(current.Name, pi.Name, StringComparison.Ordinal))
            return;


        var newName = pi.Name?.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            // Revert to current stored name
            _isUpdatingProfileName = true;
            try
            {
                pi.Name = current.Name;
            }
            finally
            {
                _isUpdatingProfileName = false;
            }
            StatusText = "プロファイル名を空にすることはできません。";
            return;
        }

        var finalName = _store.RenameProfile(pi.Id, newName);
        if (finalName == null)
        {
            StatusText = "プロファイルが見つかりません。";
            return;
        }

        // If the name was adjusted due to conflict, update the UI
        if (finalName != newName)
        {
            _isUpdatingProfileName = true;
            try
            {
                pi.Name = finalName;
            }
            finally
            {
                _isUpdatingProfileName = false;
            }
        }

        _syncManager.ScheduleRebuild();
        StatusText = $"名前を変更しました: {finalName}";
    }

    private void OnProfileSyncChanged(ProfileItem pi)
    {
        var profile = _store.FindById(pi.Id);
        if (profile != null)
        {
            profile.SyncMinMax = pi.SyncMinMax ? 1 : 0;
            _store.SaveProfile(profile);
            // UpdateHooksIfNeeded already schedules a rebuild internally
            _syncManager.UpdateHooksIfNeeded();
            StatusText = $"連動設定({profile.Name}): {(pi.SyncMinMax ? "ON" : "OFF")}";
        }
    }

    public void Initialize()
    {
        ReloadProfiles();
        // Kick off async refresh (fire-and-forget on UI thread)
        _ = RefreshWindowsCommand.ExecuteAsync(null);
    }
}
