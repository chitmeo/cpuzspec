using System.Collections.ObjectModel;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// ViewModel for the Storage tab.
/// Loads all physical drive information (including SMART health) on construction.
/// </summary>
public sealed class StorageViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly StorageInfoService _storageInfoService;
    private string? _errorMessage;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and begins loading drive information.
    /// </summary>
    public StorageViewModel(StorageInfoService storageInfoService)
    {
        _storageInfoService = storageInfoService
            ?? throw new ArgumentNullException(nameof(storageInfoService));

        _ = LoadDrivesAsync();
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Observable collection of all detected physical storage devices.
    /// Bound to the drive list in the view.
    /// </summary>
    public ObservableCollection<StorageDriveInfo> Drives { get; } = [];

    /// <summary>Non-null when data could not be loaded; bound to the error panel.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // -------------------------------------------------------------------------
    // Data loading
    // -------------------------------------------------------------------------

    private async Task LoadDrivesAsync()
    {
        ErrorMessage = null;
        try
        {
            var drives = await _storageInfoService.GetAllDrivesAsync();

            Drives.Clear();
            foreach (var drive in drives)
                Drives.Add(drive);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load storage information: {ex.Message}";
        }
    }
}
