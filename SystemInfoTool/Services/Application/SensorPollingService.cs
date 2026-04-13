using System.Windows.Threading;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.Services.Application;

/// <summary>
/// Polls CPU and GPU sensors on a background thread at a configurable interval
/// and marshals results to the UI thread via <see cref="Dispatcher.InvokeAsync"/>.
/// </summary>
public sealed class SensorPollingService : IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const int MinIntervalMs = 500;
    private const int MaxIntervalMs = 10_000;
    private const int DefaultIntervalMs = 1_000;

    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly CpuInfoService _cpuInfoService;
    private readonly GraphicsInfoService _graphicsInfoService;
    private readonly Dispatcher _dispatcher;

    private TimeSpan _interval = TimeSpan.FromMilliseconds(DefaultIntervalMs);
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="SensorPollingService"/>.
    /// </summary>
    /// <param name="cpuInfoService">Service used to read CPU sensor data.</param>
    /// <param name="graphicsInfoService">Service used to read GPU sensor data.</param>
    /// <param name="dispatcher">
    /// WPF dispatcher used to marshal sensor updates to the UI thread.
    /// Defaults to <c>Application.Current.Dispatcher</c> when <c>null</c>.
    /// </param>
    public SensorPollingService(
        CpuInfoService cpuInfoService,
        GraphicsInfoService graphicsInfoService,
        Dispatcher? dispatcher = null)
    {
        _cpuInfoService = cpuInfoService ?? throw new ArgumentNullException(nameof(cpuInfoService));
        _graphicsInfoService = graphicsInfoService ?? throw new ArgumentNullException(nameof(graphicsInfoService));
        _dispatcher = dispatcher ?? System.Windows.Application.Current.Dispatcher;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised on the UI thread each time a new <see cref="SensorSnapshot"/> is available.
    /// </summary>
    public event EventHandler<SensorSnapshot>? OnSensorUpdate;

    /// <summary>
    /// Gets a value indicating whether the polling loop is currently active.
    /// </summary>
    public bool IsRunning => _pollingTask is { IsCompleted: false };

    /// <summary>
    /// Clamps <paramref name="ms"/> to the valid interval range [500, 10 000].
    /// </summary>
    /// <param name="ms">Requested interval in milliseconds.</param>
    /// <returns>A value in the closed range [500, 10 000].</returns>
    public static int ClampInterval(int ms) => Math.Clamp(ms, MinIntervalMs, MaxIntervalMs);

    /// <summary>
    /// Starts the sensor polling loop. No-op if already running.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _pollingTask = RunPollingLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the sensor polling loop. No-op if not running.
    /// </summary>
    public void Stop()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Updates the polling interval. The new interval is clamped to [500 ms, 10 000 ms]
    /// and takes effect on the next timer tick.
    /// </summary>
    /// <param name="interval">Requested polling interval.</param>
    public void SetInterval(TimeSpan interval)
    {
        int clampedMs = ClampInterval((int)interval.TotalMilliseconds);
        _interval = TimeSpan.FromMilliseconds(clampedMs);
    }

    // -------------------------------------------------------------------------
    // Background polling loop
    // -------------------------------------------------------------------------

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // Capture the current interval; if it changed, recreate the timer.
                // PeriodicTimer does not support period changes after construction,
                // so we restart the loop when the interval is updated.
                TimeSpan currentInterval = _interval;

                SensorSnapshot snapshot = await CollectSnapshotAsync().ConfigureAwait(false);

                await _dispatcher.InvokeAsync(() => OnSensorUpdate?.Invoke(this, snapshot));

                // If the interval changed, restart the loop with the new period.
                if (_interval != currentInterval)
                {
                    // Re-enter with the updated interval by cancelling and restarting.
                    // The caller (Start) will not restart automatically; we do it here.
                    _ = Task.Run(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            // Restart the inner loop with the new interval.
                            _ = RunPollingLoopAsync(cancellationToken);
                        }
                    }, CancellationToken.None);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow.
        }
        catch (Exception)
        {
            // Unexpected error — swallow to keep the service alive.
        }
    }

    private async Task<SensorSnapshot> CollectSnapshotAsync()
    {
        IReadOnlyList<CpuSensorData> cpuSensors;
        IReadOnlyList<GpuSensorData> gpuSensors;

        try
        {
            cpuSensors = await _cpuInfoService.ReadSensorsAsync().ConfigureAwait(false);
        }
        catch
        {
            cpuSensors = Array.Empty<CpuSensorData>();
        }

        try
        {
            gpuSensors = await _graphicsInfoService.ReadSensorsAsync().ConfigureAwait(false);
        }
        catch
        {
            gpuSensors = Array.Empty<GpuSensorData>();
        }

        return new SensorSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            CpuSensors: cpuSensors,
            GpuSensors: gpuSensors
        );
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }
}
