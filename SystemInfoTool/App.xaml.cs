using System.IO;
using System.Windows;
using System.Windows.Forms;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Application;
using SystemInfoTool.Services.Hardware;
using SystemInfoTool.ViewModels;

using MessageBox = System.Windows.Forms.MessageBox;

namespace SystemInfoTool;

/// <summary>
/// Application entry point and service wiring.
/// Handles startup, DPI configuration, theme application, service instantiation,
/// ViewModel injection, and clean shutdown.
/// </summary>
public partial class App : System.Windows.Application
{
    // -------------------------------------------------------------------------
    // Services — owned by App, disposed on shutdown
    // -------------------------------------------------------------------------

    private ThemeService? _themeService;
    private SensorPollingService? _pollingService;
    private TrayService? _trayService;
    private SettingsService? _settingsService;
    private AppSettings _appSettings = new(RefreshIntervalMs: 1000,
                                           LastWindowPlacement: new WindowPlacement(100, 100, 900, 650));

    // -------------------------------------------------------------------------
    // Startup
    // -------------------------------------------------------------------------

    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        // 1. Configure PerMonitorV2 DPI awareness before any window is created.
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // 2. Register global unhandled-exception handler.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 3. Load persisted settings (falls back to defaults on any error).
        _settingsService = new SettingsService();
        _appSettings = await _settingsService.LoadAsync();

        // 4. Instantiate application services.
        _themeService = new ThemeService();

        var cpuInfoService        = new CpuInfoService();
        var graphicsInfoService   = new GraphicsInfoService();
        var motherboardInfoService = new MotherboardInfoService();
        var memoryInfoService     = new MemoryInfoService();
        var storageInfoService    = new StorageInfoService();
        var exportService         = new SnapshotExportService();

        _pollingService = new SensorPollingService(cpuInfoService, graphicsInfoService);

        // 5. Apply theme before the main window is shown.
        _themeService.ApplyTheme();

        // 6. Create the main window (does NOT show it yet).
        var mainWindow = new MainWindow();

        // 7. Instantiate TrayService now that we have a window reference.
        _trayService = new TrayService(mainWindow);

        // 8. Instantiate child ViewModels.
        var cpuViewModel        = new CpuViewModel(cpuInfoService, _pollingService);
        var motherboardViewModel = new MotherboardViewModel(motherboardInfoService);
        var memoryViewModel     = new MemoryViewModel(memoryInfoService);
        var graphicsViewModel   = new GraphicsViewModel(graphicsInfoService, _pollingService);
        var storageViewModel    = new StorageViewModel(storageInfoService);

        // 9. Instantiate MainViewModel and inject all dependencies.
        var mainViewModel = new MainViewModel(
            _pollingService,
            exportService,
            _trayService,
            _themeService);

        // 10. Wire child ViewModels into MainViewModel.
        mainViewModel.CpuViewModel        = cpuViewModel;
        mainViewModel.MotherboardViewModel = motherboardViewModel;
        mainViewModel.MemoryViewModel     = memoryViewModel;
        mainViewModel.GraphicsViewModel   = graphicsViewModel;
        mainViewModel.StorageViewModel    = storageViewModel;

        // 11. Wire the export data provider so the snapshot includes live data.
        mainViewModel.ExportDataProvider = () =>
        {
            var cpuInfos = cpuViewModel.StaticInfo is not null
                ? new[] { cpuViewModel.StaticInfo }
                : Array.Empty<CpuStaticInfo>();

            var motherboard = motherboardViewModel.Info
                ?? new MotherboardInfo("Unavailable", "Unavailable", "Unavailable",
                                       "Unavailable", "Unavailable", "Unavailable",
                                       null, 0, 0);

            var memory = memoryViewModel.Info
                ?? new MemoryInfo(0, "Unavailable", "Unknown", 0, null, []);

            var gpus = graphicsViewModel.GpuList.ToArray();
            var drives = storageViewModel.Drives.ToArray();

            return (cpuInfos, motherboard, memory, gpus, drives);
        };

        // 12. Apply saved refresh interval.
        mainViewModel.RefreshIntervalMs = _appSettings.RefreshIntervalMs;

        // 13. Restore window placement from settings.
        var placement = _appSettings.LastWindowPlacement;
        mainWindow.Left   = placement.Left;
        mainWindow.Top    = placement.Top;
        mainWindow.Width  = placement.Width;
        mainWindow.Height = placement.Height;

        // 14. Set DataContext and show the main window.
        mainWindow.DataContext = mainViewModel;
        MainWindow = mainWindow;
        mainWindow.Show();

        // 15. Start sensor polling after the window is visible.
        _pollingService.Start();
    }

    // -------------------------------------------------------------------------
    // Clean shutdown
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs an orderly shutdown: stops polling, saves settings, disposes services.
    /// Called on <see cref="TrayService.ExitRequested"/> (via <see cref="MainViewModel"/>)
    /// or when the OS terminates the application.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        // Stop sensor polling first to avoid callbacks during teardown.
        _pollingService?.Stop();
        _pollingService?.Dispose();

        // Persist current settings.
        if (_settingsService is not null)
        {
            try
            {
                // Capture window placement from TrayService if available,
                // otherwise read directly from the main window.
                WindowPlacement placement;
                if (_trayService?.SavedPlacement is { } saved)
                {
                    placement = saved;
                }
                else if (MainWindow is MainWindow mw)
                {
                    placement = new WindowPlacement(mw.Left, mw.Top, mw.Width, mw.Height);
                }
                else
                {
                    placement = _appSettings.LastWindowPlacement;
                }

                var settings = new AppSettings(
                    RefreshIntervalMs: (MainWindow?.DataContext as MainViewModel)?.RefreshIntervalMs
                                       ?? _appSettings.RefreshIntervalMs,
                    LastWindowPlacement: placement);

                await _settingsService.SaveAsync(settings);
            }
            catch (IOException) { /* best-effort — don't block shutdown */ }
            catch (UnauthorizedAccessException) { /* best-effort */ }
        }

        // Dispose tray icon so it disappears from the system tray immediately.
        _trayService?.Dispose();

        // Dispose theme service (unsubscribes from SystemEvents).
        _themeService?.Dispose();

        base.OnExit(e);
    }

    // -------------------------------------------------------------------------
    // Global exception handler
    // -------------------------------------------------------------------------

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Log to a simple text file in %APPDATA%\SystemInfoTool\error.log
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SystemInfoTool");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "error.log");
            File.AppendAllText(logPath,
                $"[{DateTimeOffset.Now:O}] Unhandled exception: {e.Exception}\n\n");
        }
        catch { /* logging must never crash the app */ }

        // Show a non-fatal dialog so the user is aware of the error.
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will continue running.",
            "Unexpected Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);

        // Mark as handled so the application does not terminate.
        e.Handled = true;
    }
}
