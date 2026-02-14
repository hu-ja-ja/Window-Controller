using System.Windows;
using System.Windows.Input;
using WindowController.Win32;
using Wpf.Ui.Controls;

namespace WindowController.App;

/// <summary>
/// Item shown in the desktop picker list.
/// </summary>
public class DesktopPickerItem
{
    public int Number { get; init; }
    public Guid DesktopId { get; init; }
    public string NumberLabel => $"{Number}:";
    public string DisplayName { get; init; } = "";
    public string CurrentBadge { get; init; } = "";
}

/// <summary>
/// Modal dialog that lists available virtual desktops and lets the user pick one.
/// Supports keyboard shortcuts (1–9, NumPad, Escape).
/// </summary>
public partial class DesktopPickerWindow : FluentWindow
{
    private readonly List<DesktopPickerItem> _items;
    public Guid? SelectedDesktopId { get; private set; }

    public DesktopPickerWindow(
        List<VirtualDesktopService.VirtualDesktopInfo> desktops,
        Guid? currentDesktopId,
        string monitorDescription)
    {
        InitializeComponent();
        MonitorInfoText.Text = monitorDescription;

        _items = desktops.Select(d => new DesktopPickerItem
        {
            Number = d.Number,
            DesktopId = d.Id,
            DisplayName = string.IsNullOrEmpty(d.Name)
                ? $"デスクトップ {d.Number}"
                : d.Name,
            CurrentBadge = d.Id == currentDesktopId ? "(現在)" : ""
        }).ToList();
        DesktopList.ItemsSource = _items;
    }

    private void DesktopButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DesktopPickerItem item)
        {
            SelectedDesktopId = item.DesktopId;
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
            SelectedDesktopId = _items[num - 1].DesktopId;
            DialogResult = true;
            e.Handled = true;
        }
    }
}
