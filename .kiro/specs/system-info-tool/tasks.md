# Implementation Plan: system-info-tool

## Overview

Incremental implementation of the CPU-Z-like Windows desktop application in C# (.NET 10) + WPF following the MVVM pattern. Tasks are ordered so each step compiles and runs before the next begins. Hardware services are built one category at a time, each wired into its ViewModel and View before moving on.

## Tasks

- [x] 1. Project scaffold and core infrastructure
  - Create a new WPF (.NET 10) solution `SystemInfoTool.sln` with  project:
    - `SystemInfoTool` — main WPF application (`<UseWPF>true</UseWPF>`)
  - Add `<UseWindowsForms>true</UseWindowsForms>` to the main project for `NotifyIcon` support
  - Add `System.Management` NuGet package to the main project (WMI access)
  - Define the folder structure: `Models/`, `Services/Hardware/`, `Services/Application/`, `ViewModels/`, `Views/`, `Themes/`, `PInvoke/`
  - Implement `ObservableObject` base class (`INotifyPropertyChanged` via `[CallerMemberName]`)
  - Implement `RelayCommand` and `RelayCommand<T>` (no external MVVM framework)
  - _Requirements: 1.1, 1.4_

- [x] 2. Data models
  - [x] 2.1 Define all immutable record types and enums
    - Implement `CpuStaticInfo`, `CacheInfo`, `CpuSensorData` (Models/CpuModels.cs)
    - Implement `MotherboardInfo` (Models/MotherboardModels.cs)
    - Implement `MemoryInfo`, `MemorySlotInfo`, `MemoryTimings` (Models/MemoryModels.cs)
    - Implement `GpuStaticInfo`, `GpuSensorData` (Models/GraphicsModels.cs)
    - Implement `DriveInfo`, `SmartStatus` enum (Models/StorageModels.cs)
    - Implement `SensorSnapshot`, `AppSettings`, `WindowPlacement` (Models/AppModels.cs)
    - _Requirements: 2.1–2.8, 3.1–3.5, 4.1–4.7, 5.1–5.7, 6.1–6.6_

- [x] 3. P/Invoke layer and CPUID support
  - [x] 3.1 Implement `NativeMethods` static class (PInvoke/NativeMethods.cs)
    - Declare `CreateFile`, `DeviceIoControl`, `CloseHandle` P/Invoke signatures for SMART access
    - Define IOCTL constants: `IOCTL_STORAGE_QUERY_PROPERTY`, `IOCTL_SMART_GET_VERSION`, `IOCTL_SMART_RCV_DRIVE_DATA`
    - Use `System.Runtime.Intrinsics.X86.X86Base.CpuId()` for CPUID leaf queries (eliminates native shim)
    - _Requirements: 2.5, 6.4, 6.5_

  - [x] 3.2 Implement ISA extension flag mapping helper (Services/Hardware/CpuIdHelper.cs)
    - Query CPUID leaves 1, 7, and extended leaves via `X86Base.CpuId()`
    - Map bitmask bits to extension name strings (SSE4.2, AVX2, AVX-512F, etc.)
    - Wrap in `try/catch`; return empty list on `PlatformNotSupportedException`
    - _Requirements: 2.5_

- [x] 4. CPU hardware service
  - [x] 4.1 Implement `CpuInfoService` (Services/Hardware/CpuInfoService.cs)
    - Query `Win32_Processor` (WMI) for name, core/thread counts, base clock, cache sizes, package type, TDP
    - Call `CpuIdHelper` for ISA extensions
    - Query `MSAcpi_ThermalZoneTemperature` (`root\wmi`) for temperature; return `null` if unavailable
    - Support multi-socket by iterating all `Win32_Processor` instances
    - Wrap all WMI calls in `try/catch (ManagementException, COMException)`; return sentinel model on failure
    - _Requirements: 2.1–2.8_

  - [x] 4.2 Implement CPU temperature unit conversion helper
    - Convert raw WMI Kelvin×10 value to Celsius: `(rawValue / 10.0) - 273.15`
    - _Requirements: 2.8_

- [x] 5. Motherboard hardware service
  - [x] 5.1 Implement `MotherboardInfoService` (Services/Hardware/MotherboardInfoService.cs)
    - Query `Win32_BaseBoard` for manufacturer, product, version
    - Query `Win32_BIOS` for vendor, version, release date
    - Query `Win32_PhysicalMemoryArray` for total and populated slot counts
    - Attempt chipset identification via `Win32_PnPEntity` and registry
    - Return `"Unavailable"` for any field that cannot be read; never propagate exceptions
    - _Requirements: 3.1–3.5_

- [x] 6. Memory hardware service
  - [x] 6.1 Implement `MemoryInfoService` (Services/Hardware/MemoryInfoService.cs)
    - Query `Win32_PhysicalMemory` for per-slot: capacity, manufacturer, part number, serial number, speed, memory type, configured clock speed
    - Aggregate total installed RAM and memory type from slot data
    - Infer channel configuration from populated slot count
    - Derive XMP frequency from `ConfiguredClockSpeed` vs `Speed`
    - Return `"N/A"` for SPD-dependent fields when inaccessible
    - _Requirements: 4.1–4.7_

- [ ] 7. Graphics hardware service
  - [x] 7.1 Implement `GraphicsInfoService` (Services/Hardware/GraphicsInfoService.cs)
    - Query `Win32_VideoController` for name, adapter RAM, driver version, driver date, video memory type
    - Extract GPU chip model from adapter description string
    - Attempt live clock/temperature via `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` (NVIDIA); mark unavailable for others
    - Wrap all queries in `try/catch`; return sentinel model on failure
    - _Requirements: 5.1–5.7_

- [x] 8. Storage hardware service
  - [x] 8.1 Implement `StorageInfoService` (Services/Hardware/StorageInfoService.cs)
    - Query `Win32_DiskDrive` for model, manufacturer, interface type, size, firmware revision
    - Open `\\.\PhysicalDriveN` handles via `CreateFile` P/Invoke
    - Issue `IOCTL_SMART_GET_VERSION` + `IOCTL_SMART_RCV_DRIVE_DATA` via `DeviceIoControl`
    - Map SMART overall status byte and critical attribute thresholds to `SmartStatus` enum
    - Set `SmartStatus.Unavailable` on `Win32Exception`, access denied, or USB drives
    - _Requirements: 6.1–6.6_

  - [x] 8.2 Implement SMART status classification function
    - Accept raw SMART attribute set; return exactly one of `{Good, Caution, Bad}` for valid data
    - Return `Unavailable` for null or error data; no unhandled enum value
    - _Requirements: 6.5, 6.6_

- [x] 9. Checkpoint — Ensure all hardware services compile and run correctly
  - Build and verify the project compiles; ask the user if questions arise.

- [x] 10. SensorPollingService
  - [x] 10.1 Implement `SensorPollingService` (Services/Application/SensorPollingService.cs)
    - Own a `System.Threading.PeriodicTimer` on a background thread; marshal results via `Dispatcher.InvokeAsync`
    - Implement `ClampInterval(int ms)` — clamps to [500, 10 000]
    - Implement `Start()`, `Stop()`, `SetInterval(TimeSpan)` methods
    - Expose `IsRunning` property and `OnSensorUpdate` event carrying `SensorSnapshot`
    - Default interval: 1 000 ms
    - _Requirements: 7.1–7.5_

- [x] 11. ThemeService
  - [x] 11.1 Implement `ThemeService` (Services/Application/ThemeService.cs)
    - Read `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (DWORD)
    - Implement `DetectTheme(uint value)` — returns `Theme.Light` for 1, `Theme.Dark` for all other values
    - Swap `ResourceDictionary` between `Themes/Light.xaml` and `Themes/Dark.xaml`
    - Subscribe to `SystemEvents.UserPreferenceChanged` to react to live theme changes
    - Default to light theme on `SecurityException`
    - _Requirements: 10.2_

- [x] 12. TrayService
  - [x] 12.1 Implement `TrayService` (Services/Application/TrayService.cs)
    - Wrap `System.Windows.Forms.NotifyIcon` with an embedded application icon
    - Build `ContextMenuStrip` with "Open" and "Exit" items
    - Implement `MinimizeToTray()` — hides main window, removes from taskbar, shows tray icon
    - Implement `RestoreFromTray()` — shows main window, restores to previous size/position
    - Fire `RestoreRequested` and `ExitRequested` events
    - _Requirements: 9.1–9.4_

  - [x] 12.2 Implement `WindowPlacement` persistence
    - Record window size and position before minimize
    - Restore exact size and position on `RestoreFromTray()`
    - Persist `AppSettings` (including `LastWindowPlacement`) to `%APPDATA%\SystemInfoTool\settings.json` via `System.Text.Json`
    - _Requirements: 9.2_

- [x] 13. SnapshotExportService
  - [x] 13.1 Implement `SnapshotExportService` (Services/Application/SnapshotExportService.cs)
    - Accept all hardware model objects; serialise to a plain-text report (field name: value, one per line, grouped by category)
    - Implement `Parse()` method that recovers all field names and values from the text report
    - Use `Microsoft.Win32.SaveFileDialog` for destination path selection
    - Write to a temp file first; use `File.Replace` to atomically swap on success
    - Handle `IOException` and `UnauthorizedAccessException`; never overwrite existing file on failure
    - _Requirements: 8.1–8.5_

- [x] 14. Checkpoint — Ensure all application services compile and run correctly
  - Build and verify the project compiles; ask the user if questions arise.

- [x] 15. Theme resource dictionaries and base styles
  - [x] 15.1 Create `Themes/Light.xaml` and `Themes/Dark.xaml` resource dictionaries
    - Define colour brushes: background, foreground, accent, border, error panel background
    - Define base `Style` for `TextBlock`, `Label`, `TabControl`, `TabItem`, `ComboBox`, `Button`
    - Minimum body font size: 11pt; all sizes defined as named resources
    - _Requirements: 10.2, 10.4_

  - [x] 15.2 Configure DPI awareness in app manifest
    - Set `<dpiAware>true/PM</dpiAware>` and `<dpiAwareness>PerMonitorV2</dpiAwareness>` in `app.manifest`
    - Set `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` in `Program.cs` entry point
    - _Requirements: 10.1_

- [x] 16. ViewModels
  - [x] 16.1 Implement `MainViewModel`
    - Properties: `SelectedTabIndex`, `RefreshIntervalMs`, `IsBusy`
    - Commands: `ExportSnapshotCommand`, `ExitCommand`
    - Inject `SensorPollingService`, `SnapshotExportService`, `TrayService`, `ThemeService`
    - Wire `TrayService.RestoreRequested` → restore window; `TrayService.ExitRequested` → clean shutdown
    - _Requirements: 1.2, 1.3, 7.1–7.3, 8.1, 9.3, 9.4_

  - [x] 16.2 Implement `CpuViewModel`
    - Properties: `StaticInfo` (`CpuStaticInfo`), `SensorData` (`CpuSensorData`), `SocketList`, `SelectedSocketIndex`, `ErrorMessage`
    - Load static info on construction via `CpuInfoService.GetStaticInfoAsync()`
    - Update `SensorData` on each `SensorPollingService.OnSensorUpdate` event
    - Expose `ErrorMessage` for binding to error panel
    - _Requirements: 2.1–2.8_

  - [x] 16.3 Implement `MotherboardViewModel`
    - Properties: `Info` (`MotherboardInfo`), `ErrorMessage`
    - Load on construction via `MotherboardInfoService.GetStaticInfoAsync()`
    - _Requirements: 3.1–3.5_

  - [x] 16.4 Implement `MemoryViewModel`
    - Properties: `Info` (`MemoryInfo`), `Slots` (observable collection of `MemorySlotInfo`), `ErrorMessage`
    - Load on construction via `MemoryInfoService.GetStaticInfoAsync()`
    - _Requirements: 4.1–4.7_

  - [x] 16.5 Implement `GraphicsViewModel`
    - Properties: `GpuList` (observable collection of `GpuStaticInfo`), `SelectedGpuIndex`, `SensorData` (`GpuSensorData`), `ErrorMessage`
    - Load static info on construction; update sensor data on polling tick
    - _Requirements: 5.1–5.7_

  - [x] 16.6 Implement `StorageViewModel`
    - Properties: `Drives` (observable collection of `DriveInfo`), `ErrorMessage`
    - Load on construction via `StorageInfoService.GetAllDrivesAsync()`
    - _Requirements: 6.1–6.6_

- [x] 17. Main window and tab views (XAML)
  - [x] 17.1 Implement `MainWindow.xaml`
    - `TabControl` bound to `MainViewModel.SelectedTabIndex`; tabs: CPU, Motherboard, Memory, Graphics, Storage
    - Menu bar: File > Export Snapshot (bound to `ExportSnapshotCommand`); View > Refresh Interval (slider/spinner 500–10 000 ms)
    - Default selected tab: CPU (index 0)
    - Minimum window size: 800×600; resizable to full screen
    - Handle `Window.StateChanged` to call `TrayService.MinimizeToTray()` on minimize
    - _Requirements: 1.1–1.5, 7.1–7.3, 8.1, 9.1_

  - [x] 17.2 Implement `CpuView.xaml`
    - Display: full name, code name, package type, core/thread/logical counts, base clock, current clock
    - Display: L1/L2/L3 cache sizes and associativity, ISA extensions list, TDP, temperature
    - Multi-socket `ComboBox` bound to `CpuViewModel.SocketList` / `SelectedSocketIndex`
    - Error panel (styled, visible when `ErrorMessage` is non-null)
    - _Requirements: 2.1–2.8_

  - [x] 17.3 Implement `MotherboardView.xaml`
    - Display: manufacturer, product name, revision, BIOS vendor, BIOS version, BIOS release date, chipset, memory slot counts
    - Error panel
    - _Requirements: 3.1–3.5_

  - [x] 17.4 Implement `MemoryView.xaml`
    - Display: total RAM, memory type, channel config, current frequency, XMP frequency
    - `DataGrid` or `ItemsControl` for per-slot details: slot label, capacity, manufacturer, part number, serial number, timings
    - Error panel
    - _Requirements: 4.1–4.7_

  - [x] 17.5 Implement `GraphicsView.xaml`
    - Display: GPU name, chip model, manufacturer, VRAM, memory type, driver version, driver date, core clock, memory clock, temperature
    - Multi-GPU `ComboBox` bound to `GraphicsViewModel.GpuList` / `SelectedGpuIndex`
    - Error panel
    - _Requirements: 5.1–5.7_

  - [x] 17.6 Implement `StorageView.xaml`
    - Display: per-drive list with model, manufacturer, interface type, capacity, firmware revision, SMART health indicator (colour-coded: Good=green, Caution=yellow, Bad=red, Unavailable=grey)
    - Error panel
    - _Requirements: 6.1–6.6_

- [x] 18. Application entry point and wiring
  - [x] 18.1 Implement `App.xaml.cs` startup sequence
    - Register `Application.DispatcherUnhandledException` handler (log + non-fatal dialog)
    - Instantiate all services; inject into ViewModels
    - Call `ThemeService.ApplyTheme()` before main window is shown
    - Load `AppSettings` from `%APPDATA%\SystemInfoTool\settings.json`; fall back to defaults on error
    - Start `SensorPollingService` after main window is shown
    - _Requirements: 1.1, 1.4, 7.2, 10.2_

  - [x] 18.2 Implement clean shutdown
    - On `TrayService.ExitRequested` or `Application.Exit`: stop `SensorPollingService`, dispose `TrayService` (`NotifyIcon`), save `AppSettings`
    - _Requirements: 9.4_

- [x] 19. Checkpoint — Full application wiring complete
  - Build and verify the project compiles end-to-end; ask the user if questions arise.

- [x] 20. Integration tests (live system, CI-excluded)
  - [x] 20.1 Write integration test: `CpuInfoService` returns non-null model with non-empty `Name`
    - Tag with `[Trait("Category", "Integration")]`
    - _Requirements: 2.1_

  - [x] 20.2 Write integration test: `StorageInfoService` returns at least one drive
    - Tag with `[Trait("Category", "Integration")]`
    - _Requirements: 6.1_

  - [x] 20.3 Write integration test: `SnapshotExportService` writes a readable file to a temp path
    - Tag with `[Trait("Category", "Integration")]`
    - _Requirements: 8.2, 8.3_

- [x] 21. Final checkpoint — Verify integration tests pass
  - Run integration tests and ask the user if questions arise.

## Notes
- Each task references specific requirements for traceability
- `System.Runtime.Intrinsics.X86.X86Base.CpuId()` is used for CPUID — no native shim required
- SMART access requires the application to run with administrator privileges; document this in the README
- Sensor data for AMD/Intel GPUs is best-effort via WMI performance counters; NVIDIA path uses `GPUPerformanceCounters`
