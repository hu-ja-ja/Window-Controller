using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
    private readonly AppSettingsStore _appSettings;
    private readonly ILogger _log;
    private bool _isUpdatingProfileName;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private bool _syncEnabled;
    [ObservableProperty] private bool _showGuiOnStartup;
    [ObservableProperty] private ProfileItem? _selectedProfile;
    [ObservableProperty] private string _profilesPathDisplay = "";

    public ObservableCollection<WindowItem> Windows { get; } = new();
    public ObservableCollection<ProfileItem> Profiles { get; } = new();

    public MainViewModel(ProfileStore store, WindowEnumerator enumerator,
        WindowArranger arranger, BrowserUrlRetriever urlRetriever,
        SyncManager syncManager, AppSettingsStore appSettings, ILogger log)
    {
        _store = store;
        _enumerator = enumerator;
        _arranger = arranger;
        _urlRetriever = urlRetriever;
        _syncManager = syncManager;
        _appSettings = appSettings;
        _log = log;

        SyncEnabled = _store.Data.Settings.SyncMinMax != 0;
        ShowGuiOnStartup = _store.Data.Settings.ShowGuiOnStartup != 0;
        ProfilesPathDisplay = _store.FilePath;
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

            Snap? snap = null;
            Core.Models.MonitorInfo? monitor = null;
            if (minMax == 0)
            {
                var mon = MonitorHelper.GetMonitorForRect(rect.X, rect.Y, rect.W, rect.H);
                if (mon != null)
                {
                    var snapType = SnapCalculator.DetectSnap(rect.X, rect.Y, rect.W, rect.H, mon.WorkArea);
                    if (!string.IsNullOrEmpty(snapType))
                    {
                        snap = new Snap { Type = snapType };
                        monitor = new Core.Models.MonitorInfo { Index = mon.Index, Name = mon.DeviceName };
                    }
                }
            }

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
                MinMax = minMax,
                Snap = snap,
                Monitor = monitor
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
                StatusText = $"プロファイルが見つかりません";
                return;
            }

            var profileName = profile.Name;

            var candidates = GetCandidates();
            int applied = 0;
            var failures = new List<string>();

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

                    _arranger.Arrange(hwnd, entry);
                    applied++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{entry.Match.Exe} | {entry.Match.Title} : {ex.Message}");
                    _log.Warning(ex, "ApplyProfile item failed");
                }
            }

            var msg = $"{profileName} を適用: {applied}/{profile.Windows.Count}";
            if (failures.Count > 0)
                msg += $"（失敗 {failures.Count}件: {string.Join(", ", failures.Take(3))}）";
            StatusText = msg;
            _syncManager.ScheduleRebuild();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ApplyProfile failed");
            StatusText = $"適用に失敗: {ex.Message}";
        }
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
                WindowCount = p.Windows.Count
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

    partial void OnSyncEnabledChanged(bool value)
    {
        _store.Data.Settings.SyncMinMax = value ? 1 : 0;
        _store.Save();
        _syncManager.UpdateHooksIfNeeded();
        StatusText = $"連動設定: {(value ? "ON" : "OFF")}";
    }

    partial void OnShowGuiOnStartupChanged(bool value)
    {
        _store.Data.Settings.ShowGuiOnStartup = value ? 1 : 0;
        _store.Save();
        StatusText = $"起動時GUI表示: {(value ? "ON" : "OFF")}";
    }

    public void Initialize()
    {
        ReloadProfiles();
        // Kick off async refresh (fire-and-forget on UI thread)
        _ = RefreshWindowsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void BrowseProfilesPath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "profiles.json の保存先を選択",
            Filter = "JSONファイル|profiles.json|All files|*.*",
            FileName = "profiles.json",
            CheckFileExists = false,
        };

        // Set initial directory from current path
        var currentDir = Path.GetDirectoryName(_store.FilePath);
        if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
            dlg.InitialDirectory = currentDir;

        if (dlg.ShowDialog() != true)
            return;

        var selectedPath = dlg.FileName;

        // Ensure the filename is profiles.json
        if (!Path.GetFileName(selectedPath).Equals("profiles.json", StringComparison.OrdinalIgnoreCase))
            selectedPath = Path.Combine(Path.GetDirectoryName(selectedPath) ?? selectedPath, "profiles.json");

        try
        {
            _appSettings.SetProfilesPath(selectedPath);
            _store.ChangePath(selectedPath);
            ProfilesPathDisplay = _store.FilePath;

            SyncEnabled = _store.Data.Settings.SyncMinMax != 0;
            ShowGuiOnStartup = _store.Data.Settings.ShowGuiOnStartup != 0;

            ReloadProfiles();
            _syncManager.ScheduleRebuild();
            StatusText = $"保存先を変更しました: {selectedPath}";
            _log.Information("Profiles path changed to {Path}", selectedPath);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "BrowseProfilesPath failed");
            StatusText = $"保存先の変更に失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetProfilesPath()
    {
        try
        {
            _appSettings.SetProfilesPath("");
            var defaultPath = _appSettings.EffectiveProfilesPath;
            _store.ChangePath(defaultPath);
            ProfilesPathDisplay = _store.FilePath;

            SyncEnabled = _store.Data.Settings.SyncMinMax != 0;
            ShowGuiOnStartup = _store.Data.Settings.ShowGuiOnStartup != 0;

            ReloadProfiles();
            _syncManager.ScheduleRebuild();
            StatusText = $"保存先を既定に戻しました: {defaultPath}";
            _log.Information("Profiles path reset to default: {Path}", defaultPath);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ResetProfilesPath failed");
            StatusText = $"既定へのリセットに失敗: {ex.Message}";
        }
    }
}
