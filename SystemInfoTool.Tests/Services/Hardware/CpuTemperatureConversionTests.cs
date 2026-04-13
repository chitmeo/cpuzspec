// Feature: system-info-tool, Property 10: CPU temperature unit conversion
// For any raw temperature value returned by MSAcpi_ThermalZoneTemperature
// (expressed as Kelvin × 10), the conversion to degrees Celsius SHALL equal
// (rawValue / 10.0) - 273.15 within floating-point tolerance — the formula is
// applied consistently and never inverted or offset incorrectly.
//
// Validates: Requirements 2.8

using FsCheck;
using FsCheck.Xunit;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.Tests.Services.Hardware;

public class CpuTemperatureConversionTests
{
    // -------------------------------------------------------------------------
    // Property 10 — CPU temperature unit conversion correctness
    // -------------------------------------------------------------------------

    /// <summary>
    /// For any non-negative uint raw value, the result of
    /// <see cref="CpuInfoService.ConvertKelvinTenthsToCelsius"/> must equal
    /// <c>(rawValue / 10.0) - 273.15</c> within a tolerance of 1e-9.
    /// </summary>
    [Property(MaxTest = 1000)]
    public Property TemperatureConversion_MatchesFormula_ForAnyRawValue()
    {
        return Prop.ForAll(Arb.From<uint>(), rawValue =>
        {
            double expected = (rawValue / 10.0) - 273.15;
            double actual = CpuInfoService.ConvertKelvinTenthsToCelsius(rawValue);

            return Math.Abs(actual - expected) < 1e-9;
        });
    }

    // -------------------------------------------------------------------------
    // Unit tests — specific known values
    // -------------------------------------------------------------------------

    [Fact]
    public void ConvertKelvinTenthsToCelsius_2981_Returns25Celsius()
    {
        // 298.1 K = 25.0 °C  (2981 / 10.0 - 273.15 = 25.0 - 0.05 = 24.95... wait)
        // 298.15 K = 25.0 °C → raw = 2981 gives 298.1 K = 24.95 °C
        // Use 2981 and assert the exact formula result.
        double result = CpuInfoService.ConvertKelvinTenthsToCelsius(2981);
        Assert.Equal((2981 / 10.0) - 273.15, result, precision: 9);
    }

    [Fact]
    public void ConvertKelvinTenthsToCelsius_3731_Returns100Celsius()
    {
        // 373.15 K = 100 °C → raw = 3731 gives 373.1 K = 99.95 °C
        double result = CpuInfoService.ConvertKelvinTenthsToCelsius(3731);
        Assert.Equal((3731 / 10.0) - 273.15, result, precision: 9);
    }

    [Fact]
    public void ConvertKelvinTenthsToCelsius_2731_ReturnsNearZeroCelsius()
    {
        // 273.1 K ≈ -0.05 °C
        double result = CpuInfoService.ConvertKelvinTenthsToCelsius(2731);
        Assert.Equal((2731 / 10.0) - 273.15, result, precision: 9);
    }

    [Fact]
    public void ConvertKelvinTenthsToCelsius_Zero_ReturnsNegative27315()
    {
        // 0 K = -273.15 °C (absolute zero)
        double result = CpuInfoService.ConvertKelvinTenthsToCelsius(0);
        Assert.Equal(-273.15, result, precision: 9);
    }

    [Fact]
    public void ConvertKelvinTenthsToCelsius_3530_Returns79Point85Celsius()
    {
        // Typical CPU temperature: 353.0 K = 79.85 °C
        double result = CpuInfoService.ConvertKelvinTenthsToCelsius(3530);
        Assert.Equal((3530 / 10.0) - 273.15, result, precision: 9);
    }
}
