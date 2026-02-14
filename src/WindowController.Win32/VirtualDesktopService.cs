using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace WindowController.Win32;

/// <summary>
/// COM interface IVirtualDesktopManager (public, documented).
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

/// <summary>
/// Safe wrapper around IVirtualDesktopManager COM object.
/// All methods swallow COM / RPC errors and return safe defaults.
/// </summary>
public class VirtualDesktopService : IDisposable
{
    private static readonly Guid CLSID_VirtualDesktopManager =
        new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    private readonly ILogger _log;
    private IVirtualDesktopManager? _manager;
    private bool _disposed;

    public VirtualDesktopService(ILogger logger)
    {
        _log = logger;
        try
        {
            var obj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(CLSID_VirtualDesktopManager)!);
            _manager = (IVirtualDesktopManager?)obj;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "VirtualDesktopManager COM init failed — virtual desktop features will be unavailable");
            _manager = null;
        }
    }

    /// <summary>Whether the underlying COM object was created successfully.</summary>
    public bool IsAvailable => _manager != null;

    /// <summary>
    /// Get the virtual desktop GUID for a window. Returns null on failure.
    /// </summary>
    public Guid? GetWindowDesktopId(nint hwnd)
    {
        if (_manager == null) return null;
        try
        {
            int hr = _manager.GetWindowDesktopId(hwnd, out var id);
            return hr == 0 ? id : null;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "GetWindowDesktopId failed for hwnd {Hwnd}", hwnd);
            return null;
        }
    }

    /// <summary>
    /// Check if a window is on the current virtual desktop. Returns null on failure.
    /// </summary>
    public bool? IsWindowOnCurrentDesktop(nint hwnd)
    {
        if (_manager == null) return null;
        try
        {
            int hr = _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
            return hr == 0 ? onCurrent : null;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "IsWindowOnCurrentVirtualDesktop failed for hwnd {Hwnd}", hwnd);
            return null;
        }
    }

    /// <summary>
    /// Move a window to another virtual desktop. Returns true on success.
    /// Falls back to undocumented internal COM when the public API returns
    /// E_ACCESSDENIED (cross-process restriction).
    /// </summary>
    public bool MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        if (_manager == null) return false;
        try
        {
            int hr = _manager.MoveWindowToDesktop(hwnd, ref desktopId);
            if (hr == 0) return true;

            // Public API only works for same-process windows.
            // For cross-process windows, fall back to the internal COM helper.
            if (hr == unchecked((int)0x80070005)) // E_ACCESSDENIED
            {
                _log.Debug("MoveWindowToDesktop E_ACCESSDENIED for hwnd {Hwnd} — trying internal COM", hwnd);
                return VirtualDesktopMoveHelper.TryMoveWindowToDesktop(hwnd, desktopId, _log);
            }

            _log.Warning("MoveWindowToDesktop failed for hwnd {Hwnd}, hr=0x{Hr:X8}", hwnd, hr);
            return false;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "MoveWindowToDesktop failed for hwnd {Hwnd}", hwnd);
            return false;
        }
    }

    /// <summary>
    /// Get the desktop Id of the "current" desktop by probing a known hwnd
    /// (typically the app's own main window).
    /// </summary>
    public Guid? GetCurrentDesktopId(nint appHwnd)
    {
        return GetWindowDesktopId(appHwnd);
    }

    /// <summary>
    /// Check if a window is "cloaked" (DWM), which usually means it's on another virtual desktop
    /// or is a hidden Store app.
    /// </summary>
    public static bool IsWindowCloaked(nint hwnd)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    // ── Registry-based desktop enumeration ──

    private const string VdRegRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>
    /// Information about a single virtual desktop.
    /// </summary>
    public record VirtualDesktopInfo(Guid Id, int Number, string Name);

    /// <summary>
    /// Enumerate all existing virtual desktops via the registry.
    /// Returns them in order.  Name is the user-assigned name (empty string if not renamed).
    /// </summary>
    public List<VirtualDesktopInfo> GetAllDesktops()
    {
        var result = new List<VirtualDesktopInfo>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VdRegRoot);
            if (key == null) return result;

            // VirtualDesktopIDs is a REG_BINARY containing concatenated 16-byte GUIDs.
            var raw = key.GetValue("VirtualDesktopIDs") as byte[];
            if (raw == null || raw.Length < 16) return result;

            int count = raw.Length / 16;
            for (int i = 0; i < count; i++)
            {
                var guid = new Guid(raw.AsSpan(i * 16, 16));
                var name = GetDesktopName(guid);
                result.Add(new VirtualDesktopInfo(guid, i + 1, name));
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "GetAllDesktops registry read failed");
        }
        return result;
    }

    /// <summary>
    /// Read the user-assigned name for a desktop from the registry.
    /// Returns empty string if not set.
    /// </summary>
    private static string GetDesktopName(Guid desktopId)
    {
        try
        {
            var subPath = $@"{VdRegRoot}\Desktops\{{{desktopId}}}";
            using var key = Registry.CurrentUser.OpenSubKey(subPath);
            return key?.GetValue("Name") as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_manager != null)
        {
            Marshal.ReleaseComObject(_manager);
            _manager = null;
        }
    }
}
