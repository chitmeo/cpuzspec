// Feature: system-info-tool, Property 4: Theme detection from registry value
// For any DWORD value read from the AppsUseLightTheme registry key,
// ThemeService.DetectTheme(value) SHALL return Theme.Light when the value is 1
// and Theme.Dark for any other value (including 0 and missing key), ensuring a
// defined theme is always selected.
//
// Validates: Requirements 10.2

using FsCheck;
using FsCheck.Xunit;
using SystemInfoTool.Services.Application;

namespace SystemInfoTool.Tests.Services.Application;

public class ThemeServiceTests
{
    // -------------------------------------------------------------------------
    // Property 4 — DetectTheme always returns a defined Theme
    // -------------------------------------------------------------------------

    /// <summary>
    /// For any uint value, <see cref="ThemeService.DetectTheme"/> must return
    /// <see cref="Theme.Light"/> when the value is 1 and <see cref="Theme.Dark"/>
    /// for every other value.
    /// </summary>
    [Property(MaxTest = 1000)]
    public Property DetectTheme_ReturnsLightForOne_DarkForAllOtherValues()
    {
        return Prop.ForAll(Arb.From<uint>(), value =>
        {
            Theme result = ThemeService.DetectTheme(value);

            if (value == 1u)
                return result == Theme.Light;
            else
                return result == Theme.Dark;
        });
    }

    // -------------------------------------------------------------------------
    // Unit tests — specific representative values
    // -------------------------------------------------------------------------

    [Fact]
    public void DetectTheme_ValueOne_ReturnsLight()
    {
        Assert.Equal(Theme.Light, ThemeService.DetectTheme(1u));
    }

    [Fact]
    public void DetectTheme_ValueZero_ReturnsDark()
    {
        Assert.Equal(Theme.Dark, ThemeService.DetectTheme(0u));
    }

    [Fact]
    public void DetectTheme_ValueTwo_ReturnsDark()
    {
        Assert.Equal(Theme.Dark, ThemeService.DetectTheme(2u));
    }

    [Fact]
    public void DetectTheme_MaxValue_ReturnsDark()
    {
        Assert.Equal(Theme.Dark, ThemeService.DetectTheme(uint.MaxValue));
    }
}
