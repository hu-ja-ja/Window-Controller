using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;
using WindowController.Win32;

namespace WindowController.App;

/// <summary>
/// Manages global hotkey (Ctrl+Alt+W) registration.
/// </summary>
public class HotkeyManager : IDisposable
{
    private const int HOTKEY_ID = 1;
    private const int WM_HOTKEY = 0x0312;

    private readonly ILogger _log;
    private HwndSource? _hwndSource;
    private Action? _callback;
    private bool _disposed;

    public HotkeyManager(ILogger log)
    {
        _log = log;
    }

    public void Register(Action callback)
    {
        _callback = callback;

        // Create a hidden window for hotkey messages
        var parameters = new HwndSourceParameters("WindowControllerHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0 // invisible
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        var hwnd = _hwndSource.Handle;
        var result = NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_W);
        if (result)
            _log.Information("Hotkey Ctrl+Alt+W registered");
        else
            _log.Warning("Failed to register hotkey Ctrl+Alt+W");
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _callback?.Invoke();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwndSource != null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
