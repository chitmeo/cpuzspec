using System.Runtime.Intrinsics.X86;

namespace SystemInfoTool.Services.Hardware;

/// <summary>
/// Queries CPUID leaves via <see cref="X86Base.CpuId"/> and maps bitmask bits to
/// ISA extension name strings (SSE4.2, AVX2, AVX-512F, etc.).
/// </summary>
internal static class CpuIdHelper
{
    // -------------------------------------------------------------------------
    // CPUID leaf / register / bit definitions
    // -------------------------------------------------------------------------

    // Each entry: (leaf, subleaf, register index, bit, name)
    // Register index: 0=EAX, 1=EBX, 2=ECX, 3=EDX
    private static readonly (uint Leaf, uint Subleaf, int RegIndex, int Bit, string Name)[] s_extensions =
    [
        // Leaf 1, ECX
        (1u, 0u, 2, 0,  "SSE3"),
        (1u, 0u, 2, 9,  "SSSE3"),
        (1u, 0u, 2, 19, "SSE4.1"),
        (1u, 0u, 2, 20, "SSE4.2"),
        (1u, 0u, 2, 25, "AES"),
        (1u, 0u, 2, 28, "AVX"),
        (1u, 0u, 2, 30, "RDRND"),

        // Leaf 1, EDX
        (1u, 0u, 3, 23, "MMX"),
        (1u, 0u, 3, 25, "SSE"),
        (1u, 0u, 3, 26, "SSE2"),

        // Leaf 7, subleaf 0, EBX
        (7u, 0u, 1, 3,  "BMI1"),
        (7u, 0u, 1, 5,  "AVX2"),
        (7u, 0u, 1, 8,  "BMI2"),
        (7u, 0u, 1, 16, "AVX-512F"),
        (7u, 0u, 1, 17, "AVX-512DQ"),
        (7u, 0u, 1, 18, "RDSEED"),
        (7u, 0u, 1, 19, "ADX"),
        (7u, 0u, 1, 21, "AVX-512IFMA"),
        (7u, 0u, 1, 26, "AVX-512PF"),
        (7u, 0u, 1, 27, "AVX-512ER"),
        (7u, 0u, 1, 28, "AVX-512CD"),
        (7u, 0u, 1, 29, "SHA"),
        (7u, 0u, 1, 30, "AVX-512BW"),
        (7u, 0u, 1, 31, "AVX-512VL"),

        // Leaf 7, subleaf 0, ECX
        (7u, 0u, 2, 1,  "AVX-512VBMI"),
        (7u, 0u, 2, 6,  "AVX-512VBMI2"),
        (7u, 0u, 2, 11, "AVX-512VNNI"),
        (7u, 0u, 2, 12, "AVX-512BITALG"),
        (7u, 0u, 2, 14, "AVX-512VPOPCNTDQ"),

        // Extended leaf 0x80000001, ECX
        (0x80000001u, 0u, 2, 5,  "LZCNT"),
        (0x80000001u, 0u, 2, 8,  "PREFETCHW"),

        // Extended leaf 0x80000001, EDX
        (0x80000001u, 0u, 3, 29, "EM64T"),
    ];

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the list of ISA extension names supported by the current CPU.
    /// Returns an empty list if the platform does not support CPUID or any
    /// exception is thrown during the query.
    /// </summary>
    internal static IReadOnlyList<string> GetIsaExtensions()
    {
        try
        {
            if (!X86Base.IsSupported)
                return [];

            // Cache leaf results so each leaf is queried at most once.
            var leafCache = new Dictionary<(uint Leaf, uint Subleaf), (uint Eax, uint Ebx, uint Ecx, uint Edx)>();

            var result = new List<string>();

            foreach (var (leaf, subleaf, regIndex, bit, name) in s_extensions)
            {
                var key = (leaf, subleaf);
                if (!leafCache.TryGetValue(key, out var regs))
                {
                    var (eax, ebx, ecx, edx) = X86Base.CpuId((int)leaf, (int)subleaf);
                    regs = ((uint)eax, (uint)ebx, (uint)ecx, (uint)edx);
                    leafCache[key] = regs;
                }

                uint regValue = regIndex switch
                {
                    0 => regs.Eax,
                    1 => regs.Ebx,
                    2 => regs.Ecx,
                    3 => regs.Edx,
                    _ => 0u
                };

                if ((regValue & (1u << bit)) != 0)
                    result.Add(name);
            }

            return result;
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }

    // -------------------------------------------------------------------------
    // Internal helper — used by property-based tests to exercise the mapping
    // logic without calling real CPUID hardware.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a set of pre-supplied register values (keyed by leaf/subleaf/register-index)
    /// to ISA extension names. This overload is used by tests to verify the bit-mapping
    /// logic independently of the hardware CPUID instruction.
    /// </summary>
    /// <param name="registerValues">
    /// Dictionary mapping (leaf, subleaf, registerIndex) → 32-bit register value.
    /// Any missing key is treated as 0.
    /// </param>
    internal static IReadOnlyList<string> MapExtensionsFromRegisters(
        IReadOnlyDictionary<(uint Leaf, uint Subleaf, int RegIndex), uint> registerValues)
    {
        var result = new List<string>();

        foreach (var (leaf, subleaf, regIndex, bit, name) in s_extensions)
        {
            var key = (leaf, subleaf, regIndex);
            uint regValue = registerValues.TryGetValue(key, out var v) ? v : 0u;

            if ((regValue & (1u << bit)) != 0)
                result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// Exposes the full extension table for use in property-based tests.
    /// Each entry describes one ISA extension: (leaf, subleaf, registerIndex, bit, name).
    /// </summary>
    internal static IReadOnlyList<(uint Leaf, uint Subleaf, int RegIndex, int Bit, string Name)> ExtensionTable
        => s_extensions;
}
