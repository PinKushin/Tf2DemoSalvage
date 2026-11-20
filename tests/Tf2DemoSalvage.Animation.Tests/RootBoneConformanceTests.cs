using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <c>C_BaseAnimating::GetRootBone</c> — where a <c>PATTACH_ROOTBONE_FOLLOW</c> effect stands (B69,
/// <c>particle_property.cpp:642-656</c>).
/// </summary>
/// <remarks>
/// <c>c_baseanimating.cpp:2250-2259</c>: a bone-merged entity with a move parent and a merge cache answers
/// <c>CBoneMergeCache::GetRootBone</c> — the FOLLOW's bone that its first merged bone pairs with, or false when nothing merged
/// (<c>bone_merge_cache.cpp:261-273</c>); anything else answers its own bone 0, <c>GetBoneTransform( 0, … )</c>.
/// Subject: a wearer standing at (100, 200, 300) with <c>bip_pelvis</c> at its origin and <c>bip_head</c> five units along X;
/// a hat whose own bones are <c>mvm</c> then <c>bip_head</c>. The hat's first merged bone is its <c>bip_head</c>, which pairs
/// with the wearer's bone 1, so the root is (105, 200, 300) — not the hat's own bone 0 and not the wearer's.
/// </remarks>
public sealed class RootBoneConformanceTests
{
    private const int Anything = StudioBoneFlags.UsedByAnything;

    private static readonly float[] StandsAt = [1f, 0f, 0f, 100f, 0f, 1f, 0f, 200f, 0f, 0f, 1f, 300f];

    [Test]
    public void RootBone_AMergedItem_IsTheFollowsBoneItsFirstMergedBonePairsWith()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity wearer = Entity(clock, Bone("bip_pelvis", 0f), Bone("bip_head", 5f));
        AnimatingEntity hat = Entity(clock, Bone("mvm", 1f), Bone("bip_head", 2f));
        hat.Follows = wearer;

        float[] root = new float[12];

        hat.RootBone(0d, root).ShouldBeTrue();
        (root[3], root[7], root[11]).ShouldBe((105f, 200f, 300f));
    }

    /// <summary>Nothing merged: <c>CBoneMergeCache::GetRootBone</c> returns false (<c>:266-267</c>).</summary>
    [Test]
    public void RootBone_AMergedItemMatchingNoBone_IsNone()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity wearer = Entity(clock, Bone("bip_pelvis", 0f));
        AnimatingEntity hat = Entity(clock, Bone("mvm", 1f));
        hat.Follows = wearer;

        hat.RootBone(0d, new float[12]).ShouldBeFalse();
    }

    /// <summary>The control: an entity that follows nothing answers its own bone 0.</summary>
    [Test]
    public void RootBone_AnEntityFollowingNothing_IsItsOwnBoneZero()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity hat = Entity(clock, Bone("mvm", 1f), Bone("bip_head", 2f));

        float[] root = new float[12];

        hat.RootBone(0d, root).ShouldBeTrue();
        (root[3], root[7], root[11]).ShouldBe((101f, 200f, 300f));
    }

    private static AnimatingEntity Entity(BoneFrameCounter clock, params StudioBone[] bones) =>
        new(new SkeletonPose(bones, (_, _, _, _) => []) { EntityTransform = StandsAt }, clock);

    private static StudioBone Bone(string name, float x) =>
        new(
            Name: name,
            Parent: -1,
            Position: (x, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
            Flags: Anything);
}
