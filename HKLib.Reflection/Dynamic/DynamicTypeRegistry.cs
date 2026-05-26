using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

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

    public DynamicTypeRegistry(string baseSchemaPath)
    {
        _parent = null;
        if (!File.Exists(baseSchemaPath))
        {
            throw new FileNotFoundException(
                $"Base schema file not found. Ensure '{baseSchemaPath}' is in the application directory.",
                baseSchemaPath);
        }
        ParseFromXml(baseSchemaPath);
    }

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
    /// Initializes a new instance of the <see cref="DynamicTypeRegistry"/> class from a collection of types.
    /// Used for creating local registries for serialization.
    /// </summary>
    public DynamicTypeRegistry(IEnumerable<DynamicHavokType> types)
    {
        foreach (DynamicHavokType type in types)
        {
            _types.TryAdd(type.Name, type);
            ulong hash = GetTypeHash(type); // This assumes parent registry has hash info if needed
            if (hash != 0) _typesByHash.TryAdd(hash, type);
        }
    }

    private static global::HKLib.Reflection.HavokType.TypeKind GetKindFromXmlTag(string? tagName, string typeName)
    {
        if (typeName.Contains("Enum")) return global::HKLib.Reflection.HavokType.TypeKind.Enum;
        if (typeName.Contains("Flags")) return global::HKLib.Reflection.HavokType.TypeKind.Flags;

        return tagName switch
        {
            "Void" => global::HKLib.Reflection.HavokType.TypeKind.Void,
            "Opaque" => global::HKLib.Reflection.HavokType.TypeKind.Opaque,
            "Bool" => global::HKLib.Reflection.HavokType.TypeKind.Bool,
            "String" => typeName switch
            {
                "char" => global::HKLib.Reflection.HavokType.TypeKind.Char,
                "const char*" => global::HKLib.Reflection.HavokType.TypeKind.CString,
                _ => global::HKLib.Reflection.HavokType.TypeKind.String
            },
            "Int" => typeName switch
            {
                "char" => global::HKLib.Reflection.HavokType.TypeKind.Char,
                "signed char" or "hkInt8" => global::HKLib.Reflection.HavokType.TypeKind.Int8,
                "unsigned char" or "hkUint8" => global::HKLib.Reflection.HavokType.TypeKind.UInt8,
                "short" or "hkInt16" => global::HKLib.Reflection.HavokType.TypeKind.Int16,
                "unsigned short" or "hkUint16" => global::HKLib.Reflection.HavokType.TypeKind.UInt16,
                "int" or "hkInt32" => global::HKLib.Reflection.HavokType.TypeKind.Int32,
                "unsigned int" or "hkUint32" => global::HKLib.Reflection.HavokType.TypeKind.UInt32,
                "long long" or "hkInt64" => global::HKLib.Reflection.HavokType.TypeKind.Int64,
                "unsigned long long" or "hkUint64" => global::HKLib.Reflection.HavokType.TypeKind.UInt64,
                _ => global::HKLib.Reflection.HavokType.TypeKind.Int32
            },
            "Float" => typeName switch
            {
                "hkHalf" or "hkHalf16" => global::HKLib.Reflection.HavokType.TypeKind.Half,
                "float" or "hkReal" => global::HKLib.Reflection.HavokType.TypeKind.Float,
                "double" or "hkDouble64" => global::HKLib.Reflection.HavokType.TypeKind.Double,
                _ => global::HKLib.Reflection.HavokType.TypeKind.Float
            },
            "Pointer" => global::HKLib.Reflection.HavokType.TypeKind.Pointer,
            "Record" => global::HKLib.Reflection.HavokType.TypeKind.Record,
            "Array" => global::HKLib.Reflection.HavokType.TypeKind.Array,
            _ => global::HKLib.Reflection.HavokType.TypeKind.Record
        };
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
            string? parentTagName = typeElement.Parent?.Name.LocalName;
            string? hashStr = typeElement.Attribute("Hash")?.Value;
            string? id = typeElement.Attribute("Id")?.Value;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hashStr) || string.IsNullOrEmpty(id)) continue;
            if (_types.ContainsKey(name)) continue;

            var dynamicType = new DynamicHavokType
            {
                Name = name,
                Size = int.TryParse(typeElement.Attribute("Size")?.Value, out int size) ? size : 0,
                Kind = GetKindFromXmlTag(parentTagName, name)
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

            // Parse Members
            foreach (XElement memberElement in typeElement.Elements("Members").Elements("Member"))
            {
                string? memberName = memberElement.Attribute("Name")?.Value;
                string? memberTypeId = memberElement.Attribute("Type")?.Value;
                string? memberOffsetStr = memberElement.Attribute("Offset")?.Value;
                string? memberFlagsStr = memberElement.Attribute("Flags")?.Value;

                if (string.IsNullOrEmpty(memberName) || string.IsNullOrEmpty(memberTypeId) ||
                    !xmlTypesById.TryGetValue(memberTypeId, out DynamicHavokType? memberType) ||
                    !int.TryParse(memberOffsetStr, out int memberOffset))
                {
                    continue;
                }

                var field = new DynamicHavokField
                {
                    Name = memberName,
                    Type = memberType,
                    Offset = memberOffset,
                    Flags = int.TryParse(memberFlagsStr, out int flags) ? flags : 0
                };
                dynamicType.Fields.Add(field);
            }

            // Parse Template
            foreach (XElement templateElement in typeElement.Elements("Template").Elements("Parameter"))
            {
                string? paramName = templateElement.Attribute("Name")?.Value;
                string? paramKind = templateElement.Attribute("Kind")?.Value;
                string? paramValue = templateElement.Attribute("Value")?.Value;

                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(paramKind) || string.IsNullOrEmpty(paramValue)) continue;

                var templateParam = new DynamicHavokTemplateParameter { Name = paramName, Kind = paramKind };

                if (paramKind == "Type" && xmlTypesById.TryGetValue(paramValue, out DynamicHavokType? paramType))
                {
                    templateParam.Type = paramType;
                }
                else if (paramKind == "Value")
                {
                    templateParam.Value = paramValue;
                }
                dynamicType.TemplateParameters.Add(templateParam);
            }

            // Parse Presets
            foreach (XElement presetElement in typeElement.Elements("Presets").Elements("Preset"))
            {
                dynamicType.Presets.Add(new DynamicHavokPreset { Name = presetElement.Attribute("Name")!.Value, Value = presetElement.Attribute("Value")?.Value ?? "null" });
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
            int subSectionDataSize = reader.ReadInt32();
            reader.BaseStream.Position += 8; // Skip padding/version

            // Make the scanning more robust. If we don't recognize a section,
            // read its size and skip it instead of stopping the entire parse.
            if (subMagic is "TST1" or "FST1" or "THSH" or "STR1")
            {
                // The offset is the start of the sub-section header, not its data.
                sections.Add(subMagic, (subSectionHeaderOffset, subSectionDataSize));
            }
            else
            {
                // Skip unknown or irrelevant sections and continue scanning.
                Debug.WriteLine($"Skipping unknown or irrelevant section '{subMagic}' in TYPE container.");
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

    private static global::HKLib.Reflection.HavokType.TypeKind GetKindFromBinaryFlags(int flags)
    {
        // This is a guess based on hk2018 format. The lower 5 bits often represent the type kind.
        int kindValue = flags & 0x1F;
        return kindValue switch
        {
            0 => global::HKLib.Reflection.HavokType.TypeKind.Void,
            1 => global::HKLib.Reflection.HavokType.TypeKind.Opaque,
            2 => global::HKLib.Reflection.HavokType.TypeKind.Bool,
            3 => global::HKLib.Reflection.HavokType.TypeKind.Char,
            4 => global::HKLib.Reflection.HavokType.TypeKind.Int8,
            5 => global::HKLib.Reflection.HavokType.TypeKind.UInt8,
            6 => global::HKLib.Reflection.HavokType.TypeKind.Int16,
            7 => global::HKLib.Reflection.HavokType.TypeKind.UInt16,
            8 => global::HKLib.Reflection.HavokType.TypeKind.Int32,
            9 => global::HKLib.Reflection.HavokType.TypeKind.UInt32,
            10 => global::HKLib.Reflection.HavokType.TypeKind.Int64,
            11 => global::HKLib.Reflection.HavokType.TypeKind.UInt64,
            12 => global::HKLib.Reflection.HavokType.TypeKind.Real,
            13 => global::HKLib.Reflection.HavokType.TypeKind.Vector4,
            14 => global::HKLib.Reflection.HavokType.TypeKind.Quaternion,
            15 => global::HKLib.Reflection.HavokType.TypeKind.Matrix3,
            16 => global::HKLib.Reflection.HavokType.TypeKind.Rotation,
            17 => global::HKLib.Reflection.HavokType.TypeKind.QsTransform,
            18 => global::HKLib.Reflection.HavokType.TypeKind.Matrix4,
            19 => global::HKLib.Reflection.HavokType.TypeKind.Transform,
            20 => global::HKLib.Reflection.HavokType.TypeKind.Pointer,
            21 => global::HKLib.Reflection.HavokType.TypeKind.FunctionPointer,
            22 => global::HKLib.Reflection.HavokType.TypeKind.Array,
            23 => global::HKLib.Reflection.HavokType.TypeKind.InplaceArray,
            24 => global::HKLib.Reflection.HavokType.TypeKind.Enum,
            25 => global::HKLib.Reflection.HavokType.TypeKind.Record,
            _ => global::HKLib.Reflection.HavokType.TypeKind.Record,
        };
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

            var havokType = new DynamicHavokType
            {
                Name = name,
                Size = size,
                Kind = GetKindFromBinaryFlags(flags)
            };
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
            // Use an indexer to ensure the binary compendium's types overwrite any from the base XML schema.
            // The binary compendium is the source of truth.
            _typesByHash[hash] = _tempTypes[i].Type;
        }
        Debug.WriteLine($"Loaded {numHashes} type hashes from THSH section.");
    }

    public DynamicHavokType? GetType(string name)
    {
        if (_types.TryGetValue(name, out DynamicHavokType? type))
        {
            return type;
        }

        return _parent?.GetType(name);
    }

    public DynamicHavokType? GetType(ulong hash)
    {
        if (_typesByHash.TryGetValue(hash, out DynamicHavokType? type))
        {
            return type;
        }

        return _parent?.GetType(hash);
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

    public ulong GetTypeHash(DynamicHavokType type)
    {
        foreach (var (hash, havokType) in _typesByHash)
        {
            if (havokType == type) return hash;
        }

        return _parent?.GetTypeHash(type) ?? 0;
    }

    public void WriteToBinary(Stream stream)
    {
        var writer = new BinaryWriter(stream, Encoding.ASCII, true);

        // Collect all necessary strings, types, and fields
        var stringMap = new Dictionary<string, int>();
        var typeList = _types.Values.ToList();
        var typeToIndex = typeList.Select((t, i) => (t, i)).ToDictionary(pair => pair.t, pair => pair.i);
        var fieldList = new List<DynamicHavokField>();

        void AddString(string s)
        {
            if (!string.IsNullOrEmpty(s) && !stringMap.ContainsKey(s))
            {
                stringMap.Add(s, 0);
            }
        }

        foreach (var type in typeList)
        {
            AddString(type.Name);
            foreach (var field in type.Fields)
            {
                AddString(field.Name);
                fieldList.Add(field);
            }
        }

        var sortedStrings = stringMap.Keys.OrderBy(s => s).ToList();
        for (int i = 0; i < sortedStrings.Count; i++)
        {
            stringMap[sortedStrings[i]] = i;
        }

        // Write sections
        writer.Write(Encoding.ASCII.GetBytes("TYPE"));
        writer.Write(0); // Placeholder for size
        writer.Write(0L);

        long typeStart = stream.Position;

        // STR1
        long str1Start = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("STR1"));
        var str1Stream = new MemoryStream();
        var str1Writer = new BinaryWriter(str1Stream);
        foreach (string s in sortedStrings)
        {
            str1Writer.Write(Encoding.ASCII.GetBytes(s));
            str1Writer.Write((byte)0);
        }
        writer.Write((int)str1Stream.Length);
        writer.Write(0L);
        str1Stream.WriteTo(stream);
        Align(stream, 16);

        // TST1
        long tst1Start = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("TST1"));
        writer.Write(typeList.Count * 32);
        writer.Write(typeList.Count);
        writer.Write(0);
        int fieldIdxCounter = 0;
        foreach (var type in typeList)
        {
            writer.Write(stringMap[type.Name]);
            writer.Write(type.Parent is null ? -1 : typeToIndex[type.Parent]);
            writer.Write(type.Size);
            writer.Write(16); // Alignment
            writer.Write(0); // Flags
            writer.Write(0); // Version
            writer.Write(fieldIdxCounter);
            writer.Write(type.Fields.Count);
            fieldIdxCounter += type.Fields.Count;
        }
        Align(stream, 16);

        // FST1
        long fst1Start = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("FST1"));
        writer.Write(fieldList.Count * 16);
        writer.Write(fieldList.Count);
        writer.Write(0);
        foreach (var field in fieldList)
        {
            writer.Write(typeToIndex[field.Type]);
            writer.Write(stringMap[field.Name]);
            writer.Write(field.Offset);
            writer.Write(field.Flags);
        }
        Align(stream, 16);

        // THSH
        writer.Write(Encoding.ASCII.GetBytes("THSH"));
        writer.Write(typeList.Count * 8);
        writer.Write(typeList.Count);
        writer.Write(0);
        foreach (var type in typeList)
        {
            writer.Write(GetTypeHash(type));
        }
        Align(stream, 16);

        long typeEnd = stream.Position;
        stream.Position = typeStart - 12;
        writer.Write((int)(typeEnd - typeStart));
        stream.Position = typeEnd;
    }

    private static void Align(Stream stream, int alignment)
    {
        long remainder = stream.Position % alignment;
        if (remainder == 0) return;
        for (int i = 0; i < alignment - remainder; i++)
        {
            stream.WriteByte(0);
        }
    }
}