using System.Windows;

namespace WindowController.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Hide instead of close (tray resident)
        e.Cancel = true;
        Hide();
    }
}