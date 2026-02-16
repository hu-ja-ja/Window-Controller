using System.Windows;
using Wpf.Ui.Controls;

namespace WindowController.App;

/// <summary>
/// Dialog shown when no default profiles are configured.
/// Returns <c>true</c> via DialogResult when the user chooses to open settings.
/// </summary>
public partial class DefaultProfilesMissingDialog : FluentWindow
{
    public DefaultProfilesMissingDialog()
    {
        InitializeComponent();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
