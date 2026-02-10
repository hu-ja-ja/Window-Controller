using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using WindowController.App.ViewModels;
using WindowController.Browser;
using WindowController.Core;
using WindowController.Win32;

namespace WindowController.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private SyncManager? _syncManager;
    private HotkeyManager? _hotkeyManager;
    private ILogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Determine config directory (next to exe, or fallback)
            var baseDir = AppContext.BaseDirectory;
            var configDir = Path.Combine(baseDir, "config");
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            var logPath = Path.Combine(configDir, "window-controller.log");
            var profilesPath = Path.Combine(configDir, "profiles.json");

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

            // Core services
            var store = new ProfileStore(profilesPath, _log);
            store.Load();

            var urlRetriever = new BrowserUrlRetriever(_log);
            var enumerator = new WindowEnumerator(_log, (hwnd, exe) => urlRetriever.TryGetUrl(hwnd, exe));
            var arranger = new WindowArranger(_log);
            var hookManager = new WinEventHookManager(_log);
            _syncManager = new SyncManager(store, enumerator, hookManager, _log);

            _viewModel = new MainViewModel(store, enumerator, arranger, urlRetriever, _syncManager, _log);
            _viewModel.Initialize();

            // Start sync hooks if enabled
            _syncManager.UpdateHooksIfNeeded();

            // Create main window
            _mainWindow = new MainWindow();
            _mainWindow.DataContext = _viewModel;

            // Setup tray icon
            SetupTrayIcon();

            // Setup hotkey (Ctrl+Alt+W)
            _hotkeyManager = new HotkeyManager(_log);
            // We need an HWND for hotkey registration; use a helper window
            _hotkeyManager.Register(() => ShowMainWindow());

            // Show GUI if setting says so
            if (store.Data.Settings.ShowGuiOnStartup != 0)
                ShowMainWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初期化に失敗しました。\n{ex.Message}", "Window-Controller", MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Startup failed");
            Shutdown();
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

        var menuApply = new System.Windows.Controls.MenuItem { Header = "プロファイルを適用(配置のみ)" };
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
        base.OnExit(e);
    }
}

