using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SystemInfoTool.PInvoke;

/// <summary>
/// Internal P/Invoke declarations for SMART data access via DeviceIoControl.
/// CPUID leaf queries use System.Runtime.Intrinsics.X86.X86Base.CpuId() directly — no native shim required.
/// </summary>
internal static class NativeMethods
{
    // -------------------------------------------------------------------------
    // IOCTL constants
    // -------------------------------------------------------------------------

    /// <summary>IOCTL code to query storage device properties (e.g. bus type, vendor ID).</summary>
    internal const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    /// <summary>IOCTL code to retrieve the SMART version / capabilities from the drive.</summary>
    internal const uint IOCTL_SMART_GET_VERSION = 0x00074080;

    /// <summary>IOCTL code to read SMART attribute data from the drive.</summary>
    internal const uint IOCTL_SMART_RCV_DRIVE_DATA = 0x0007C088;

    // -------------------------------------------------------------------------
    // CreateFile flags / constants
    // -------------------------------------------------------------------------

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    internal const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    // -------------------------------------------------------------------------
    // P/Invoke: kernel32.dll
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens a file, device, or I/O device. Used to open \\.\PhysicalDriveN handles for SMART access.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    /// <summary>
    /// Sends a control code directly to a device driver, enabling SMART queries via IOCTL codes.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        nint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    /// <summary>
    /// Closes an open object handle. Included for completeness; prefer SafeFileHandle disposal.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint hObject);
}

// =============================================================================
// Supporting structs for DeviceIoControl / SMART access
// =============================================================================



// -------------------------------------------------------------------------
// STORAGE_QUERY_PROPERTY structs
// -------------------------------------------------------------------------

/// <summary>Identifies the type of storage property to query.</summary>
internal enum StoragePropertyId : uint
{
    StorageDeviceProperty = 0,
    StorageAdapterProperty = 1,
}

/// <summary>Identifies the type of query to perform.</summary>
internal enum StorageQueryType : uint
{
    PropertyStandardQuery = 0,
    PropertyExistsQuery = 1,
    PropertyMaskQuery = 2,
}

/// <summary>
/// Input buffer for IOCTL_STORAGE_QUERY_PROPERTY.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StoragePropertyQuery
{
    public StoragePropertyId PropertyId;
    public StorageQueryType QueryType;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public byte[] AdditionalParameters;
}

/// <summary>
/// Output buffer for IOCTL_STORAGE_QUERY_PROPERTY (StorageDeviceProperty).
/// Contains bus type, vendor ID, product ID, and other device-level metadata.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct StorageDeviceDescriptor
{
    public uint Version;
    public uint Size;
    public byte DeviceType;
    public byte DeviceTypeModifier;
    [MarshalAs(UnmanagedType.U1)]
    public bool RemovableMedia;
    [MarshalAs(UnmanagedType.U1)]
    public bool CommandQueueing;
    public uint VendorIdOffset;
    public uint ProductIdOffset;
    public uint ProductRevisionOffset;
    public uint SerialNumberOffset;
    public StorageBusType BusType;
    public uint RawPropertiesLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public byte[] RawDeviceProperties;
}

/// <summary>Storage bus type values returned in StorageDeviceDescriptor.</summary>
internal enum StorageBusType : uint
{
    BusTypeUnknown = 0x00,
    BusTypeScsi = 0x01,
    BusTypeAtapi = 0x02,
    BusTypeAta = 0x03,
    BusType1394 = 0x04,
    BusTypeSsa = 0x05,
    BusTypeFibre = 0x06,
    BusTypeUsb = 0x07,
    BusTypeRAID = 0x08,
    BusTypeiScsi = 0x09,
    BusTypeSas = 0x0A,
    BusTypeSata = 0x0B,
    BusTypeSd = 0x0C,
    BusTypeMmc = 0x0D,
    BusTypeVirtual = 0x0E,
    BusTypeFileBackedVirtual = 0x0F,
    BusTypeSpaces = 0x10,
    BusTypeNvme = 0x11,
    BusTypeSCM = 0x12,
    BusTypeUfs = 0x13,
    BusTypeMax = 0x14,
    BusTypeMaxReserved = 0x7F,
}

// -------------------------------------------------------------------------
// SMART / ATA structs (IOCTL_SMART_GET_VERSION, IOCTL_SMART_RCV_DRIVE_DATA)
// -------------------------------------------------------------------------

/// <summary>
/// Output buffer for IOCTL_SMART_GET_VERSION.
/// Reports the SMART version and capabilities supported by the drive.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GetVersionInParams
{
    public byte bVersion;
    public byte bRevision;
    public byte bReserved;
    public byte bIDEDeviceMap;
    public uint fCapabilities;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public uint[] dwReserved;
}

/// <summary>
/// ATA task file register values used to specify the SMART command.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IdeRegs
{
    public byte bFeaturesReg;
    public byte bSectorCountReg;
    public byte bSectorNumberReg;
    public byte bCylLowReg;
    public byte bCylHighReg;
    public byte bDriveHeadReg;
    public byte bCommandReg;
    public byte bReserved;
}

/// <summary>
/// Input buffer for IOCTL_SMART_RCV_DRIVE_DATA.
/// Specifies the drive number and the ATA command registers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SendCmdInParams
{
    public uint cBufferSize;
    public IdeRegs irDriveRegs;
    public byte bDriveNumber;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] bReserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public uint[] dwReserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public byte[] bBuffer;
}

/// <summary>
/// Status block returned alongside SMART data.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DriverStatus
{
    public byte bDriverError;
    public byte bIDEError;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] bReserved;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public uint[] dwReserved;
}

/// <summary>
/// Output buffer for IOCTL_SMART_RCV_DRIVE_DATA.
/// Contains the driver status and the raw data returned by the drive.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SendCmdOutParams
{
    public uint cBufferSize;
    public DriverStatus DriverStatus;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public byte[] bBuffer;
}

// -------------------------------------------------------------------------
// SMART attribute structs (SMART READ DATA — 512-byte payload)
// -------------------------------------------------------------------------

/// <summary>
/// A single SMART attribute entry (12 bytes per attribute in the 512-byte SMART data block).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SmartAttribute
{
    /// <summary>Attribute ID (0 = unused slot).</summary>
    public byte AttributeId;
    /// <summary>Status flags (pre-failure / advisory, online data collection, etc.).</summary>
    public ushort StatusFlags;
    /// <summary>Current normalised value (1–253; 0 and 254–255 are reserved).</summary>
    public byte CurrentValue;
    /// <summary>Worst normalised value ever recorded.</summary>
    public byte WorstValue;
    /// <summary>Raw attribute data (6 bytes, vendor-specific interpretation).</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] RawValue;
    /// <summary>Reserved byte.</summary>
    public byte Reserved;
}

/// <summary>
/// The 512-byte SMART data block returned by IOCTL_SMART_RCV_DRIVE_DATA (READ DATA command).
/// Contains up to 30 attribute entries.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SmartData
{
    /// <summary>Revision number of the SMART data structure.</summary>
    public ushort RevisionNumber;
    /// <summary>Array of up to 30 SMART attribute entries.</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
    public SmartAttribute[] Attributes;
    /// <summary>Off-line data collection status byte.</summary>
    public byte OfflineDataCollectionStatus;
    /// <summary>Self-assessment test status byte.</summary>
    public byte SelfAssessmentTestStatus;
    /// <summary>Time in minutes to complete off-line data collection.</summary>
    public ushort OfflineDataCollectionTime;
    public byte Reserved1;
    /// <summary>Off-line data collection capability flags.</summary>
    public byte OfflineDataCollectionCapability;
    /// <summary>SMART capability flags.</summary>
    public ushort SmartCapability;
    /// <summary>Error logging capability flags.</summary>
    public byte ErrorLoggingCapability;
    public byte Reserved2;
    /// <summary>Short self-test polling time in minutes.</summary>
    public byte ShortSelfTestPollingTime;
    /// <summary>Extended self-test polling time in minutes.</summary>
    public byte ExtendedSelfTestPollingTime;
    /// <summary>Conveyance self-test polling time in minutes.</summary>
    public byte ConveyanceSelfTestPollingTime;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
    public byte[] Reserved3;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 125)]
    public byte[] VendorSpecific;
    /// <summary>Data structure checksum (two's complement of bytes 0–510).</summary>
    public byte DataStructureChecksum;
}

/// <summary>
/// The full output buffer for IOCTL_SMART_RCV_DRIVE_DATA (READ DATA).
/// Wraps SendCmdOutParams with the 512-byte SmartData payload.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SmartReadDataOutput
{
    public uint cBufferSize;
    public DriverStatus DriverStatus;
    public SmartData SmartData;
}

/// <summary>
/// The full output buffer for IOCTL_SMART_RCV_DRIVE_DATA (IDENTIFY DEVICE).
/// Contains the 512-byte ATA IDENTIFY DEVICE response (IdSector).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SmartIdentifyOutput
{
    public uint cBufferSize;
    public DriverStatus DriverStatus;
    public IdSector IdSector;
}

/// <summary>
/// ATA IDENTIFY DEVICE response (512 bytes).
/// Contains model number, firmware revision, serial number, and capabilities.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct IdSector
{
    public ushort wGenConfig;
    public ushort wNumCyls;
    public ushort wReserved;
    public ushort wNumHeads;
    public ushort wBytesPerTrack;
    public ushort wBytesPerSector;
    public ushort wSectorsPerTrack;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public ushort[] wVendorUnique;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] sSerialNumber;
    public ushort wBufferType;
    public ushort wBufferSize;
    public ushort wECCSize;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] sFirmwareRev;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
    public byte[] sModelNumber;
    public ushort wMoreVendorUnique;
    public ushort wDoubleWordIO;
    public ushort wCapabilities;
    public ushort wReserved1;
    public ushort wPIOTiming;
    public ushort wDMATiming;
    public ushort wBS;
    public ushort wNumCurrentCyls;
    public ushort wNumCurrentHeads;
    public ushort wNumCurrentSectorsPerTrack;
    public uint ulCurrentSectorCapacity;
    public ushort wMultSectorStuff;
    public uint ulTotalAddressableSectors;
    public ushort wSingleWordDMA;
    public ushort wMultiWordDMA;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 382)]
    public byte[] bReserved;
}
