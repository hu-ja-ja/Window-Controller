using System.Windows;
using Wpf.Ui.Controls;

namespace WindowController.App;

/// <summary>
/// Confirmation dialog that displays monitor-transform warnings before applying
/// a profile to a different monitor.  Returns <c>true</c> via DialogResult
/// if the user chooses to proceed.
/// </summary>
public partial class MonitorWarningDialog : FluentWindow
{
    public MonitorWarningDialog(string monitorDescription, IReadOnlyList<string> warnings)
    {
        InitializeComponent();
        MonitorInfoText.Text = monitorDescription;
        WarningList.ItemsSource = warnings;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
