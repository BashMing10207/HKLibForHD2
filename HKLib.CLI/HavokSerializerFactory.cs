using System.Text;
using HKLib.Serialization;
using HavokBinarySerializer2018 = HKLib.Serialization.hk2018.Binary.HavokBinarySerializer;
using HavokBinarySerializer2019 = HKLib.Serialization.hk2019.Binary.HavokBinarySerializer;

namespace HKLib.CLI;

/// <summary>
/// A factory for creating the appropriate Havok serializer based on the file version.
/// </summary>
public static class HavokSerializerFactory
{
    public static IHavokSerializer? CreateSerializer(string filePath)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
        using BinaryReader reader = new(fs, Encoding.ASCII);

        if (fs.Length < 0x1C) return null;

        // The SDK Version is at a fixed offset in the TAG0 header.
        reader.BaseStream.Position = 0x10;
        string sdkVersion = Encoding.ASCII.GetString(reader.ReadBytes(12));

        if (sdkVersion == "SDKV20190100")
        {
            Console.WriteLine("Detected Havok 2019 format. Using hk2019 serializer.");
            return new HavokBinarySerializer2019();
        }

        // Fallback to the old hk2018 serializer for other versions.
        Console.WriteLine($"Detected Havok 2018 or older format ({sdkVersion}). Using hk2018 serializer as a fallback.");
        return new HavokBinarySerializer2018();
    }
}