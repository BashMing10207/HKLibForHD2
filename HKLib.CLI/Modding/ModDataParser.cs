using System.Text.Json;

namespace HKLib.Modding;

/// <summary>
/// A utility class to parse bone data from JSON and identify additions.
/// </summary>
public static class ModDataParser
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Compares an original and a modified bone JSON file content to find newly added bones.
    /// </summary>
    /// <param name="originalBonesJson">The JSON content of the original bones.json file.</param>
    /// <param name="modifiedBonesJson">The JSON content of the modified_bones.json file from Blender.</param>
    /// <returns>A list of <see cref="BoneData"/> objects representing the bones that were added.</returns>
    public static List<BoneData> FindAddedBones(string originalBonesJson, string modifiedBonesJson)
    {
        var originalBones = JsonSerializer.Deserialize<List<BoneData>>(originalBonesJson, s_jsonOptions)
                            ?? new List<BoneData>();
        var modifiedBones = JsonSerializer.Deserialize<List<BoneData>>(modifiedBonesJson, s_jsonOptions)
                            ?? new List<BoneData>();

        var originalBoneNames = new HashSet<string>(originalBones.Select(b => b.Name));

        return modifiedBones
            .Where(b => !originalBoneNames.Contains(b.Name))
            .ToList();
    }
}