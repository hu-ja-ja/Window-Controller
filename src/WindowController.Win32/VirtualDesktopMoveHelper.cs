using System.Runtime.InteropServices;
using Serilog;

namespace WindowController.Win32;

/// <summary>
/// Uses undocumented internal shell COM interfaces to move windows between
/// virtual desktops without the per-process restriction of the public API.
///
/// The public IVirtualDesktopManager::MoveWindowToDesktop returns E_ACCESSDENIED
/// (0x80070005) for windows that don't belong to the calling process.
/// This helper uses the shell's internal IVirtualDesktopManagerInternal which
/// bypasses that restriction.
///
/// Each Windows build changes the IVirtualDesktopManagerInternal IID and
/// may insert/remove vtable methods. We declare a separate [ComImport] interface
/// per build family so the CLR handles vtable dispatch safely. If the IID
/// doesn't match, QueryService fails cleanly instead of crashing.
/// </summary>
internal static class VirtualDesktopMoveHelper
{
    // ── Stable GUIDs ──

    private static readonly Guid CLSID_ImmersiveShell =
        new("C2F03A33-21F5-47FA-B4BB-156362A2F239");

    private static readonly Guid GUID_VdmInternalService =
        new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    private static readonly Guid GUID_AppViewCollection =
        new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    // ── Stable COM helpers ──

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider10
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
    }

    // ── IApplicationViewCollection (stable across 21H2 – 24H2) ──

    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int _Stub_GetViews();                // slot 3
        [PreserveSig] int _Stub_GetViewsByZOrder();        // slot 4
        [PreserveSig] int _Stub_GetViewsByAppUserModelId();// slot 5
        [PreserveSig] int GetViewForHwnd(nint hwnd, out nint ppView); // slot 6
    }

    // ── IVirtualDesktopManagerInternal — 24H2 (Build 26100.2033+) ──
    //
    // Vtable layout (methods start at IUnknown slot 3):
    //   3: GetCount
    //   4: MoveViewToDesktop(view, desktop)
    //   5: CanViewMoveDesktops
    //   6: GetCurrentDesktop
    //   7: GetAllCurrentDesktops  ← NEW in 24H2
    //   8: GetDesktops
    //   9: GetAdjacentDesktop
    //  10: SwitchDesktop
    //  11: SwitchDesktopAndMoveForegroundView  ← added in 26100.2033 (KB5044384)
    //  12: CreateDesktop
    //  13: MoveDesktop
    //  14: RemoveDesktop
    //  15: FindDesktop(ref Guid, out desktop)

    [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVdmInternal_24H2
    {
        [PreserveSig] int _GetCount();                // 3
        [PreserveSig] int MoveViewToDesktop(nint pView, nint pDesktop); // 4
        [PreserveSig] int _CanViewMoveDesktops();     // 5
        [PreserveSig] int _GetCurrentDesktop();       // 6
        [PreserveSig] int _GetAllCurrentDesktops();   // 7  (24H2 only)
        [PreserveSig] int _GetDesktops();             // 8
        [PreserveSig] int _GetAdjacentDesktop();      // 9
        [PreserveSig] int _SwitchDesktop();           // 10
        [PreserveSig] int _SwitchDesktopAndMoveForegroundView(); // 11 (26100.2033+)
        [PreserveSig] int _CreateDesktop();           // 12
        [PreserveSig] int _MoveDesktop();             // 13
        [PreserveSig] int _RemoveDesktop();           // 14
        [PreserveSig] int FindDesktop(ref Guid desktopId, out nint ppDesktop); // 15
    }

    // ── IVirtualDesktopManagerInternal — 22H2 / 23H2 (Build 22621–22631) ──
    //
    //  Same layout minus GetAllCurrentDesktops, so FindDesktop is at slot 13.

    [ComImport, Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVdmInternal_22H2
    {
        [PreserveSig] int _GetCount();                // 3
        [PreserveSig] int MoveViewToDesktop(nint pView, nint pDesktop); // 4
        [PreserveSig] int _CanViewMoveDesktops();     // 5
        [PreserveSig] int _GetCurrentDesktop();       // 6
        // NO GetAllCurrentDesktops in 22H2
        [PreserveSig] int _GetDesktops();             // 7
        [PreserveSig] int _GetAdjacentDesktop();      // 8
        [PreserveSig] int _SwitchDesktop();           // 9
        [PreserveSig] int _CreateDesktop();           // 10
        [PreserveSig] int _MoveDesktop();             // 11
        [PreserveSig] int _RemoveDesktop();           // 12
        [PreserveSig] int FindDesktop(ref Guid desktopId, out nint ppDesktop); // 13
    }

    // ── Public API ──

    /// <summary>
    /// Move a window to the specified virtual desktop using undocumented internal COM.
    /// Tries each known build config; returns true on the first successful move.
    /// </summary>
    /// <remarks>
    /// All COM operations are dispatched to a dedicated MTA thread.
    /// WPF runs on STA, and the shell's internal interfaces have no registered
    /// proxy/stub — calling through a cross-apartment proxy causes
    /// AccessViolationException. Running in MTA avoids the proxy entirely.
    /// </remarks>
    public static bool TryMoveWindowToDesktop(nint hwnd, Guid desktopId, ILogger log)
    {
        bool result = false;
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = TryMoveCore(hwnd, desktopId, log);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        })
        {
            IsBackground = true,
            Name = "VDMoveHelper-MTA",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();

        // Avoid blocking indefinitely in case the shell COM call hangs.
        var completed = thread.Join(TimeSpan.FromSeconds(5));
        if (!completed)
        {
            log.Warning("VDMoveHelper: MTA thread did not complete within the timeout; aborting move");
            return false;
        }

        if (threadEx != null)
            log.Warning(threadEx, "VDMoveHelper: MTA thread error");

        return result;
    }

    /// <summary>
    /// Core implementation — MUST run on an MTA thread.
    /// </summary>
    private static bool TryMoveCore(nint hwnd, Guid desktopId, ILogger log)
    {
        object? shellObj = null;
        object? avcObj = null;

        try
        {
            var shellType = Type.GetTypeFromCLSID(CLSID_ImmersiveShell);
            if (shellType == null)
            {
                log.Debug("VDMoveHelper: ImmersiveShell CLSID not registered");
                return false;
            }

            shellObj = Activator.CreateInstance(shellType);
            if (shellObj is not IServiceProvider10 sp)
            {
                log.Debug("VDMoveHelper: shell does not implement IServiceProvider");
                return false;
            }

            // 1. Get IApplicationViewCollection
            var guidAvc = GUID_AppViewCollection;
            int hr = sp.QueryService(ref guidAvc, ref guidAvc, out avcObj);
            if (hr != 0 || avcObj is not IApplicationViewCollection avc)
            {
                log.Debug("VDMoveHelper: IApplicationViewCollection not available, hr=0x{Hr:X8}", hr);
                return false;
            }

            // 2. Get the IApplicationView for the target window
            hr = avc.GetViewForHwnd(hwnd, out nint pView);
            if (hr != 0 || pView == 0)
            {
                log.Debug("VDMoveHelper: GetViewForHwnd failed, hr=0x{Hr:X8}", hr);
                return false;
            }

            try
            {
                // 3. Try 24H2 first, then 22H2
                if (TryWith24H2(sp, pView, desktopId, log))
                    return true;
                if (TryWith22H2(sp, pView, desktopId, log))
                    return true;
            }
            finally
            {
                Marshal.Release(pView);
            }

            log.Warning("VDMoveHelper: no matching internal interface for this Windows build");
            return false;
        }
        catch (InvalidCastException)
        {
            log.Warning("VDMoveHelper: COM interface cast failed — unsupported build");
            return false;
        }
        catch (COMException ex)
        {
            log.Warning("VDMoveHelper: COM error 0x{Hr:X8}", ex.HResult);
            return false;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "VDMoveHelper: unexpected error");
            return false;
        }
        finally
        {
            if (avcObj != null) Marshal.ReleaseComObject(avcObj);
            if (shellObj != null) Marshal.ReleaseComObject(shellObj);
        }
    }

    // ── Build-specific helpers ──

    private static bool TryWith24H2(IServiceProvider10 sp, nint pView, Guid desktopId, ILogger log)
    {
        object? vdmObj = null;
        try
        {
            var svc = GUID_VdmInternalService;
            var iid = typeof(IVdmInternal_24H2).GUID;
            if (sp.QueryService(ref svc, ref iid, out vdmObj) != 0 || vdmObj == null)
                return false;

            if (vdmObj is not IVdmInternal_24H2 vdm)
                return false;

            log.Debug("VDMoveHelper: matched 24H2 interface");
            return DoMove(vdm, pView, desktopId, log);
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (vdmObj != null) Marshal.ReleaseComObject(vdmObj);
        }
    }

    private static bool TryWith22H2(IServiceProvider10 sp, nint pView, Guid desktopId, ILogger log)
    {
        object? vdmObj = null;
        try
        {
            var svc = GUID_VdmInternalService;
            var iid = typeof(IVdmInternal_22H2).GUID;
            if (sp.QueryService(ref svc, ref iid, out vdmObj) != 0 || vdmObj == null)
                return false;

            if (vdmObj is not IVdmInternal_22H2 vdm)
                return false;

            log.Debug("VDMoveHelper: matched 22H2 interface");
            return DoMove(vdm, pView, desktopId, log);
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (vdmObj != null) Marshal.ReleaseComObject(vdmObj);
        }
    }

    /// <summary>
    /// FindDesktop + MoveViewToDesktop — generic over both build interfaces.
    /// </summary>
    private static bool DoMove(IVdmInternal_24H2 vdm, nint pView, Guid desktopId, ILogger log)
    {
        nint pDesktop = 0;
        try
        {
            int hr = vdm.FindDesktop(ref desktopId, out pDesktop);
            if (hr != 0 || pDesktop == 0)
            {
                log.Debug("VDMoveHelper: FindDesktop hr=0x{Hr:X8}", hr);
                return false;
            }

            hr = vdm.MoveViewToDesktop(pView, pDesktop);
            if (hr != 0)
            {
                log.Debug("VDMoveHelper: MoveViewToDesktop hr=0x{Hr:X8}", hr);
                return false;
            }

            log.Debug("VDMoveHelper: moved window to desktop {Desktop}", desktopId);
            return true;
        }
        finally
        {
            if (pDesktop != 0) Marshal.Release(pDesktop);
        }
    }

    private static bool DoMove(IVdmInternal_22H2 vdm, nint pView, Guid desktopId, ILogger log)
    {
        nint pDesktop = 0;
        try
        {
            int hr = vdm.FindDesktop(ref desktopId, out pDesktop);
            if (hr != 0 || pDesktop == 0)
            {
                log.Debug("VDMoveHelper: FindDesktop hr=0x{Hr:X8}", hr);
                return false;
            }

            hr = vdm.MoveViewToDesktop(pView, pDesktop);
            if (hr != 0)
            {
                log.Debug("VDMoveHelper: MoveViewToDesktop hr=0x{Hr:X8}", hr);
                return false;
            }

            log.Debug("VDMoveHelper: moved window to desktop {Desktop}", desktopId);
            return true;
        }
        finally
        {
            if (pDesktop != 0) Marshal.Release(pDesktop);
        }
    }
}
