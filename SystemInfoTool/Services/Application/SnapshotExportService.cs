using System.IO;
using System.Text;
using Microsoft.Win32;
using SystemInfoTool.Models;

namespace SystemInfoTool.Services.Application;

/// <summary>
/// Serialises all hardware model objects to a plain-text snapshot report and
/// writes it to a user-chosen file via a standard Windows Save dialog.
/// Implements an atomic write strategy (temp file → <see cref="File.Replace"/>)
/// so that an existing destination file is never partially overwritten on failure.
/// </summary>
public sealed class SnapshotExportService
{
    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prompts the user for a destination path, serialises all hardware data,
    /// and writes the report atomically.
    /// </summary>
    /// <returns>
    /// The destination file path on success; <see langword="null"/> if the user
    /// cancelled the dialog or if an I/O error occurred.
    /// </returns>
    public Task<string?> ExportAsync(
        CpuStaticInfo[] cpuInfos,
        MotherboardInfo motherboard,
        MemoryInfo memory,
        GpuStaticInfo[] gpus,
        StorageDriveInfo[] drives)
    {
        // SaveFileDialog must run on the UI thread; since this is a WPF app the
        // caller is expected to invoke from the UI thread (or marshal via
        // Dispatcher).  We keep the method async so callers can await it.
        string? destPath = PromptForDestinationPath();
        if (destPath is null)
            return Task.FromResult<string?>(null);

        string report = Serialize(cpuInfos, motherboard, memory, gpus, drives);

        string tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, report, Encoding.UTF8);

            if (File.Exists(destPath))
            {
                // File.Replace atomically swaps tempPath → destPath; the
                // previous content is moved to the backup path (null = delete).
                File.Replace(tempPath, destPath, null);
            }
            else
            {
                // Destination does not exist yet — a simple move is sufficient.
                File.Move(tempPath, destPath);
            }

            return Task.FromResult<string?>(destPath);
        }
        catch (IOException)
        {
            TryDeleteTemp(tempPath);
            return Task.FromResult<string?>(null);
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteTemp(tempPath);
            return Task.FromResult<string?>(null);
        }
    }

    // -------------------------------------------------------------------------
    // Serialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serialises all hardware model objects to a plain-text report.
    /// Each field is written as "FieldName: Value" on its own line.
    /// Sections are separated by a category header such as "[CPU 0]".
    /// </summary>
    internal static string Serialize(
        CpuStaticInfo[] cpuInfos,
        MotherboardInfo motherboard,
        MemoryInfo memory,
        GpuStaticInfo[] gpus,
        StorageDriveInfo[] drives)
    {
        var sb = new StringBuilder();

        // --- CPU ---
        for (int i = 0; i < cpuInfos.Length; i++)
        {
            CpuStaticInfo cpu = cpuInfos[i];
            string prefix = cpuInfos.Length == 1 ? "CPU" : $"CPU {i}";
            AppendSection(sb, prefix);
            AppendField(sb, $"{prefix}.SocketIndex", cpu.SocketIndex.ToString());
            AppendField(sb, $"{prefix}.Name", cpu.Name);
            AppendField(sb, $"{prefix}.CodeName", cpu.CodeName);
            AppendField(sb, $"{prefix}.PackageType", cpu.PackageType);
            AppendField(sb, $"{prefix}.PhysicalCores", cpu.PhysicalCores.ToString());
            AppendField(sb, $"{prefix}.LogicalProcessors", cpu.LogicalProcessors.ToString());
            AppendField(sb, $"{prefix}.BaseClockMhz", cpu.BaseClockMhz.ToString("R"));
            AppendField(sb, $"{prefix}.L1.SizeKb", cpu.L1.SizeKb.ToString());
            AppendField(sb, $"{prefix}.L1.Associativity", cpu.L1.Associativity.ToString());
            AppendField(sb, $"{prefix}.L1.Type", cpu.L1.Type);
            AppendField(sb, $"{prefix}.L2.SizeKb", cpu.L2.SizeKb.ToString());
            AppendField(sb, $"{prefix}.L2.Associativity", cpu.L2.Associativity.ToString());
            AppendField(sb, $"{prefix}.L2.Type", cpu.L2.Type);
            AppendField(sb, $"{prefix}.L3.SizeKb", cpu.L3.SizeKb.ToString());
            AppendField(sb, $"{prefix}.L3.Associativity", cpu.L3.Associativity.ToString());
            AppendField(sb, $"{prefix}.L3.Type", cpu.L3.Type);
            AppendField(sb, $"{prefix}.IsaExtensions", string.Join(",", cpu.IsaExtensions));
            AppendField(sb, $"{prefix}.TdpWatts", cpu.TdpWatts.HasValue ? cpu.TdpWatts.Value.ToString("R") : "");
        }

        // --- Motherboard ---
        AppendSection(sb, "Motherboard");
        AppendField(sb, "Motherboard.Manufacturer", motherboard.Manufacturer);
        AppendField(sb, "Motherboard.ProductName", motherboard.ProductName);
        AppendField(sb, "Motherboard.Version", motherboard.Version);
        AppendField(sb, "Motherboard.BiosVendor", motherboard.BiosVendor);
        AppendField(sb, "Motherboard.BiosVersion", motherboard.BiosVersion);
        AppendField(sb, "Motherboard.BiosReleaseDate", motherboard.BiosReleaseDate);
        AppendField(sb, "Motherboard.ChipsetModel", motherboard.ChipsetModel ?? "");
        AppendField(sb, "Motherboard.TotalMemorySlots", motherboard.TotalMemorySlots.ToString());
        AppendField(sb, "Motherboard.PopulatedMemorySlots", motherboard.PopulatedMemorySlots.ToString());

        // --- Memory ---
        AppendSection(sb, "Memory");
        AppendField(sb, "Memory.TotalInstalledMb", memory.TotalInstalledMb.ToString());
        AppendField(sb, "Memory.MemoryType", memory.MemoryType);
        AppendField(sb, "Memory.ChannelConfig", memory.ChannelConfig);
        AppendField(sb, "Memory.CurrentFrequencyMhz", memory.CurrentFrequencyMhz.ToString());
        AppendField(sb, "Memory.XmpFrequencyMhz", memory.XmpFrequencyMhz.HasValue ? memory.XmpFrequencyMhz.Value.ToString() : "");
        AppendField(sb, "Memory.SlotCount", memory.Slots.Count.ToString());

        for (int i = 0; i < memory.Slots.Count; i++)
        {
            MemorySlotInfo slot = memory.Slots[i];
            string sp = $"Memory.Slot[{i}]";
            AppendField(sb, $"{sp}.SlotLabel", slot.SlotLabel);
            AppendField(sb, $"{sp}.IsPopulated", slot.IsPopulated.ToString());
            AppendField(sb, $"{sp}.CapacityMb", slot.CapacityMb.HasValue ? slot.CapacityMb.Value.ToString() : "");
            AppendField(sb, $"{sp}.Manufacturer", slot.Manufacturer ?? "");
            AppendField(sb, $"{sp}.PartNumber", slot.PartNumber ?? "");
            AppendField(sb, $"{sp}.SerialNumber", slot.SerialNumber ?? "");
            AppendField(sb, $"{sp}.SpeedMhz", slot.SpeedMhz.HasValue ? slot.SpeedMhz.Value.ToString() : "");
            if (slot.Timings is not null)
            {
                AppendField(sb, $"{sp}.Timings.CL", slot.Timings.CL.ToString());
                AppendField(sb, $"{sp}.Timings.tRCD", slot.Timings.tRCD.ToString());
                AppendField(sb, $"{sp}.Timings.tRP", slot.Timings.tRP.ToString());
                AppendField(sb, $"{sp}.Timings.tRAS", slot.Timings.tRAS.ToString());
            }
            else
            {
                AppendField(sb, $"{sp}.Timings.CL", "");
                AppendField(sb, $"{sp}.Timings.tRCD", "");
                AppendField(sb, $"{sp}.Timings.tRP", "");
                AppendField(sb, $"{sp}.Timings.tRAS", "");
            }
        }

        // --- GPU ---
        for (int i = 0; i < gpus.Length; i++)
        {
            GpuStaticInfo gpu = gpus[i];
            string prefix = gpus.Length == 1 ? "GPU" : $"GPU {i}";
            AppendSection(sb, prefix);
            AppendField(sb, $"{prefix}.AdapterIndex", gpu.AdapterIndex.ToString());
            AppendField(sb, $"{prefix}.Name", gpu.Name);
            AppendField(sb, $"{prefix}.ChipModel", gpu.ChipModel ?? "");
            AppendField(sb, $"{prefix}.Manufacturer", gpu.Manufacturer);
            AppendField(sb, $"{prefix}.VideoMemoryMb", gpu.VideoMemoryMb.ToString());
            AppendField(sb, $"{prefix}.MemoryType", gpu.MemoryType ?? "");
            AppendField(sb, $"{prefix}.DriverVersion", gpu.DriverVersion);
            AppendField(sb, $"{prefix}.DriverDate", gpu.DriverDate);
        }

        // --- Storage ---
        for (int i = 0; i < drives.Length; i++)
        {
            StorageDriveInfo drive = drives[i];
            string prefix = drives.Length == 1 ? "Drive" : $"Drive {i}";
            AppendSection(sb, prefix);
            AppendField(sb, $"{prefix}.DriveIndex", drive.DriveIndex.ToString());
            AppendField(sb, $"{prefix}.Model", drive.Model);
            AppendField(sb, $"{prefix}.Manufacturer", drive.Manufacturer ?? "");
            AppendField(sb, $"{prefix}.InterfaceType", drive.InterfaceType);
            AppendField(sb, $"{prefix}.CapacityGb", drive.CapacityGb.ToString());
            AppendField(sb, $"{prefix}.FirmwareRevision", drive.FirmwareRevision ?? "");
            AppendField(sb, $"{prefix}.SmartHealth", drive.SmartHealth.ToString());
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a plain-text snapshot report produced by <see cref="Serialize"/>
    /// and returns a dictionary mapping every field name to its value.
    /// Section header lines (e.g. <c>[CPU]</c>) are skipped.
    /// </summary>
    public static Dictionary<string, string> Parse(string reportText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(reportText))
            return result;

        foreach (string rawLine in reportText.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');

            // Skip blank lines and section headers
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('['))
                continue;

            int colonIndex = line.IndexOf(": ", StringComparison.Ordinal);
            if (colonIndex < 0)
                continue;

            string key = line[..colonIndex];
            string value = line[(colonIndex + 2)..];
            result[key] = value;
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string? PromptForDestinationPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            Title = "Export Hardware Snapshot"
        };

        bool? result = dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    private static void AppendSection(StringBuilder sb, string name)
    {
        if (sb.Length > 0)
            sb.AppendLine();
        sb.AppendLine($"[{name}]");
    }

    private static void AppendField(StringBuilder sb, string name, string value)
    {
        sb.AppendLine($"{name}: {value}");
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try { File.Delete(tempPath); }
        catch { /* best-effort */ }
    }
}
