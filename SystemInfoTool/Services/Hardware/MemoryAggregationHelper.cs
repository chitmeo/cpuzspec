using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Pure aggregation helper for memory slot data.
/// Extracted so it can be tested independently of WMI infrastructure.
/// </summary>
public static class MemoryAggregationHelper
{
    /// <summary>
    /// Computes aggregate values from a list of memory slots.
    /// </summary>
    /// <param name="slots">The list of memory slots to aggregate.</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///   <item><description><c>TotalInstalledMb</c> — sum of <see cref="MemorySlotInfo.CapacityMb"/> for all populated slots (null capacity treated as 0).</description></item>
    ///   <item><description><c>PopulatedMemorySlots</c> — count of slots where <see cref="MemorySlotInfo.IsPopulated"/> is <c>true</c>.</description></item>
    /// </list>
    /// </returns>
    public static (long TotalInstalledMb, int PopulatedMemorySlots) ComputeAggregates(
        IReadOnlyList<MemorySlotInfo> slots)
    {
        long totalMb = 0;
        int populatedCount = 0;

        foreach (var slot in slots)
        {
            if (slot.IsPopulated)
            {
                totalMb += slot.CapacityMb ?? 0L;
                populatedCount++;
            }
        }

        return (totalMb, populatedCount);
    }
}
