namespace SystemInfoTool.Models;

/// <summary>
/// Immutable aggregate memory information for the system.
/// </summary>
public record MemoryInfo(
    long TotalInstalledMb,
    string MemoryType,
    string ChannelConfig,
    int CurrentFrequencyMhz,
    int? XmpFrequencyMhz,
    IReadOnlyList<MemorySlotInfo> Slots
);

/// <summary>
/// Immutable per-slot memory information.
/// </summary>
public record MemorySlotInfo(
    string SlotLabel,
    bool IsPopulated,
    long? CapacityMb,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    int? SpeedMhz,
    MemoryTimings? Timings
);

/// <summary>
/// Immutable memory timing parameters for a single DIMM slot.
/// </summary>
public record MemoryTimings(int CL, int tRCD, int tRP, int tRAS);
