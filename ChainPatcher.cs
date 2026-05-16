using System.Collections.Generic;
using HKLib.hk2018;

namespace HKLib.Modding;

/// <summary>
/// Provides methods to patch various Havok file types (Physics, Ragdoll) to synchronize them with a master skeleton.
/// This corresponds to the "Chained Synchronization Patching" step of Phase 2.
/// </summary>
public static class ChainPatcher
{
    /// <summary>
    /// Patches the physics system data by expanding its bone-to-body mapping array to the new total bone count.
    /// </summary>
    /// <param name="physicsData">The hknpPhysicsSystemData object to patch.</param>
    /// <param name="newBoneCount">The total number of bones in the master skeleton ($N$).</param>
    public static void PatchPhysics(hknpPhysicsSystemData physicsData, int newBoneCount)
    {
        // The boneIdToBodyIdMap array maps each bone index to a physics body index.
        var originalMap = new List<int>(physicsData.m_boneIdToBodyIdMap);
        int originalCount = originalMap.Count;

        if (newBoneCount > originalCount)
        {
            int newBonesToAdd = newBoneCount - originalCount;
            for (int i = 0; i < newBonesToAdd; i++)
            {
                // Add new entries for the added bones. A value of -1 indicates no associated physics body (No Collision).
                originalMap.Add(-1);
            }

            // Replace the old array with the new, expanded one.
            physicsData.m_boneIdToBodyIdMap = new hkArray<int>(originalMap);
        }
    }

    /// <summary>
    /// Patches the skeleton mapper in a ragdoll file by updating its skeleton references and expanding its bone map.
    /// </summary>
    /// <param name="skeletonMapper">The hknpSkeletonMapper object to patch.</param>
    /// <param name="masterSkeleton">The master skeleton (Source of Truth) containing all bones.</param>
    public static void PatchRagdoll(hknpSkeletonMapper skeletonMapper, hkaSkeleton masterSkeleton)
    {
        var mapping = skeletonMapper.m_mapping;
        int newBoneCount = masterSkeleton.m_bones.Count;

        // The mapper should now reference the new master skeleton to maintain data consistency.
        if (mapping.m_skeletonA?.m_bones.Count < newBoneCount)
        {
            mapping.m_skeletonA = masterSkeleton;
        }

        // Expand the bone map array to the new total bone count.
        var boneMapList = new List<short>(mapping.m_boneMap);
        int originalCount = boneMapList.Count;

        if (newBoneCount > originalCount)
        {
            int bonesToAdd = newBoneCount - originalCount;
            for (int i = 0; i < bonesToAdd; i++)
            {
                // Per the TODO, add new entries to the tail. A value of -1 indicates no mapping for the new bone.
                boneMapList.Add(-1);
            }
            mapping.m_boneMap = new hkArray<short>(boneMapList);
        }
    }
}