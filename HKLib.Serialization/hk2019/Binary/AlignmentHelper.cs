namespace HKLib.Serialization.hk2019.Binary;

/// <summary>
/// Provides helper methods for data alignment, a critical part of Havok serialization.
/// </summary>
internal static class AlignmentHelper
{
    /// <summary>
    /// Calculates the number of padding bytes needed to align the current position.
    /// </summary>
    /// <param name="currentPosition">The current position in the stream.</param>
    /// <param name="alignment">The required alignment, typically 16 for Havok.</param>
    /// <returns>The number of bytes to add as padding.</returns>
    public static int GetPadding(long currentPosition, int alignment)
    {
        long remainder = currentPosition % alignment;
        return remainder == 0 ? 0 : (int)(alignment - remainder);
    }

    /// <summary>
    /// Aligns a position value to the specified boundary.
    /// </summary>
    public static void Align(ref long position, int alignment)
    {
        int padding = GetPadding(position, alignment);
        position += padding;
    }

    /// <summary>
    /// Writes padding bytes to the writer to align the stream to the specified boundary.
    /// </summary>
    public static void WritePadding(BinaryWriter writer, int alignment)
    {
        int padding = GetPadding(writer.BaseStream.Position, alignment);
        if (padding > 0)
        {
            // In Havok, padding is usually 0s, but can sometimes be other values for debugging.
            // Using a zero-byte array is the standard approach.
            writer.Write(new byte[padding]);
        }
    }
}