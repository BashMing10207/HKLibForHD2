using System.Text;
using HKLib.Reflection.Dynamic;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Calculates type hashes for the hk2019 format.
/// </summary>
internal static class TypeHasher
{
    /// <summary>
    /// Calculates the 8-byte hash for a given Havok type definition.
    /// The hk2019 hash algorithm is different from hk2018. It's a 64-bit hash
    /// that depends on the type name, parent name, and the names and types of all members.
    /// </summary>
    /// <param name="type">The dynamic type definition.</param>
    /// <returns>The calculated 64-bit hash.</returns>
    public static ulong CalculateHash(DynamicHavokType type)
    {
        // NOTE: This is a conceptual implementation. The actual polynomial and initial value
        // for the CRC64-like algorithm used by Havok 2019 would need to be reverse-engineered.
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.ASCII);

        if (type.Parent is not null)
        {
            writer.Write(Encoding.ASCII.GetBytes(type.Parent.Name));
        }

        writer.Write(Encoding.ASCII.GetBytes(type.Name));

        foreach (DynamicHavokField field in type.Fields.OrderBy(f => f.Offset))
        {
            writer.Write(Encoding.ASCII.GetBytes(field.Name));
            writer.Write(Encoding.ASCII.GetBytes(field.Type?.Name ?? ""));
        }

        // A real implementation would use the specific Havok CRC64 variant.
        return 0; // Placeholder
    }
}