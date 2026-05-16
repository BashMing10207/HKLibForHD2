using System.Numerics;
using System.Text.Json.Serialization;

namespace HKLib.Modding;

/// <summary>
/// Represents the structure of a single bone as defined in the JSON files from Blender.
/// </summary>
public class BoneData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("parent")]
    public int ParentIndex { get; set; }

    [JsonPropertyName("transform")]
    public TransformData Transform { get; set; } = new();

    /// <summary>
    /// Converts the transform data into a standard hkQsTransform.
    /// </summary>
    public hk2018.hkQsTransform ToHkQsTransform()
    {
        return new hk2018.hkQsTransform
        {
            m_translation = new Vector4(Transform.Translation[0], Transform.Translation[1], Transform.Translation[2], 0),
            m_rotation = new Quaternion(Transform.Rotation[0], Transform.Rotation[1], Transform.Rotation[2], Transform.Rotation[3]),
            m_scale = new Vector4(Transform.Scale[0], Transform.Scale[1], Transform.Scale[2], 0)
        };
    }
}

/// <summary>
/// Represents the transform of a bone.
/// </summary>
public class TransformData
{
    [JsonPropertyName("translation")]
    public float[] Translation { get; set; } = { 0, 0, 0 };

    [JsonPropertyName("rotation")]
    public float[] Rotation { get; set; } = { 0, 0, 0, 1 }; // Identity quaternion

    [JsonPropertyName("scale")]
    public float[] Scale { get; set; } = { 1, 1, 1 };
}