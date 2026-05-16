using System.Collections.Generic;
using System.Numerics;
using HKLib.hk2018;

namespace HKLib.Modding;

/// <summary>
/// Provides methods to patch animation files to synchronize them with a modified skeleton.
/// This corresponds to the "Automatic correction of animation tracks" step of Phase 3.
/// </summary>
public static class AnimationPatcher
{
    /// <summary>
    /// Patches an animation by updating its transform track count and adding identity transforms for new bones.
    /// </summary>
    /// <param name="animation">The hkaAnimation object to patch.</param>
    /// <param name="newBoneCount">The total number of bones in the master skeleton ($N$).</param>
    public static void PatchAnimation(hkaAnimation animation, int newBoneCount)
    {
        if (animation.m_numberOfTransformTracks >= newBoneCount)
        {
            // No patching needed if the track count is already sufficient.
            return;
        }

        // 1. Update the track count to match the new total bone count.
        animation.m_numberOfTransformTracks = newBoneCount;

        // 2. Expand the transform tracks array with identity transforms for the new bones.
        var transformTracks = new List<hkQsTransform>(animation.m_transformTracks);
        int tracksToAdd = newBoneCount - transformTracks.Count;

        if (tracksToAdd > 0)
        {
            var identityTransform = new hkQsTransform
            {
                m_translation = new Vector4(0, 0, 0, 0),
                m_rotation = Quaternion.Identity, // (0, 0, 0, 1)
                m_scale = new Vector4(1, 1, 1, 0) // w-component is typically unused for scale
            };

            for (int i = 0; i < tracksToAdd; i++)
            {
                // Add a new instance of the identity transform for each new bone track.
                transformTracks.Add((hkQsTransform)identityTransform.DeepClone());
            }

            animation.m_transformTracks = new hkArray<hkQsTransform>(transformTracks);
        }
    }
}