using System;
using System.IO;
using HKLib.hk2018;
using HKLib.Reflection;

namespace HKLib.Serialization;

/// <summary>
/// Implements the 2-Pass Serializer to rebuild a Havok binary file from an in-memory object graph.
/// </summary>
public class HavokPacker
{
    public byte[] Pack(IHavokObject root)
    {
        var packerData = new PackerData();

        // Pass 1: Layout and Address Calculation
        LayoutPass(root, packerData);

        // Pass 2: Data Writing and Patch Collection
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);

        WritePass(root, writer, packerData);

        // TODO: After the data is written, the final file needs to be assembled.
        // This involves writing the Havok header, the classnames/types sections,
        // the __data__ section (which is what we just wrote), and then the
        // newly generated PTCH and TPAD sections based on packerData.

        return memoryStream.ToArray();
    }

    private void LayoutPass(IHavokObject root, PackerData data)
    {
        long currentOffset = 0; // Start of the __data__ section

        HavokObjectWalker.Walk(root, obj =>
        {
            // 1. Calculate and apply 16-byte alignment padding
            long alignmentPadding = (16 - (currentOffset % 16)) % 16;
            if (alignmentPadding > 0)
            {
                data.PaddingInfo.Add(new PaddingPatch(currentOffset, (byte)alignmentPadding));
                currentOffset += alignmentPadding;
            }

            // 2. Store the aligned address of the object
            data.AddressMap[obj] = currentOffset;

            // 3. Add the object's own size to the offset
            HavokType type = HavokTypeRegistry.GetType(obj.GetType())!;
            currentOffset += type.Size;
        });
    }

    private void WritePass(IHavokObject root, BinaryWriter writer, PackerData data)
    {
        HavokObjectWalker.Walk(root, obj =>
        {
            long expectedOffset = data.AddressMap[obj];

            // Write padding bytes to ensure alignment
            long alignmentPadding = expectedOffset - writer.BaseStream.Position;
            if (alignmentPadding > 0)
            {
                writer.Write(new byte[alignmentPadding]);
            }

            // Sanity check
            if (writer.BaseStream.Position != expectedOffset)
            {
                throw new InvalidOperationException("Writer position does not match expected layout offset.");
            }

            // Write the object's fields using the reflection system
            HavokObjectWriter.Write(writer, obj, data);
        });
    }
}