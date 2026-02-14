using System.Windows;
using System.Windows.Interop;
using WindowController.Win32;

namespace WindowController.App;

/// <summary>
/// Semi-transparent overlay window that shows a large monitor number.
/// One instance is placed on each physical monitor while the monitor picker is open.
/// </summary>
public partial class MonitorOverlayWindow : Window
{
    private readonly int _monX, _monY, _monW, _monH;

    public MonitorOverlayWindow(int number, string info,
        int monX, int monY, int monW, int monH)
    {
        InitializeComponent();
        NumberText.Text = number.ToString();
        InfoText.Text = info;
        _monX = monX;
        _monY = monY;
        _monW = monW;
        _monH = monH;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Position the overlay centered on the target monitor using physical pixel coordinates.
    /// Done in SourceInitialized to avoid flicker (before the window is first rendered).
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        const int owPx = 400, ohPx = 320;

        // WPF sizes are in DIPs; SetWindowPos expects device pixels.
        // Set Width/Height so the physical size matches owPx/ohPx at current DPI.
        var source = HwndSource.FromHwnd(hwnd);
        var m = source?.CompositionTarget?.TransformToDevice;
        var scaleX = m?.M11 ?? 1.0;
        var scaleY = m?.M22 ?? 1.0;
        if (scaleX <= 0) scaleX = 1.0;
        if (scaleY <= 0) scaleY = 1.0;
        Width = owPx / scaleX;
        Height = ohPx / scaleY;

        int cx = _monX + (_monW - owPx) / 2;
        int cy = _monY + (_monH - ohPx) / 2;
        NativeMethods.SetWindowPos(hwnd, 0, cx, cy, owPx, ohPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }
}
