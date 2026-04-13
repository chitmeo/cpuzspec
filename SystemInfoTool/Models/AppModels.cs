namespace SystemInfoTool.Models;

/// <summary>
/// A point-in-time snapshot of all live sensor readings.
/// </summary>
public record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<CpuSensorData> CpuSensors,
    IReadOnlyList<GpuSensorData> GpuSensors
);

/// <summary>
/// Persisted application settings (serialised to %APPDATA%\SystemInfoTool\settings.json).
/// </summary>
public record AppSettings(
    int RefreshIntervalMs,
    WindowPlacement LastWindowPlacement
);

/// <summary>
/// Recorded main-window size and position used to restore after a tray cycle.
/// </summary>
public record WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height
);
