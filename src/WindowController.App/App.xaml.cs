using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using Wpf.Ui.Appearance;
using WindowController.App.ViewModels;
using WindowController.Browser;
using WindowController.Core;
using WindowController.Win32;

namespace WindowController.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private MainViewModel? _viewModel;
    private SettingsViewModel? _settingsViewModel;
    private SyncManager? _syncManager;
    private HotkeyManager? _hotkeyManager;
    private ProfileApplier? _profileApplier;
    private ProfileStore? _profileStore;
    private AppSettingsStore? _appSettingsStore;
    private ILogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- Single-instance guard ---
        _singleInstanceMutex = new Mutex(true, "Global\\WindowController_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Window-Controller は既に起動しています。", "Window-Controller",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            // --- Data directory: %LOCALAPPDATA%\WindowController ---
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowController");
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            var logPath = Path.Combine(dataDir, "window-controller.log");
            var defaultProfilesPath = Path.Combine(dataDir, "profiles.json");
            var appSettingsPath = Path.Combine(dataDir, "appsettings.json");

            // Setup Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Infinite,
                    retainedFileCountLimit: null,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss}  [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            _log = Log.Logger;
            _log.Information("Window-Controller starting");

            // Load app-level settings (profiles path etc.)
            _appSettingsStore = new AppSettingsStore(appSettingsPath, defaultProfilesPath, _log);
            _appSettingsStore.Load();

            var profilesPath = _appSettingsStore.EffectiveProfilesPath;
            _log.Information("Profiles path: {Path}", profilesPath);

            // Core services
            _profileStore = new ProfileStore(profilesPath, _log);
            _profileStore.Load();

            var urlRetriever = new BrowserUrlRetriever(_log);
            var enumerator = new WindowEnumerator(_log, (hwnd, exe) => urlRetriever.TryGetUrl(hwnd, exe));
            var arranger = new WindowArranger(_log);
            var hookManager = new WinEventHookManager(_log);
            _syncManager = new SyncManager(_profileStore, enumerator, hookManager, _log);

            // Profile applier for hotkey access
            _profileApplier = new ProfileApplier(_profileStore, enumerator, arranger, _syncManager, _log);

            _viewModel = new MainViewModel(_profileStore, enumerator, arranger, urlRetriever, _syncManager, _appSettingsStore, _log);
            _viewModel.Initialize();

            // Start sync hooks if enabled
            _syncManager.UpdateHooksIfNeeded();

            // Create main window
            _mainWindow = new MainWindow();
            _mainWindow.DataContext = _viewModel;
            _mainWindow.SettingsRequested += (_, _) => ShowSettingsWindow();

            // Apply WPF-UI theme (follow system dark/light)
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica);

            // Accent color (水色)
            var appTheme = ApplicationThemeManager.GetAppTheme();
            ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0, 153, 255), appTheme, false, false);
            SystemThemeWatcher.Watch(_mainWindow);

            // Setup tray icon
            SetupTrayIcon();

            // Setup hotkey manager
            _hotkeyManager = new HotkeyManager(_log);
            RegisterAllHotkeys();

            // Create settings window (lazily shown)
            CreateSettingsWindow();

            // Show GUI if setting says so
            if (_profileStore.Data.Settings.ShowGuiOnStartup != 0)
                ShowMainWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初期化に失敗しました。\n{ex.Message}", "Window-Controller", MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Startup failed");
            Shutdown();
        }
    }

    private void CreateSettingsWindow()
    {
        if (_profileStore == null || _appSettingsStore == null || _hotkeyManager == null || _syncManager == null || _log == null)
            return;

        _settingsViewModel = new SettingsViewModel(
            _profileStore,
            _appSettingsStore,
            _hotkeyManager,
            _syncManager,
            _log,
            refreshHotkeysCallback: RegisterAllHotkeys,
            applyProfileCallback: async (profileId, launchMissing) =>
            {
                if (_profileApplier != null)
                    await _profileApplier.ApplyByIdAsync(profileId, launchMissing);
            });

        _settingsWindow = new SettingsWindow();
        _settingsWindow.DataContext = _settingsViewModel;
    }

    private void RegisterAllHotkeys()
    {
        if (_hotkeyManager == null || _appSettingsStore == null || _profileApplier == null)
            return;

        // Unregister all profile hotkeys first
        _hotkeyManager.UnregisterAllProfileHotkeys();

        // Register GUI hotkey
        var guiHotkey = _appSettingsStore.Data.Hotkeys.ShowGui;
        if (!guiHotkey.IsEmpty)
        {
            _hotkeyManager.UpdateGuiHotkey(guiHotkey, () => ShowMainWindow());
        }
        else
        {
            _hotkeyManager.UpdateGuiHotkey(new Core.Models.HotkeyBinding(), () => { });
        }

        // Register profile hotkeys
        foreach (var (profileId, binding) in _appSettingsStore.Data.Hotkeys.Profiles)
        {
            if (binding.IsEmpty) continue;

            var capturedProfileId = profileId;
            _hotkeyManager.RegisterProfileHotkey(profileId, binding, () =>
            {
                // Apply profile on hotkey press (arrange only, no launch)
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    if (_profileApplier != null)
                    {
                        var result = await _profileApplier.ApplyByIdAsync(capturedProfileId, false);
                        var profile = _profileStore?.FindById(capturedProfileId);
                        var name = profile?.Name ?? capturedProfileId;
                        if (_viewModel != null)
                        {
                            _viewModel.StatusText = result.ToStatusMessage(name);
                        }
                    }
                });
            });
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Window-Controller",
            Icon = SystemIcons.Application,
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var menuOpen = new System.Windows.Controls.MenuItem { Header = "GUIを開く" };
        menuOpen.Click += (_, _) => ShowMainWindow();
        contextMenu.Items.Add(menuOpen);

        var menuSettings = new System.Windows.Controls.MenuItem { Header = "設定…" };
        menuSettings.Click += (_, _) => ShowSettingsWindow();
        contextMenu.Items.Add(menuSettings);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var menuApply = new System.Windows.Controls.MenuItem { Header = "プロファイルを適用(配置のみ)" };
        // TODO: Add submenu for profile selection if needed
        menuApply.Click += (_, _) => ShowMainWindow();
        contextMenu.Items.Add(menuApply);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var menuExit = new System.Windows.Controls.MenuItem { Header = "終了" };
        menuExit.Click += (_, _) => ExitApp();
        contextMenu.Items.Add(menuExit);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null) return;

        // Refresh profile list in settings
        _settingsViewModel?.RefreshProfiles();

        _settingsWindow.Show();
        _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _log?.Information("Window-Controller exiting");
        _hotkeyManager?.Dispose();
        _syncManager?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Log.CloseAndFlush();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _syncManager?.Dispose();
        _trayIcon?.Dispose();
        Log.CloseAndFlush();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

