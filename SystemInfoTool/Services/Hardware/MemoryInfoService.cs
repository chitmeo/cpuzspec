using System.Management;
using System.Runtime.InteropServices;
using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Retrieves static memory information from WMI (<c>Win32_PhysicalMemory</c>).
/// Aggregates total installed RAM, memory type, channel configuration, and
/// per-slot details including SPD-dependent fields where accessible.
/// All WMI calls are wrapped in try/catch; sentinel values are returned on failure
/// and exceptions are never propagated to the caller.
/// </summary>
public sealed class MemoryInfoService
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns aggregate memory information including per-slot details.
    /// Returns a sentinel model with safe defaults if data cannot be retrieved.
    /// </summary>
    public Task<MemoryInfo> GetStaticInfoAsync() =>
        Task.Run(GetStaticInfo);

    // -------------------------------------------------------------------------
    // Core implementation
    // -------------------------------------------------------------------------

    private static MemoryInfo GetStaticInfo()
    {
        var slots = new List<MemorySlotInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT DeviceLocator, Capacity, Manufacturer, PartNumber, SerialNumber, " +
                "Speed, MemoryType, SMBIOSMemoryType, ConfiguredClockSpeed, " +
                "DataWidth, TotalWidth " +
                "FROM Win32_PhysicalMemory");

            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    slots.Add(MapSlot(obj));
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        // If WMI returned nothing, return a minimal sentinel
        if (slots.Count == 0)
        {
            return new MemoryInfo(
                TotalInstalledMb: 0,
                MemoryType: "Unavailable",
                ChannelConfig: "Unknown",
                CurrentFrequencyMhz: 0,
                XmpFrequencyMhz: null,
                Slots: []
            );
        }

        return AggregateSlots(slots);
    }

    // -------------------------------------------------------------------------
    // Per-slot mapping
    // -------------------------------------------------------------------------

    private static MemorySlotInfo MapSlot(ManagementObject obj)
    {
        try
        {
            string slotLabel = (obj["DeviceLocator"] as string)?.Trim() ?? "Unknown";

            // Capacity is reported in bytes by WMI
            long? capacityMb = null;
            if (obj["Capacity"] is ulong capacityBytes && capacityBytes > 0)
                capacityMb = (long)(capacityBytes / (1024UL * 1024UL));

            bool isPopulated = capacityMb.HasValue && capacityMb.Value > 0;

            string? manufacturer = NullIfEmpty(obj["Manufacturer"] as string);
            string? partNumber   = NullIfEmpty(obj["PartNumber"]   as string);
            string? serialNumber = NullIfEmpty(obj["SerialNumber"] as string);

            // Speed = rated/SPD speed; ConfiguredClockSpeed = actual running speed
            int? speedMhz = ToNullableInt(obj["Speed"]);
            int? configuredMhz = ToNullableInt(obj["ConfiguredClockSpeed"]);

            // Use ConfiguredClockSpeed as the effective speed; fall back to Speed
            int? effectiveSpeedMhz = configuredMhz ?? speedMhz;

            // Memory timings are not directly exposed by Win32_PhysicalMemory;
            // they require direct SPD access which is not available via WMI.
            MemoryTimings? timings = null;

            return new MemorySlotInfo(
                SlotLabel: slotLabel,
                IsPopulated: isPopulated,
                CapacityMb: isPopulated ? capacityMb : null,
                Manufacturer: isPopulated ? manufacturer : null,
                PartNumber: isPopulated ? partNumber : null,
                SerialNumber: isPopulated ? serialNumber : null,
                SpeedMhz: isPopulated ? effectiveSpeedMhz : null,
                Timings: timings
            );
        }
        catch (Exception)
        {
            // Return a safe unpopulated slot on any mapping error
            string fallbackLabel = "Unknown";
            try { fallbackLabel = (obj["DeviceLocator"] as string)?.Trim() ?? "Unknown"; }
            catch { /* ignore */ }

            return new MemorySlotInfo(
                SlotLabel: fallbackLabel,
                IsPopulated: false,
                CapacityMb: null,
                Manufacturer: null,
                PartNumber: null,
                SerialNumber: null,
                SpeedMhz: null,
                Timings: null
            );
        }
    }

    // -------------------------------------------------------------------------
    // Aggregation
    // -------------------------------------------------------------------------

    private static MemoryInfo AggregateSlots(List<MemorySlotInfo> slots)
    {
        // Total installed RAM and populated slot count via aggregation helper
        var (totalInstalledMb, populatedCount) = MemoryAggregationHelper.ComputeAggregates(slots);

        // Memory type — derive from WMI SMBIOSMemoryType of the first populated slot.
        // We re-query here because MemorySlotInfo doesn't carry the raw type code;
        // instead we resolve it during the initial WMI pass via a separate helper.
        string memoryType = ResolveMemoryType();

        // Channel configuration — inferred from populated slot count
        string channelConfig = InferChannelConfig(populatedCount);

        // Current frequency — use the highest ConfiguredClockSpeed among populated slots
        int currentFrequencyMhz = slots
            .Where(s => s.IsPopulated && s.SpeedMhz.HasValue)
            .Select(s => s.SpeedMhz!.Value)
            .DefaultIfEmpty(0)
            .Max();

        // XMP frequency — detect if ConfiguredClockSpeed > rated Speed for any slot.
        // We store the rated speed separately; since MemorySlotInfo only holds one
        // speed value (the effective/configured speed), XMP detection is best-effort.
        int? xmpFrequencyMhz = ResolveXmpFrequency();

        return new MemoryInfo(
            TotalInstalledMb: totalInstalledMb,
            MemoryType: memoryType,
            ChannelConfig: channelConfig,
            CurrentFrequencyMhz: currentFrequencyMhz,
            XmpFrequencyMhz: xmpFrequencyMhz,
            Slots: slots
        );
    }

    // -------------------------------------------------------------------------
    // Memory type resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Re-queries <c>Win32_PhysicalMemory.SMBIOSMemoryType</c> to resolve the
    /// memory type string (DDR4, DDR5, etc.) for the first populated slot.
    /// Returns <c>"Unknown"</c> if the type cannot be determined.
    /// </summary>
    private static string ResolveMemoryType()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT SMBIOSMemoryType, Capacity FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    // Only consider populated slots
                    if (obj["Capacity"] is not ulong cap || cap == 0)
                        continue;

                    ushort typeCode = Convert.ToUInt16(obj["SMBIOSMemoryType"] ?? 0);
                    string typeName = MapSmbiosMemoryType(typeCode);
                    if (typeName != "Unknown")
                        return typeName;
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        return "Unknown";
    }

    /// <summary>
    /// Attempts to detect an XMP/EXPO profile by comparing
    /// <c>ConfiguredClockSpeed</c> against the rated <c>Speed</c>.
    /// Returns the configured speed as the XMP frequency when it exceeds the
    /// rated speed, indicating an XMP/EXPO profile is active.
    /// Returns <c>null</c> when no XMP profile is detected or data is unavailable.
    /// </summary>
    private static int? ResolveXmpFrequency()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Speed, ConfiguredClockSpeed, Capacity FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    if (obj["Capacity"] is not ulong cap || cap == 0)
                        continue;

                    int? rated      = ToNullableInt(obj["Speed"]);
                    int? configured = ToNullableInt(obj["ConfiguredClockSpeed"]);

                    if (rated.HasValue && configured.HasValue && configured.Value > rated.Value)
                        return configured.Value;
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        return null;
    }

    // -------------------------------------------------------------------------
    // Channel configuration inference
    // -------------------------------------------------------------------------

    /// <summary>
    /// Infers the memory channel configuration from the number of populated slots.
    /// This is a best-effort heuristic; exact channel detection requires SMBIOS
    /// or chipset-level data not available via WMI.
    /// </summary>
    internal static string InferChannelConfig(int populatedSlots) =>
        populatedSlots switch
        {
            0 => "Unknown",
            1 => "Single",
            2 => "Dual",    // most common: 2 sticks in dual-channel
            3 => "Single",  // odd count — likely single-channel fallback
            4 => "Dual",    // 4 sticks can be dual or quad; dual is more common
            6 => "Triple",  // hexa-channel workstation boards
            8 => "Quad",    // octa-channel HEDT/server
            _ => populatedSlots % 2 == 0 ? "Dual" : "Single"
        };

    // -------------------------------------------------------------------------
    // SMBIOS memory type mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a <c>Win32_PhysicalMemory.SMBIOSMemoryType</c> value to a
    /// human-readable memory type string per the SMBIOS specification.
    /// </summary>
    internal static string MapSmbiosMemoryType(ushort typeCode) =>
        typeCode switch
        {
            0x01 => "Other",
            0x02 => "Unknown",
            0x03 => "DRAM",
            0x04 => "EDRAM",
            0x05 => "VRAM",
            0x06 => "SRAM",
            0x07 => "RAM",
            0x08 => "ROM",
            0x09 => "Flash",
            0x0A => "EEPROM",
            0x0B => "FEPROM",
            0x0C => "EPROM",
            0x0D => "CDRAM",
            0x0E => "3DRAM",
            0x0F => "SDRAM",
            0x10 => "SGRAM",
            0x11 => "RDRAM",
            0x12 => "DDR",
            0x13 => "DDR2",
            0x14 => "DDR2 FB-DIMM",
            0x18 => "DDR3",
            0x19 => "FBD2",
            0x1A => "DDR4",
            0x1B => "LPDDR",
            0x1C => "LPDDR2",
            0x1D => "LPDDR3",
            0x1E => "LPDDR4",
            0x1F => "Logical non-volatile device",
            0x20 => "HBM",
            0x21 => "HBM2",
            0x22 => "DDR5",
            0x23 => "LPDDR5",
            0x24 => "HBM3",
            _    => "Unknown"
        };

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns <c>null</c> for null, empty, or whitespace-only strings.</summary>
    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Converts a WMI property value to a nullable <c>int</c>.</summary>
    private static int? ToNullableInt(object? value)
    {
        if (value == null) return null;
        try
        {
            int result = Convert.ToInt32(value);
            return result > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}
