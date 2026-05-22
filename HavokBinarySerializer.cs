using HKLib.Reflection.Dynamic;
using HKLib.Serialization.Util;
using System.Text;
using System.Diagnostics;
using HKLib.Serialization;
using HKLib.hk2018;
using HKLib.Serialization.hk2019.Binary;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// The serializer for the Havok 2019 binary format.
/// </summary>
public class HavokBinarySerializer : IHavokSerializer
{
    private DynamicTypeRegistry? _typeRegistry;

    public void LoadCompendium(Stream stream)
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
        const string baseSchemaPath = "HavokTypeRegistry20190100.xml";
        _typeRegistry = new DynamicTypeRegistry(baseSchemaPath, stream, typesOffset);
    }

    public void LoadCompendium(string path)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        LoadCompendium(fs);
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

        var reader = new BinaryReaderEx(false, stream); // Assuming little-endian for HD2 Havok files based on previous context

        // 1. Read TAG0 header and get section information
        Dictionary<string, (long Offset, int Size)> sectionInfo = ReadTAG0(reader);

        // 2. Find the __data__ section
        (long dataSectionOffset, int dataSectionSize) = FindSectionInfo(sectionInfo, "__data__");
        if (dataSectionOffset == -1)
        {
            throw new InvalidDataException("Could not find __data__ section in Havok file.");
        }

        // 3. Seek to the __data__ section and read TBDY magic
        reader.Position = dataSectionOffset;
        string tbdYMagic = reader.ReadASCII(4); // TBDY (Type Body)
        if (tbdYMagic != "TBDY")
        {
            throw new InvalidDataException($"Invalid data section magic. Expected TBDY, got {tbdYMagic}.");
        }

        // Skip TBDY header (usually 0x10 bytes, but can vary)
        // For now, assume a fixed 0x10 header for TBDY. A robust implementation would parse this header.
        reader.Skip(0x0C); // Skip remaining 12 bytes of TBDY header

        // 4. Start reading the root object from the data section
        // The actual deserialization of the object graph is a complex recursive process
        // that needs to be implemented based on DynamicHavokType.
        return ReadObjectData(reader, _typeRegistry);
    }

    /// <summary>
    /// Parses the TAG0 header of a Havok 2019 file to extract section information.
    /// </summary>
    private Dictionary<string, (long Offset, int Size)> ReadTAG0(BinaryReaderEx reader)
    {
        long tag0Offset = FindTag0Offset(reader.Stream);
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
    private long FindTag0Offset(Stream stream)
    {
        long originalPosition = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);

        byte[] pattern = Encoding.ASCII.GetBytes("TAG0");
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
    /// Finds a section by performing a simple byte scan on the stream. Used as a fallback.
    /// </summary>
    private long FindSectionByScanning(Stream stream, string sectionName)
    {
        long originalPosition = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);

        byte[] pattern = Encoding.ASCII.GetBytes(sectionName);
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
        // 1. Read the 8-byte type hash. This is the core change for Phase 2.5.
        ulong typeHash = reader.ReadUInt64();

        // 2. Look up the type in the registry using the hash.
        DynamicHavokType type = registry.GetType(typeHash) ?? throw new InvalidDataException($"Type with hash {typeHash} not found in registry.");

        // 3. For now, we only identify the type and skip its data.
        // A full implementation would recursively deserialize the object's fields.
        // For the purpose of resolving the compile error and moving forward, we skip the bytes
        // and return a placeholder object that satisfies the interface.
        if (type.Size > 0)
        {
            reader.Skip(type.Size);
        }

        // Return a simple concrete implementation of IHavokObject to satisfy the compiler.
        return new hkReferencedObject();
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

        HavokBinaryWriter2019 writer = new(stream, _typeRegistry);
        writer.Write(rootObject);
    }
}