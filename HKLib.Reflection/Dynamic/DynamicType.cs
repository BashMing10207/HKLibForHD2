using HKLib.hk2018;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// Represents a Havok class definition parsed dynamically from a file's __types__ section.
/// </summary>
public class DynamicHavokType
{
    public required string Name { get; init; }
    public string? ParentName { get; set; }
    public DynamicHavokType? Parent { get; set; }
    public DynamicHavokType? SubType { get; set; }
    public int Size { get; set; }
    public List<DynamicHavokField> Fields { get; } = new();
    public List<DynamicHavokTemplateParameter> TemplateParameters { get; } = new();
}

public class DynamicHavokTemplateParameter
{
    public required DynamicHavokType Type { get; init; }
}

/// <summary>
/// Represents a field within a dynamically parsed Havok class.
/// </summary>
public class DynamicHavokField
{
    public required string Name { get; init; }
    public DynamicHavokType? Type { get; init; }
    public string? TypeName { get; init; }
    public int Offset { get; init; }
    public int Flags { get; init; }
    public HKLib.Reflection.hk2018.HavokType.TypeKind Kind { get; init; }
}