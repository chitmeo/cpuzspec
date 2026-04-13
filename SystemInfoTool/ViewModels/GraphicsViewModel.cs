using System.Collections.ObjectModel;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Application;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// ViewModel for the Graphics tab.
/// Loads static GPU info on construction and updates live sensor data on each
/// <see cref="SensorPollingService.OnSensorUpdate"/> event.
/// Supports multi-GPU systems via <see cref="GpuList"/> / <see cref="SelectedGpuIndex"/>.
/// </summary>
public sealed class GraphicsViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly GraphicsInfoService _graphicsInfoService;
    private readonly SensorPollingService _pollingService;

    private IReadOnlyList<GpuStaticInfo> _allGpus = [];
    private GpuSensorData? _sensorData;
    private int _selectedGpuIndex;
    private string? _errorMessage;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and begins loading static GPU information.
    /// </summary>
    public GraphicsViewModel(GraphicsInfoService graphicsInfoService, SensorPollingService pollingService)
    {
        _graphicsInfoService = graphicsInfoService ?? throw new ArgumentNullException(nameof(graphicsInfoService));
        _pollingService      = pollingService      ?? throw new ArgumentNullException(nameof(pollingService));

        _pollingService.OnSensorUpdate += OnSensorUpdate;

        _ = LoadStaticInfoAsync();
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Observable collection of static GPU info for all detected adapters.
    /// Bound to the GPU selector ComboBox.
    /// </summary>
    public ObservableCollection<GpuStaticInfo> GpuList { get; } = [];

    /// <summary>
    /// Index of the currently selected GPU in <see cref="GpuList"/>.
    /// Changing this updates <see cref="SensorData"/> to the matching adapter's readings.
    /// </summary>
    public int SelectedGpuIndex
    {
        get => _selectedGpuIndex;
        set => SetProperty(ref _selectedGpuIndex, value);
    }

    /// <summary>Live sensor readings for the currently selected GPU.</summary>
    public GpuSensorData? SensorData
    {
        get => _sensorData;
        private set => SetProperty(ref _sensorData, value);
    }

    /// <summary>Non-null when data could not be loaded; bound to the error panel.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // -------------------------------------------------------------------------
    // Data loading
    // -------------------------------------------------------------------------

    private async Task LoadStaticInfoAsync()
    {
        ErrorMessage = null;
        try
        {
            _allGpus = await _graphicsInfoService.GetStaticInfoAsync();

            GpuList.Clear();
            foreach (var gpu in _allGpus)
                GpuList.Add(gpu);

            _selectedGpuIndex = 0;
            OnPropertyChanged(nameof(SelectedGpuIndex));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load graphics information: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // Sensor update
    // -------------------------------------------------------------------------

    private void OnSensorUpdate(object? sender, SensorSnapshot snapshot)
    {
        if (snapshot.GpuSensors.Count == 0) return;

        int idx = Math.Clamp(_selectedGpuIndex, 0, snapshot.GpuSensors.Count - 1);
        SensorData = snapshot.GpuSensors[idx];
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    /// <summary>Unsubscribes from the polling service.</summary>
    public void Dispose()
    {
        _pollingService.OnSensorUpdate -= OnSensorUpdate;
    }
}
