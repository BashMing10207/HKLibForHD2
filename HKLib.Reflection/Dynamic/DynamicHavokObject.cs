using System.Collections.Generic;
using System.Text.Json.Serialization;
using HKLib.hk2018;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// A dynamic representation of a Havok object, used during deserialization.
/// </summary>
public class DynamicHavokObject : hkReferencedObject
{
    [JsonIgnore] public DynamicHavokType Type { get; }
    public Dictionary<string, object?> Fields { get; } = new();

    public DynamicHavokObject(DynamicHavokType type)
    {
        Type = type;
    }
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