using System.Windows;
using SystemInfoTool.ViewModels;

namespace SystemInfoTool;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// Handles window state changes to drive tray minimise behaviour.
/// All other logic lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// When the window is minimised, delegate to TrayService via MainViewModel.
    /// </summary>
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized &&
            DataContext is MainViewModel vm)
        {
            vm.MinimizeToTray();
        }
    }
}
