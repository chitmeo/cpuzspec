// Feature: system-info-tool, Property 1: Sensor polling interval clamping
// For any integer value passed as a refresh interval,
// SensorPollingService.ClampInterval(value) SHALL return a value in the
// closed range [500, 10 000].
//
// Validates: Requirements 7.1, 7.3

using FsCheck;
using FsCheck.Xunit;
using SystemInfoTool.Services.Application;

namespace SystemInfoTool.Tests.Services.Application;

public class SensorPollingServiceTests
{
    // -------------------------------------------------------------------------
    // Property 1 — ClampInterval always returns a value in [500, 10 000]
    // -------------------------------------------------------------------------

    /// <summary>
    /// For any integer value (including <see cref="int.MinValue"/> and
    /// <see cref="int.MaxValue"/>), <see cref="SensorPollingService.ClampInterval"/>
    /// must return a value in the closed range [500, 10 000].
    /// </summary>
    [Property(MaxTest = 1000)]
    public Property ClampInterval_AlwaysReturnsValueInValidRange_ForAnyInt()
    {
        return Prop.ForAll(Arb.From<int>(), value =>
        {
            int result = SensorPollingService.ClampInterval(value);
            return result >= 500 && result <= 10_000;
        });
    }

    // -------------------------------------------------------------------------
    // Unit tests — specific boundary and representative values
    // -------------------------------------------------------------------------

    [Fact]
    public void ClampInterval_BelowMinimum_Returns500()
    {
        Assert.Equal(500, SensorPollingService.ClampInterval(0));
        Assert.Equal(500, SensorPollingService.ClampInterval(-1));
        Assert.Equal(500, SensorPollingService.ClampInterval(int.MinValue));
        Assert.Equal(500, SensorPollingService.ClampInterval(499));
    }

    [Fact]
    public void ClampInterval_AtMinimum_Returns500()
    {
        Assert.Equal(500, SensorPollingService.ClampInterval(500));
    }

    [Fact]
    public void ClampInterval_WithinRange_ReturnsUnchanged()
    {
        Assert.Equal(1_000, SensorPollingService.ClampInterval(1_000));
        Assert.Equal(5_000, SensorPollingService.ClampInterval(5_000));
        Assert.Equal(9_999, SensorPollingService.ClampInterval(9_999));
    }

    [Fact]
    public void ClampInterval_AtMaximum_Returns10000()
    {
        Assert.Equal(10_000, SensorPollingService.ClampInterval(10_000));
    }

    [Fact]
    public void ClampInterval_AboveMaximum_Returns10000()
    {
        Assert.Equal(10_000, SensorPollingService.ClampInterval(10_001));
        Assert.Equal(10_000, SensorPollingService.ClampInterval(int.MaxValue));
    }
}
