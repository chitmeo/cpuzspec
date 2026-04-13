using SystemInfoTool.ViewModels;

namespace SystemInfoTool.Models;

/// <summary>
/// Immutable static information about a single graphics adapter.
/// </summary>
public record GpuStaticInfo(
    int AdapterIndex,
    string Name,
    string? ChipModel,
    string Manufacturer,
    long VideoMemoryMb,
    string? MemoryType,
    string DriverVersion,
    string DriverDate
);

/// <summary>
/// Mutable live sensor readings for a single GPU.
/// Extends <see cref="ObservableObject"/> so the UI updates in place.
/// </summary>
public class GpuSensorData : ObservableObject
{
    private double? _coreClockMhz;
    private double? _memoryClockMhz;
    private double? _temperatureCelsius;

    public double? CoreClockMhz
    {
        get => _coreClockMhz;
        set => SetProperty(ref _coreClockMhz, value);
    }

    public double? MemoryClockMhz
    {
        get => _memoryClockMhz;
        set => SetProperty(ref _memoryClockMhz, value);
    }

    public double? TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => SetProperty(ref _temperatureCelsius, value);
    }
}
