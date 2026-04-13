namespace SystemInfoTool.Models;

/// <summary>
/// SMART health summary for a physical storage device.
/// </summary>
public enum SmartStatus
{
    Good,
    Caution,
    Bad,
    Unavailable
}

/// <summary>
/// Immutable static information about a single physical storage device.
/// Named <c>StorageDriveInfo</c> to avoid conflict with <see cref="System.IO.DriveInfo"/>.
/// </summary>
public record StorageDriveInfo(
    int DriveIndex,
    string Model,
    string? Manufacturer,
    string InterfaceType,
    long CapacityGb,
    string? FirmwareRevision,
    SmartStatus SmartHealth
);
