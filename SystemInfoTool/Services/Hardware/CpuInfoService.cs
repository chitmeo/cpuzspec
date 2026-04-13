using System.Management;
using System.Runtime.InteropServices;

using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Retrieves static CPU information from WMI (<c>Win32_Processor</c>) and live
/// temperature readings from <c>MSAcpi_ThermalZoneTemperature</c>.
/// Supports multi-socket systems by iterating all <c>Win32_Processor</c> instances.
/// All WMI calls are wrapped in try/catch; a sentinel model is returned on failure.
/// </summary>
public sealed class CpuInfoService
{
    // -------------------------------------------------------------------------
    // Sentinel / fallback values
    // -------------------------------------------------------------------------

    private static CpuStaticInfo BuildSentinel(int socketIndex) => new(
        SocketIndex: socketIndex,
        Name: "Unavailable",
        CodeName: "Unavailable",
        PackageType: "Unavailable",
        PhysicalCores: 0,
        LogicalProcessors: 0,
        BaseClockMhz: 0,
        L1: new CacheInfo(0, 0, "Unavailable"),
        L2: new CacheInfo(0, 0, "Unavailable"),
        L3: new CacheInfo(0, 0, "Unavailable"),
        IsaExtensions: [],
        TdpWatts: null
    );

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns static information for every physical CPU socket found via WMI.
    /// Returns a single sentinel entry if WMI is unavailable or throws.
    /// </summary>
    public Task<IReadOnlyList<CpuStaticInfo>> GetStaticInfoAsync() =>
        Task.Run(GetStaticInfo);

    /// <summary>
    /// Returns live sensor readings (current clock, temperature) for every
    /// physical CPU socket. Temperature is <c>null</c> when unavailable.
    /// </summary>
    public Task<IReadOnlyList<CpuSensorData>> ReadSensorsAsync() =>
        Task.Run(ReadSensors);

    // -------------------------------------------------------------------------
    // Static info — Win32_Processor
    // -------------------------------------------------------------------------

    private IReadOnlyList<CpuStaticInfo> GetStaticInfo()
    {
        var results = new List<CpuStaticInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT * FROM Win32_Processor");

            using var collection = searcher.Get();

            int socketIndex = 0;
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    results.Add(MapProcessor(obj, socketIndex));
                    socketIndex++;
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

    private static CpuStaticInfo MapProcessor(ManagementObject obj, int socketIndex)
    {
        try
        {
            string name = obj["Name"] as string ?? "Unavailable";
            string packageType = MapPackageType(obj["UpgradeMethod"]);
            int physicalCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
            int logicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
            double baseClockMhz = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0);

            // Cache sizes — Win32_Processor exposes L2 and L3 directly.
            // L1 is not exposed by Win32_Processor; use 0 as sentinel.
            long l2SizeKb = Convert.ToInt64(obj["L2CacheSize"] ?? 0L);
            long l3SizeKb = Convert.ToInt64(obj["L3CacheSize"] ?? 0L);

            // TDP — not a standard Win32_Processor property; attempt to read it
            // from the optional ExtClock or leave null.
            double? tdpWatts = null;

            // ISA extensions via CPUID
            IReadOnlyList<string> isaExtensions = CpuIdHelper.GetIsaExtensions();

            // Code name is not available from WMI; use "Unknown" as placeholder.
            string codeName = "Unknown";

            var l1 = new CacheInfo(0, 0, "Data");
            var l2 = new CacheInfo(l2SizeKb, 0, "Unified");
            var l3 = new CacheInfo(l3SizeKb, 0, "Unified");

            return new CpuStaticInfo(
                SocketIndex: socketIndex,
                Name: name.Trim(),
                CodeName: codeName,
                PackageType: packageType,
                PhysicalCores: physicalCores,
                LogicalProcessors: logicalProcessors,
                BaseClockMhz: baseClockMhz,
                L1: l1,
                L2: l2,
                L3: l3,
                IsaExtensions: isaExtensions,
                TdpWatts: tdpWatts
            );
        }
        catch (ManagementException)
        {
            return BuildSentinel(socketIndex);
        }
        catch (COMException)
        {
            return BuildSentinel(socketIndex);
        }
        catch (Exception)
        {
            return BuildSentinel(socketIndex);
        }
    }

    // -------------------------------------------------------------------------
    // Sensor data — MSAcpi_ThermalZoneTemperature + current clock
    // -------------------------------------------------------------------------

    private IReadOnlyList<CpuSensorData> ReadSensors()
    {
        var results = new List<CpuSensorData>();

        // Read current clock speeds from Win32_Processor
        var clockSpeeds = ReadCurrentClockSpeeds();

        // Read temperatures from MSAcpi_ThermalZoneTemperature
        var temperatures = ReadThermalZoneTemperatures();

        // Pair up by socket index (best-effort; thermal zones may not map 1:1)
        int count = Math.Max(clockSpeeds.Count, 1);
        for (int i = 0; i < count; i++)
        {
            double clock = i < clockSpeeds.Count ? clockSpeeds[i] : 0;
            double? temp = temperatures.Count > 0 ? temperatures[Math.Min(i, temperatures.Count - 1)] : null;

            results.Add(new CpuSensorData
            {
                CurrentClockMhz = clock,
                TemperatureCelsius = temp
            });
        }

        if (results.Count == 0)
            results.Add(new CpuSensorData { CurrentClockMhz = 0, TemperatureCelsius = null });

        return results;
    }

    private static List<double> ReadCurrentClockSpeeds()
    {
        var speeds = new List<double>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT CurrentClockSpeed FROM Win32_Processor");

            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    double speed = Convert.ToDouble(obj["CurrentClockSpeed"] ?? 0);
                    speeds.Add(speed);
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        return speeds;
    }

    private static List<double?> ReadThermalZoneTemperatures()
    {
        var temps = new List<double?>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    var raw = obj["CurrentTemperature"];
                    if (raw != null)
                    {
                        uint rawValue = Convert.ToUInt32(raw);
                        double celsius = ConvertKelvinTenthsToCelsius(rawValue);
                        temps.Add(celsius);
                    }
                    else
                    {
                        temps.Add(null);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // Thermal zone WMI class may not be available on all systems
        }
        catch (COMException)
        {
            // COM errors are non-fatal; temperature remains null
        }
        catch (Exception)
        {
            // Any other error — temperature unavailable
        }

        return temps;
    }

    // -------------------------------------------------------------------------
    // Temperature conversion helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a raw <c>MSAcpi_ThermalZoneTemperature.CurrentTemperature</c> value
    /// (expressed as Kelvin × 10) to degrees Celsius.
    /// Formula: <c>(rawValue / 10.0) - 273.15</c>
    /// </summary>
    internal static double ConvertKelvinTenthsToCelsius(uint rawValue) =>
        (rawValue / 10.0) - 273.15;

    // -------------------------------------------------------------------------
    // Package type mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the <c>Win32_Processor.UpgradeMethod</c> value to a human-readable
    /// package type string.
    /// </summary>
    private static string MapPackageType(object? upgradeMethod)
    {
        if (upgradeMethod == null)
            return "Unknown";

        return Convert.ToUInt16(upgradeMethod) switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Daughter Board",
            4 => "ZIF Socket",
            5 => "Replacement/Piggy Back",
            6 => "None",
            7 => "LIF Socket",
            8 => "Slot 1",
            9 => "Slot 2",
            10 => "370-pin Socket",
            11 => "Slot A",
            12 => "Slot M",
            13 => "Socket 423",
            14 => "Socket A (Socket 462)",
            15 => "Socket 478",
            16 => "Socket 754",
            17 => "Socket 940",
            18 => "Socket 939",
            19 => "Socket mPGA604",
            20 => "Socket LGA771",
            21 => "Socket LGA775",
            22 => "Socket S1",
            23 => "Socket AM2",
            24 => "Socket F (1207)",
            25 => "Socket LGA1366",
            26 => "Socket G34",
            27 => "Socket AM3",
            28 => "Socket C32",
            29 => "Socket LGA1156",
            30 => "Socket LGA1567",
            31 => "Socket PGA988A",
            32 => "Socket BGA1288",
            33 => "Socket rPGA988B",
            34 => "Socket BGA1023",
            35 => "Socket BGA1224",
            36 => "Socket LGA1155",
            37 => "Socket LGA1356",
            38 => "Socket LGA2011",
            39 => "Socket FS1",
            40 => "Socket FS2",
            41 => "Socket FM1",
            42 => "Socket FM2",
            43 => "Socket LGA2011-3",
            44 => "Socket LGA1356-3",
            45 => "Socket LGA1150",
            46 => "Socket BGA1168",
            47 => "Socket BGA1234",
            48 => "Socket BGA1364",
            49 => "Socket AM4",
            50 => "Socket LGA1151",
            51 => "Socket BGA1356",
            52 => "Socket BGA1440",
            53 => "Socket BGA1515",
            54 => "Socket LGA3647-1",
            55 => "Socket SP3",
            56 => "Socket SP3r2",
            57 => "Socket LGA2066",
            58 => "Socket BGA1392",
            59 => "Socket BGA1510",
            60 => "Socket BGA1528",
            61 => "Socket LGA4189",
            62 => "Socket LGA1200",
            63 => "Socket LGA4677",
            64 => "Socket LGA1700",
            65 => "Socket BGA1744",
            66 => "Socket BGA1781",
            67 => "Socket BGA1211",
            68 => "Socket BGA2422",
            69 => "Socket LGA5773",
            70 => "Socket BGA5773",
            _ => "Unknown"
        };
    }
}
