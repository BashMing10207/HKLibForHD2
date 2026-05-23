using System.Diagnostics;
using System.Numerics;
using System.Text;
using HKLib.Reflection;
using HKLib.Reflection.Dynamic;
using HKLib.Serialization.Util;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Handles the process of writing a Havok object graph to a hk2019 binary stream.
/// </summary>
public class HavokBinaryWriter2019
{
    private readonly Stream _stream;
    private readonly DynamicTypeRegistry _registry;
    private readonly BinaryWriterEx _writer;

    // Layout pass data
    private readonly Dictionary<IHavokObject, long> _objectAddresses = new();
    private readonly List<IHavokObject> _objectsToWrite = new();
    private readonly Dictionary<string, long> _stringAddresses = new();
    private readonly List<string> _stringsToWrite = new();
    private readonly Dictionary<object, long> _arrayAddresses = new(); // Key is the array object itself
    private readonly List<object> _arraysToWrite = new(); // The array objects
    private long _currentDataOffset;

    public HavokBinaryWriter2019(Stream stream, DynamicTypeRegistry registry)
    {
        _stream = stream;
        _registry = registry;
        _writer = new BinaryWriterEx(false, stream);
    }

    public void Write(IHavokObject rootObject)
    {
        // 1. Layout Pass: Traverse graph and calculate offsets for all objects, arrays, and strings.
        LayoutGraph(rootObject);

        // 2. Identify and prepare local types if any are present.
        DynamicTypeRegistry? localRegistry = CreateLocalRegistry(rootObject);
        using var typesStream = new MemoryStream();
        if (localRegistry is not null)
        {
            localRegistry.WriteToBinary(typesStream);
        }

        // 3. Write Pass: Write the actual data to a temporary memory stream.
        using var dataStream = new MemoryStream();
        var dataWriter = new BinaryWriterEx(false, dataStream);
        WriteData(dataWriter);

        // 4. Finalize: Write the TAG0 header and all sections to the output stream.
        var sections = new Dictionary<string, MemoryStream> { { "__data__", dataStream } };
        if (typesStream.Length > 0)
        {
            sections.Add("__types__", typesStream);
        }

        WriteTag0Header(sections);
        dataStream.Position = 0;
        typesStream.Position = 0;
        foreach (var stream in sections.Values)
        {
            stream.CopyTo(_stream);
        }
    }

    /// <summary>
    /// Traverses the object graph, identifies all unique objects, arrays, and strings,
    /// and calculates their size and offset within the future __data__ section.
    /// </summary>
    private void LayoutGraph(IHavokObject root)
    {
        var queue = new Queue<IHavokObject>();
        queue.Enqueue(root);
        _objectAddresses.Add(root, 16); // Root object is always at offset 16 (after TBDY header)
        _objectsToWrite.Add(root);

        _currentDataOffset = 16 + ((root.GetType() as DynamicHavokType)!.Size);

        while (queue.Count > 0)
        {
            IHavokObject current = queue.Dequeue();
            if (current is not DynamicHavokObject dho) continue;

            foreach (DynamicHavokField field in dho.Type.GetAllFields())
            {
                if (!dho.Fields.TryGetValue(field.Name, out object? fieldValue) || fieldValue is null) continue;

                if (fieldValue is IHavokObject referencedObject)
                {
                    if (!_objectAddresses.ContainsKey(referencedObject))
                    {
                        _currentDataOffset = Align(_currentDataOffset, 16);
                        _objectAddresses.Add(referencedObject, _currentDataOffset);
                        _objectsToWrite.Add(referencedObject);
                        queue.Enqueue(referencedObject);
                        _currentDataOffset += (referencedObject.GetType() as DynamicHavokType)!.Size + 8;
                    }
                }
                else if (fieldValue is object[] array)
                {
                    if (!_arrayAddresses.ContainsKey(array))
                    {
                        LayoutArray(array, field, queue);
                    }
                }
                else if (field.Type.Name == "hkStringPtr" && fieldValue is string str)
                {
                    if (!string.IsNullOrEmpty(str) && !_stringAddresses.ContainsKey(str))
                    {
                        _stringsToWrite.Add(str);
                        _stringAddresses.Add(str, 0); // Placeholder
                    }
                }
            }
        }

        // Layout arrays
        foreach (object array in _arraysToWrite)
        {
            _currentDataOffset = Align(_currentDataOffset, 16);
            _arrayAddresses[array] = _currentDataOffset;
            if (array is object?[] objArray && objArray.Length > 0)
            {
                var firstElement = objArray.FirstOrDefault(x => x != null);
                if (firstElement is null) continue;

                var elementType = (firstElement.GetType().GetProperty("Type")!.GetValue(firstElement) as DynamicHavokType)!;
                _currentDataOffset += (long)objArray.Length * elementType.Size;
            }
        }

        // Layout strings
        foreach (string str in _stringsToWrite)
        {
            _currentDataOffset = Align(_currentDataOffset, 1);
            _stringAddresses[str] = _currentDataOffset;
            _currentDataOffset += Encoding.ASCII.GetByteCount(str) + 1; // +1 for null terminator
        }
    }

    private void LayoutArray(object[] array, DynamicHavokField field, Queue<IHavokObject> queue)
    {
        _arraysToWrite.Add(array);
        _arrayAddresses.Add(array, 0); // Placeholder

        foreach (object? item in array)
        {
            if (item is IHavokObject referencedObject && !_objectAddresses.ContainsKey(referencedObject))
            {
                _currentDataOffset = Align(_currentDataOffset, 16);
                _objectAddresses.Add(referencedObject, _currentDataOffset);
                _objectsToWrite.Add(referencedObject);
                queue.Enqueue(referencedObject);
                _currentDataOffset += (referencedObject.GetType() as DynamicHavokType)!.Size + 8;
            }
        }
    }

    private void WriteData(BinaryWriterEx writer)
    {
        writer.WriteASCII("TBDY");
        writer.Write(0); // Placeholder for size
        writer.Write(0L); // Padding

        foreach (IHavokObject obj in _objectsToWrite)
        {
            writer.Position = _objectAddresses[obj] - 8;
            WriteObject(writer, obj);
        }

        foreach (object array in _arraysToWrite)
        {
            writer.Position = _arrayAddresses[array];
            if (array is not object?[] objArray || objArray.Length == 0) continue;

            var firstElement = objArray.FirstOrDefault(x => x != null);
            if (firstElement is null) continue;

            var elementType = (firstElement.GetType().GetProperty("Type")!.GetValue(firstElement) as DynamicHavokType)!;
            foreach (var item in objArray)
            {
                WriteSingleValue(writer, item, elementType);
            }
        }

        foreach (string str in _stringsToWrite)
        {
            writer.Position = _stringAddresses[str];
            writer.WriteASCII(str, true);
        }

        long finalPosition = writer.Position;
        writer.Position = 4;
        writer.Write((int)(finalPosition - 16));
        writer.Position = finalPosition;
    }

    private void WriteObject(BinaryWriterEx writer, IHavokObject obj)
    {
        if (obj is not DynamicHavokObject dho) throw new NotSupportedException("Serialization of non-dynamic objects is not supported.");

        ulong hash = _registry.GetTypeHash(dho.Type);
        if (hash == 0) throw new InvalidDataException($"Could not find hash for type '{dho.Type.Name}'.");
        writer.Write(hash);

        WriteObjectFields(writer, dho);
    }

    private void WriteObjectFields(BinaryWriterEx writer, DynamicHavokObject dho)
    {
        long objectStart = writer.Position;
        foreach (DynamicHavokField field in dho.Type.GetAllFields())
        {
            writer.Position = objectStart + field.Offset;
            dho.Fields.TryGetValue(field.Name, out object? fieldValue);
            WriteFieldValue(writer, fieldValue, field);
        }
    }

    private void WriteFieldValue(BinaryWriterEx writer, object? value, DynamicHavokField field)
    {
        var nParam = field.Type.TemplateParameters.FirstOrDefault(p => p.Name == "N" && p.Kind == "Value");
        if (nParam != null && int.TryParse(nParam.Value, out int arraySize) && value is object[] cStyleArray)
        {
            var tParam = field.Type.TemplateParameters.FirstOrDefault(p => p.Name == "T" && p.Kind == "Type");
            var elementType = tParam?.Type ?? _registry.GetType(field.Type.Name.Split('[')[0])!;

            for (int i = 0; i < arraySize; i++)
            {
                WriteSingleValue(writer, cStyleArray.ElementAtOrDefault(i), elementType);
            }
            return;
        }

        WriteSingleValue(writer, value, field.Type);
    }

    private void WriteSingleValue(BinaryWriterEx writer, object? value, DynamicHavokType type)
    {
        if (value is null)
        {
            for (int i = 0; i < type.Size; i++) writer.Write((byte)0);
            return;
        }

        switch (type.Kind)
        {
            case HavokType.TypeKind.Bool: writer.Write((bool)value); break;
            case HavokType.TypeKind.Char or HavokType.TypeKind.Int8: writer.Write(Convert.ToSByte(value)); break;
            case HavokType.TypeKind.UInt8: writer.Write(Convert.ToByte(value)); break;
            case HavokType.TypeKind.Int16: writer.Write(Convert.ToInt16(value)); break;
            case HavokType.TypeKind.UInt16: writer.Write(Convert.ToUInt16(value)); break;
            case HavokType.TypeKind.Int32: writer.Write(Convert.ToInt32(value)); break;
            case HavokType.TypeKind.UInt32: writer.Write(Convert.ToUInt32(value)); break;
            case HavokType.TypeKind.Int64: writer.Write(Convert.ToInt64(value)); break;
            case HavokType.TypeKind.UInt64: writer.Write(Convert.ToUInt64(value)); break;
            case HavokType.TypeKind.Half: writer.Write(BitConverter.HalfToUInt16Bits((Half)Convert.ToSingle(value))); break;
            case HavokType.TypeKind.Real or HavokType.TypeKind.Float: writer.Write(Convert.ToSingle(value)); break;
            case HavokType.TypeKind.Double: writer.Write(Convert.ToDouble(value)); break;
            case HavokType.TypeKind.Vector4: writer.WriteVector4((Vector4)value); break;
            case HavokType.TypeKind.Pointer: writer.Write(_objectAddresses[(IHavokObject)value]); break;
            case HavokType.TypeKind.CString or HavokType.TypeKind.String: writer.Write(_stringAddresses[(string)value]); break;
            case HavokType.TypeKind.Record: if (value is DynamicHavokObject dho) WriteObjectFields(writer, dho); break;
            case HavokType.TypeKind.Array:
                if (value is DynamicHavokObject arrayObj)
                {
                    arrayObj.Fields.TryGetValue("m_data", out object? data);
                    arrayObj.Fields.TryGetValue("m_size", out object? size);
                    writer.Write(data is object[] arrayData ? _arrayAddresses[arrayData] : 0L);
                    writer.Write(Convert.ToInt32(size));
                }
                break;
            case HavokType.TypeKind.Enum or HavokType.TypeKind.Flags:
                writer.Write(Convert.ToUInt32(value));
                break;
            default:
                Debug.WriteLine($"Warning: Writing for TypeKind '{type.Kind}' ('{type.Name}') is not implemented. Writing zeros.");
                for (int i = 0; i < type.Size; i++) writer.Write((byte)0);
                break;
        }
    }

    private void WriteTag0Header(Dictionary<string, MemoryStream> sections)
    {
        _writer.WriteASCII("TAG0");
        _writer.Write(0x01000000);
        _writer.WriteASCII("SDKV20190100");
        _writer.Write(0);

        long headerSize = 32 + (long)sections.Count * 32;
        long currentOffset = Align(headerSize, 16);
        long totalDataSize = 0;

        var sectionInfos = new List<(string Name, long Offset, int Size)>();

        // Layout sections
        foreach (var (name, stream) in sections)
        {
            sectionInfos.Add((name, currentOffset, (int)stream.Length));
            currentOffset += stream.Length;
            currentOffset = Align(currentOffset, 16);
            totalDataSize += stream.Length;
        }

        long fileSize = headerSize + totalDataSize + (sections.Count * 16); // Rough alignment padding
        _writer.Write((uint)fileSize);
        _writer.Write(sections.Count); // numSections
        _writer.Write(0L);

        foreach (var (name, offset, size) in sectionInfos)
        {
            _writer.WriteASCII(name.PadRight(16, '\0'));
            _writer.Write(offset);
            _writer.Write(size);
            _writer.Write(0);
        }
    }

    private DynamicTypeRegistry? CreateLocalRegistry(IHavokObject root)
    {
        var localTypes = new HashSet<DynamicHavokType>();
        var queue = new Queue<IHavokObject>();
        queue.Enqueue(root);

        var visited = new HashSet<IHavokObject>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is null || !visited.Add(current)) continue;

            if (current.GetType() is DynamicHavokType dht && _registry.GetType(dht.Name) is null)
            {
                localTypes.Add(dht);
            }
            // Continue traversal...
        }

        return localTypes.Count > 0 ? new DynamicTypeRegistry(localTypes) : null;
    }

    private static long Align(long value, int alignment)
    {
        long remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }
}