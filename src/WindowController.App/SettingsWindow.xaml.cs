using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WindowController.App.ViewModels;
using Wpf.Ui.Controls;

namespace WindowController.App;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Hide instead of close (tray resident app)
        e.Cancel = true;
        Hide();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            if (vm.IsCapturingGuiHotkey)
            {
                vm.OnGuiHotkeyKeyDown(e);
            }
            else if (vm.IsCapturingProfileHotkey)
            {
                vm.OnProfileHotkeyKeyDown(e);
            }
        }
    }
}

/// <summary>
/// Converter to display "設定…" or "入力待ち…" based on capturing state.
/// </summary>
public class BoolToCapturingTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "入力待ち…" : "設定…";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
