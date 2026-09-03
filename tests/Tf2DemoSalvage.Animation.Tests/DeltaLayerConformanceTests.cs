using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A delta layer ADDS to the base pose; it does not blend toward it.
/// </summary>
/// <remarks>
/// **<c>SlerpBones</c> splits on <c>STUDIO_DELTA</c> before anything else**
/// (<c>bone_setup.cpp:1434</c>):
///
/// <code>
///   if ( seqdesc.flags &amp; STUDIO_DELTA )
///   {
///       if ( seqdesc.flags &amp; STUDIO_POST ) QuaternionMA( q1[i], s2, q2[i], q1[i] );
///       else                                QuaternionSM( s2, q2[i], q1[i], q1[i] );
///       pos1[i] = pos1[i] + pos2[i] * s2;
///       ...
///       return;
///   }
/// </code>
///
/// **Every TF2 player gesture takes this branch**, measured on `scout.mdl`:
/// `PRIMARY_reload_start` and `jumpland_primary` both carry the delta bit on the sequence and on
/// the animation behind it, and both carry <c>STUDIO_POST</c>.
///
/// **What it cost to learn (B284).** Composing them as absolute poses replaced the skeleton with a
/// difference and laid a reloading player flat on the ground; the owner found it within an hour of
/// the feature shipping, on the one player in the match who happened to be mid-gesture.
/// </remarks>
public sealed class DeltaLayerConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    [Test]
    public void Build_WithADeltaLayer_AddsToTheBaseRatherThanReplacingIt()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 5f, delta: true);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(
            15f,
            Tolerance,
            "pos1[i] = pos1[i] + pos2[i] * s2 — a delta is a difference, so it adds to whatever " +
            "the base sequence put there");
    }

    /// <remarks>
    /// **The control, and the whole point of the flag.** The same two numbers composed as absolute
    /// poses land on the layer's own value, not on the sum. Without this the test above would pass
    /// against code that always added, which would be as wrong for an ordinary sequence as
    /// replacing is for a delta.
    /// </remarks>
    [Test]
    public void Build_WithAnAbsoluteLayer_ReplacesTheBase()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 5f, delta: false);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(
            5f, Tolerance, "an ordinary layer at full weight slerps the bone onto its own pose");
    }

    /// <remarks>
    /// **The weight scales the difference, not the destination**: <c>pos2[i] * s2</c>. Half a delta
    /// is half the offset added, where half an absolute layer is half the distance travelled — the
    /// two agree only when the base is zero, which is why the base here is not.
    /// </remarks>
    [Test]
    public void Build_WithAHalfWeightedDeltaLayer_AddsHalfTheDifference()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 5f, delta: true, boneWeight: 0.5f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(12.5f, Tolerance, "10 + 5 x 0.5");
    }

    /// <remarks>
    /// **A bone the delta does not weight is untouched, not zeroed.** <c>s2 &lt;= 0</c> is skipped
    /// before the branch, so the base survives — and for a gesture that is most of the skeleton.
    /// </remarks>
    [Test]
    public void Build_WithADeltaLayerWeightedOffABone_LeavesTheBaseThere()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 5f, delta: true);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(
            10f, Tolerance, "bone 1 carries no weight in the fixture, so it keeps the base");
    }

    /// <summary>A pose whose base sits at one X and whose single layer contributes another.</summary>
    private static SkeletonPose Posed(
        float baseAt, float layerAt, bool delta, float boneWeight = 1f) =>
        new(Bones(), Sampler(baseAt, layerAt))
        {
            Sequence = 0,
            Layers =
            [
                new PoseLayer(1, 0, 0f, 1f, [boneWeight, 0f], Delta: delta, Post: true),
            ],
        };

    /// <summary>A sampler answering the base for sequence 0 and the layer for anything else.</summary>
    private static Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>>
        Sampler(float baseAt, float layerAt) =>
        (sequence, _, _, _) =>
        {
            float x = sequence == 0 ? baseAt : layerAt;

            return
            [
                new StudioBonePose(0, (x, 0f, 0f), (0f, 0f, 0f, 1f)),
                new StudioBonePose(1, (x, 0f, 0f), (0f, 0f, 0f, 1f)),
            ];
        };

    /// <summary>Two parentless bones, so a built matrix carries one bone's answer alone.</summary>
    private static IReadOnlyList<StudioBone> Bones() =>
    [
        new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone("other", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
    ];

    /// <summary>The X translation of a built bone matrix.</summary>
    private static float XOf(BoneAccessor bones, int bone) => bones.BoneForWrite(bone)[3];
}
