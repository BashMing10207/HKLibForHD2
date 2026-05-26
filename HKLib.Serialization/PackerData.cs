using HKLib.hk2018;

namespace HKLib.Serialization;

/// <summary>
/// Holds the state and results of the packing process.
/// </summary>
public class PackerData
{
    /// <summary>
    /// Maps each Havok object instance to its calculated absolute offset within the __data__ section.
    /// </summary>
    public Dictionary<IHavokObject, long> AddressMap { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// A list of all pointer patches that need to be written to the PTCH section.
    /// </summary>
    public List<PointerPatch> Patches { get; } = new();

    /// <summary>
    /// A list of all padding entries that need to be written to the TPAD section.
    /// </summary>
    public List<PaddingPatch> PaddingInfo { get; } = new();
}

public record PointerPatch(long SourceOffset, long DestinationOffset);

public record PaddingPatch(long Offset, byte Size);