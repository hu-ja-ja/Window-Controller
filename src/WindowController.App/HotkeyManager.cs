using System.Windows.Input;
using System.Windows.Interop;
using Serilog;
using WindowController.Core.Models;
using WindowController.Win32;

namespace WindowController.App;

/// <summary>
/// Result of a hotkey registration attempt.
/// </summary>
public record HotkeyRegistrationResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Manages multiple global hotkeys with dynamic registration/unregistration.
/// </summary>
public class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    // Reserved hotkey IDs
    private const int HOTKEY_ID_GUI = 1;
    private const int HOTKEY_ID_PROFILE_BASE = 1000;

    private readonly ILogger _log;
    private HwndSource? _hwndSource;
    private bool _disposed;

    // Currently registered hotkeys
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Dictionary<int, HotkeyBinding> _registeredBindings = new();

    // Mapping from profile Id to hotkey Id
    private readonly Dictionary<string, int> _profileHotkeyIds = new();
    private int _nextProfileHotkeyId = HOTKEY_ID_PROFILE_BASE;

    public HotkeyManager(ILogger log)
    {
        _log = log;
        InitializeHwndSource();
    }

    private void InitializeHwndSource()
    {
        // Create a hidden window for hotkey messages
        var parameters = new HwndSourceParameters("WindowControllerHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0 // invisible
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Register the GUI toggle hotkey.
    /// </summary>
    public HotkeyRegistrationResult RegisterGuiHotkey(HotkeyBinding binding, Action callback)
    {
        return RegisterHotkey(HOTKEY_ID_GUI, binding, callback, "GUI");
    }

    /// <summary>
    /// Update the GUI toggle hotkey. Unregisters the old one first.
    /// </summary>
    public HotkeyRegistrationResult UpdateGuiHotkey(HotkeyBinding binding, Action callback)
    {
        UnregisterHotkey(HOTKEY_ID_GUI);
        if (binding.IsEmpty)
            return new HotkeyRegistrationResult(true);
        return RegisterHotkey(HOTKEY_ID_GUI, binding, callback, "GUI");
    }

    /// <summary>
    /// Register a profile hotkey.
    /// </summary>
    public HotkeyRegistrationResult RegisterProfileHotkey(string profileId, HotkeyBinding binding, Action callback)
    {
        // Clean up any existing registration for this profile
        if (_profileHotkeyIds.TryGetValue(profileId, out var existingId))
        {
            UnregisterHotkey(existingId);
            _profileHotkeyIds.Remove(profileId);
        }

        if (binding.IsEmpty)
            return new HotkeyRegistrationResult(true);

        var hotkeyId = _nextProfileHotkeyId++;
        var result = RegisterHotkey(hotkeyId, binding, callback, $"Profile:{profileId}");
        if (result.Success)
        {
            _profileHotkeyIds[profileId] = hotkeyId;
        }
        return result;
    }

    /// <summary>
    /// Unregister a profile hotkey.
    /// </summary>
    public void UnregisterProfileHotkey(string profileId)
    {
        if (_profileHotkeyIds.TryGetValue(profileId, out var hotkeyId))
        {
            UnregisterHotkey(hotkeyId);
            _profileHotkeyIds.Remove(profileId);
        }
    }

    /// <summary>
    /// Unregister all profile hotkeys.
    /// </summary>
    public void UnregisterAllProfileHotkeys()
    {
        foreach (var (_, hotkeyId) in _profileHotkeyIds)
        {
            UnregisterHotkey(hotkeyId);
        }
        _profileHotkeyIds.Clear();
    }

    /// <summary>
    /// Test if a hotkey can be registered without actually keeping it registered.
    /// Used to validate hotkey before saving to settings.
    /// </summary>
    public HotkeyRegistrationResult TestHotkey(HotkeyBinding binding)
    {
        if (binding.IsEmpty)
            return new HotkeyRegistrationResult(true);

        if (_hwndSource == null)
            return new HotkeyRegistrationResult(false, "HotkeyManager not initialized");

        var hwnd = _hwndSource.Handle;
        var modifiers = GetModifiers(binding);
        var vkCode = GetVirtualKeyCode(binding.Key);

        if (vkCode == 0)
            return new HotkeyRegistrationResult(false, $"無効なキー: {binding.Key}");

        // Check for conflicts with already registered hotkeys
        foreach (var (id, existingBinding) in _registeredBindings)
        {
            if (existingBinding.Equals(binding))
            {
                return new HotkeyRegistrationResult(false, $"このホットキーは既に登録されています: {binding}");
            }
        }

        // Try to register with a temporary ID
        const int testId = 99999;
        var result = NativeMethods.RegisterHotKey(hwnd, testId, modifiers | NativeMethods.MOD_NOREPEAT, (uint)vkCode);
        if (result)
        {
            // Immediately unregister
            NativeMethods.UnregisterHotKey(hwnd, testId);
            return new HotkeyRegistrationResult(true);
        }
        else
        {
            return new HotkeyRegistrationResult(false, $"ホットキー {binding} は他のアプリで使用中か、システムで予約されています");
        }
    }

    private HotkeyRegistrationResult RegisterHotkey(int hotkeyId, HotkeyBinding binding, Action callback, string description)
    {
        if (_hwndSource == null)
            return new HotkeyRegistrationResult(false, "HotkeyManager not initialized");

        if (binding.IsEmpty)
            return new HotkeyRegistrationResult(true);

        var hwnd = _hwndSource.Handle;
        var modifiers = GetModifiers(binding);
        var vkCode = GetVirtualKeyCode(binding.Key);

        if (vkCode == 0)
        {
            _log.Warning("Invalid key for hotkey {Description}: {Key}", description, binding.Key);
            return new HotkeyRegistrationResult(false, $"無効なキー: {binding.Key}");
        }

        var result = NativeMethods.RegisterHotKey(hwnd, hotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, (uint)vkCode);
        if (result)
        {
            _callbacks[hotkeyId] = callback;
            _registeredBindings[hotkeyId] = binding.Clone();
            _log.Information("Hotkey [{Description}] {Binding} registered", description, binding);
            return new HotkeyRegistrationResult(true);
        }
        else
        {
            _log.Warning("Failed to register hotkey [{Description}] {Binding}", description, binding);
            return new HotkeyRegistrationResult(false, $"ホットキー {binding} の登録に失敗しました（他のアプリで使用中の可能性）");
        }
    }

    private void UnregisterHotkey(int hotkeyId)
    {
        if (_hwndSource == null) return;

        if (_callbacks.ContainsKey(hotkeyId))
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, hotkeyId);
            if (_registeredBindings.TryGetValue(hotkeyId, out var binding))
            {
                _log.Information("Hotkey {Binding} unregistered", binding);
            }
            _callbacks.Remove(hotkeyId);
            _registeredBindings.Remove(hotkeyId);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            if (_callbacks.TryGetValue(hotkeyId, out var callback))
            {
                callback();
                handled = true;
            }
        }
        return 0;
    }

    private static uint GetModifiers(HotkeyBinding binding)
    {
        uint mods = 0;
        if (binding.Ctrl) mods |= NativeMethods.MOD_CONTROL;
        if (binding.Alt) mods |= NativeMethods.MOD_ALT;
        if (binding.Shift) mods |= NativeMethods.MOD_SHIFT;
        if (binding.Win) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    /// <summary>
    /// Convert a key string to a virtual key code.
    /// </summary>
    public static int GetVirtualKeyCode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        // Try parsing as a Key enum first
        if (Enum.TryParse<Key>(key, ignoreCase: true, out var wpfKey))
        {
            return KeyInterop.VirtualKeyFromKey(wpfKey);
        }

        // Handle single character keys
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z')
                return c; // VK_A to VK_Z are same as ASCII
            if (c >= '0' && c <= '9')
                return c; // VK_0 to VK_9 are same as ASCII
        }

        // Handle function keys
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key.AsSpan(1), out var fNum) && fNum >= 1 && fNum <= 24)
        {
            return 0x70 + fNum - 1; // VK_F1 = 0x70
        }

        // Handle special keys
        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "ESCAPE" or "ESC" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PRINTSCREEN" or "PRTSC" => 0x2C,
            "PAUSE" => 0x13,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            "CAPSLOCK" => 0x14,
            _ => 0
        };
    }

    /// <summary>
    /// Get the key string from a WPF Key value.
    /// </summary>
    public static string GetKeyString(Key key)
    {
        return key switch
        {
            Key.None => "",
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + ((int)key - (int)Key.NumPad0),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => key.ToString()
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwndSource != null)
        {
            // Unregister all hotkeys
            foreach (var hotkeyId in _callbacks.Keys.ToList())
            {
                NativeMethods.UnregisterHotKey(_hwndSource.Handle, hotkeyId);
            }
            _callbacks.Clear();
            _registeredBindings.Clear();
            _profileHotkeyIds.Clear();

            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }
}
