using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Retrieves static motherboard and BIOS information from WMI.
/// Queries <c>Win32_BaseBoard</c>, <c>Win32_BIOS</c>, <c>Win32_PhysicalMemoryArray</c>,
/// and attempts chipset identification via <c>Win32_PnPEntity</c> and the registry.
/// All WMI and registry calls are wrapped in try/catch; a sentinel model is returned
/// on failure and exceptions are never propagated to the caller.
/// </summary>
public sealed class MotherboardInfoService
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns static motherboard and BIOS information.
    /// Returns a sentinel model with all fields set to <c>"Unavailable"</c> if
    /// data cannot be retrieved.
    /// </summary>
    public Task<MotherboardInfo> GetStaticInfoAsync() =>
        Task.Run(GetStaticInfo);

    // -------------------------------------------------------------------------
    // Core implementation
    // -------------------------------------------------------------------------

    private static MotherboardInfo GetStaticInfo()
    {
        string manufacturer = "Unavailable";
        string productName = "Unavailable";
        string version = "Unavailable";
        string biosVendor = "Unavailable";
        string biosVersion = "Unavailable";
        string biosReleaseDate = "Unavailable";
        string? chipsetModel = null;
        int totalMemorySlots = 0;
        int populatedMemorySlots = 0;

        // --- Win32_BaseBoard ---
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    manufacturer = (obj["Manufacturer"] as string)?.Trim() ?? "Unavailable";
                    productName  = (obj["Product"]      as string)?.Trim() ?? "Unavailable";
                    version      = (obj["Version"]      as string)?.Trim() ?? "Unavailable";
                    break; // only one base board expected
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // --- Win32_BIOS ---
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    biosVendor      = (obj["Manufacturer"]      as string)?.Trim() ?? "Unavailable";
                    biosVersion     = (obj["SMBIOSBIOSVersion"]  as string)?.Trim() ?? "Unavailable";
                    biosReleaseDate = FormatBiosDate(obj["ReleaseDate"] as string);
                    break;
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // --- Win32_PhysicalMemoryArray ---
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    totalMemorySlots += Convert.ToInt32(obj["MemoryDevices"] ?? 0);
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // --- Populated slot count from Win32_PhysicalMemory ---
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Capacity FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    populatedMemorySlots++;
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // --- Chipset identification ---
        chipsetModel = TryGetChipsetModel();

        return new MotherboardInfo(
            Manufacturer: manufacturer,
            ProductName: productName,
            Version: version,
            BiosVendor: biosVendor,
            BiosVersion: biosVersion,
            BiosReleaseDate: biosReleaseDate,
            ChipsetModel: chipsetModel,
            TotalMemorySlots: totalMemorySlots,
            PopulatedMemorySlots: populatedMemorySlots
        );
    }

    // -------------------------------------------------------------------------
    // Chipset identification
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to identify the chipset model by:
    /// 1. Querying <c>Win32_PnPEntity</c> for devices in the "System" class whose
    ///    name contains common chipset keywords.
    /// 2. Falling back to the registry path
    ///    <c>HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0</c>.
    /// Returns <c>null</c> if the chipset cannot be determined.
    /// </summary>
    private static string? TryGetChipsetModel()
    {
        // Strategy 1: Win32_PnPEntity — look for PCH / chipset devices
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'System'");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    string? name = obj["Name"] as string;
                    if (name == null) continue;

                    if (IsChipsetName(name))
                        return name.Trim();
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // Strategy 2: Registry — CentralProcessor identifier as a last resort
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);

            if (key != null)
            {
                string? identifier = key.GetValue("Identifier") as string;
                if (!string.IsNullOrWhiteSpace(identifier))
                    return identifier.Trim();
            }
        }
        catch (Exception) { }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when the PnP entity name looks like a chipset / PCH device.
    /// </summary>
    private static bool IsChipsetName(string name)
    {
        // Common keywords found in Intel PCH and AMD FCH/chipset device names
        ReadOnlySpan<string> keywords =
        [
            "PCH", "Chipset", "Platform Controller Hub",
            "Fusion Controller Hub", "FCH", "SMBus",
            "LPC Controller", "ISA Bridge"
        ];

        foreach (var kw in keywords)
        {
            if (name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // BIOS date formatting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a WMI CIM_DATETIME string (e.g. <c>"20230415000000.000000+000"</c>)
    /// to a human-readable date string (<c>"2023-04-15"</c>).
    /// Returns <c>"Unavailable"</c> if the input is null or cannot be parsed.
    /// </summary>
    private static string FormatBiosDate(string? cimDateTime)
    {
        if (string.IsNullOrWhiteSpace(cimDateTime))
            return "Unavailable";

        try
        {
            // CIM_DATETIME format: yyyyMMddHHmmss.ffffff+UUU
            if (cimDateTime.Length >= 8 &&
                int.TryParse(cimDateTime[..4], out int year)  &&
                int.TryParse(cimDateTime[4..6], out int month) &&
                int.TryParse(cimDateTime[6..8], out int day))
            {
                return $"{year:D4}-{month:D2}-{day:D2}";
            }
        }
        catch (Exception) { }

        return "Unavailable";
    }
}
