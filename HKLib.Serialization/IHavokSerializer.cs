namespace HKLib.Serialization;

/// <summary>
/// Defines a common interface for Havok serializers of different versions.
/// </summary>
public interface IHavokSerializer
{
    object Read(Stream stream);
    void Write(Stream stream, object rootObject);
}