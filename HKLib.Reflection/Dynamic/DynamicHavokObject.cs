using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// A dynamic representation of a Havok object, used during deserialization.
/// </summary>
public class DynamicHavokObject : IHavokObject
{
    [JsonIgnore] public DynamicHavokType Type { get; }
    public Dictionary<string, object?> Fields { get; } = new();

    public DynamicHavokObject(DynamicHavokType type)
    {
        Type = type;
    }

    public IHavokType GetType() => Type;
}

/// <summary>
/// Represents a pointer to another location in the Havok file.
/// </summary>
public class HavokPointer
{
    public HavokPointer(long address) => Address = address;
    public long Address { get; }
    public override string ToString() => $"<ptr:0x{Address:X}>";
}

/// <summary>
/// Represents a variant, which is a pointer to an object and its type.
/// </summary>
public class HavokVariant
{
    public HavokVariant(long objectAddress, long typeAddress)
    {
        ObjectAddress = objectAddress;
        TypeAddress = typeAddress;
    }
    public long ObjectAddress { get; }
    public long TypeAddress { get; }
    public override string ToString() => $"<variant obj:0x{ObjectAddress:X} type:0x{TypeAddress:X}>";
}