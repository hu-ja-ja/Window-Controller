using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace WindowController.App;

public partial class MainWindow : FluentWindow
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

    /// <summary>
    /// Prevent inner DataGrid scroll from bubbling to the outer ScrollViewer.
    /// </summary>
    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid dg) return;

        var sv = FindVisualChild<ScrollViewer>(dg);
        if (sv is null) return;

        if (e.Delta > 0)
            for (int i = 0; i < SystemParameters.WheelScrollLines; i++) sv.LineUp();
        else
            for (int i = 0; i < SystemParameters.WheelScrollLines; i++) sv.LineDown();

        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}