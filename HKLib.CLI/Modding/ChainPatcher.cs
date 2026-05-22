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
        // TODO: Implement actual physics patching logic based on correct hknpPhysicsSystemData fields.
    }

    /// <summary>
    /// Patches the skeleton mapper in a ragdoll file by updating its skeleton references and expanding its bone map.
    /// </summary>
    /// <param name="skeletonMapper">The hkaSkeletonMapper object to patch.</param>
    /// <param name="masterSkeleton">The master skeleton (Source of Truth) containing all bones.</param>
    public static void PatchRagdoll(hkaSkeletonMapper skeletonMapper, hkaSkeleton masterSkeleton)
    {
        var mapping = skeletonMapper.m_mapping;
        int newBoneCount = masterSkeleton.m_bones.Count;

        // The mapper should now reference the new master skeleton to maintain data consistency.
        if (mapping.m_skeletonA?.m_bones.Count < newBoneCount)
        {
            mapping.m_skeletonA = masterSkeleton;
        }

        // TODO: Implement actual mapping expansion based on hkaSkeletonMapperData fields.
    }
}
