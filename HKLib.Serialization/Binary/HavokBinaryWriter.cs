using HKLib.Reflection.Dynamic;
using HKLib.hk2018;
using System.Collections;
using System.Text;
using System.Diagnostics;
using IHavokObject = HKLib.hk2018.IHavokObject;

namespace HKLib.Serialization.Binary;

/// <summary>
/// A writer for the Havok 2019 binary format. This class will implement the 2-pass writing algorithm.
/// </summary>
public class HavokBinaryWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryWriter _writer;
    private readonly DynamicTypeRegistry _typeRegistry;

    public HavokBinaryWriter(Stream stream, DynamicTypeRegistry typeRegistry)
    {
        _stream = stream;
        _writer = new BinaryWriter(stream, Encoding.ASCII, true);
        _typeRegistry = typeRegistry;
    }

    /// <summary>
    /// Writes the given root object and its dependencies to the stream using a 2-pass algorithm.
    /// </summary>
    public void Write(IHavokObject rootObject)
    {
        // Phase 2.2: 2-Pass Writing Algorithm
        
        // Data structures for the 2-pass algorithm
        var objectQueue = new Queue<IHavokObject>();
        var objectOffsets = new Dictionary<IHavokObject, long>();
        var patchLocations = new List<(long location, IHavokObject target)>();
        long currentOffset = 0; // This will be the virtual offset within the __data__ section

        // Pass 1: Layout Pre-computation
        // Discover all objects, calculate their sizes and virtual offsets.
        Pass1_CalculateLayout(rootObject, objectQueue, objectOffsets, ref currentOffset);

        // Pass 2: Actual Data Writing
        // Write the objects to the stream and record pointer locations for patching.
        Pass2_WriteData(objectQueue, objectOffsets, patchLocations);
        
        // TODO: Write other sections like __classnames__, __types__, etc.
        
        // TODO: Write the __patch__ section using the information from patchLocations.
    }
    
    /// <summary>
    /// Pass 1: Recursively discovers all objects, calculates their size and layout,
    /// and populates the object queue and offset map.
    /// </summary>
    private void Pass1_CalculateLayout(IHavokObject? obj, Queue<IHavokObject> objectQueue, Dictionary<IHavokObject, long> objectOffsets, ref long currentOffset)
    {
        if (obj is null || objectOffsets.ContainsKey(obj))
        {
            return;
        }

        objectQueue.Enqueue(obj);
        
        // TODO: Get the actual Havok type information for the object
        // DynamicHavokType type = _typeRegistry.GetType(obj.GetType());
        
        // TODO: Implement proper alignment calculation based on type.Alignment
        const int alignment = 16;
        currentOffset = (currentOffset + alignment - 1) & -alignment;
        
        objectOffsets.Add(obj, currentOffset);
        
        // TODO: Calculate the actual size of the object based on its type and fields.
        // currentOffset += type.Size;
        currentOffset += 64; // Placeholder size

        // TODO: Recursively call for all referenced objects (pointers and arrays of pointers)
    }

    /// <summary>
    /// Pass 2: Writes the actual object data to the stream based on the pre-calculated layout.
    /// Records the locations of pointers that need to be patched.
    /// </summary>
    private void Pass2_WriteData(Queue<IHavokObject> objectQueue, 
                                Dictionary<IHavokObject, long> objectOffsets, 
                                List<(long location, IHavokObject target)> patchLocations)
    {
        // TODO: Implement data writing logic.
    }

    /// <summary>
    /// Writes padding bytes to the stream until the stream position is a multiple of the specified alignment.
    /// </summary>
    /// <param name="alignment">The alignment to pad to. Must be a power of 2.</param>
    public void WriteAlignment(int alignment)
    {
        // Phase 2.1 implementation
        long currentPosition = _stream.Position;
        long remainder = currentPosition % alignment;
        if (remainder != 0)
        {
            int paddingRequired = (int)(alignment - remainder);
            for (int i = 0; i < paddingRequired; i++)
            {
                _writer.Write((byte)0x00);
            }
        }
    }

    private static IEnumerable<DynamicHavokType> GetSelfAndSubTypes(DynamicHavokType type)
    {
        var seen = new HashSet<DynamicHavokType>();
        var queue = new Queue<DynamicHavokType>();
        queue.Enqueue(type);
        seen.Add(type);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            if (current.SubType is not null && seen.Add(current.SubType))
            {
                queue.Enqueue(current.SubType);
            }

            foreach (var field in current.Fields)
            {
                if (seen.Add(field.Type))
                {
                    queue.Enqueue(field.Type);
                }
            }
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }
}