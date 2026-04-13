// Feature: system-info-tool, Property 11: ISA extension flag mapping
// For any CPUID leaf bitmask, the set of ISA extension strings returned by the
// flag-mapping function SHALL contain exactly the extensions whose corresponding
// bits are set in the bitmask — no extension is reported when its bit is clear,
// and no extension is omitted when its bit is set.
//
// Validates: Requirements 2.5

using FsCheck;
using FsCheck.Xunit;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.Tests.Services.Hardware;

public class CpuIdHelperTests
{
    // -------------------------------------------------------------------------
    // Property 11 — ISA extension flag mapping correctness
    // -------------------------------------------------------------------------

    /// <summary>
    /// For any set of 32-bit register values supplied for each (leaf, subleaf, regIndex)
    /// combination, <see cref="CpuIdHelper.MapExtensionsFromRegisters"/> must return
    /// exactly the extensions whose corresponding bits are set — no more, no less.
    /// </summary>
    [Property(MaxTest = 500)]
    public Property IsaExtensionMapping_ReturnsExactlyExtensionsWhoseBitsAreSet()
    {
        // Generator: produce one random uint per unique (leaf, subleaf, regIndex) key
        // that appears in the extension table.
        var keys = CpuIdHelper.ExtensionTable
            .Select(e => (e.Leaf, e.Subleaf, e.RegIndex))
            .Distinct()
            .ToList();

        var gen = Gen.ListOf(keys.Count, Arb.Generate<uint>())
            .Select(values =>
            {
                var dict = new Dictionary<(uint, uint, int), uint>();
                for (int i = 0; i < keys.Count; i++)
                    dict[keys[i]] = values[i];
                return (IReadOnlyDictionary<(uint Leaf, uint Subleaf, int RegIndex), uint>)dict;
            });

        return Prop.ForAll(Arb.From(gen), registerValues =>
        {
            var result = CpuIdHelper.MapExtensionsFromRegisters(registerValues);
            var resultSet = new HashSet<string>(result);

            foreach (var (leaf, subleaf, regIndex, bit, name) in CpuIdHelper.ExtensionTable)
            {
                var key = (leaf, subleaf, regIndex);
                uint regValue = registerValues.TryGetValue(key, out var v) ? v : 0u;
                bool bitIsSet = (regValue & (1u << bit)) != 0;

                // If the bit is set, the extension MUST appear in the result.
                // If the bit is clear, the extension MUST NOT appear in the result.
                if (bitIsSet != resultSet.Contains(name))
                    return false;
            }

            return true;
        });
    }

    /// <summary>
    /// When all register values are zero, no extensions should be reported.
    /// </summary>
    [Property(MaxTest = 1)]
    public Property IsaExtensionMapping_AllZeroRegisters_ReturnsEmptyList()
    {
        var emptyRegisters = new Dictionary<(uint, uint, int), uint>();
        var gen = Gen.Constant(
            (IReadOnlyDictionary<(uint Leaf, uint Subleaf, int RegIndex), uint>)emptyRegisters);

        return Prop.ForAll(Arb.From(gen), registerValues =>
        {
            var result = CpuIdHelper.MapExtensionsFromRegisters(registerValues);
            return result.Count == 0;
        });
    }

    /// <summary>
    /// When all register bits are set (0xFFFFFFFF), every extension in the table
    /// should be reported exactly once.
    /// </summary>
    [Property(MaxTest = 1)]
    public Property IsaExtensionMapping_AllBitsSet_ReturnsAllExtensions()
    {
        var keys = CpuIdHelper.ExtensionTable
            .Select(e => (e.Leaf, e.Subleaf, e.RegIndex))
            .Distinct();

        var allSetRegisters = keys.ToDictionary(k => k, _ => uint.MaxValue);
        var gen = Gen.Constant(
            (IReadOnlyDictionary<(uint Leaf, uint Subleaf, int RegIndex), uint>)allSetRegisters);

        return Prop.ForAll(Arb.From(gen), registerValues =>
        {
            var result = CpuIdHelper.MapExtensionsFromRegisters(registerValues);
            var expectedNames = CpuIdHelper.ExtensionTable.Select(e => e.Name).ToHashSet();
            var actualNames = new HashSet<string>(result);
            return expectedNames.SetEquals(actualNames);
        });
    }

    // -------------------------------------------------------------------------
    // Unit tests — specific bit examples
    // -------------------------------------------------------------------------

    [Fact]
    public void GetIsaExtensions_DoesNotThrow()
    {
        // On any platform (x86 or not), GetIsaExtensions() must not throw.
        var ex = Record.Exception(() => CpuIdHelper.GetIsaExtensions());
        Assert.Null(ex);
    }

    [Fact]
    public void MapExtensionsFromRegisters_Leaf1Ecx_Bit20_ReturnsSse42()
    {
        // Leaf 1, ECX, bit 20 → SSE4.2
        var registers = new Dictionary<(uint, uint, int), uint>
        {
            { (1u, 0u, 2), 1u << 20 }
        };

        var result = CpuIdHelper.MapExtensionsFromRegisters(registers);

        Assert.Contains("SSE4.2", result);
    }

    [Fact]
    public void MapExtensionsFromRegisters_Leaf7Ebx_Bit5_ReturnsAvx2()
    {
        // Leaf 7, subleaf 0, EBX, bit 5 → AVX2
        var registers = new Dictionary<(uint, uint, int), uint>
        {
            { (7u, 0u, 1), 1u << 5 }
        };

        var result = CpuIdHelper.MapExtensionsFromRegisters(registers);

        Assert.Contains("AVX2", result);
        Assert.DoesNotContain("AVX-512F", result); // bit 16 not set
    }

    [Fact]
    public void MapExtensionsFromRegisters_Leaf7Ebx_Bit16_ReturnsAvx512F()
    {
        // Leaf 7, subleaf 0, EBX, bit 16 → AVX-512F
        var registers = new Dictionary<(uint, uint, int), uint>
        {
            { (7u, 0u, 1), 1u << 16 }
        };

        var result = CpuIdHelper.MapExtensionsFromRegisters(registers);

        Assert.Contains("AVX-512F", result);
    }

    [Fact]
    public void MapExtensionsFromRegisters_ExtendedLeaf_Bit5_ReturnsLzcnt()
    {
        // Extended leaf 0x80000001, ECX, bit 5 → LZCNT
        var registers = new Dictionary<(uint, uint, int), uint>
        {
            { (0x80000001u, 0u, 2), 1u << 5 }
        };

        var result = CpuIdHelper.MapExtensionsFromRegisters(registers);

        Assert.Contains("LZCNT", result);
    }

    [Fact]
    public void MapExtensionsFromRegisters_NoDuplicateNames()
    {
        // All bits set — result should have no duplicate extension names.
        var keys = CpuIdHelper.ExtensionTable
            .Select(e => (e.Leaf, e.Subleaf, e.RegIndex))
            .Distinct();

        var allSetRegisters = keys.ToDictionary(k => k, _ => uint.MaxValue);
        var result = CpuIdHelper.MapExtensionsFromRegisters(allSetRegisters);

        Assert.Equal(result.Count, result.Distinct().Count());
    }
}
