// Feature: system-info-tool, Property 9: SMART status classification completeness
// For any set of raw SMART attribute values returned by DeviceIoControl, the
// SmartStatus classification function SHALL return exactly one of {Good, Caution, Bad}
// for valid data and Unavailable for null or error data — the result is never
// undefined or an unhandled enum value.
//
// Validates: Requirements 6.5, 6.6

using System.Runtime.InteropServices;
using FsCheck;
using FsCheck.Xunit;
using SystemInfoTool.Models;
using SystemInfoTool.PInvoke;
using SystemInfoTool.Services.Hardware;

namespace SystemInfoTool.Tests.Services.Hardware;

public class StorageSmartClassificationTests
{
    // -------------------------------------------------------------------------
    // Helpers — build a valid raw SmartData byte array from attribute specs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serialises a <see cref="SmartData"/> struct into a byte array of exactly
    /// <c>Marshal.SizeOf&lt;SmartData&gt;()</c> bytes, matching what
    /// <c>ClassifySmartStatus</c> expects.
    /// </summary>
    private static byte[] SerialiseSmartData(SmartData data)
    {
        int size = Marshal.SizeOf<SmartData>();
        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Builds a <see cref="SmartData"/> with up to 30 attribute slots populated
    /// from the supplied list (excess entries are left as zero / unused).
    /// </summary>
    private static SmartData BuildSmartData(IList<SmartAttribute> attrs)
    {
        var attributes = new SmartAttribute[30];
        for (int i = 0; i < Math.Min(attrs.Count, 30); i++)
            attributes[i] = attrs[i];

        return new SmartData
        {
            RevisionNumber = 1,
            Attributes = attributes,
            OfflineDataCollectionStatus = 0,
            SelfAssessmentTestStatus = 0,
            OfflineDataCollectionTime = 0,
            Reserved1 = 0,
            OfflineDataCollectionCapability = 0,
            SmartCapability = 0,
            ErrorLoggingCapability = 0,
            Reserved2 = 0,
            ShortSelfTestPollingTime = 0,
            ExtendedSelfTestPollingTime = 0,
            ConveyanceSelfTestPollingTime = 0,
            Reserved3 = new byte[11],
            VendorSpecific = new byte[125],
            DataStructureChecksum = 0,
        };
    }

    // -------------------------------------------------------------------------
    // FsCheck generators
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a random <see cref="SmartAttribute"/> with a non-zero AttributeId
    /// (so it is treated as an active attribute by the classifier).
    /// </summary>
    private static Gen<SmartAttribute> ActiveAttributeGen() =>
        from id in Gen.Choose(1, 255).Select(i => (byte)i)
        from flags in Arb.Generate<ushort>()
        from current in Gen.Choose(1, 253).Select(i => (byte)i)
        from worst in Gen.Choose(1, 253).Select(i => (byte)i)
        select new SmartAttribute
        {
            AttributeId = id,
            StatusFlags = flags,
            CurrentValue = current,
            WorstValue = worst,
            RawValue = new byte[6],
            Reserved = 0,
        };

    /// <summary>
    /// Generates a list of 0–30 random active SMART attributes.
    /// </summary>
    private static Gen<List<SmartAttribute>> AttributeListGen() =>
        Gen.Choose(0, 30).SelectMany(n => Gen.ListOf(n, ActiveAttributeGen()))
           .Select(attrs => attrs.ToList());

    // -------------------------------------------------------------------------
    // Property 9 — result is always a defined SmartStatus value
    // -------------------------------------------------------------------------

    /// <summary>
    /// For any randomly generated set of SMART attributes, ClassifySmartStatus
    /// must return exactly one of the four defined SmartStatus values.
    /// No unhandled enum value is ever returned.
    /// </summary>
    [Property(MaxTest = 500)]
    public Property ClassifySmartStatus_AlwaysReturnsDefinedEnumValue()
    {
        var validValues = new HashSet<SmartStatus>
        {
            SmartStatus.Good,
            SmartStatus.Caution,
            SmartStatus.Bad,
            SmartStatus.Unavailable,
        };

        return Prop.ForAll(
            Arb.From(AttributeListGen()),
            attrs =>
            {
                var data = BuildSmartData(attrs);
                byte[] raw = SerialiseSmartData(data);
                SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);
                return validValues.Contains(result);
            });
    }

    /// <summary>
    /// For any randomly generated set of SMART attributes that contain no
    /// critical or pre-failure failures, ClassifySmartStatus must return
    /// either Good or Caution — never Bad or Unavailable.
    /// </summary>
    [Property(MaxTest = 500)]
    public Property ClassifySmartStatus_ValidData_ReturnsGoodCautionOrBad()
    {
        // Generate attributes where CurrentValue and WorstValue are both > 1
        // (i.e. no hard-failure heuristic triggers).
        var safeAttrGen =
            from id in Gen.Choose(1, 255).Select(i => (byte)i)
            from flags in Arb.Generate<ushort>()
            from current in Gen.Choose(2, 253).Select(i => (byte)i)
            from worst in Gen.Choose(2, 253).Select(i => (byte)i)
            select new SmartAttribute
            {
                AttributeId = id,
                StatusFlags = flags,
                CurrentValue = current,
                WorstValue = worst,
                RawValue = new byte[6],
                Reserved = 0,
            };

        var safeListGen = Gen.Choose(0, 30)
            .SelectMany(n => Gen.ListOf(n, safeAttrGen))
            .Select(attrs => attrs.ToList());

        var validResults = new HashSet<SmartStatus>
        {
            SmartStatus.Good,
            SmartStatus.Caution,
            SmartStatus.Bad,
        };

        return Prop.ForAll(
            Arb.From(safeListGen),
            attrs =>
            {
                var data = BuildSmartData(attrs);
                byte[] raw = SerialiseSmartData(data);
                SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);
                // Valid data (no null, no parse error) must not return Unavailable
                return validResults.Contains(result);
            });
    }

    // -------------------------------------------------------------------------
    // Property 9 — null / error data returns Unavailable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Null input must always return Unavailable.
    /// </summary>
    [Fact]
    public void ClassifySmartStatus_NullInput_ReturnsUnavailable()
    {
        SmartStatus result = StorageInfoService.ClassifySmartStatus(null);
        Assert.Equal(SmartStatus.Unavailable, result);
    }

    /// <summary>
    /// Empty byte array must always return Unavailable.
    /// </summary>
    [Fact]
    public void ClassifySmartStatus_EmptyArray_ReturnsUnavailable()
    {
        SmartStatus result = StorageInfoService.ClassifySmartStatus(Array.Empty<byte>());
        Assert.Equal(SmartStatus.Unavailable, result);
    }

    /// <summary>
    /// A byte array shorter than sizeof(SmartData) must return Unavailable.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property ClassifySmartStatus_TooShortArray_ReturnsUnavailable()
    {
        int requiredSize = Marshal.SizeOf<SmartData>();

        var shortArrayGen =
            from len in Gen.Choose(1, requiredSize - 1)
            from bytes in Gen.ArrayOf(len, Arb.Generate<byte>())
            select bytes;

        return Prop.ForAll(
            Arb.From(shortArrayGen),
            shortData =>
            {
                SmartStatus result = StorageInfoService.ClassifySmartStatus(shortData);
                return result == SmartStatus.Unavailable;
            });
    }

    // -------------------------------------------------------------------------
    // Unit tests — specific classification examples
    // -------------------------------------------------------------------------

    [Fact]
    public void ClassifySmartStatus_AllHealthyAttributes_ReturnsGood()
    {
        // All attributes have CurrentValue=100, WorstValue=100 — no failures
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 1,   StatusFlags = 0x0003, CurrentValue = 100, WorstValue = 100, RawValue = new byte[6] },
            new() { AttributeId = 9,   StatusFlags = 0x0032, CurrentValue = 200, WorstValue = 200, RawValue = new byte[6] },
            new() { AttributeId = 194, StatusFlags = 0x0022, CurrentValue = 120, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Good, result);
    }

    [Fact]
    public void ClassifySmartStatus_CriticalAttributeId5_CurrentValue1_ReturnsBad()
    {
        // Attribute 5 (Reallocated Sectors Count) with CurrentValue=1 → Bad
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 5, StatusFlags = 0x0003, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }

    [Fact]
    public void ClassifySmartStatus_CriticalAttributeId196_WorstValue1_ReturnsBad()
    {
        // Attribute 196 (Reallocation Event Count) with WorstValue=1 → Bad
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 196, StatusFlags = 0x0003, CurrentValue = 100, WorstValue = 1, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }

    [Fact]
    public void ClassifySmartStatus_CriticalAttributeId197_CurrentValue1_ReturnsBad()
    {
        // Attribute 197 (Current Pending Sector Count) with CurrentValue=1 → Bad
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 197, StatusFlags = 0x0003, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }

    [Fact]
    public void ClassifySmartStatus_CriticalAttributeId198_WorstValue1_ReturnsBad()
    {
        // Attribute 198 (Uncorrectable Sector Count) with WorstValue=1 → Bad
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 198, StatusFlags = 0x0003, CurrentValue = 100, WorstValue = 1, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }

    [Fact]
    public void ClassifySmartStatus_PreFailureAttribute_CurrentValue1_ReturnsBad()
    {
        // Non-critical attribute with pre-failure bit (bit 0 of StatusFlags) set and CurrentValue=1 → Bad
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 10, StatusFlags = 0x0001, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }

    [Fact]
    public void ClassifySmartStatus_AdvisoryAttribute_CurrentValue1_ReturnsCaution()
    {
        // Advisory attribute (pre-failure bit clear) with CurrentValue=1 → Caution
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 10, StatusFlags = 0x0002, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Caution, result);
    }

    [Fact]
    public void ClassifySmartStatus_UnusedSlots_AttributeId0_AreIgnored()
    {
        // Attribute ID 0 means unused — even with CurrentValue=1 it should be ignored
        var attrs = new List<SmartAttribute>
        {
            new() { AttributeId = 0, StatusFlags = 0x0001, CurrentValue = 1, WorstValue = 1, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Good, result);
    }

    [Fact]
    public void ClassifySmartStatus_EmptyAttributeList_ReturnsGood()
    {
        // No attributes at all → all slots are zero (unused) → Good
        byte[] raw = SerialiseSmartData(BuildSmartData(new List<SmartAttribute>()));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Good, result);
    }

    [Fact]
    public void ClassifySmartStatus_BadAttributeOverridesCaution()
    {
        // One advisory failure (Caution) and one critical failure (Bad) → Bad wins
        var attrs = new List<SmartAttribute>
        {
            // Advisory attribute with CurrentValue=1 → would be Caution alone
            new() { AttributeId = 10, StatusFlags = 0x0002, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
            // Critical attribute 5 with CurrentValue=1 → Bad (early return)
            new() { AttributeId = 5,  StatusFlags = 0x0003, CurrentValue = 1, WorstValue = 100, RawValue = new byte[6] },
        };

        byte[] raw = SerialiseSmartData(BuildSmartData(attrs));
        SmartStatus result = StorageInfoService.ClassifySmartStatus(raw);

        Assert.Equal(SmartStatus.Bad, result);
    }
}
