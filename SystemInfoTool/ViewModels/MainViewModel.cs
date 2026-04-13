using System.Windows;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Application;

using Application = System.Windows.Application;
using MessageBox = System.Windows.Forms.MessageBox;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// Top-level ViewModel for the main window.
/// Owns tab selection, refresh interval, snapshot export, and tray/theme lifecycle.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly SensorPollingService _pollingService;
    private readonly SnapshotExportService _exportService;
    private readonly TrayService _trayService;
    private readonly ThemeService _themeService;

    private int _selectedTabIndex;
    private int _refreshIntervalMs = 1_000;
    private bool _isBusy;
    private bool _disposed;

    // Child ViewModels — set by the application wiring layer
    private CpuViewModel? _cpuViewModel;
    private MotherboardViewModel? _motherboardViewModel;
    private MemoryViewModel? _memoryViewModel;
    private GraphicsViewModel? _graphicsViewModel;
    private StorageViewModel? _storageViewModel;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and wires tray events.
    /// </summary>
    public MainViewModel(
        SensorPollingService pollingService,
        SnapshotExportService exportService,
        TrayService trayService,
        ThemeService themeService)
    {
        _pollingService = pollingService ?? throw new ArgumentNullException(nameof(pollingService));
        _exportService  = exportService  ?? throw new ArgumentNullException(nameof(exportService));
        _trayService    = trayService    ?? throw new ArgumentNullException(nameof(trayService));
        _themeService   = themeService   ?? throw new ArgumentNullException(nameof(themeService));

        // Wire tray events
        _trayService.RestoreRequested += OnRestoreRequested;
        _trayService.ExitRequested    += OnExitRequested;

        // Commands
        ExportSnapshotCommand = new RelayCommand(ExecuteExportSnapshot, () => !IsBusy);
        ExitCommand           = new RelayCommand(ExecuteExit);
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Index of the currently selected tab (0 = CPU, 1 = Motherboard, …).</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    /// <summary>
    /// Sensor polling interval in milliseconds (clamped to [500, 10 000]).
    /// Changing this property immediately updates the polling service.
    /// </summary>
    public int RefreshIntervalMs
    {
        get => _refreshIntervalMs;
        set
        {
            int clamped = SensorPollingService.ClampInterval(value);
            if (SetProperty(ref _refreshIntervalMs, clamped))
                _pollingService.SetInterval(TimeSpan.FromMilliseconds(clamped));
        }
    }

    /// <summary>True while an async operation (e.g. export) is in progress.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ExportSnapshotCommand.RaiseCanExecuteChanged();
        }
    }

    // -------------------------------------------------------------------------
    // Child ViewModel properties (set by App wiring after construction)
    // -------------------------------------------------------------------------

    /// <summary>ViewModel for the CPU tab.</summary>
    public CpuViewModel? CpuViewModel
    {
        get => _cpuViewModel;
        set => SetProperty(ref _cpuViewModel, value);
    }

    /// <summary>ViewModel for the Motherboard tab.</summary>
    public MotherboardViewModel? MotherboardViewModel
    {
        get => _motherboardViewModel;
        set => SetProperty(ref _motherboardViewModel, value);
    }

    /// <summary>ViewModel for the Memory tab.</summary>
    public MemoryViewModel? MemoryViewModel
    {
        get => _memoryViewModel;
        set => SetProperty(ref _memoryViewModel, value);
    }

    /// <summary>ViewModel for the Graphics tab.</summary>
    public GraphicsViewModel? GraphicsViewModel
    {
        get => _graphicsViewModel;
        set => SetProperty(ref _graphicsViewModel, value);
    }

    /// <summary>ViewModel for the Storage tab.</summary>
    public StorageViewModel? StorageViewModel
    {
        get => _storageViewModel;
        set => SetProperty(ref _storageViewModel, value);
    }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    /// <summary>Triggers a hardware snapshot export via <see cref="SnapshotExportService"/>.</summary>
    public RelayCommand ExportSnapshotCommand { get; }

    /// <summary>Exits the application cleanly.</summary>
    public RelayCommand ExitCommand { get; }

    // -------------------------------------------------------------------------
    // Command implementations
    // -------------------------------------------------------------------------

    private async void ExecuteExportSnapshot()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // Gather current hardware data from child ViewModels via the
            // ExportDataProvider delegate if wired, otherwise export empty arrays.
            var (cpuInfos, motherboard, memory, gpus, drives) = GetExportData();

            string? path = await _exportService.ExportAsync(cpuInfos, motherboard, memory, gpus, drives);

            if (path is not null)
            {
                MessageBox.Show(
                    $"Snapshot saved to:\n{path}",
                    "Export Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ExecuteExit()
    {
        Application.Current.Shutdown();
    }

    // -------------------------------------------------------------------------
    // Export data provider
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional delegate that supplies the current hardware data for export.
    /// Set by the application wiring layer after all child ViewModels are created.
    /// When null, empty arrays are used.
    /// </summary>
    public Func<(CpuStaticInfo[], MotherboardInfo, MemoryInfo, GpuStaticInfo[], StorageDriveInfo[])>?
        ExportDataProvider { get; set; }

    private (CpuStaticInfo[], MotherboardInfo, MemoryInfo, GpuStaticInfo[], StorageDriveInfo[]) GetExportData()
    {
        if (ExportDataProvider is not null)
            return ExportDataProvider();

        // Fallback: empty / sentinel data
        var emptyCpu = Array.Empty<CpuStaticInfo>();
        var emptyMotherboard = new MotherboardInfo(
            "Unavailable", "Unavailable", "Unavailable",
            "Unavailable", "Unavailable", "Unavailable",
            null, 0, 0);
        var emptyMemory = new MemoryInfo(0, "Unavailable", "Unknown", 0, null, []);
        var emptyGpus = Array.Empty<GpuStaticInfo>();
        var emptyDrives = Array.Empty<StorageDriveInfo>();

        return (emptyCpu, emptyMotherboard, emptyMemory, emptyGpus, emptyDrives);
    }

    // -------------------------------------------------------------------------
    // Tray integration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by <see cref="MainWindow"/> when the window is minimised.
    /// Delegates to <see cref="TrayService"/> and suspends sensor polling.
    /// </summary>
    public void MinimizeToTray()
    {
        _trayService.MinimizeToTray();
        _pollingService.Stop();
    }

    // -------------------------------------------------------------------------
    // Tray event handlers
    // -------------------------------------------------------------------------

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        _trayService.RestoreFromTray();
        _pollingService.Start();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        ExecuteExit();
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trayService.RestoreRequested -= OnRestoreRequested;
        _trayService.ExitRequested    -= OnExitRequested;
    }
}
