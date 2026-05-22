using HKLib.hk2018;
using HKLib.Reflection.Dynamic;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Contains all pre-calculated layout information from the first serialization pass.
/// </summary>
internal class LayoutInfo
{
    public Dictionary<IHavokObject, ObjectLayout> ObjectLayouts { get; } = new(ReferenceEqualityComparer.Instance);
    public List<PointerPatch> PointerPatches { get; } = new();
    public List<string> ClassNames { get; } = new();
    public List<DynamicHavokType> Types { get; } = new();

    public long ClassNamesSectionOffset { get; set; }
    public long TypesSectionOffset { get; set; }
    public long DataSectionOffset { get; set; }
    public long FileSize { get; set; }
}

internal class ObjectLayout
{
    public long Offset { get; set; }
    public int Size { get; set; }
}

internal class PointerPatch
{
    public long PatchOffset { get; set; }
    public IHavokObject TargetObject { get; set; } = null!;
}