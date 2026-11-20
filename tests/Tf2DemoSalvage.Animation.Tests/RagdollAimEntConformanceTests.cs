using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Where a corpse's worn item stands when none of its bones merges — the ragdoll's abs origin (B69).
/// </summary>
/// <remarks>
/// A corpse's item is a <c>C_EconWearableGib</c> attached with <c>FollowEntity</c> alone (<c>c_tf_player.cpp:10216-10240</c>),
/// so no fast-cull, and with nothing merged <c>CBoneMergeCache::GetAimEntOrigin</c> returns false (<c>bone_merge_cache.cpp:232</c>)
/// and the base answer is the parent's abs origin and angles (<c>c_baseentity.cpp:3156-3163</c>). A ragdoll's are set by
/// <c>C_BaseAnimating::RagdollMoved</c> (<c>c_baseanimating.cpp:4606-4613</c>): <c>SetAbsOrigin( m_pRagdoll-&gt;GetRagdollOrigin() )</c>,
/// the first element's position (<c>ragdoll.cpp:177-181</c>), and <c>SetAbsAngles( vec3_angle )</c>.
/// Subject: the corpse died at (100, 200, 300) facing yaw 90; its first element now lies at (10, 20, 30). The item's only bone,
/// <c>mvm</c>, rests one unit along X, so it stands at (11, 20, 30) — not at the death spot, and not turned by the yaw.
/// </remarks>
public sealed class RagdollAimEntConformanceTests
{
    private const int Vertices = 0x100;

    [Test]
    public void SetupBones_ACorpseItemMatchingNoBone_StandsAtTheRagdollsFirstElementUnturned()
    {
        float[] diedAt = [0f, -1f, 0f, 100f, 1f, 0f, 0f, 200f, 0f, 0f, 1f, 300f];
        BoneFrameCounter clock = new();

        AnimatingEntity corpse = new(new SkeletonPose([Bone("bip_pelvis", 0f)], (_, _, _, _) => []) { EntityTransform = diedAt }, clock)
        {
            Ragdoll = (_, _) => { },
            RagdollOrigin = new Vector3(10f, 20f, 30f),
        };

        AnimatingEntity item = new(new SkeletonPose([Bone("mvm", 1f)], (_, _, _, _) => []) { EntityTransform = diedAt }, clock)
        {
            Follows = corpse,
            MergeWithoutFastCull = true,
        };

        item.SetupBones(Vertices, 0d).ShouldBeTrue();

        float[] bone = item.Bones.Bone(0).ToArray();
        (bone[3], bone[7], bone[11]).ShouldBe((11f, 20f, 30f));
        (bone[0], bone[5], bone[10]).ShouldBe((1f, 1f, 1f), "zero angles: the death yaw does not turn it");
    }

    /// <summary>The control: a parent that is not a ragdoll leaves the item at the transform placement gave it.</summary>
    [Test]
    public void SetupBones_AnItemMatchingNoBoneOnALivingParent_KeepsItsPlacement()
    {
        float[] standsAt = [1f, 0f, 0f, 100f, 0f, 1f, 0f, 200f, 0f, 0f, 1f, 300f];
        BoneFrameCounter clock = new();

        AnimatingEntity wearer = new(new SkeletonPose([Bone("bip_pelvis", 0f)], (_, _, _, _) => []) { EntityTransform = standsAt }, clock)
        {
            RagdollOrigin = new Vector3(10f, 20f, 30f),
        };

        AnimatingEntity item = new(new SkeletonPose([Bone("mvm", 1f)], (_, _, _, _) => []) { EntityTransform = standsAt }, clock)
        {
            Follows = wearer,
            MergeWithoutFastCull = true,
        };

        item.SetupBones(Vertices, 0d).ShouldBeTrue();

        float[] bone = item.Bones.Bone(0).ToArray();
        (bone[3], bone[7], bone[11]).ShouldBe((101f, 200f, 300f));
    }

    private static StudioBone Bone(string name, float x) =>
        new(
            Name: name,
            Parent: -1,
            Position: (x, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
            Flags: ~0);
}
