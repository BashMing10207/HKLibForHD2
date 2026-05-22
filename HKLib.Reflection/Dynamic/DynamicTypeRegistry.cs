using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using HKLib.hk2018;

namespace HKLib.Reflection.Dynamic;

/// <summary>
/// Parses TST1 and FST1 sections from a Havok file to build an in-memory schema registry.
/// </summary>
public class DynamicTypeRegistry
{
    private readonly DynamicTypeRegistry? _parent;
    private readonly Dictionary<string, DynamicHavokType> _types = new();
    private readonly Dictionary<ulong, DynamicHavokType> _typesByHash = new();
    private readonly List<string> _strings = new();
    private List<(DynamicHavokType Type, int ParentIndex, int FirstFieldIndex, int NumFields)>? _tempTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicTypeRegistry"/> class by loading a base XML schema
    /// and supplementing it with a binary schema from a Havok file.
    /// </summary>
    /// <param name="baseSchemaPath">Path to the base Havok Type Registry XML file (e.g., HavokTypeRegistry20190100.xml).</param>
    /// <param name="gameFileStream">A stream for the game-specific Havok file (e.g., global.havok_physics_properties.main).</param>
    /// <param name="typesSectionOffset">The offset of the __types__ section in the game file.</param>
    public DynamicTypeRegistry(string baseSchemaPath, Stream gameFileStream, long typesSectionOffset)
    {
        _parent = null;
        // Phase 0.1: Load the base SDK schema from XML.
        if (!File.Exists(baseSchemaPath))
        {
            throw new FileNotFoundException(
                $"Base schema file not found. Ensure '{baseSchemaPath}' is in the application directory.",
                baseSchemaPath);
        }
        ParseFromXml(baseSchemaPath);

        // Phase 0.2: Parse and merge the game-specific schema from the binary file.
        using var reader = new BinaryReader(gameFileStream, Encoding.ASCII, true);
        ParseFromBinary(reader, typesSectionOffset);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicTypeRegistry"/> class by chaining it to a parent registry
    /// and loading a local binary schema.
    /// </summary>
    /// <param name="parent">The parent registry, which should contain the base and global compendium types.</param>
    /// <param name="localStream">A stream for the asset-specific Havok file containing a local __types__ section.</param>
    /// <param name="localTypesOffset">The offset of the __types__ section in the local file.</param>
    public DynamicTypeRegistry(DynamicTypeRegistry parent, Stream localStream, long localTypesOffset)
    {
        _parent = parent;
        using var reader = new BinaryReader(localStream, Encoding.ASCII, true);
        ParseFromBinary(reader, localTypesOffset);
    }

    /// <summary>
    /// Parses a Havok Type Registry XML file and populates the internal type dictionary.
    /// </summary>
    private void ParseFromXml(string xmlPath)
    {
        XDocument doc = XDocument.Load(xmlPath);
        Dictionary<string, DynamicHavokType> xmlTypesById = new();
        Dictionary<DynamicHavokType, (string? parentId, string? subTypeId)> references = new();

        // Pass 1: Create all type objects
        foreach (XElement typeElement in doc.Descendants("HavokType"))
        {
            string? name = typeElement.Attribute("Name")?.Value;
            string? hashStr = typeElement.Attribute("Hash")?.Value;
            string? id = typeElement.Attribute("Id")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hashStr) || string.IsNullOrEmpty(id)) continue;
            if (_types.ContainsKey(name)) continue;

            var dynamicType = new DynamicHavokType
            {
                Name = name,
                Size = int.TryParse(typeElement.Attribute("Size")?.Value, out int size) ? size : 0,
            };

            references.Add(dynamicType, (typeElement.Attribute("Parent")?.Value, typeElement.Attribute("SubType")?.Value));

            ulong hash = ulong.Parse(hashStr);
            _types.TryAdd(name, dynamicType);
            _typesByHash.TryAdd(hash, dynamicType);
            xmlTypesById.Add(id, dynamicType);
        }

        // Pass 2: Link parents and subtypes
        foreach (XElement typeElement in doc.Descendants("HavokType"))
        {
            string? id = typeElement.Attribute("Id")?.Value;
            if (id is null || !xmlTypesById.TryGetValue(id, out DynamicHavokType? dynamicType)) continue;

            var (parentId, subTypeId) = references[dynamicType];

            if (parentId is not null && xmlTypesById.TryGetValue(parentId, out DynamicHavokType? parentType))
            {
                dynamicType.Parent = parentType;
            }

            if (subTypeId is not null && xmlTypesById.TryGetValue(subTypeId, out DynamicHavokType? subType))
            {
                dynamicType.SubType = subType;
            }

            // Also add SubType as a template parameter for consistency with hkArray<T>
            if (dynamicType.SubType is not null)
            {
                dynamicType.TemplateParameters.Add(new DynamicHavokTemplateParameter
                {
                    Type = dynamicType.SubType
                });
            }
        }
        Debug.WriteLine($"Loaded base schema from {Path.GetFileName(xmlPath)}.");
    }

    /// <summary>
    /// Parses the __types__ (TST1/FST1) section from a Havok binary file and merges it into the registry.
    /// </summary>
    private void ParseFromBinary(BinaryReader reader, long typesSectionOffset)
    {
        // A more robust implementation that parses the TYPE section header instead of scanning.
        reader.BaseStream.Position = typesSectionOffset;
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "TYPE")
        {
            throw new InvalidDataException(
                $"Expected 'TYPE' section magic at offset {typesSectionOffset}, but found '{magic}'.");
        }

        // The compendium's TYPE section is a container for other sections (TST1, FST1, etc.).
        // It does not have its own size header; it's just a sequence of sub-sections.
        // We must parse each sub-section sequentially.
        Dictionary<string, (long offset, int size)> sections = new();
        long streamEnd = reader.BaseStream.Length;

        while (reader.BaseStream.Position < streamEnd - 16) // Ensure there's enough space for a header
        {
            long subSectionHeaderOffset = reader.BaseStream.Position;
            string subMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));

            if (subMagic is not ("TST1" or "FST1" or "THSH" or "STR1" or "TPTR" or "TBDY" or "TPAD"))
            {
                // We've likely reached the end of the TYPE section or encountered unknown data.
                // Seek back 4 bytes so we don't consume the magic of the next section.
                reader.BaseStream.Position -= 4;
                break;
            }

            int subSectionDataSize = reader.ReadInt32();
            reader.BaseStream.Position += 8; // Skip padding/version

            if (!sections.ContainsKey(subMagic))
            {
                // The offset is the start of the sub-section header, not its data.
                sections.Add(subMagic, (subSectionHeaderOffset, subSectionDataSize));
            }

            // Jump to the start of the next section
            long nextSectionOffset = subSectionHeaderOffset + 16 + subSectionDataSize;

            // Align to 16 bytes for the next section header
            if (nextSectionOffset % 16 != 0)
            {
                nextSectionOffset += 16 - (nextSectionOffset % 16);
            }

            if (nextSectionOffset >= streamEnd)
            {
                break;
            }

            reader.BaseStream.Position = nextSectionOffset;
        }

        // Parse sections in order of dependency
        if (sections.TryGetValue("STR1", out var str1Info))
        {
            ParseStringTable(reader, str1Info.offset, str1Info.size);
        }

        if (sections.TryGetValue("TST1", out var tst1Info))
        {
            ParseTst1(reader, tst1Info.offset);
        }

        if (sections.TryGetValue("FST1", out var fst1Info) && _tempTypes != null)
        {
            ParseFst1(reader, fst1Info.offset);
        }

        if (sections.TryGetValue("THSH", out var thshInfo) && _tempTypes != null)
        {
            ParseThsh(reader, thshInfo.offset);
        }

        // Clean up temporary data
        _tempTypes = null;

        Debug.WriteLine($"Parsed game-specific schema from binary stream at offset {typesSectionOffset}.");
        Debug.WriteLine($"Found sub-sections: {string.Join(", ", sections.Keys)}");
    }

    /// <summary>
    /// Parses the STR1 (string table) section of a Havok binary schema.
    /// </summary>
    private void ParseStringTable(BinaryReader reader, long str1Offset, int dataSize)
    {
        reader.BaseStream.Position = str1Offset;
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "STR1") throw new InvalidDataException("Not a valid STR1 section.");

        reader.BaseStream.Position = str1Offset + 16; // Skip header

        if (dataSize <= 0)
        {
            Debug.WriteLine("STR1 section has no string data.");
            return;
        }

        byte[] stringData = reader.ReadBytes(dataSize);
        _strings.Clear();

        int current = 0;
        int start = 0;
        while (current < stringData.Length)
        {
            if (stringData[current] == 0)
            {
                if (current > start)
                {
                    _strings.Add(Encoding.ASCII.GetString(stringData, start, current - start));
                }
                start = current + 1;
            }
            current++;
        }
        Debug.WriteLine($"Loaded {_strings.Count} strings from STR1 section.");
    }

    /// <summary>
    /// Parses the TST1 (type section table) section of a Havok binary schema.
    /// </summary>
    private void ParseTst1(BinaryReader reader, long tst1Offset)
    {
        reader.BaseStream.Position = tst1Offset;
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "TST1") throw new InvalidDataException("Not a valid TST1 section.");

        // This header structure is a guess based on common patterns.
        // It will need to be verified through reverse engineering.
        int sectionSize = reader.ReadInt32();
        int numTypes = reader.ReadInt32();
        reader.ReadInt32(); // Padding or version

        // A temporary structure to hold parsed data before linking parents and fields.
        _tempTypes = new List<(DynamicHavokType Type, int ParentIndex, int FirstFieldIndex, int NumFields)>(numTypes);

        // Pass 1: Read all type definitions and create initial objects
        for (int i = 0; i < numTypes; i++)
        {
            // This struct layout is a guess and will likely need adjustment.
            // Let's assume 32 bytes per definition for now.
            int nameStringIndex = reader.ReadInt32();
            int parentTypeIndex = reader.ReadInt32();
            int size = reader.ReadInt32();
            int alignment = reader.ReadInt32();
            int flags = reader.ReadInt32();
            int version = reader.ReadInt32();
            int firstFieldIndex = reader.ReadInt32();
            int numFields = reader.ReadInt32();

            string name = _strings[nameStringIndex];

            var havokType = new DynamicHavokType { Name = name, Size = size };
            _tempTypes.Add((havokType, parentTypeIndex, firstFieldIndex, numFields));

            // Add to the main dictionary, overwriting XML definitions if necessary,
            // as the binary compendium is the source of truth for the game.
            _types[name] = havokType;
        }

        // Pass 2: Link parents
        for (int i = 0; i < _tempTypes.Count; i++)
        {
            var (type, parentIndex, _, _) = _tempTypes[i];
            if (parentIndex >= 0 && parentIndex < _tempTypes.Count)
            {
                type.Parent = _tempTypes[parentIndex].Type;
            }
        }

        Debug.WriteLine($"Loaded {numTypes} type definitions from TST1 section.");
    }

    /// <summary>
    /// Parses the FST1 (field section table) section of a Havok binary schema.
    /// </summary>
    private void ParseFst1(BinaryReader reader, long fst1Offset)
    {
        reader.BaseStream.Position = fst1Offset;
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "FST1") throw new InvalidDataException("Not a valid FST1 section.");

        int sectionSize = reader.ReadInt32();
        int numFields = reader.ReadInt32();
        reader.ReadInt32(); // Padding or version

        var allFields = new List<DynamicHavokField>(numFields);
        for (int i = 0; i < numFields; i++)
        {
            // This struct layout is a guess and will likely need adjustment.
            // Let's assume 16 bytes per definition for now.
            int typeIndex = reader.ReadInt32();
            int nameStringIndex = reader.ReadInt32();
            int offset = reader.ReadInt32();
            int flags = reader.ReadInt32();

            string name = _strings[nameStringIndex];
            DynamicHavokType fieldType = _tempTypes![typeIndex].Type;

            allFields.Add(new DynamicHavokField
            {
                Name = name,
                Type = fieldType,
                Offset = offset,
                Flags = flags
            });
        }

        // Link fields to their owner types
        foreach (var (ownerType, _, firstFieldIndex, fieldCount) in _tempTypes!)
        {
            if (fieldCount > 0)
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    ownerType.Fields.Add(allFields[firstFieldIndex + i]);
                }
            }
        }

        Debug.WriteLine($"Loaded {numFields} field definitions from FST1 section.");
    }

    /// <summary>
    /// Parses the THSH (type hash) section of a Havok binary schema.
    /// This section contains the 8-byte hash for each type defined in TST1.
    /// </summary>
    private void ParseThsh(BinaryReader reader, long thshOffset)
    {
        reader.BaseStream.Position = thshOffset;
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "THSH") throw new InvalidDataException("Not a valid THSH section.");

        // This header structure is a guess and will likely need adjustment.
        int sectionSize = reader.ReadInt32();
        int numHashes = reader.ReadInt32();
        reader.ReadInt32(); // Padding or version

        if (_tempTypes == null || numHashes != _tempTypes.Count)
        {
            throw new InvalidDataException("Number of hashes in THSH does not match number of types in TST1.");
        }

        for (int i = 0; i < numHashes; i++)
        {
            ulong hash = reader.ReadUInt64();
            // TODO: This should use an indexer `_typesByHash[hash] = ...` instead of `TryAdd`.
            // The binary compendium is the source of truth and its types must overwrite any
            // types from the base XML schema that have the same hash. `TryAdd` silently
            // fails if the key already exists, which can lead to the wrong type definition
            // being used and cause "Type with hash not found" or data corruption errors.
            _typesByHash.TryAdd(hash, _tempTypes[i].Type);
        }
        Debug.WriteLine($"Loaded {numHashes} type hashes from THSH section.");
    }

    public DynamicHavokType? GetType(string name)
    {
        _types.TryGetValue(name, out DynamicHavokType? type);
        return type;
    }

    public DynamicHavokType? GetType(ulong hash)
    {
        _typesByHash.TryGetValue(hash, out DynamicHavokType? type);
        return type;
    }

    /// <summary>
    /// Calculates the size of a type, crucial for reading UnknownFields.
    /// </summary>
    public int GetSizeOf(string typeName)
    {
        // This would look up the type in the registry and return its size.
        // It needs to handle primitives, structs, and pointers.
        if (GetType(typeName) is { } type && type.Size > 0)
        {
            return type.Size;
        }

        // Handle primitive types
        return typeName switch
        {
            "hkInt32" => 4,
            "hkUint32" => 4,
            "hkHalf16" => 2,
            "hkReal" => 4,
            "hkBool" => 1,
            "hkRefPtr" => 8,
            "hkpConstraintData*" => 8,
            "hkRelArray32" => 8, // The struct itself is 8 bytes (offset + size)
            _ => throw new KeyNotFoundException($"Size for type '{typeName}' is not defined.")
        };
    }
}