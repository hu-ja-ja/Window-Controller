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
        const int ow = 400, oh = 320;
        int cx = _monX + (_monW - ow) / 2;
        int cy = _monY + (_monH - oh) / 2;
        NativeMethods.SetWindowPos(hwnd, 0, cx, cy, ow, oh,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }
}
