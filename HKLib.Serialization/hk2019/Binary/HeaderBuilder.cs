using System.Text;

namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Responsible for building and writing the hk2019 file header.
/// </summary>
internal class HeaderBuilder
{
    private readonly BinaryWriter _writer;

    public HeaderBuilder(BinaryWriter writer)
    {
        _writer = writer;
    }

    /// <summary>
    /// Writes the initial TAG0 header with placeholder values.
    /// </summary>
    public void WriteInitialHeader()
    {
        // Magic bytes
        _writer.Write(Encoding.ASCII.GetBytes("TAG0"));

        // File size placeholder (will be updated at the end)
        _writer.Write(0);

        // Unknown value (often 0x01000000 or similar)
        _writer.Write(0x01000000);

        // Unknown value (often 0)
        _writer.Write(0);

        // SDK Version
        byte[] sdkVersion = Encoding.ASCII.GetBytes("SDKV20190100");
        _writer.Write(sdkVersion);

        // Endianness (0 for little-endian) and Pointer Size (8 for 64-bit)
        _writer.Write((byte)0); // Little Endian
        _writer.Write((byte)8); // 64-bit pointers

        // Padding to 0x20
        _writer.Write(new byte[14]);

        // Pad to the end of the header section (typically 0x80 bytes total)
        long headerSize = 0x80;
        long paddingNeeded = headerSize - _writer.BaseStream.Position;
        if (paddingNeeded > 0) _writer.Write(new byte[paddingNeeded]);
    }

    /// <summary>
    /// Updates the header with final values after serialization is complete.
    /// </summary>
    public void UpdateHeader(LayoutInfo layoutInfo)
    {
        long originalPosition = _writer.BaseStream.Position;

        // Update File Size
        _writer.BaseStream.Position = 0x4;
        _writer.Write((int)layoutInfo.FileSize);

        // TODO: Update other header fields like section offsets if they are stored here.

        _writer.BaseStream.Position = originalPosition;
    }
}