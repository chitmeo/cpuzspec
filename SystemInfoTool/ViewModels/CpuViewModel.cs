using System.Collections.ObjectModel;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Application;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// ViewModel for the CPU tab.
/// Loads static CPU info on construction and updates live sensor data on each
/// <see cref="SensorPollingService.OnSensorUpdate"/> event.
/// Supports multi-socket systems via <see cref="SocketList"/> / <see cref="SelectedSocketIndex"/>.
/// </summary>
public sealed class CpuViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly CpuInfoService _cpuInfoService;
    private readonly SensorPollingService _pollingService;

    private IReadOnlyList<CpuStaticInfo> _allSockets = [];
    private CpuStaticInfo? _staticInfo;
    private CpuSensorData? _sensorData;
    private int _selectedSocketIndex;
    private string? _errorMessage;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and begins loading static CPU information.
    /// </summary>
    public CpuViewModel(CpuInfoService cpuInfoService, SensorPollingService pollingService)
    {
        _cpuInfoService = cpuInfoService ?? throw new ArgumentNullException(nameof(cpuInfoService));
        _pollingService = pollingService ?? throw new ArgumentNullException(nameof(pollingService));

        _pollingService.OnSensorUpdate += OnSensorUpdate;

        // Fire-and-forget; errors are surfaced via ErrorMessage
        _ = LoadStaticInfoAsync();
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Static information for the currently selected CPU socket.</summary>
    public CpuStaticInfo? StaticInfo
    {
        get => _staticInfo;
        private set => SetProperty(ref _staticInfo, value);
    }

    /// <summary>Live sensor readings for the currently selected CPU socket.</summary>
    public CpuSensorData? SensorData
    {
        get => _sensorData;
        private set => SetProperty(ref _sensorData, value);
    }

    /// <summary>Display names for each detected CPU socket (e.g. "CPU 0 — Intel Core i9-13900K").</summary>
    public ObservableCollection<string> SocketList { get; } = [];

    /// <summary>
    /// Index of the currently selected socket in <see cref="SocketList"/>.
    /// Changing this updates <see cref="StaticInfo"/> and <see cref="SensorData"/>.
    /// </summary>
    public int SelectedSocketIndex
    {
        get => _selectedSocketIndex;
        set
        {
            if (SetProperty(ref _selectedSocketIndex, value))
                ApplySocketSelection();
        }
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
            _allSockets = await _cpuInfoService.GetStaticInfoAsync();

            SocketList.Clear();
            foreach (var cpu in _allSockets)
            {
                string label = _allSockets.Count == 1
                    ? cpu.Name
                    : $"CPU {cpu.SocketIndex} — {cpu.Name}";
                SocketList.Add(label);
            }

            _selectedSocketIndex = 0;
            OnPropertyChanged(nameof(SelectedSocketIndex));
            ApplySocketSelection();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load CPU information: {ex.Message}";
        }
    }

    private void ApplySocketSelection()
    {
        if (_allSockets.Count == 0) return;

        int idx = Math.Clamp(_selectedSocketIndex, 0, _allSockets.Count - 1);
        StaticInfo = _allSockets[idx];
    }

    // -------------------------------------------------------------------------
    // Sensor update
    // -------------------------------------------------------------------------

    private void OnSensorUpdate(object? sender, SensorSnapshot snapshot)
    {
        if (snapshot.CpuSensors.Count == 0) return;

        int idx = Math.Clamp(_selectedSocketIndex, 0, snapshot.CpuSensors.Count - 1);
        SensorData = snapshot.CpuSensors[idx];
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
