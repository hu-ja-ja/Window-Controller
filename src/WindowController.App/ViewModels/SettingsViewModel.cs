using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;
using WindowController.Core;
using WindowController.Core.Models;

namespace WindowController.App.ViewModels;

/// <summary>
/// Represents a hotkey item for a profile in the settings UI.
/// </summary>
public partial class ProfileHotkeyItem : ObservableObject
{
    [ObservableProperty] private string _profileId = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _hotkeyDisplay = "なし";
    [ObservableProperty] private HotkeyBinding _binding = new();

    public void UpdateDisplay()
    {
        HotkeyDisplay = Binding.IsEmpty ? "なし" : Binding.ToString();
    }
}

/// <summary>
/// ViewModel for the SettingsWindow.
/// Manages both profiles.json settings (via ProfileStore) and appsettings.json (via AppSettingsStore).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore;
    private readonly AppSettingsStore _appSettingsStore;
    private readonly HotkeyManager _hotkeyManager;
    private readonly SyncManager _syncManager;
    private readonly ILogger _log;

    // Callback to refresh hotkeys after settings change
    private readonly Action? _refreshHotkeysCallback;

    // ========== Existing settings from profiles.json ==========
    [ObservableProperty] private bool _syncEnabled;
    [ObservableProperty] private bool _showGuiOnStartup;

    // ========== Settings from appsettings.json ==========
    [ObservableProperty] private string _profilesPathDisplay = "";

    // ========== Hotkey settings ==========
    [ObservableProperty] private string _guiHotkeyDisplay = "";
    [ObservableProperty] private HotkeyBinding _guiHotkeyBinding = new();
    [ObservableProperty] private bool _isCapturingGuiHotkey;

    public ObservableCollection<ProfileHotkeyItem> ProfileHotkeys { get; } = new();

    [ObservableProperty] private ProfileHotkeyItem? _selectedProfileHotkey;
    [ObservableProperty] private bool _isCapturingProfileHotkey;

    [ObservableProperty] private string _statusText = "";

    public SettingsViewModel(
        ProfileStore profileStore,
        AppSettingsStore appSettingsStore,
        HotkeyManager hotkeyManager,
        SyncManager syncManager,
        ILogger log,
        Action? refreshHotkeysCallback = null)
    {
        _profileStore = profileStore;
        _appSettingsStore = appSettingsStore;
        _hotkeyManager = hotkeyManager;
        _syncManager = syncManager;
        _log = log;
        _refreshHotkeysCallback = refreshHotkeysCallback;

        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load from profiles.json
        SyncEnabled = _profileStore.Data.Settings.SyncMinMax != 0;
        ShowGuiOnStartup = _profileStore.Data.Settings.ShowGuiOnStartup != 0;

        // Load from appsettings.json
        ProfilesPathDisplay = _profileStore.FilePath;

        // Load hotkey settings
        GuiHotkeyBinding = _appSettingsStore.Data.Hotkeys.ShowGui.Clone();
        UpdateGuiHotkeyDisplay();

        // Load profile hotkeys
        LoadProfileHotkeys();
    }

    // ========== Reset-to-default (per section) ==========

    [RelayCommand]
    private void ResetGeneralSettings()
    {
        // Defaults from ProfileStore.CreateDefault(): SyncMinMax=0, ShowGuiOnStartup=1
        SyncEnabled = false;
        ShowGuiOnStartup = true;
        StatusText = "全般設定を既定に戻しました";
    }

    [RelayCommand]
    private void ResetGuiHotkeyToDefault()
    {
        // Default GUI hotkey: Ctrl+Alt+W
        var defaultBinding = new HotkeyBinding { Key = "W", Ctrl = true, Alt = true };

        // Strict flow: test -> save only if success
        var testResult = _hotkeyManager.TestHotkey(defaultBinding);
        if (!testResult.Success)
        {
            StatusText = testResult.ErrorMessage ?? "既定ホットキーの登録に失敗しました";
            return;
        }

        GuiHotkeyBinding = defaultBinding;
        UpdateGuiHotkeyDisplay();
        _appSettingsStore.Data.Hotkeys.ShowGui = defaultBinding.Clone();
        _appSettingsStore.Save();
        _refreshHotkeysCallback?.Invoke();
        StatusText = $"GUI表示ホットキーを既定に戻しました: {defaultBinding}";
    }

    [RelayCommand]
    private void ResetAllProfileHotkeys()
    {
        // Default for profile hotkeys: no binds
        _appSettingsStore.Data.Hotkeys.Profiles.Clear();
        _appSettingsStore.Save();

        foreach (var item in ProfileHotkeys)
        {
            item.Binding = new HotkeyBinding();
            item.UpdateDisplay();
        }

        _refreshHotkeysCallback?.Invoke();
        StatusText = "プロファイル適用ホットキーを既定に戻しました";
    }

    private void LoadProfileHotkeys()
    {
        ProfileHotkeys.Clear();
        foreach (var profile in _profileStore.Data.Profiles)
        {
            var item = new ProfileHotkeyItem
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                Binding = _appSettingsStore.Data.Hotkeys.Profiles.TryGetValue(profile.Id, out var binding)
                    ? binding.Clone()
                    : new HotkeyBinding()
            };
            item.UpdateDisplay();
            ProfileHotkeys.Add(item);
        }
    }

    private void UpdateGuiHotkeyDisplay()
    {
        GuiHotkeyDisplay = GuiHotkeyBinding.IsEmpty ? "なし" : GuiHotkeyBinding.ToString();
    }

    // ========== Settings changes ==========

    partial void OnSyncEnabledChanged(bool value)
    {
        _profileStore.Data.Settings.SyncMinMax = value ? 1 : 0;
        _profileStore.Save();
        _syncManager.UpdateHooksIfNeeded();
        StatusText = $"連動設定: {(value ? "ON" : "OFF")}";
    }

    partial void OnShowGuiOnStartupChanged(bool value)
    {
        _profileStore.Data.Settings.ShowGuiOnStartup = value ? 1 : 0;
        _profileStore.Save();
        StatusText = $"起動時GUI表示: {(value ? "ON" : "OFF")}";
    }

    // ========== Profiles path commands ==========

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

        var currentDir = Path.GetDirectoryName(_profileStore.FilePath);
        if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
            dlg.InitialDirectory = currentDir;

        if (dlg.ShowDialog() != true)
            return;

        var selectedPath = dlg.FileName;
        if (!Path.GetFileName(selectedPath).Equals("profiles.json", StringComparison.OrdinalIgnoreCase))
            selectedPath = Path.Combine(Path.GetDirectoryName(selectedPath) ?? selectedPath, "profiles.json");

        try
        {
            _appSettingsStore.SetProfilesPath(selectedPath);
            _profileStore.ChangePath(selectedPath);
            ProfilesPathDisplay = _profileStore.FilePath;

            // Reload settings from new file
            SyncEnabled = _profileStore.Data.Settings.SyncMinMax != 0;
            ShowGuiOnStartup = _profileStore.Data.Settings.ShowGuiOnStartup != 0;

            // Reload profile hotkeys
            LoadProfileHotkeys();

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
            _appSettingsStore.SetProfilesPath("");
            var defaultPath = _appSettingsStore.EffectiveProfilesPath;
            _profileStore.ChangePath(defaultPath);
            ProfilesPathDisplay = _profileStore.FilePath;

            SyncEnabled = _profileStore.Data.Settings.SyncMinMax != 0;
            ShowGuiOnStartup = _profileStore.Data.Settings.ShowGuiOnStartup != 0;

            LoadProfileHotkeys();

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

    // ========== GUI Hotkey ==========

    [RelayCommand]
    private void CaptureGuiHotkey()
    {
        IsCapturingGuiHotkey = true;
        StatusText = "キーを押してください… (Escでキャンセル)";
    }

    public void OnGuiHotkeyKeyDown(KeyEventArgs e)
    {
        if (!IsCapturingGuiHotkey) return;

        e.Handled = true;

        // Cancel on Escape
        if (e.Key == Key.Escape)
        {
            IsCapturingGuiHotkey = false;
            StatusText = "キャンセルしました";
            return;
        }

        // Ignore modifier-only presses
        if (IsModifierKey(e.Key))
            return;

        var binding = CreateBindingFromKeyEvent(e);
        if (binding.IsEmpty)
        {
            StatusText = "無効なキーです";
            return;
        }

        // Test registration before saving
        var testResult = _hotkeyManager.TestHotkey(binding);
        if (!testResult.Success)
        {
            StatusText = testResult.ErrorMessage ?? "ホットキーの登録に失敗しました";
            IsCapturingGuiHotkey = false;
            return;
        }

        // Save to settings
        GuiHotkeyBinding = binding;
        UpdateGuiHotkeyDisplay();
        _appSettingsStore.Data.Hotkeys.ShowGui = binding.Clone();
        _appSettingsStore.Save();

        // Re-register hotkeys
        _refreshHotkeysCallback?.Invoke();

        IsCapturingGuiHotkey = false;
        StatusText = $"GUI表示ホットキーを設定しました: {binding}";
    }

    [RelayCommand]
    private void ClearGuiHotkey()
    {
        GuiHotkeyBinding = new HotkeyBinding();
        UpdateGuiHotkeyDisplay();
        _appSettingsStore.Data.Hotkeys.ShowGui = new HotkeyBinding();
        _appSettingsStore.Save();
        _refreshHotkeysCallback?.Invoke();
        StatusText = "GUI表示ホットキーを無効化しました";
    }

    // ========== Profile Hotkey ==========

    [RelayCommand]
    private void CaptureProfileHotkey()
    {
        if (SelectedProfileHotkey == null)
        {
            StatusText = "プロファイルを選択してください";
            return;
        }
        IsCapturingProfileHotkey = true;
        StatusText = $"[{SelectedProfileHotkey.ProfileName}] キーを押してください… (Escでキャンセル)";
    }

    public void OnProfileHotkeyKeyDown(KeyEventArgs e)
    {
        if (!IsCapturingProfileHotkey || SelectedProfileHotkey == null) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            IsCapturingProfileHotkey = false;
            StatusText = "キャンセルしました";
            return;
        }

        if (IsModifierKey(e.Key))
            return;

        var binding = CreateBindingFromKeyEvent(e);
        if (binding.IsEmpty)
        {
            StatusText = "無効なキーです";
            return;
        }

        // Check for conflicts with GUI hotkey
        if (GuiHotkeyBinding.Equals(binding))
        {
            StatusText = "GUI表示ホットキーと重複しています";
            IsCapturingProfileHotkey = false;
            return;
        }

        // Check for conflicts with other profile hotkeys
        foreach (var item in ProfileHotkeys)
        {
            if (item.ProfileId != SelectedProfileHotkey.ProfileId && item.Binding.Equals(binding))
            {
                StatusText = $"プロファイル「{item.ProfileName}」のホットキーと重複しています";
                IsCapturingProfileHotkey = false;
                return;
            }
        }

        // Test registration
        var testResult = _hotkeyManager.TestHotkey(binding);
        if (!testResult.Success)
        {
            StatusText = testResult.ErrorMessage ?? "ホットキーの登録に失敗しました";
            IsCapturingProfileHotkey = false;
            return;
        }

        // Save to settings
        SelectedProfileHotkey.Binding = binding;
        SelectedProfileHotkey.UpdateDisplay();
        _appSettingsStore.Data.Hotkeys.Profiles[SelectedProfileHotkey.ProfileId] = binding.Clone();
        _appSettingsStore.Save();

        _refreshHotkeysCallback?.Invoke();

        IsCapturingProfileHotkey = false;
        StatusText = $"[{SelectedProfileHotkey.ProfileName}] ホットキーを設定しました: {binding}";
    }

    [RelayCommand]
    private void ClearProfileHotkey()
    {
        if (SelectedProfileHotkey == null)
        {
            StatusText = "プロファイルを選択してください";
            return;
        }

        SelectedProfileHotkey.Binding = new HotkeyBinding();
        SelectedProfileHotkey.UpdateDisplay();
        _appSettingsStore.Data.Hotkeys.Profiles.Remove(SelectedProfileHotkey.ProfileId);
        _appSettingsStore.Save();
        _refreshHotkeysCallback?.Invoke();
        StatusText = $"[{SelectedProfileHotkey.ProfileName}] ホットキーを無効化しました";
    }

    // ========== Helpers ==========

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LWin || key == Key.RWin ||
               key == Key.System; // Alt key generates System key
    }

    private static HotkeyBinding CreateBindingFromKeyEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
            return new HotkeyBinding();

        var keyString = HotkeyManager.GetKeyString(key);
        if (string.IsNullOrEmpty(keyString))
            return new HotkeyBinding();

        return new HotkeyBinding
        {
            Key = keyString,
            Ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            Alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
            Shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            Win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0
        };
    }

    /// <summary>
    /// Refresh the profile list (e.g., after profiles are added/removed in main window).
    /// </summary>
    public void RefreshProfiles()
    {
        LoadProfileHotkeys();
    }
}
