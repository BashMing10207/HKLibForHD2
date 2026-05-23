﻿﻿﻿﻿﻿using System.Diagnostics;
using System;
using System.Numerics;
using HKLib.Reflection;
using HKLib.Reflection.Dynamic; // For DynamicTypeRegistry
using HKLib.Serialization.Util;
using System.Text;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// The main serializer for the Havok 2019 binary format.
/// </summary>
public class HavokBinarySerializer : IHavokSerializer
{
    private DynamicTypeRegistry? _typeRegistry;
    public DynamicTypeRegistry? TypeRegistry => _typeRegistry;
    private readonly Dictionary<long, IHavokObject> _objectsByAddress = new();

    object IHavokSerializer.Read(Stream stream) => Read(stream);

    void IHavokSerializer.Write(Stream stream, object rootObject)
    {
        if (rootObject is IHavokObject havokObj)
            Write(stream, havokObj);
        else
            throw new ArgumentException("rootObject must be IHavokObject", nameof(rootObject));
    }

    public void LoadCompendium(Stream stream, string? baseSchemaPath = null)
    {
        long typesOffset = -1;
        try
        {
            // Prefer parsing the header for a robust offset discovery.
            var reader = new BinaryReaderEx(false, stream);
            Dictionary<string, (long Offset, int Size)> sectionInfo = ReadTAG0(reader);
            (typesOffset, _) = FindSectionInfo(sectionInfo, "__types__");
        }
        catch (Exception ex)
        {
            // This will catch failures in ReadTAG0, like if it's not a TAG0 file.
            Debug.WriteLine($"Could not parse TAG0 header in compendium, will fall back to scanning. Error: {ex.Message}");
        }

        // If TAG0 parsing failed OR if the __types__ section wasn't found in the header
        if (typesOffset == -1)
        {
            Debug.WriteLine("Could not find __types__ section in TAG0 header, scanning for TYPE section magic instead.");
            // The actual data block we need to parse starts with "TYPE"
            typesOffset = FindSectionByScanning(stream, "TYPE");
        }

        if (typesOffset == -1)
        {
            throw new InvalidDataException("Could not find the __types__ or TYPE section in the compendium file.");
        }

        // Assumes the XML is in a known location. This should be made configurable.
        const string defaultSchemaPath = "HavokTypeRegistry20190100.xml";
        _typeRegistry = new DynamicTypeRegistry(baseSchemaPath ?? defaultSchemaPath, stream, typesOffset);
    }

    public void LoadCompendium(string path, string? baseSchemaPath = null)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LoadCompendium(fs, baseSchemaPath);
    }

    public IHavokObject Read(string path)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(fs);
    }

    public IHavokObject Read(Stream stream)
    {
        if (_typeRegistry is null)
        {
            throw new InvalidOperationException(
                "A compendium/master schema must be loaded via LoadCompendium() before deserialization.");
        }
        
        _objectsByAddress.Clear();
        try
        {
            var reader = new BinaryReaderEx(false, stream); // Assuming little-endian for HD2 Havok files based on previous context

            long dataSectionOffset = -1;
            Dictionary<string, (long Offset, int Size)>? sectionInfo = null;

            // 1. Try to find the __data__ section via the TAG0 header first.
            try
            {
                sectionInfo = ReadTAG0(reader);
                (dataSectionOffset, _) = FindSectionInfo(sectionInfo, "__data__");
            }
            catch (Exception ex)
            {
                // This will catch failures in ReadTAG0, e.g., if it's not a TAG0 file.
                Debug.WriteLine(
                    $"Could not parse TAG0 header in asset file, will fall back to scanning. Error: {ex.Message}");
            }

            // 2. If TAG0 parsing failed or didn't contain a __data__ section, scan for the TBDY magic as a fallback.
            // Some asset files might be raw data chunks without a full header.
            if (dataSectionOffset == -1)
            {
                Debug.WriteLine("Could not find __data__ section via TAG0, scanning for TBDY section magic instead.");
                dataSectionOffset = FindSectionByScanning(stream, "TBDY");
            }

            if (dataSectionOffset == -1)
            {
                throw new InvalidDataException("Could not find the __data__ or TBDY section in the Havok file.");
            }

            // 3. Seek to the data section and process it
            reader.Position = dataSectionOffset;
            string tbdYMagic = reader.ReadASCII(4); // TBDY (Type Body)
            if (tbdYMagic != "TBDY")
            {
                throw new InvalidDataException($"Invalid data section magic. Expected TBDY, got {tbdYMagic}.");
            }

            // The TBDY header is 16 bytes total. The data size is not needed for now, but we skip the rest of the header.
            reader.ReadInt32(); // Section data size
            reader.Skip(8); // Padding/Version

            // 4. Check for a local schema and prepare the type registry
            DynamicTypeRegistry registryToUse = _typeRegistry;
            if (sectionInfo is not null)
            {
                (long localTypesOffset, _) = FindSectionInfo(sectionInfo, "__types__");
                if (localTypesOffset != -1)
                {
                    Debug.WriteLine(
                        "Found local __types__ section. Creating a temporary, chained type registry for this file.");
                    registryToUse = new DynamicTypeRegistry(_typeRegistry, stream, localTypesOffset);
                }
            }

            // 5. Start reading the root object from the data section
            IHavokObject rootObject = ReadObjectData(reader, registryToUse);

            // 6. Perform fix-ups for pointers, arrays, and strings
            PerformFixups(reader, registryToUse);

            return rootObject;
        }
        finally
        {
            _objectsByAddress.Clear();
        }
    }

    /// <summary>
    /// Parses the TAG0 header of a Havok 2019 file to extract section information.
    /// </summary>
    private Dictionary<string, (long Offset, int Size)> ReadTAG0(BinaryReaderEx reader)
    {
        long tag0Offset = FindSectionByScanning(reader.Stream, "TAG0");
        if (tag0Offset == -1)
        {
            throw new InvalidDataException("Valid TAG0 section not found in stream.");
        }
        reader.Position = tag0Offset;

        string tag0Magic = reader.ReadASCII(4); // "TAG0"
        if (tag0Magic != "TAG0")
        {
            throw new InvalidDataException($"Invalid Havok file magic. Expected TAG0, got {tag0Magic}.");
        }

        reader.Skip(4); // Skip unknown 4 bytes (often 0x01000000)
        string sdkVersion = reader.ReadASCII(12); // "SDKV20190100"
        if (sdkVersion != "SDKV20190100")
        {
            throw new InvalidDataException($"Unsupported Havok SDK version. Expected SDKV20190100, got {sdkVersion}.");
        }

        reader.Skip(4); // Skip unknown 4 bytes (often 0x00000000)
        uint fileSize = reader.ReadUInt32();
        uint numSections = reader.ReadUInt32();
        reader.Skip(8); // Skip unknown 8 bytes (often 0x0000000000000000)

        var sectionInfo = new Dictionary<string, (long Offset, int Size)>();
        for (int i = 0; i < numSections; i++)
        {
            string sectionMagic = reader.ReadASCII(16).TrimEnd('\0'); // e.g., "__classnames__"
            long sectionOffset = reader.ReadInt64();
            int sectionSize = reader.ReadInt32();
            reader.Skip(4); // Padding
            sectionInfo[sectionMagic] = (sectionOffset, sectionSize);
        }
        return sectionInfo;
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
    private IHavokObject ReadObjectData(BinaryReaderEx reader, DynamicTypeRegistry registry)
    {
        // 1. Read the 8-byte type hash.
        ulong typeHash = reader.ReadUInt64();
        if (typeHash == 0) return null!; // Null pointer

        // 2. Look up the type in the registry using the hash.
        DynamicHavokType type = registry.GetType(typeHash) ??
                                throw new InvalidDataException($"Type with hash {typeHash} not found in registry.");

        long objectDataStart = reader.Position;
        var havokObject = new DynamicHavokObject(type);
        _objectsByAddress[objectDataStart - 8] = havokObject;

        // 3. Recursively read fields for the type and its parents
        ReadObjectFields(reader, registry, havokObject, objectDataStart);

        // 4. After reading all fields, advance the stream past the object
        reader.Position = objectDataStart + type.Size;

        return havokObject;
    }
    
    private void PerformFixups(BinaryReaderEx reader, DynamicTypeRegistry registry)
    {
        foreach (IHavokObject obj in _objectsByAddress.Values)
        {
            if (obj is not DynamicHavokObject dho) continue;

            foreach (DynamicHavokField field in dho.Type.GetAllFields())
            {
                if (!dho.Fields.TryGetValue(field.Name, out object? fieldValue)) continue;

                // Fixup hkArray and hkRelArray
                if (fieldValue is DynamicHavokObject fieldObject &&
                    (field.Type.Name == "hkArray" || field.Type.Name == "hkRelArray32"))
                {
                    if (fieldObject.Fields.TryGetValue("m_data", out object? data) &&
                        data is HavokPointer dataPtr &&
                        fieldObject.Fields.TryGetValue("m_size", out object? sizeObj) &&
                        sizeObj is int size and > 0)
                    {
                        DynamicHavokType? elementType = field.Type.SubType ?? field.Type.TemplateParameters.FirstOrDefault()?.Type;
                        if (elementType is null)
                        {
                            throw new InvalidDataException($"Could not determine element type for array '{field.Name}'.");
                        }

                        object?[] arrayData = ReadArrayData(reader, dataPtr.Address, size, elementType, registry);
                        dho.Fields[field.Name] = arrayData;
                    }
                    else
                    {
                        dho.Fields[field.Name] = Array.CreateInstance(typeof(object), 0);
                    }
                }
                // Fixup hkVariant
                else if (fieldValue is HavokPointer variantPtr && field.Type.Name == "hkRefVariant")
                {
                    if (variantPtr.Address == 0)
                    {
                        dho.Fields[field.Name] = null;
                        continue;
                    }

                    reader.StepIn(variantPtr.Address);
                    long objectPtr = reader.ReadInt64();
                    long classPtr = reader.ReadInt64(); // In hk2019 this is likely a hash, but let's treat as opaque for now
                    reader.StepOut();

                    if (objectPtr == 0)
                    {
                        dho.Fields[field.Name] = null;
                    }
                    else if (_objectsByAddress.TryGetValue(objectPtr, out var targetObject))
                    {
                        dho.Fields[field.Name] = targetObject;
                    }
                    else
                    {
                        // Could be a pointer to a type description, or something else not in the __data__ section.
                        // Store it as a variant for later inspection.
                        dho.Fields[field.Name] = new HavokVariant(objectPtr, classPtr);
                    }
                }
                // Fixup pointers (including hkStringPtr)
                else if (fieldValue is HavokPointer ptr)
                {
                    if (field.Type.Name == "hkStringPtr")
                    {
                        reader.StepIn(ptr.Address);
                        dho.Fields[field.Name] = reader.ReadASCII();
                        reader.StepOut();
                    }
                    else
                    {
                        if (_objectsByAddress.TryGetValue(ptr.Address, out IHavokObject? target))
                        {
                            dho.Fields[field.Name] = target;
                        }
                        else
                        {
                            // Pointer to something not in the __data__ section, e.g., a string or external data.
                            // For now, we leave it as a pointer.
                        }
                    }
                }
            }
        }
    }

    private object?[] ReadArrayData(BinaryReaderEx reader, long address, int count, DynamicHavokType elementType,
        DynamicTypeRegistry registry)
    {
        reader.StepIn(address);
        try
        {
            var array = new object?[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = ReadSingleValue(reader, registry, elementType);
            }

            return array;
        }
        finally
        {
            reader.StepOut();
        }
    }

    /// <summary>
    /// Reads all fields for a given object, including those from its parent types.
    /// </summary>
    private void ReadObjectFields(BinaryReaderEx reader, DynamicTypeRegistry registry, DynamicHavokObject havokObject,
        long objectDataStart)
    {
        var typesToProcess = new List<DynamicHavokType>();
        DynamicHavokType? currentType = havokObject.Type;
        while (currentType != null)
        {
            typesToProcess.Add(currentType);
            currentType = currentType.Parent;
        }

        // Process from base class to most derived
        typesToProcess.Reverse();

        foreach (DynamicHavokType type in typesToProcess)
        {
            foreach (DynamicHavokField field in type.Fields)
            {
                // Position the reader at the start of the field
                reader.Position = objectDataStart + field.Offset;

                // Read the field value
                object? fieldValue = ReadFieldValue(reader, registry, field);
                havokObject.Fields[field.Name] = fieldValue;
            }
        }
    }

    /// <summary>
    /// Reads a single field's value from the stream based on its type information.
    /// </summary>
    private object? ReadFieldValue(BinaryReaderEx reader, DynamicTypeRegistry registry, DynamicHavokField field)
    {
        var fieldType = field.Type!;

        // Check for C-style array e.g. float[4]
        var nParam = fieldType.TemplateParameters.FirstOrDefault(p => p.Name == "N" && p.Kind == "Value");
        if (nParam != null && int.TryParse(nParam.Value, out int arraySize))
        {
            // It's a C-style array.
            var tParam = fieldType.TemplateParameters.FirstOrDefault(p => p.Name == "T" && p.Kind == "Type");
            DynamicHavokType? elementType = tParam?.Type;
            if (elementType == null)
            {
                // Infer from name, e.g., "char[N]"
                string baseName = fieldType.Name.Split(new[] { '[', '<' })[0];
                elementType = registry.GetType(baseName);
            }

            if (elementType == null)
            {
                throw new InvalidDataException($"Could not determine element type for C-style array '{fieldType.Name}'.");
            }

            var array = new object?[arraySize];
            long arrayDataStart = reader.Position;

            int elementSize = registry.GetSizeOf(elementType.Name);
            if (elementSize == 0 && arraySize > 0)
            {
                throw new InvalidDataException($"Size of array element type '{elementType.Name}' is zero or not defined for C-style array.");
            }

            for (int i = 0; i < arraySize; i++)
            {
                reader.Position = arrayDataStart + (i * elementSize);
                array[i] = ReadSingleValue(reader, registry, elementType);
            }
            return array;
        }

        return ReadSingleValue(reader, registry, fieldType);
    }

    /// <summary>
    /// Reads a single, non-array value from the stream.
    /// </summary>
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
        case HavokType.TypeKind.CString or HavokType.TypeKind.String or HavokType.TypeKind.Pointer or HavokType.TypeKind.Variant:
            long address = reader.ReadInt64();
            return address == 0 ? null : new HavokPointer(address);
        case HavokType.TypeKind.Record or HavokType.TypeKind.Array: // hkArray is a struct
            var nestedObject = new DynamicHavokObject(type);
            ReadObjectFields(reader, registry, nestedObject, reader.Position);
            return nestedObject;
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
            if (type.Size > 0)
            {
                Debug.WriteLine($"Unhandled TypeKind '{type.Kind}' for type '{type.Name}'. Treating as a Record as a fallback.");
                var fallbackObject = new DynamicHavokObject(type);
                ReadObjectFields(reader, registry, fallbackObject, reader.Position);
                return fallbackObject;
            }
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

        var writer = new HavokBinaryWriter2019(stream, _typeRegistry);
        writer.Write(rootObject);
    }
}