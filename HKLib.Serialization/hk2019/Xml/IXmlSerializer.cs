namespace HKLib.Serialization.hk2019.Xml;

public interface IXmlSerializer
{
    object Read(string path);
    void Write(object root, string path, byte[]? prependData = null);
}