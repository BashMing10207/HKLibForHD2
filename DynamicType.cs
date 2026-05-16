using HKLib.hk2018;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// Represents a Havok class definition parsed dynamically from a file's __types__ section.
/// </summary>
public class DynamicHavokType
{
    public required string Name { get; init; }
    public string? ParentName { get; set; }
    public int Size { get; set; }
    public List<DynamicHavokField> Fields { get; } = new();
}

/// <summary>
/// Represents a field within a dynamically parsed Havok class.
/// </summary>
public record DynamicHavokField(string Name, string TypeName, int Offset, HavokType.TypeKind Kind,
    HavokType.TypeSubKind SubKind);