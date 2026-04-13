using System.Management;
using System.Runtime.InteropServices;
using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Retrieves static GPU information from WMI (<c>Win32_VideoController</c>) and
/// attempts live sensor readings via <c>Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine</c>
/// for NVIDIA adapters. Non-NVIDIA adapters return <c>null</c> for sensor values.
/// All WMI calls are wrapped in try/catch; a sentinel model is returned on failure.
/// </summary>
public sealed class GraphicsInfoService
{
    // -------------------------------------------------------------------------
    // Sentinel / fallback values
    // -------------------------------------------------------------------------

    private static GpuStaticInfo BuildSentinel(int adapterIndex) => new(
        AdapterIndex: adapterIndex,
        Name: "Unavailable",
        ChipModel: null,
        Manufacturer: "Unavailable",
        VideoMemoryMb: 0,
        MemoryType: null,
        DriverVersion: "Unavailable",
        DriverDate: "Unavailable"
    );

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns static information for every graphics adapter found via WMI.
    /// Returns a single sentinel entry if WMI is unavailable or throws.
    /// </summary>
    public Task<IReadOnlyList<GpuStaticInfo>> GetStaticInfoAsync() =>
        Task.Run(GetStaticInfo);

    /// <summary>
    /// Returns live sensor readings for every graphics adapter.
    /// NVIDIA adapters attempt clock/temperature via GPU performance counters.
    /// Non-NVIDIA adapters return <c>null</c> for all sensor values.
    /// </summary>
    public Task<IReadOnlyList<GpuSensorData>> ReadSensorsAsync() =>
        Task.Run(ReadSensors);

    // -------------------------------------------------------------------------
    // Static info — Win32_VideoController
    // -------------------------------------------------------------------------

    private IReadOnlyList<GpuStaticInfo> GetStaticInfo()
    {
        var results = new List<GpuStaticInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT * FROM Win32_VideoController");

            using var collection = searcher.Get();

            int adapterIndex = 0;
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    results.Add(MapVideoController(obj, adapterIndex));
                    adapterIndex++;
                }
            }
        }
        catch (ManagementException)
        {
            results.Add(BuildSentinel(0));
        }
        catch (COMException)
        {
            results.Add(BuildSentinel(0));
        }
        catch (Exception)
        {
            results.Add(BuildSentinel(0));
        }

        if (results.Count == 0)
            results.Add(BuildSentinel(0));

        return results;
    }

    private static GpuStaticInfo MapVideoController(ManagementObject obj, int adapterIndex)
    {
        try
        {
            string name = obj["Name"] as string ?? "Unavailable";
            string description = obj["VideoProcessor"] as string
                                 ?? obj["Caption"] as string
                                 ?? name;

            string? chipModel = ExtractChipModel(name);
            string manufacturer = ExtractManufacturer(name);

            // AdapterRAM is in bytes; convert to MB
            long videoMemoryMb = 0;
            var adapterRam = obj["AdapterRAM"];
            if (adapterRam != null)
            {
                long bytes = Convert.ToInt64(adapterRam);
                videoMemoryMb = bytes / (1024L * 1024L);
            }

            string? memoryType = MapVideoMemoryType(obj["VideoMemoryType"]);

            string driverVersion = obj["DriverVersion"] as string ?? "Unavailable";
            string driverDate = ParseDriverDate(obj["DriverDate"] as string);

            return new GpuStaticInfo(
                AdapterIndex: adapterIndex,
                Name: name.Trim(),
                ChipModel: chipModel,
                Manufacturer: manufacturer,
                VideoMemoryMb: videoMemoryMb,
                MemoryType: memoryType,
                DriverVersion: driverVersion,
                DriverDate: driverDate
            );
        }
        catch (ManagementException)
        {
            return BuildSentinel(adapterIndex);
        }
        catch (COMException)
        {
            return BuildSentinel(adapterIndex);
        }
        catch (Exception)
        {
            return BuildSentinel(adapterIndex);
        }
    }

    // -------------------------------------------------------------------------
    // Sensor data — GPUPerformanceCounters (NVIDIA only)
    // -------------------------------------------------------------------------

    private IReadOnlyList<GpuSensorData> ReadSensors()
    {
        var results = new List<GpuSensorData>();

        // First, get the list of adapters to know which are NVIDIA
        IReadOnlyList<GpuStaticInfo> adapters;
        try
        {
            adapters = GetStaticInfo();
        }
        catch
        {
            adapters = new List<GpuStaticInfo> { BuildSentinel(0) };
        }

        // Try to read NVIDIA GPU engine performance counters
        var nvidiaCounters = ReadNvidiaGpuCounters();

        for (int i = 0; i < adapters.Count; i++)
        {
            var adapter = adapters[i];
            bool isNvidia = adapter.Manufacturer.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase);

            if (isNvidia && nvidiaCounters.Count > 0)
            {
                // Use the first matching counter set for this adapter index
                var counter = nvidiaCounters.Count > i ? nvidiaCounters[i] : nvidiaCounters[0];
                results.Add(counter);
            }
            else
            {
                // Non-NVIDIA or no counter data available — return null sensor values
                results.Add(new GpuSensorData
                {
                    CoreClockMhz = null,
                    MemoryClockMhz = null,
                    TemperatureCelsius = null
                });
            }
        }

        if (results.Count == 0)
            results.Add(new GpuSensorData
            {
                CoreClockMhz = null,
                MemoryClockMhz = null,
                TemperatureCelsius = null
            });

        return results;
    }

    private static List<GpuSensorData> ReadNvidiaGpuCounters()
    {
        var counters = new List<GpuSensorData>();

        try
        {
            // Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine provides
            // engine utilization data; clock speeds are not directly available here.
            // We attempt to read what we can and leave unavailable fields as null.
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            using var collection = searcher.Get();

            // Aggregate by adapter — multiple engine entries may exist per GPU
            var adapterData = new Dictionary<string, GpuSensorData>(StringComparer.OrdinalIgnoreCase);

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    try
                    {
                        // The Name property typically contains "luid_0x...._phys_0_eng_0_engtype_3D"
                        string instanceName = obj["Name"] as string ?? string.Empty;

                        // Extract a stable adapter key from the LUID portion
                        string adapterKey = ExtractAdapterKey(instanceName);

                        if (!adapterData.ContainsKey(adapterKey))
                        {
                            adapterData[adapterKey] = new GpuSensorData
                            {
                                CoreClockMhz = null,
                                MemoryClockMhz = null,
                                TemperatureCelsius = null
                            };
                        }

                        // UtilizationPercentage is available but not a clock speed.
                        // Clock speeds are not exposed by this WMI class on most systems.
                        // Leave CoreClockMhz/MemoryClockMhz as null (unavailable).
                    }
                    catch (ManagementException) { }
                    catch (COMException) { }
                    catch (Exception) { }
                }
            }

            counters.AddRange(adapterData.Values);
        }
        catch (ManagementException)
        {
            // GPUPerformanceCounters WMI class may not be available on all systems
        }
        catch (COMException)
        {
            // COM errors are non-fatal
        }
        catch (Exception)
        {
            // Any other error — sensor data unavailable
        }

        return counters;
    }

    // -------------------------------------------------------------------------
    // Chip model extraction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the GPU chip model from the adapter name by stripping known
    /// manufacturer prefixes. For example:
    /// "NVIDIA GeForce RTX 4090" → "GeForce RTX 4090"
    /// "AMD Radeon RX 7900 XTX" → "Radeon RX 7900 XTX"
    /// "Intel(R) UHD Graphics 770" → "UHD Graphics 770"
    /// </summary>
    internal static string? ExtractChipModel(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return null;

        string name = adapterName.Trim();

        // Strip known manufacturer prefixes (case-insensitive)
        string[] prefixes = ["NVIDIA ", "AMD ", "ATI ", "Intel(R) ", "Intel "];
        foreach (string prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string chip = name[prefix.Length..].Trim();
                return string.IsNullOrWhiteSpace(chip) ? null : chip;
            }
        }

        return name;
    }

    // -------------------------------------------------------------------------
    // Manufacturer extraction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the GPU manufacturer from the adapter name.
    /// Returns "NVIDIA", "AMD", "Intel", or "Unknown".
    /// </summary>
    internal static string ExtractManufacturer(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return "Unknown";

        string name = adapterName.Trim();

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ATI", StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return "Unknown";
    }

    // -------------------------------------------------------------------------
    // Video memory type mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the <c>Win32_VideoController.VideoMemoryType</c> value to a
    /// human-readable memory type string.
    /// </summary>
    internal static string? MapVideoMemoryType(object? videoMemoryType)
    {
        if (videoMemoryType == null)
            return null;

        return Convert.ToUInt32(videoMemoryType) switch
        {
            2  => "Unknown",
            3  => "VRAM",
            4  => "DRAM",
            5  => "SRAM",
            6  => "WRAM",
            7  => "EDO RAM",
            8  => "Burst Synchronous DRAM",
            9  => "Pipelined Burst SRAM",
            10 => "CDRAM",
            11 => "3DRAM",
            12 => "SDRAM",
            13 => "SGRAM",
            _  => null
        };
    }

    // -------------------------------------------------------------------------
    // Driver date parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses the WMI driver date string in the format "YYYYMMDD000000.000000+000"
    /// to a readable date string "YYYY-MM-DD".
    /// Returns "Unavailable" if the input is null, empty, or cannot be parsed.
    /// </summary>
    internal static string ParseDriverDate(string? wmiDate)
    {
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 8)
            return "Unavailable";

        try
        {
            // WMI datetime format: "YYYYMMDDHHMMSS.mmmmmm+UUU"
            // We only need the first 8 characters for the date portion
            string datePart = wmiDate[..8];

            if (datePart.Length == 8 &&
                int.TryParse(datePart[..4], out int year) &&
                int.TryParse(datePart[4..6], out int month) &&
                int.TryParse(datePart[6..8], out int day) &&
                year > 1970 && month >= 1 && month <= 12 && day >= 1 && day <= 31)
            {
                return $"{year:D4}-{month:D2}-{day:D2}";
            }
        }
        catch
        {
            // Fall through to return "Unavailable"
        }

        return "Unavailable";
    }

    // -------------------------------------------------------------------------
    // Adapter key extraction helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts a stable adapter key from a GPU engine instance name.
    /// Instance names typically follow the pattern:
    /// "luid_0x00000000_0x00012345_phys_0_eng_0_engtype_3D"
    /// We extract the LUID portion as the adapter key.
    /// </summary>
    private static string ExtractAdapterKey(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            return "default";

        // Try to extract "luid_0x..._0x..." prefix
        int physIndex = instanceName.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
        if (physIndex > 0)
            return instanceName[..physIndex];

        return instanceName;
    }
}
