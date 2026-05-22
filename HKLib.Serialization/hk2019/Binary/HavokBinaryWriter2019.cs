using System.Text;
using HKLib.hk2018;
using HKLib.Reflection.Dynamic;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// The main writer for the hk2019 binary format. This will implement the 2-pass serialization logic.
/// </summary>
public class HavokBinaryWriter2019
{
    private readonly BinaryWriter _writer;
    private readonly HeaderBuilder _headerBuilder;
    private readonly LayoutCalculator _layoutCalculator;
    private readonly DynamicTypeRegistry _typeRegistry;

    public HavokBinaryWriter2019(Stream stream, DynamicTypeRegistry typeRegistry)
    {
        _writer = new BinaryWriter(stream, Encoding.ASCII, true);
        _headerBuilder = new HeaderBuilder(_writer);
        _typeRegistry = typeRegistry;
        _layoutCalculator = new LayoutCalculator(typeRegistry);
    }

    public void Write(IHavokObject rootObject)
    {
        // Pass 1: Calculate the complete layout of the file.
        LayoutInfo layoutInfo = _layoutCalculator.Calculate(rootObject);

        // Write a placeholder header.
        _headerBuilder.WriteInitialHeader();

        // Pass 2: Write the actual data based on the calculated layout.
        WriteClassnamesSection(layoutInfo);
        WriteTypesSection(layoutInfo);
        WriteDataSection(layoutInfo);
        WritePatchSection(layoutInfo);

        // Final step: Go back and update the header with the correct offsets and file size.
        _headerBuilder.UpdateHeader(layoutInfo);
    }

    private void WriteClassnamesSection(LayoutInfo layoutInfo)
    {
        _writer.BaseStream.Position = layoutInfo.ClassNamesSectionOffset;
        _writer.Write(Encoding.ASCII.GetBytes("CNM1"));

        // Placeholder for CNM1 header (e.g., section size)
        _writer.Write(0); // Size placeholder
        _writer.Write(0); // Padding
        _writer.Write(0); // Padding

        foreach (string className in layoutInfo.ClassNames)
        {
            _writer.Write(Encoding.ASCII.GetBytes(className));
            _writer.Write((byte)0); // Null terminator
        }

        AlignmentHelper.WritePadding(_writer, 16);
    }

    private void WriteTypesSection(LayoutInfo layoutInfo)
    {
        _writer.BaseStream.Position = layoutInfo.TypesSectionOffset;

        // This is a highly simplified representation. A full implementation would require
        // building complex string and definition tables for TST1 and FST1.

        // Write TST1 (Type Section Table)
        _writer.Write(Encoding.ASCII.GetBytes("TST1"));
        // ... TST1 header and data ...
        AlignmentHelper.WritePadding(_writer, 16);

        // Write FST1 (Field Section Table)
        _writer.Write(Encoding.ASCII.GetBytes("FST1"));
        // ... FST1 header and data ...
        AlignmentHelper.WritePadding(_writer, 16);

        // Write THSH (Type Hash)
        _writer.Write(Encoding.ASCII.GetBytes("THSH"));
        // ... THSH header ...
        foreach (DynamicHavokType type in layoutInfo.Types)
        {
            ulong hash = TypeHasher.CalculateHash(type);
            _writer.Write(hash);
        }

        AlignmentHelper.WritePadding(_writer, 16);
    }

    private void WriteDataSection(LayoutInfo layoutInfo)
    {
        _writer.BaseStream.Position = layoutInfo.DataSectionOffset;
        _writer.Write(Encoding.ASCII.GetBytes("TBDY"));
        // ... TBDY header ...

        // This is the core of Pass 2 data writing.
        // A full implementation would iterate through layoutInfo.ObjectLayouts,
        // seek to the calculated offset, and write the object's fields.
        // This is extremely complex and depends on the full dynamic type information.
        // For now, we acknowledge the structure.
    }

    private void WritePatchSection(LayoutInfo layoutInfo)
    {
        AlignmentHelper.WritePadding(_writer, 16);
        _writer.Write(Encoding.ASCII.GetBytes("PTCH"));
        // ... PTCH header ...

        foreach (PointerPatch patch in layoutInfo.PointerPatches)
        {
            long targetAddress = layoutInfo.ObjectLayouts[patch.TargetObject].Offset;
            _writer.BaseStream.Position = patch.PatchOffset;
            _writer.Write(targetAddress);
        }

        AlignmentHelper.WritePadding(_writer, 16);
    }
}