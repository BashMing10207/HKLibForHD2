using System.Diagnostics;
using System.Text;
using HKLib.hk2018;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// Parses TST1 and FST1 sections from a Havok file to build an in-memory schema registry.
/// </summary>
public class DynamicTypeRegistry
{
    private readonly Dictionary<string, DynamicHavokType> _types = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicTypeRegistry"/> class by parsing the __types__ section.
    //  NOTE: This is a conceptual implementation. Actual offsets and struct layouts
    //  for TST1/FST1 need to be reverse-engineered and implemented in the BinaryReader.
    /// </summary>
    public DynamicTypeRegistry(BinaryReader reader, long typesSectionOffset)
    {
        reader.BaseStream.Position = typesSectionOffset;
        // In a real implementation, you would read the TST1/FST1 headers here
        // to find the offsets and counts for type definitions, field definitions, and string tables.

        // For demonstration, let's assume these are parsed into intermediate structures.
        // var parsedTypes = ParseTst1(reader);
        // var parsedFields = ParseFst1(reader);

        // Build the registry
        // foreach (var parsedType in parsedTypes)
        // {
        //     _types.Add(parsedType.Name, new DynamicHavokType { ... });
        // }
        //
        // foreach (var parsedField in parsedFields)
        // {
        //     var ownerType = _types[parsedField.OwnerTypeName];
        //     ownerType.Fields.Add(new DynamicHavokField(...));
        // }

        // The result of the parsing would populate the _types dictionary.
        // Since we don't have the file bytes, we cannot write the actual parser.
        // The logic inside the reader (next file) will assume this registry is correctly populated.
    }

    public DynamicHavokType? GetType(string name)
    {
        _types.TryGetValue(name, out DynamicHavokType? type);
        return type;
    }

    /// <summary>
    /// Calculates the size of a type, crucial for reading UnknownFields.
    /// </summary>
    public int GetSizeOf(string typeName)
    {
        // This would look up the type in the registry and return its size.
        // It needs to handle primitives, structs, and pointers.
        if (_types.TryGetValue(typeName, out var type))
        {
            return type.Size;
        }

        // Handle primitive types
        return typeName switch
        {
            "hkInt32" => 4,
            "hkUint32" => 4,
            "hkHalf16" => 2, // As per TODO
            "hkReal" => 4,
            "hkBool" => 1,
            "hkRefPtr" => 8,
            "hkpConstraintData*" => 8,
            "hkRelArray32" => 8, // As per TODO (size of the struct itself, not the data it points to)
            _ => throw new KeyNotFoundException($"Size for type '{typeName}' is not defined.")
        };
    }
}