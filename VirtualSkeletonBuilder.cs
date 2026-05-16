using System.Collections.Generic;
using System.Linq;
using HKLib.hk2018;

namespace HKLib.Modding;

/// <summary>
/// A utility to build a new, expanded skeleton from an original skeleton and added bone data.
/// </summary>
public static class VirtualSkeletonBuilder
{
    /// <summary>
    /// Creates a new "master" skeleton by adding new bones to an existing skeleton.
    /// This new skeleton becomes the "Source of Truth" for all subsequent patching operations.
    /// </summary>
    /// <param name="originalSkeleton">The original hkaSkeleton object.</param>
    /// <param name="addedBones">A list of new bones to add, identified by the ModDataParser.</param>
    /// <returns>A new hkaSkeleton instance containing both original and new bones.</returns>
    public static hkaSkeleton CreateMasterSkeleton(hkaSkeleton originalSkeleton, List<BoneData> addedBones)
    {
        // 1. Create new lists by copying original data
        var newBones = new List<hkaBone>(originalSkeleton.m_bones);
        var newReferencePose = new List<hkQsTransform>(originalSkeleton.m_referencePose);
        var newParentIndices = new List<short>(originalSkeleton.m_parentIndices);

        // 2. Add new bones to the lists
        foreach (var addedBone in addedBones)
        {
            newBones.Add(new hkaBone { m_name = addedBone.Name });
            newReferencePose.Add(addedBone.ToHkQsTransform());
            newParentIndices.Add((short)addedBone.ParentIndex);
        }

        // 3. Create a new skeleton by cloning the original and then overwriting the arrays.
        var masterSkeleton = (hkaSkeleton)originalSkeleton.DeepClone(); // Assumes DeepClone() exists to preserve all properties.
        masterSkeleton.m_bones = new hkArray<hkaBone>(newBones);
        masterSkeleton.m_referencePose = new hkArray<hkQsTransform>(newReferencePose);
        masterSkeleton.m_parentIndices = new hkArray<short>(newParentIndices);

        return masterSkeleton;
    }
}