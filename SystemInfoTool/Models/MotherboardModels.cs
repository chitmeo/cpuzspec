namespace SystemInfoTool.Models;

/// <summary>
/// Immutable static information about the host motherboard and BIOS/UEFI.
/// </summary>
public record MotherboardInfo(
    string Manufacturer,
    string ProductName,
    string Version,
    string BiosVendor,
    string BiosVersion,
    string BiosReleaseDate,
    string? ChipsetModel,
    int TotalMemorySlots,
    int PopulatedMemorySlots
);
