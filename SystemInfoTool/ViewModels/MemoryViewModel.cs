using System.Collections.ObjectModel;
using SystemInfoTool.Models;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.ViewModels;

/// <summary>
/// ViewModel for the Memory tab.
/// Loads aggregate memory info and per-slot details on construction.
/// </summary>
public sealed class MemoryViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly MemoryInfoService _memoryInfoService;

    private MemoryInfo? _info;
    private string? _errorMessage;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the ViewModel and begins loading memory information.
    /// </summary>
    public MemoryViewModel(MemoryInfoService memoryInfoService)
    {
        _memoryInfoService = memoryInfoService
            ?? throw new ArgumentNullException(nameof(memoryInfoService));

        _ = LoadStaticInfoAsync();
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Aggregate memory information (total RAM, type, channel config, frequency).</summary>
    public MemoryInfo? Info
    {
        get => _info;
        private set
        {
            if (SetProperty(ref _info, value))
                RefreshSlots(value);
        }
    }

    /// <summary>
    /// Observable collection of per-slot details, kept in sync with <see cref="Info"/>.
    /// Bound to the slot DataGrid / ItemsControl in the view.
    /// </summary>
    public ObservableCollection<MemorySlotInfo> Slots { get; } = [];

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
            Info = await _memoryInfoService.GetStaticInfoAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load memory information: {ex.Message}";
        }
    }

    private void RefreshSlots(MemoryInfo? info)
    {
        Slots.Clear();
        if (info is null) return;

        foreach (var slot in info.Slots)
            Slots.Add(slot);
    }
}
