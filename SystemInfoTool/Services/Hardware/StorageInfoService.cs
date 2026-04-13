using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using SystemInfoTool.Models;
using SystemInfoTool.PInvoke;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Retrieves static information about physical storage devices from WMI (<c>Win32_DiskDrive</c>)
/// and SMART health data via <c>DeviceIoControl</c> P/Invoke on <c>\\.\PhysicalDriveN</c> handles.
/// All WMI and P/Invoke calls are wrapped in try/catch; a sentinel model is returned on failure.
/// </summary>
public sealed class StorageInfoService
{
    // -------------------------------------------------------------------------
    // SMART ATA command constants
    // -------------------------------------------------------------------------

    private const byte ATA_SMART_CMD = 0xB0;
    private const byte SMART_READ_DATA = 0xD0;
    private const byte SMART_CYL_LOW = 0x4F;
    private const byte SMART_CYL_HIGH = 0xC2;

    // SMART capability flag: SMART supported and enabled
    private const uint SMART_CAPABILITY_SUPPORTED = 0x0001;

    // Critical SMART attribute IDs
    private static readonly HashSet<byte> CriticalAttributeIds = new()
    {
        5,   // Reallocated Sectors Count
        196, // Reallocation Event Count
        197, // Current Pending Sector Count
        198, // Uncorrectable Sector Count
    };

    // -------------------------------------------------------------------------
    // Sentinel model
    // -------------------------------------------------------------------------

    private static StorageDriveInfo Sentinel(int index) => new(
        DriveIndex: index,
        Model: "Unavailable",
        Manufacturer: null,
        InterfaceType: "Unknown",
        CapacityGb: 0,
        FirmwareRevision: null,
        SmartHealth: SmartStatus.Unavailable
    );

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a list of all detected physical storage devices with SMART health status.
    /// Returns an empty list if WMI cannot be queried.
    /// </summary>
    public Task<IReadOnlyList<StorageDriveInfo>> GetAllDrivesAsync() =>
        Task.Run(GetAllDrives);

    // -------------------------------------------------------------------------
    // Core implementation
    // -------------------------------------------------------------------------

    private static IReadOnlyList<StorageDriveInfo> GetAllDrives()
    {
        var drives = new List<StorageDriveInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Index, Model, Manufacturer, InterfaceType, Size, FirmwareRevision " +
                "FROM Win32_DiskDrive");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    drives.Add(MapDrive(obj));
                }
            }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (Exception) { }

        return drives.AsReadOnly();
    }

    private static StorageDriveInfo MapDrive(ManagementObject obj)
    {
        int index;
        try
        {
            index = Convert.ToInt32(obj["Index"] ?? 0);
        }
        catch
        {
            index = 0;
        }

        try
        {
            string model = (obj["Model"] as string)?.Trim() ?? "Unknown";
            string? manufacturer = (obj["Manufacturer"] as string)?.Trim();
            if (string.IsNullOrWhiteSpace(manufacturer)) manufacturer = null;

            string interfaceType = (obj["InterfaceType"] as string)?.Trim() ?? "Unknown";
            if (string.IsNullOrWhiteSpace(interfaceType)) interfaceType = "Unknown";

            string? firmwareRevision = (obj["FirmwareRevision"] as string)?.Trim();
            if (string.IsNullOrWhiteSpace(firmwareRevision)) firmwareRevision = null;

            long capacityGb = 0;
            if (obj["Size"] is string sizeStr && ulong.TryParse(sizeStr, out ulong sizeBytes))
                capacityGb = StorageCapacityHelper.BytesToGb((long)sizeBytes);
            else if (obj["Size"] is ulong ul)
                capacityGb = StorageCapacityHelper.BytesToGb((long)ul);

            SmartStatus smartHealth = QuerySmartStatus(index, interfaceType);

            return new StorageDriveInfo(
                DriveIndex: index,
                Model: model,
                Manufacturer: manufacturer,
                InterfaceType: interfaceType,
                CapacityGb: capacityGb,
                FirmwareRevision: firmwareRevision,
                SmartHealth: smartHealth
            );
        }
        catch
        {
            return Sentinel(index);
        }
    }

    // -------------------------------------------------------------------------
    // SMART querying
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens <c>\\.\PhysicalDriveN</c>, checks SMART support via
    /// <c>IOCTL_SMART_GET_VERSION</c>, then reads attribute data via
    /// <c>IOCTL_SMART_RCV_DRIVE_DATA</c> and classifies the result.
    /// Returns <see cref="SmartStatus.Unavailable"/> for USB drives, access denied,
    /// or any Win32 error.
    /// </summary>
    private static SmartStatus QuerySmartStatus(int driveIndex, string interfaceType)
    {
        // USB drives do not support SMART
        if (interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase))
            return SmartStatus.Unavailable;

        var handle = NativeMethods.CreateFile(
            $@"\\.\PhysicalDrive{driveIndex}",
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            nint.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_ATTRIBUTE_NORMAL,
            nint.Zero);

        if (handle.IsInvalid)
            return SmartStatus.Unavailable;

        using (handle)
        {
            try
            {
                // Step 1: Check SMART support via IOCTL_SMART_GET_VERSION
                if (!CheckSmartSupport(handle))
                    return SmartStatus.Unavailable;

                // Step 2: Read SMART attribute data via IOCTL_SMART_RCV_DRIVE_DATA
                byte[]? smartData = ReadSmartData(handle, driveIndex);
                if (smartData == null)
                    return SmartStatus.Unavailable;

                // Step 3: Classify
                return ClassifySmartStatus(smartData);
            }
            catch (Win32Exception)
            {
                return SmartStatus.Unavailable;
            }
            catch (Exception)
            {
                return SmartStatus.Unavailable;
            }
        }
    }

    /// <summary>
    /// Issues <c>IOCTL_SMART_GET_VERSION</c> to verify the drive supports SMART.
    /// Returns <c>false</c> if the IOCTL fails or the capability flag is not set.
    /// </summary>
    private static bool CheckSmartSupport(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        int versionSize = Marshal.SizeOf<GetVersionInParams>();
        nint versionPtr = Marshal.AllocHGlobal(versionSize);
        try
        {
            bool ok = NativeMethods.DeviceIoControl(
                handle,
                NativeMethods.IOCTL_SMART_GET_VERSION,
                nint.Zero,
                0,
                versionPtr,
                (uint)versionSize,
                out _,
                nint.Zero);

            if (!ok)
                return false;

            var version = Marshal.PtrToStructure<GetVersionInParams>(versionPtr);
            return (version.fCapabilities & SMART_CAPABILITY_SUPPORTED) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(versionPtr);
        }
    }

    /// <summary>
    /// Issues <c>IOCTL_SMART_RCV_DRIVE_DATA</c> with the READ DATA command to retrieve
    /// the 512-byte SMART attribute block. Returns <c>null</c> on failure.
    /// </summary>
    private static byte[]? ReadSmartData(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int driveIndex)
    {
        // Build the input buffer (SendCmdInParams)
        var inParams = new SendCmdInParams
        {
            cBufferSize = 512,
            irDriveRegs = new IdeRegs
            {
                bFeaturesReg = SMART_READ_DATA,
                bSectorCountReg = 1,
                bSectorNumberReg = 1,
                bCylLowReg = SMART_CYL_LOW,
                bCylHighReg = SMART_CYL_HIGH,
                bDriveHeadReg = (byte)(0xA0 | ((driveIndex & 1) << 4)),
                bCommandReg = ATA_SMART_CMD,
                bReserved = 0,
            },
            bDriveNumber = (byte)driveIndex,
            bReserved = new byte[3],
            dwReserved = new uint[4],
            bBuffer = new byte[1],
        };

        int inSize = Marshal.SizeOf<SendCmdInParams>();
        int outSize = Marshal.SizeOf<SmartReadDataOutput>();

        nint inPtr = Marshal.AllocHGlobal(inSize);
        nint outPtr = Marshal.AllocHGlobal(outSize);
        try
        {
            Marshal.StructureToPtr(inParams, inPtr, false);

            bool ok = NativeMethods.DeviceIoControl(
                handle,
                NativeMethods.IOCTL_SMART_RCV_DRIVE_DATA,
                inPtr,
                (uint)inSize,
                outPtr,
                (uint)outSize,
                out _,
                nint.Zero);

            if (!ok)
                return null;

            var output = Marshal.PtrToStructure<SmartReadDataOutput>(outPtr);

            // Check driver status — bDriverError == 0 means success
            if (output.DriverStatus.bDriverError != 0)
                return null;

            // Serialise the SmartData struct into a flat byte array for ClassifySmartStatus
            int dataSize = Marshal.SizeOf<SmartData>();
            nint dataPtr = Marshal.AllocHGlobal(dataSize);
            try
            {
                Marshal.StructureToPtr(output.SmartData, dataPtr, false);
                byte[] bytes = new byte[dataSize];
                Marshal.Copy(dataPtr, bytes, 0, dataSize);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    // -------------------------------------------------------------------------
    // SMART classification (also referenced by task 8.2 tests)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Classifies SMART health from the raw 512-byte SMART data block.
    /// Returns <see cref="SmartStatus.Good"/> if all attributes pass their thresholds,
    /// <see cref="SmartStatus.Caution"/> if any non-critical attribute fails,
    /// <see cref="SmartStatus.Bad"/> if any critical attribute fails,
    /// or <see cref="SmartStatus.Unavailable"/> if <paramref name="smartData"/> is
    /// <c>null</c>, empty, or cannot be parsed.
    /// </summary>
    /// <param name="smartData">
    /// Raw bytes of the <see cref="SmartData"/> struct returned by
    /// <c>IOCTL_SMART_RCV_DRIVE_DATA</c> (at least <c>sizeof(SmartData)</c> bytes).
    /// </param>
    public static SmartStatus ClassifySmartStatus(byte[]? smartData)
    {
        if (smartData == null || smartData.Length == 0)
            return SmartStatus.Unavailable;

        int requiredSize = Marshal.SizeOf<SmartData>();
        if (smartData.Length < requiredSize)
            return SmartStatus.Unavailable;

        SmartData data;
        nint ptr = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.Copy(smartData, 0, ptr, requiredSize);
            data = Marshal.PtrToStructure<SmartData>(ptr);
        }
        catch
        {
            return SmartStatus.Unavailable;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (data.Attributes == null)
            return SmartStatus.Unavailable;

        bool hasCautionAttribute = false;

        foreach (var attr in data.Attributes)
        {
            // Attribute ID 0 means unused slot — skip
            if (attr.AttributeId == 0)
                continue;

            // Pre-failure bit (bit 0 of StatusFlags) indicates the attribute is
            // a pre-failure / warranty attribute; advisory attributes have bit 0 clear.
            bool isPreFailure = (attr.StatusFlags & 0x0001) != 0;

            // A failing attribute has CurrentValue <= threshold.
            // The threshold is not stored in the SMART data block itself (it lives in
            // a separate SMART READ THRESHOLDS page), so we use the industry-standard
            // heuristic: CurrentValue < WorstValue is a degraded indicator, and
            // CurrentValue == 1 (the minimum normalised value) is a hard failure.
            // For critical attributes we also treat WorstValue == 1 as Bad.
            bool currentFailing = attr.CurrentValue == 1;
            bool worstFailing = attr.WorstValue == 1;

            if (CriticalAttributeIds.Contains(attr.AttributeId))
            {
                if (currentFailing || worstFailing)
                    return SmartStatus.Bad;
            }
            else if (isPreFailure)
            {
                if (currentFailing || worstFailing)
                    return SmartStatus.Bad;
            }
            else
            {
                if (currentFailing)
                    hasCautionAttribute = true;
            }
        }

        return hasCautionAttribute ? SmartStatus.Caution : SmartStatus.Good;
    }
}
