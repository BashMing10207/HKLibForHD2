namespace HKLib.Reflection.Dynamic;

/// <summary>
/// Represents a Havok object that can store fields not defined in the static C# class model.
/// </summary>
public interface IDynamicHavokObject
{
    /// <summary>
    /// A list of fields that were present in the source file but are not defined in this object's static class definition.
    /// This is used to preserve data during a read-modify-write cycle.
    /// </summary>
    public List<UnknownField> UnknownFields { get; set; }
}

/// <summary>
/// Stores the data for an unknown field.
/// </summary>
public class UnknownField
{
    public required string Name { get; init; }
    public required byte[] Data { get; init; }
    public required int OriginalOffset { get; init; }
}