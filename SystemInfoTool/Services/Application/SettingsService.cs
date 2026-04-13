using System.IO;
using System.Text.Json;
using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Application;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> to/from
/// <c>%APPDATA%\SystemInfoTool\settings.json</c> using <see cref="System.Text.Json"/>.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SystemInfoTool",
        "settings.json");

    private static readonly AppSettings DefaultSettings = new(
        RefreshIntervalMs: 1000,
        LastWindowPlacement: new WindowPlacement(100, 100, 900, 650));

    /// <summary>
    /// Saves <paramref name="settings"/> to the settings file asynchronously.
    /// Creates the directory if it does not exist.
    /// </summary>
    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings);
        await File.WriteAllTextAsync(SettingsFilePath, json).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads <see cref="AppSettings"/> from the settings file asynchronously.
    /// Returns <see cref="DefaultSettings"/> on any error (missing file, invalid JSON, I/O failure, etc.).
    /// </summary>
    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? DefaultSettings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return DefaultSettings;
        }
    }
}
