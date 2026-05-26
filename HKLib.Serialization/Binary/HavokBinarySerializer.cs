using System.Collections;
using System;
using System.Numerics;
using HKLib.Reflection.Dynamic;
using HKLib.Serialization.Util;
using System.Text;
using System.Diagnostics;
using HKLib.Serialization;

using HKLib.Reflection;
using IHavokObject = HKLib.hk2018.IHavokObject;

namespace HKLib.Serialization.Binary;

/// <summary>
/// The main serializer for the Havok 2019 binary format. This serializer is for the hk2019 format.
/// </summary>
public class HavokBinarySerializer : IHavokSerializer
{
    private record HavokFileHeader(
        uint FileSize,
        uint NumSections,
        long ContentsPosition,
        long ContentsClassNamePosition,
        Dictionary<string, (long Offset, int Size)> Sections
    );
    private DynamicTypeRegistry? _typeRegistry;
    public DynamicTypeRegistry? TypeRegistry => _typeRegistry;
    private readonly Dictionary<long, IHavokObject> _objectsByAddress = new();
    private readonly Dictionary<long, object> _arraysByAddress = new();

    object IHavokSerializer.Read(Stream stream) => Read(stream, null);

    void IHavokSerializer.Write(Stream stream, object rootObject)
    {
        if (rootObject is IHavokObject havokObj)
            Write(stream, havokObj);
        else
            throw new ArgumentException("rootObject must be IHavokObject", nameof(rootObject));
    }

    /// <summary>
    /// Loads type information from a compendium file. This is now private as the public Read method handles compendium
    /// loading automatically.
    /// </summary>
    private void LoadCompendium(Stream stream, string? baseSchemaPath = null)
    {
        const string defaultSchemaPath = "HavokTypeRegistry20190100.xml";
        long streamStartPos = stream.Position;
        try
        {
            // A compendium file is a full Havok file. We need to parse its TAG0 header
            // to find the __types__ section which contains the type definitions.
            var reader = new BinaryReaderEx(false, stream);
            HavokFileHeader header = ReadTAG0(reader);
            (long typesOffset, _) = FindSectionInfo(header.Sections, "__types__");
            if (typesOffset != -1)
            {
                // A types section was found. Initialize the registry with the base schema AND the types from the compendium.
                Debug.WriteLine($"Compendium types section found at offset 0x{typesOffset:X}. Loading types.");
                _typeRegistry = new DynamicTypeRegistry(baseSchemaPath ?? defaultSchemaPath, stream, typesOffset);
                return; // Success
            }
        }
        catch (Exception ex)
        {
            // This can happen if the file is not a valid Havok file, or if old/incorrect code is run.
            // Per the requirement to treat the compendium as an optional enhancement, we will catch ANY exception
            // and fall back to using the base schema only. This prevents the application from crashing.
            Debug.WriteLine($"WARNING: Could not parse compendium file. It may be invalid or in an unexpected format. Falling back to base schema. Error: {ex.Message}");
            stream.Position = streamStartPos; // Reset stream position after failure
        }

        // Fallback: If parsing failed or no __types__ section was found, initialize with base schema only.
        Debug.WriteLine("Compendium file could not be parsed or did not contain a '__types__' section. Initializing with base schema only.");
        _typeRegistry = new DynamicTypeRegistry(baseSchemaPath ?? defaultSchemaPath);
    }

    /// <summary>
    /// Loads type information from a compendium file. This is now private as the public Read method handles compendium
    /// loading automatically.
    /// </summary>
    private void LoadCompendium(string path, string? baseSchemaPath = null)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LoadCompendium(fs, baseSchemaPath);
    }

    public IHavokObject Read(string path, string? compendiumPath = null)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(fs, compendiumPath);
    }

    public IHavokObject Read(Stream stream) => Read(stream, null);

    public IHavokObject Read(Stream stream, string? compendiumPath)
    {
        long streamStartPos = stream.Position;

        // Attempt 1: Read without compendium, using only the base schema and any local types.
        var baseRegistry = new DynamicTypeRegistry("HavokTypeRegistry20190100.xml");
        try
        {
            Debug.WriteLine("Attempting to deserialize using the base schema...");
            return ReadInternal(stream, baseRegistry);
        }
        catch (Exception ex) when (compendiumPath != null && ex is InvalidDataException && ex.Message.Contains("not found"))
        {
            Debug.WriteLine($"Initial read failed due to a missing type. Retrying with compendium: {compendiumPath}");
            Debug.WriteLine($"Initial error: {ex.Message}");

            // Attempt 2: Read with compendium
            try
            {
                stream.Position = streamStartPos;
                LoadCompendium(compendiumPath); // This sets _typeRegistry
                if (_typeRegistry is null) // Should not happen if LoadCompendium is correct
                {
                    throw new InvalidOperationException("Compendium loading failed to initialize the type registry.");
                }
                return ReadInternal(stream, _typeRegistry);
            }
            catch (Exception retryEx)
            {
                throw new InvalidOperationException("Deserialization failed even after loading the compendium.", retryEx);
            }
        }
    }

    private IHavokObject ReadInternal(Stream stream, DynamicTypeRegistry registry)
    {
        _objectsByAddress.Clear();
        _arraysByAddress.Clear();
        try
        {
            var reader = new BinaryReaderEx(false, stream);

            HavokFileHeader header = ReadTAG0(reader);

            DynamicTypeRegistry registryToUse = registry;
            (long localTypesOffset, _) = FindSectionInfo(header.Sections, "__types__");
            if (localTypesOffset != -1)
            {
                Debug.WriteLine("Found local __types__ section. Creating a temporary, chained type registry for this file.");
                registryToUse = new DynamicTypeRegistry(registry, stream, localTypesOffset);
            }

            if (header.ContentsPosition <= 0)
            {
                throw new InvalidDataException(
                    $"Invalid root object offset in file header: 0x{header.ContentsPosition:X}. The file might be corrupt or an unsupported format.");
            }

            reader.Position = header.ContentsPosition;
            return ReadObject(reader, registryToUse);
        }
        finally
        {
            _objectsByAddress.Clear();
            _arraysByAddress.Clear();
        }
    }


    /// <summary>
    /// Parses the TAG0 header of a Havok 2019 file to extract section information.
    /// </summary>
    private HavokFileHeader ReadTAG0(BinaryReaderEx reader)
    {
        if (reader.Length < 16) throw new InvalidDataException("File is too small to be a valid Havok file.");
        reader.Position = reader.Length - 16;
        long tag0Offset = reader.ReadInt32();
        uint footerMagic = reader.ReadUInt32();
        if (footerMagic != 0x57E0E057)
        {
            // Fallback for files without a standard footer, maybe older variants.
            tag0Offset = FindSectionByScanning(reader.Stream, "TAG0");
            if (tag0Offset == -1)
            {
                throw new InvalidDataException("Valid TAG0 section or footer not found in stream.");
            }
        }

        reader.Position = tag0Offset;

        string tag0Magic = reader.ReadASCII(4); // "TAG0"
        if (tag0Magic != "TAG0")
        {
            throw new InvalidDataException($"Invalid Havok file magic. Expected TAG0, got {tag0Magic}.");
        }

        reader.Skip(4); // Skip version (often 0x01000000)
        string sdkVersion = reader.ReadASCII(12); // "SDKV20190100"
        if (sdkVersion != "SDKV20190100")
        {
            throw new InvalidDataException($"Unsupported Havok SDK version. Expected SDKV20190100, got {sdkVersion}.");
        }

        reader.Skip(1); // isLittleEndian
        reader.Skip(3); // padding
        uint fileSize = reader.ReadUInt32();
        uint numSections = reader.ReadUInt32();
        long contentsPosition = reader.ReadInt64();
        long contentsClassNamePosition = reader.ReadInt64();

        var sectionInfo = new Dictionary<string, (long Offset, int Size)>();
        for (int i = 0; i < numSections; i++)
        {
            string sectionName = reader.ReadASCII(16).TrimEnd('\0'); // e.g., "__classnames__"
            long sectionOffset = reader.ReadInt64();
            int sectionSize = reader.ReadInt32();
            reader.Skip(4); // Padding
            sectionInfo[sectionName] = (sectionOffset, sectionSize);
        }

        return new HavokFileHeader(fileSize, numSections, contentsPosition, contentsClassNamePosition, sectionInfo);
    }

    private (long Offset, int Size) FindSectionInfo(IReadOnlyDictionary<string, (long Offset, int Size)> sectionInfo, string sectionName)
    {
        if (sectionInfo.TryGetValue(sectionName, out var info))
        {
            return info;
        }
        return (-1, 0); // Not found
    }

    /// <summary>
    /// Finds the offset of the TAG0 section, accounting for potential prepended data.
    /// </summary>
    private long FindSectionByScanning(Stream stream, string sectionName)
    {
        long originalPosition = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);

        byte[] pattern = Encoding.ASCII.GetBytes(sectionName.PadRight(4, '\0'));
        const int bufferSize = 8192;
        var buffer = new byte[bufferSize];
        long streamPosition = 0;

        try
        {
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i <= bytesRead - pattern.Length; i++)
                {
                    if (buffer.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                    {
                        return streamPosition + i;
                    }
                }

                streamPosition += bytesRead;
                if (stream.Position < stream.Length)
                {
                    stream.Seek(-(pattern.Length - 1), SeekOrigin.Current);
                    streamPosition -= (pattern.Length - 1);
                }
            }
        }
        finally
        {
            stream.Seek(originalPosition, SeekOrigin.Begin);
        }

        return -1;
    }

    /// <summary>
    /// Reads an object from the current stream position using its type hash.
    /// This method replaces the logic that caused the "Invalid type index" error.
    /// </summary>
    private IHavokObject ReadObject(BinaryReaderEx reader, DynamicTypeRegistry registry)
    {
        long objectAddress = reader.Position;
        if (objectAddress == 0) return null!;

        if (_objectsByAddress.TryGetValue(objectAddress, out IHavokObject? cachedObject))
        {
            return cachedObject;
        }

        ulong typeHash = reader.ReadUInt64();
        DynamicHavokType type = registry.GetType(typeHash) ??
                                throw new InvalidDataException($"Type with hash {typeHash} not found in registry.");

        HKLib.Reflection.hk2018.HavokType? hk2018Type = HKLib.Reflection.hk2018.HavokTypeRegistry.Instance.GetType(type.Name);
        if (hk2018Type == null || hk2018Type.Type == null)
            throw new InvalidDataException($"Type {type.Name} not found in C# class registry.");

        var havokObject = (IHavokObject)Activator.CreateInstance(hk2018Type.Type)!;
        _objectsByAddress.Add(objectAddress, havokObject);

        var havokData = HKLib.Reflection.hk2018.HavokData.Of(havokObject, hk2018Type);

        // 1. Read optional members bitfield
        BitArray? optionals = null;
        var optionalFields = type.OptionalFields;
        if (optionalFields.Count > 0)
        {
            int numOptionalBytes = (optionalFields.Count + 7) / 8;
            byte[] optionalBytes = reader.ReadBytes(numOptionalBytes);
            optionals = new BitArray(optionalBytes);
        }

        // 2. Read fields sequentially, handling alignment and skipping non-present optional fields
        int optionalFieldIndex = 0;
        foreach (var field in type.GetAllFields())
        {
            if (field.IsOptional)
            {
                bool isPresent = optionals != null && optionalFieldIndex < optionals.Length && optionals[optionalFieldIndex];
                optionalFieldIndex++;
                if (!isPresent)
                {
                    continue; // Skip this field
                }
            }

            // Align stream for the current field
            if (field.Type!.Alignment > 0)
            {
                reader.Pad(field.Type.Alignment);
            }

            object? fieldValue = ReadFieldValue(reader, registry, field);
            havokData.TrySetField(field.Name, fieldValue);
        }

        // 3. After reading all present fields, align to the object's alignment
        if (type.Alignment > 0)
        {
            reader.Pad(type.Alignment);
        }

        return havokObject;
    }

    private object? ReadFieldValue(BinaryReaderEx reader, DynamicTypeRegistry registry, DynamicHavokField field)
    {
        var fieldType = field.Type!;
        var nParam = fieldType.TemplateParameters.FirstOrDefault(p => p.Name == "N" && p.Kind == "Value");
        if (nParam != null && int.TryParse(nParam.Value, out int arraySize))
        {
            // C-style array
            DynamicHavokType? elementType = fieldType.SubType ?? fieldType.TemplateParameters.FirstOrDefault(p => p.Name == "T" && p.Kind == "Type")?.Type;
            if (elementType == null)
            {
                string baseName = fieldType.Name.Split(new[] { '[', '<' })[0];
                elementType = registry.GetType(baseName);
            }
            if (elementType == null) throw new InvalidDataException($"Could not determine element type for C-style array '{fieldType.Name}'.");

            HKLib.Reflection.hk2018.HavokType? elementTypeHk2018 = HKLib.Reflection.hk2018.HavokTypeRegistry.Instance.GetType(elementType.Name);
            if (elementTypeHk2018 == null || elementTypeHk2018.Type == null) throw new InvalidDataException($"Type {elementType.Name} not found in C# class registry.");

            Array csharpArray = Array.CreateInstance(elementTypeHk2018.Type, arraySize);
            for (int i = 0; i < csharpArray.Length; i++)
            {
                csharpArray.SetValue(ReadSingleValue(reader, registry, elementType), i);
            }
            return csharpArray;
        }

        return ReadSingleValue(reader, registry, fieldType);
    }

    private object? ReadSingleValue(BinaryReaderEx reader, DynamicTypeRegistry registry, DynamicHavokType type)
    {
        // Special handling for known struct types by name
        switch (type.Name)
        {
            case "hkVector4":
                return reader.ReadVector4();
            case "hkQuaternion":
                return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            case "hkMatrix3":
            case "hkRotation":
                return new[] { reader.ReadVector4(), reader.ReadVector4(), reader.ReadVector4() };
            case "hkMatrix4":
            case "hkTransform":
                return new[] { reader.ReadVector4(), reader.ReadVector4(), reader.ReadVector4(), reader.ReadVector4() };
        }

        switch (type.Kind)
        {
            case HavokType.TypeKind.Void or HavokType.TypeKind.Opaque:
                return null;
            case HavokType.TypeKind.Bool:
                return reader.ReadByte() != 0;
            case HavokType.TypeKind.Char or HavokType.TypeKind.Int8:
                return reader.ReadSByte();
            case HavokType.TypeKind.UInt8:
                return reader.ReadByte();
            case HavokType.TypeKind.Int16:
                return reader.ReadInt16();
            case HavokType.TypeKind.UInt16:
                return reader.ReadUInt16();
            case HavokType.TypeKind.Int32:
                return reader.ReadInt32();
            case HavokType.TypeKind.UInt32:
                return reader.ReadUInt32();
            case HavokType.TypeKind.Int64:
                return reader.ReadInt64();
            case HavokType.TypeKind.UInt64:
                return reader.ReadUInt64();
            case HavokType.TypeKind.Half: return (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            case HavokType.TypeKind.Real or HavokType.TypeKind.Float:
                return reader.ReadSingle();
            case HavokType.TypeKind.Double:
                return reader.ReadDouble();
            case HavokType.TypeKind.Pointer or HavokType.TypeKind.Variant:
                long address = reader.ReadInt64();
                if (address == 0) return null;

                reader.StepIn(address);
                IHavokObject target = ReadObject(reader, registry);
                reader.StepOut();
                return target;

            case HavokType.TypeKind.CString or HavokType.TypeKind.String:
                long stringAddress = reader.ReadInt64();
                if (stringAddress == 0) return null;

                reader.StepIn(stringAddress);
                string str = reader.ReadASCII();
                reader.StepOut();
                return str;

            case HavokType.TypeKind.Array:
                long dataAddress = reader.ReadInt64();
                int size = reader.ReadInt32();
                reader.ReadInt32(); // capacityAndFlags

                DynamicHavokType? elementType = type.SubType ?? type.TemplateParameters.FirstOrDefault()?.Type;
                if (elementType is null)
                {
                    throw new InvalidDataException($"Could not determine element type for array '{type.Name}'.");
                }

                HKLib.Reflection.hk2018.HavokType? elementHk2018Type = HKLib.Reflection.hk2018.HavokTypeRegistry.Instance.GetType(elementType.Name);
                if (elementHk2018Type == null || elementHk2018Type.Type == null)
                    throw new InvalidDataException($"Type {elementType.Name} not found in C# class registry.");

                Type genericListType = typeof(List<>).MakeGenericType(elementHk2018Type.Type);
                IList list = (IList)Activator.CreateInstance(genericListType)!;

                if (dataAddress == 0 || size == 0)
                {
                    return list;
                }

                if (_arraysByAddress.TryGetValue(dataAddress, out object? cachedArray))
                {
                    return cachedArray;
                }

                _arraysByAddress.Add(dataAddress, list);

                reader.StepIn(dataAddress);
                for (int i = 0; i < size; i++)
                {
                    list.Add(ReadSingleValue(reader, registry, elementType));
                }
                reader.StepOut();
                return list;

            case HavokType.TypeKind.Record:
                // Align the start of the record
                if (type.Alignment > 0)
                {
                    reader.Pad(type.Alignment);
                }

                HKLib.Reflection.hk2018.HavokType? recordHk2018Type = HKLib.Reflection.hk2018.HavokTypeRegistry.Instance.GetType(type.Name);
                if (recordHk2018Type == null || recordHk2018Type.Type == null)
                    throw new InvalidDataException($"Type {type.Name} not found in C# class registry.");

                var recordObject = (IHavokObject)Activator.CreateInstance(recordHk2018Type.Type)!;
                var recordData = HKLib.Reflection.hk2018.HavokData.Of(recordObject, recordHk2018Type);

                BitArray? recordOptionals = null;
                var recordOptionalFields = type.OptionalFields;
                if (recordOptionalFields.Count > 0)
                {
                    int numOptionalBytes = (recordOptionalFields.Count + 7) / 8;
                    byte[] optionalBytes = reader.ReadBytes(numOptionalBytes);
                    recordOptionals = new BitArray(optionalBytes);
                }

                int recordOptionalIndex = 0;
                foreach (var field in type.GetAllFields())
                {
                    if (field.IsOptional)
                    {
                        bool isPresent = recordOptionals != null && recordOptionalIndex < recordOptionals.Length && recordOptionals[recordOptionalIndex];
                        recordOptionalIndex++;
                        if (!isPresent) continue;
                    }

                    if (field.Type!.Alignment > 0) reader.Pad(field.Type.Alignment);

                    object? fieldValue = ReadFieldValue(reader, registry, field);
                    recordData.TrySetField(field.Name, fieldValue);
                }

                if (type.Alignment > 0) reader.Pad(type.Alignment);
                return recordObject;

            case HavokType.TypeKind.Enum or HavokType.TypeKind.Flags:
                return type.Size switch
                {
                    1 => (object)reader.ReadByte(),
                    2 => reader.ReadUInt16(),
                    4 => reader.ReadUInt32(),
                    8 => reader.ReadUInt64(),
                    _ => throw new InvalidDataException($"Unsupported enum/flags size: {type.Size}")
                };
            default:
                throw new NotImplementedException($"Reading field of type kind {type.Kind} ('{type.Name}') is not implemented.");
        }
    }
    public void Write(IHavokObject rootObject, string path)
    {
        using FileStream fs = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Write(fs, rootObject);
    }

    public void Write(Stream stream, IHavokObject rootObject)
    {
        if (_typeRegistry is null)
        {
            // For writing, we might be able to get away without a master schema if the object graph is simple,
            // but for full compatibility, it's needed.
            throw new InvalidOperationException("A compendium/master schema should be loaded for writing as well.");
        }

        var writer = new HavokBinaryWriter(stream, _typeRegistry);
        writer.Write(rootObject);
    }
}