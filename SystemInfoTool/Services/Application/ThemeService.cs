using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace SystemInfoTool.Services.Application;

/// <summary>
/// Represents the application UI theme.
/// </summary>
public enum Theme
{
    Light,
    Dark
}

/// <summary>
/// Detects the Windows system light/dark mode preference and applies the
/// corresponding WPF <see cref="ResourceDictionary"/> theme at startup and
/// whenever the user changes the system preference.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string RegistryKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    private static readonly Uri LightThemeUri =
        new("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkThemeUri =
        new("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);

    private bool _disposed;

    /// <summary>
    /// Initialises the service and subscribes to system preference change events.
    /// </summary>
    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads the current system registry value and applies the matching theme.
    /// Defaults to <see cref="Theme.Light"/> if the registry key is inaccessible.
    /// </summary>
    public void ApplyTheme()
    {
        Theme theme;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            uint rawValue = key?.GetValue(RegistryValueName) is int v ? (uint)v : 0u;
            theme = DetectTheme(rawValue);
        }
        catch (SecurityException)
        {
            theme = Theme.Light;
        }

        ApplyTheme(theme);
    }

    /// <summary>
    /// Swaps the application <see cref="ResourceDictionary"/> to the specified theme.
    /// </summary>
    public void ApplyTheme(Theme theme)
    {
        Uri targetUri = theme == Theme.Light ? LightThemeUri : DarkThemeUri;
        Uri otherUri  = theme == Theme.Light ? DarkThemeUri  : LightThemeUri;

        var merged = System.Windows.Application.Current?.Resources?.MergedDictionaries;
        if (merged is null)
            return;

        // Remove the opposite theme dictionary if present.
        var toRemove = merged
            .Where(d => d.Source == otherUri)
            .ToList();
        foreach (var dict in toRemove)
            merged.Remove(dict);

        // Add the target theme dictionary if not already present.
        if (!merged.Any(d => d.Source == targetUri))
        {
            merged.Add(new ResourceDictionary { Source = targetUri });
        }
    }

    /// <summary>
    /// Maps a raw DWORD registry value to a <see cref="Theme"/>.
    /// Returns <see cref="Theme.Light"/> for <c>1</c> and <see cref="Theme.Dark"/>
    /// for every other value (including <c>0</c> and missing key).
    /// </summary>
    /// <param name="value">The raw DWORD value of <c>AppsUseLightTheme</c>.</param>
    public static Theme DetectTheme(uint value) =>
        value == 1u ? Theme.Light : Theme.Dark;

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _disposed = true;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // UserPreferenceCategory.General covers theme/colour changes.
        if (e.Category == UserPreferenceCategory.General)
            ApplyTheme();
    }
}
