using System.Windows;
using System.Windows.Input;
using WindowController.Win32;
using Wpf.Ui.Controls;

namespace WindowController.App;

/// <summary>
/// Item shown in the monitor picker list.
/// </summary>
public class MonitorPickerItem
{
    public int Number { get; init; }
    public MonitorData MonitorData { get; init; } = null!;
    public string Label { get; init; } = "";
}

/// <summary>
/// Modal dialog that lists available monitors and lets the user pick one.
/// Supports keyboard shortcuts (1–9, NumPad, Escape).
/// Uses FluentWindow (Mica + TitleBar) to match the rest of the app.
/// </summary>
public partial class MonitorPickerWindow : FluentWindow
{
    private readonly List<MonitorPickerItem> _items;
    public MonitorData? SelectedMonitor { get; private set; }

    public MonitorPickerWindow(List<MonitorData> monitors)
    {
        InitializeComponent();

        // Start offscreen to avoid flicker; reposition in Loaded
        Left = -10000;
        Top = -10000;

        _items = monitors.Select((m, i) => new MonitorPickerItem
        {
            Number = i + 1,
            MonitorData = m,
            Label = FormatLabel(i + 1, m)
        }).ToList();
        MonitorList.ItemsSource = _items;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Position the dialog at the bottom-center of the primary work area
    /// so it does not overlap with the numbered overlay windows that appear
    /// at the center of each monitor.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 40;
    }

    private static string FormatLabel(int num, MonitorData m)
    {
        var name = m.DeviceName;
        if (name.StartsWith(@"\\.\"))
            name = name.Substring(4);
        return $"{num}:  {name}  ({m.PixelWidth}\u00d7{m.PixelHeight})";
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MonitorPickerItem item)
        {
            SelectedMonitor = item.MonitorData;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        int num = -1;
        if (e.Key >= Key.D1 && e.Key <= Key.D9)
            num = e.Key - Key.D0;
        else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
            num = e.Key - Key.NumPad0;
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (num >= 1 && num <= _items.Count)
        {
            SelectedMonitor = _items[num - 1].MonitorData;
            DialogResult = true;
            e.Handled = true;
        }
    }
}
