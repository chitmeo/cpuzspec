# Design Document: system-info-tool

## Overview

`system-info-tool` is a Windows desktop application built with C# (.NET 10) and WPF that surfaces detailed, real-time hardware information across five categories: CPU, Motherboard, Memory, Graphics, and Storage. It targets power users and IT professionals who need accurate, low-level hardware details in a single, responsive tool — similar in scope to CPU-Z.

The application follows the **MVVM** (Model-View-ViewModel) pattern throughout. Hardware data collection is isolated in a dedicated service layer, keeping the UI layer free of any platform or WMI concerns. Sensor values refresh on a configurable timer without blocking the UI thread.

### Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| UI pattern | MVVM | Clean separation; WPF data binding is first-class |
| Hardware data | WMI (`System.Management`) | Built-in, covers most hardware classes |
| CPU feature flags | P/Invoke CPUID | WMI does not expose ISA extensions |
| SMART data | `DeviceIoControl` P/Invoke | WMI `Win32_DiskDrive` lacks raw SMART attributes |
| System tray | `System.Windows.Forms.NotifyIcon` | Built-in; no third-party tray library needed |
| Dark/light theme | Registry read + `ResourceDictionary` swap | No third-party theming library needed |
| Async model | `Task` / `async-await` + `DispatcherTimer` | Keeps UI responsive during data collection |

---

## Architecture

The application is structured in four layers:

```
┌─────────────────────────────────────────────────────────┐
│                     Presentation Layer                   │
│  MainWindow.xaml  ·  Tab Views (XAML)  ·  Converters    │
│  ViewModels: MainViewModel, CpuViewModel, …              │
└────────────────────────┬────────────────────────────────┘
                         │ data binding / commands
┌────────────────────────▼────────────────────────────────┐
│                    Application Layer                     │
│  SensorPollingService  ·  SnapshotExportService          │
│  ThemeService  ·  TrayService                            │
└────────────────────────┬────────────────────────────────┘
                         │ calls
┌────────────────────────▼────────────────────────────────┐
│                   Hardware Service Layer                 │
│  CpuInfoService  ·  MotherboardInfoService               │
│  MemoryInfoService  ·  GraphicsInfoService               │
│  StorageInfoService  ·  SensorService                    │
└────────────────────────┬────────────────────────────────┘
                         │ queries
┌────────────────────────▼────────────────────────────────┐
│                   Platform / OS Layer                    │
│  System.Management (WMI)  ·  P/Invoke (CPUID, IOCTL)    │
│  Microsoft.Win32 (Registry)  ·  System.IO                │
└─────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

- **Presentation Layer** — XAML views, ViewModels, value converters, and styles. No hardware API calls.
- **Application Layer** — Orchestrates services; owns the sensor polling timer; handles tray and theme lifecycle.
- **Hardware Service Layer** — One service per hardware category. Each service exposes async `GetAsync()` methods that return strongly-typed model objects. `SensorService` exposes a `ReadSensorsAsync()` method called by the polling loop.
- **Platform Layer** — Raw WMI queries, P/Invoke declarations, and registry reads. Encapsulated inside service implementations; never called directly from ViewModels.

---

## Components and Interfaces

### 2.1 Hardware Services

Each hardware service implements a common pattern:

```csharp
// Static data — loaded once at startup
Task<T> GetStaticInfoAsync();

// Live sensor data — called on every polling tick
Task<TSensor> ReadSensorsAsync();
```

#### CpuInfoService
- Queries `Win32_Processor` (WMI) for name, core/thread counts, base clock, cache sizes, package type, TDP.
- Invokes the `CPUID` instruction via P/Invoke to enumerate ISA extension flags (SSE4.2, AVX2, AVX-512, etc.) from CPUID leaves 1, 7, and extended leaves.
- Queries `MSAcpi_ThermalZoneTemperature` (WMI, `root\wmi` namespace) for temperature; falls back to `null` if unavailable.
- Supports multi-socket systems by iterating all `Win32_Processor` instances.

#### MotherboardInfoService
- Queries `Win32_BaseBoard` for manufacturer, product, version.
- Queries `Win32_BIOS` for vendor, version, release date.
- Queries `Win32_SystemSlot` and `Win32_PhysicalMemoryArray` for slot counts.
- Attempts chipset identification via `Win32_PnPEntity` (device class "System") and registry path `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor`.

#### MemoryInfoService
- Queries `Win32_PhysicalMemory` for per-slot data: capacity, manufacturer, part number, serial number, speed, memory type, form factor, configured clock speed.
- Derives total installed RAM and memory type from aggregated slot data.
- Channel configuration (Single/Dual/Quad) is inferred from populated slot count and board slot layout; exact channel detection is noted as best-effort.
- XMP/EXPO profile frequency is read from SPD data via `Win32_PhysicalMemory.ConfiguredClockSpeed` vs `Speed`.

#### GraphicsInfoService
- Queries `Win32_VideoController` for name, adapter RAM, driver version, driver date, video memory type.
- GPU chip model is extracted from the adapter description string where possible.
- Live clocks and temperature require vendor-specific paths: for NVIDIA, queries `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine`; for AMD/Intel, falls back to WMI performance counters or marks as unavailable.

#### StorageInfoService
- Queries `Win32_DiskDrive` for model, manufacturer, interface type, size, firmware revision.
- SMART health status is retrieved via `DeviceIoControl` P/Invoke (`IOCTL_STORAGE_QUERY_PROPERTY` / `IOCTL_SMART_GET_VERSION` + `IOCTL_SMART_RCV_DRIVE_DATA`) on `\\.\PhysicalDriveN` handles.
- SMART summary is mapped to `Good / Caution / Bad` based on the overall SMART status byte and critical attribute thresholds.
- If `DeviceIoControl` fails (e.g., USB drives, access denied), the field is set to `"Unavailable"`.

#### SensorService
- Aggregates live readings from `CpuInfoService`, `GraphicsInfoService`, and any available performance counters.
- Called by `SensorPollingService` on each timer tick.
- Returns a `SensorSnapshot` record containing all live values.

### 2.2 Application Services

#### SensorPollingService
- Owns a `System.Windows.Threading.DispatcherTimer` (fires on UI thread) or a `System.Threading.PeriodicTimer` (fires on background thread, marshalled via `Dispatcher.InvokeAsync`).
- Interval is configurable between 500 ms and 10 000 ms; defaults to 1 000 ms.
- Exposes `Start()`, `Stop()`, and `SetInterval(TimeSpan)` methods.
- Pauses automatically when the application is minimised to tray; resumes on restore.

#### SnapshotExportService
- Accepts the current set of hardware model objects and serialises them to a plain-text report.
- Uses `Microsoft.Win32.SaveFileDialog` for the destination path.
- Writes via `System.IO.StreamWriter`; handles `IOException` and reports failure without overwriting an existing file (write to a temp path, then `File.Replace`).

#### ThemeService
- Reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (DWORD) to detect the current system theme.
- Swaps the application's `ResourceDictionary` between `Themes/Light.xaml` and `Themes/Dark.xaml` at startup and on system theme change.
- Monitors registry changes via `RegistryKey.OpenSubKey` + a background `ManagementEventWatcher` on `RegistryValueChangeEvent` (WMI), or a `SystemEvents.UserPreferenceChanged` event from `Microsoft.Win32`.

#### TrayService
- Wraps `System.Windows.Forms.NotifyIcon` (requires `Microsoft.WindowsDesktop.App` reference, already included in WPF .NET 10 projects via `UseWPF=true`).
- Provides `MinimizeToTray()` and `RestoreFromTray()` methods.
- Builds a `ContextMenuStrip` with "Open" and "Exit" items.
- Fires `RestoreRequested` and `ExitRequested` events consumed by `MainViewModel`.

### 2.3 ViewModels

All ViewModels inherit from `ObservableObject` (a base class implementing `INotifyPropertyChanged` using `[CallerMemberName]`). Commands use a lightweight `RelayCommand` / `RelayCommand<T>` implementation — no external MVVM framework required.

| ViewModel | Responsibility |
|---|---|
| `MainViewModel` | Tab selection, refresh interval binding, export command, tray integration |
| `CpuViewModel` | CPU static info + live clock/temp; multi-socket dropdown |
| `MotherboardViewModel` | Motherboard and BIOS fields |
| `MemoryViewModel` | Total RAM, channel config, per-slot collection |
| `GraphicsViewModel` | Per-GPU collection, live clocks/temp, GPU selector dropdown |
| `StorageViewModel` | Per-drive collection, SMART status |

### 2.4 Views (XAML)

- `MainWindow.xaml` — hosts a `TabControl` bound to `MainViewModel.SelectedTab`; contains the menu bar (File > Export Snapshot, View > Refresh Interval).
- `CpuView.xaml`, `MotherboardView.xaml`, `MemoryView.xaml`, `GraphicsView.xaml`, `StorageView.xaml` — one `UserControl` per tab.
- `Themes/Light.xaml`, `Themes/Dark.xaml` — `ResourceDictionary` files defining colour brushes, font sizes, and control styles.
- All views use `{Binding}` exclusively; no code-behind logic beyond `InitializeComponent()`.

### 2.5 P/Invoke Layer

A static `NativeMethods` class (internal, not public) declares all P/Invoke signatures:

```csharp
// CPUID
[DllImport("kernel32.dll")]
static extern bool IsProcessorFeaturePresent(uint processorFeature);

// For raw CPUID leaf access — inline assembly not available in C#;
// a small native helper DLL (CpuidHelper.dll, ~2 KB) exposes:
//   void __cpuid(int[4] cpuInfo, int functionId)
// This is the ONE case where a tiny native shim is needed.

// SMART / DeviceIoControl
[DllImport("kernel32.dll", SetLastError = true)]
static extern SafeFileHandle CreateFile(...);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool DeviceIoControl(...);
```

> **Note on CPUID**: The `__cpuid` intrinsic is not directly callable from managed C#. The design uses a minimal native helper (`CpuidHelper.dll`) compiled from ~30 lines of C++ that exposes a single `cpuid_query(int leaf, int subleaf, int[4] out)` function. This is not a third-party library — it is a project-owned shim. Alternatively, `System.Runtime.Intrinsics.X86.X86Base.CpuId()` (available in .NET 5+) can be used directly, eliminating the native shim entirely.

---

## Data Models

All models are immutable records (C# `record` types) for static hardware data. Sensor readings use mutable classes with `INotifyPropertyChanged` so the UI updates in place.

### 3.1 CPU Models

```csharp
record CpuStaticInfo(
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
    IReadOnlyList<string> IsaExtensions,  // e.g. ["SSE4.2", "AVX2", "AVX-512F"]
    double? TdpWatts
);

record CacheInfo(long SizeKb, int Associativity, string Type);  // Type: "Data"|"Instruction"|"Unified"

class CpuSensorData : ObservableObject
{
    double CurrentClockMhz { get; set; }
    double? TemperatureCelsius { get; set; }
}
```

### 3.2 Motherboard Models

```csharp
record MotherboardInfo(
    string Manufacturer,
    string ProductName,
    string Version,
    string BiosVendor,
    string BiosVersion,
    string BiosReleaseDate,
    string? ChipsetModel,       // null if undetectable
    int TotalMemorySlots,
    int PopulatedMemorySlots
);
```

### 3.3 Memory Models

```csharp
record MemoryInfo(
    long TotalInstalledMb,
    string MemoryType,          // "DDR4", "DDR5", etc.
    string ChannelConfig,       // "Single", "Dual", "Quad", "Unknown"
    int CurrentFrequencyMhz,
    int? XmpFrequencyMhz,
    IReadOnlyList<MemorySlotInfo> Slots
);

record MemorySlotInfo(
    string SlotLabel,           // e.g. "DIMM_A1"
    bool IsPopulated,
    long? CapacityMb,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    int? SpeedMhz,
    MemoryTimings? Timings
);

record MemoryTimings(int CL, int tRCD, int tRP, int tRAS);
```

### 3.4 Graphics Models

```csharp
record GpuStaticInfo(
    int AdapterIndex,
    string Name,
    string? ChipModel,
    string Manufacturer,
    long VideoMemoryMb,
    string? MemoryType,         // "GDDR6", "LPDDR5", etc.
    string DriverVersion,
    string DriverDate
);

class GpuSensorData : ObservableObject
{
    double? CoreClockMhz { get; set; }
    double? MemoryClockMhz { get; set; }
    double? TemperatureCelsius { get; set; }
}
```

### 3.5 Storage Models

```csharp
record DriveInfo(
    int DriveIndex,
    string Model,
    string? Manufacturer,
    string InterfaceType,       // "SATA", "NVMe", "USB", etc.
    long CapacityGb,
    string? FirmwareRevision,
    SmartStatus SmartHealth     // Good | Caution | Bad | Unavailable
);

enum SmartStatus { Good, Caution, Bad, Unavailable }
```

### 3.6 Sensor Snapshot

```csharp
record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<CpuSensorData> CpuSensors,
    IReadOnlyList<GpuSensorData> GpuSensors
);
```

### 3.7 Application Settings

```csharp
record AppSettings(
    int RefreshIntervalMs,      // 500–10000, default 1000
    WindowPlacement LastWindowPlacement
);
```

Settings are persisted to `%APPDATA%\SystemInfoTool\settings.json` using `System.Text.Json`.

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Sensor polling interval clamping and application

*For any* integer value passed as a refresh interval, `SensorPollingService.ClampInterval(value)` SHALL return a value in the closed range [500, 10 000], and after `SetInterval()` is called with any valid value, the next scheduled polling tick SHALL use that new interval rather than the previous one.

**Validates: Requirements 7.1, 7.3**

---

### Property 2: Snapshot export round-trip completeness

*For any* set of hardware model objects currently held in memory, serialising them to a plain-text snapshot and then parsing the snapshot back SHALL recover every field name and value that was present in the original models — no field is silently dropped or truncated.

**Validates: Requirements 8.2**

---

### Property 3: Graceful degradation on hardware data failure

*For any* hardware service (`CpuInfoService`, `MotherboardInfoService`, `MemoryInfoService`, `GraphicsInfoService`, `StorageInfoService`) and any exception type thrown by the underlying WMI query, P/Invoke call, or registry read, the service SHALL return a model object with all affected fields set to the defined sentinel value (`null`, `"Unavailable"`, or `"N/A"`) and SHALL NOT propagate the exception to the caller.

**Validates: Requirements 1.5, 3.5, 4.7, 6.6**

---

### Property 4: Theme detection from registry value

*For any* DWORD value read from the `AppsUseLightTheme` registry key, `ThemeService.DetectTheme(value)` SHALL return `Theme.Light` when the value is 1 and `Theme.Dark` for any other value (including 0 and missing key), ensuring a defined theme is always selected.

**Validates: Requirements 10.2**

---

### Property 5: Tray suspend/resume preserves polling state

*For any* configured refresh interval, after a minimize-to-tray event `SensorPollingService.IsRunning` SHALL be `false`, and after the subsequent restore-from-tray event `SensorPollingService.IsRunning` SHALL be `true` with `SensorPollingService.Interval` equal to the interval that was set before the minimize — the interval is never reset to the default by a tray cycle.

**Validates: Requirements 7.4, 7.5**

---

### Property 6: Hardware collection completeness

*For any* list of WMI result objects returned by `Win32_VideoController` or `Win32_DiskDrive`, the corresponding model collection (`IReadOnlyList<GpuStaticInfo>` or `IReadOnlyList<DriveInfo>`) SHALL contain exactly as many entries as the input list — no device is silently dropped during mapping.

**Validates: Requirements 5.1, 6.1**

---

### Property 7: Memory slot aggregation correctness

*For any* list of `MemorySlotInfo` objects, the computed `TotalInstalledMb` SHALL equal the sum of `CapacityMb` for all slots where `IsPopulated` is `true`, and `PopulatedMemorySlots` SHALL equal the count of slots where `IsPopulated` is `true`.

**Validates: Requirements 3.4, 4.1**

---

### Property 8: Storage capacity unit conversion

*For any* raw capacity value in bytes returned by WMI, the `CapacityGb` field in `DriveInfo` SHALL equal `Math.Floor(bytes / 1_000_000_000.0)` — the conversion is lossless in the downward direction and never rounds up.

**Validates: Requirements 6.3**

---

### Property 9: SMART status classification completeness

*For any* set of raw SMART attribute values returned by `DeviceIoControl`, the `SmartStatus` classification function SHALL return exactly one of `{Good, Caution, Bad}` for valid data and `Unavailable` for null or error data — the result is never undefined or an unhandled enum value.

**Validates: Requirements 6.5, 6.6**

---

### Property 10: CPU temperature unit conversion

*For any* raw temperature value returned by `MSAcpi_ThermalZoneTemperature` (expressed as Kelvin × 10), the conversion to degrees Celsius SHALL equal `(rawValue / 10.0) - 273.15` within floating-point tolerance — the formula is applied consistently and never inverted or offset incorrectly.

**Validates: Requirements 2.8**

---

### Property 11: ISA extension flag mapping

*For any* CPUID leaf bitmask, the set of ISA extension strings returned by the flag-mapping function SHALL contain exactly the extensions whose corresponding bits are set in the bitmask — no extension is reported when its bit is clear, and no extension is omitted when its bit is set.

**Validates: Requirements 2.5**

---

### Property 12: Snapshot export file preservation on failure

*For any* pre-existing file at the export destination path, if an `IOException` occurs during the write operation, the file at the destination path SHALL retain its original content — the write-to-temp-then-replace strategy ensures the original is never partially overwritten.

**Validates: Requirements 8.5**

---

### Property 13: Window placement round-trip

*For any* `WindowPlacement` value recorded before a minimize-to-tray event, after the subsequent restore-from-tray event the main window's size and position SHALL equal the recorded placement — the placement is not lost or corrupted by the tray cycle.

**Validates: Requirements 9.2**

---

## Error Handling

### Strategy

All hardware service methods are wrapped in `try/catch` blocks. Exceptions are caught at the service boundary and converted to either a sentinel value in the model or a structured `ServiceResult<T>` that carries an error message. ViewModels expose an `ErrorMessage` property that the view binds to a visible error panel within the tab.

The application never propagates unhandled exceptions to the WPF dispatcher; a global `Application.DispatcherUnhandledException` handler logs the error and shows a non-fatal dialog.

### Per-Component Error Handling

| Component | Failure Mode | Handling |
|---|---|---|
| WMI query | `ManagementException`, `COMException` | Catch, log, return null/sentinel model |
| CPUID P/Invoke | `SEHException`, `AccessViolationException` | Catch in native shim boundary; return empty ISA list |
| SMART DeviceIoControl | `Win32Exception`, access denied | Set `SmartStatus.Unavailable` |
| Snapshot export | `IOException`, `UnauthorizedAccessException` | Write to temp file first; show error dialog on failure |
| Theme registry read | `SecurityException` | Default to light theme |
| Settings file | `JsonException`, `IOException` | Fall back to default `AppSettings` |

### Startup Error Handling

Hardware data collection runs asynchronously after the main window is shown. If a tab's data collection fails entirely, the tab displays a styled error panel with the exception message and a "Retry" button that re-invokes the service.
