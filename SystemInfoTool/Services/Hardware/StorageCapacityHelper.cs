namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Pure helper for storage capacity unit conversions.
/// </summary>
public static class StorageCapacityHelper
{
    /// <summary>
    /// Converts a byte count to whole gigabytes using floor division.
    /// </summary>
    /// <param name="bytes">Non-negative byte count.</param>
    /// <returns>Capacity in GB, floored to the nearest whole number.</returns>
    public static long BytesToGb(long bytes) =>
        (long)Math.Floor(bytes / 1_000_000_000.0);
}
