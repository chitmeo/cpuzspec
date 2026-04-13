using SystemInfoTool.ViewModels;

namespace SystemInfoTool.Models;

/// <summary>
/// Immutable static information about a single CPU socket.
/// </summary>
public record CpuStaticInfo(
    int SocketIndex,
    string Name,
    string CodeName,
    string PackageType,
    int PhysicalCores,
    int LogicalProcessors,
    double BaseClockMhz,
    CacheInfo L1,
    CacheInfo L2,
    CacheInfo L3,
    IReadOnlyList<string> IsaExtensions,
    double? TdpWatts
);

/// <summary>
/// Immutable description of a CPU cache level.
/// Type is one of: "Data", "Instruction", "Unified".
/// </summary>
public record CacheInfo(long SizeKb, int Associativity, string Type);

/// <summary>
/// Mutable live sensor readings for a single CPU socket.
/// Extends <see cref="ObservableObject"/> so the UI updates in place.
/// </summary>
public class CpuSensorData : ObservableObject
{
    private double _currentClockMhz;
    private double? _temperatureCelsius;

    public double CurrentClockMhz
    {
        get => _currentClockMhz;
        set => SetProperty(ref _currentClockMhz, value);
    }

    public double? TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => SetProperty(ref _temperatureCelsius, value);
    }
}
