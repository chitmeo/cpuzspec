using SystemInfoTool.Models;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// ViewModel for the Motherboard tab.
/// Loads static motherboard and BIOS information on construction.
/// </summary>
public sealed class MotherboardViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly MotherboardInfoService _motherboardInfoService;

    private MotherboardInfo? _info;
    private string? _errorMessage;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and begins loading motherboard information.
    /// </summary>
    public MotherboardViewModel(MotherboardInfoService motherboardInfoService)
    {
        _motherboardInfoService = motherboardInfoService
            ?? throw new ArgumentNullException(nameof(motherboardInfoService));

        _ = LoadStaticInfoAsync();
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Static motherboard and BIOS information.</summary>
    public MotherboardInfo? Info
    {
        get => _info;
        private set => SetProperty(ref _info, value);
    }

    /// <summary>Non-null when data could not be loaded; bound to the error panel.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // -------------------------------------------------------------------------
    // Data loading
    // -------------------------------------------------------------------------

    private async Task LoadStaticInfoAsync()
    {
        ErrorMessage = null;
        try
        {
            Info = await _motherboardInfoService.GetStaticInfoAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load motherboard information: {ex.Message}";
        }
    }
}
