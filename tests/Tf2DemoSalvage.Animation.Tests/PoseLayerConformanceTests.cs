using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Layers accumulate over the base pose the way <c>SlerpBones</c> does.
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimatingOverlay::AccumulateLayers</c> walks the layers in order and calls
/// <c>AccumulatePose( pos, q, sequence, cycle, weight, … )</c> for each**
/// (<c>c_baseanimatingoverlay.cpp:294-426</c>), which ends in <c>SlerpBones</c>
/// (<c>bone_setup.cpp:1373</c>). Two things there decide what a layer looks like:
///
/// <code>
///   pS2[i] = s * seqdesc.weight( i );        // per bone, and 0 leaves the bone alone
///   ...
///   s1 = 1.0 - s2;
///   QuaternionSlerp( q2[i], q1[i], s1, q3 );
///   pos1[i] = pos1[i] * s1 + pos2[i] * s2;
/// </code>
///
/// **The per-bone weight is the whole reason a reload does not stop a player running.** A gesture
/// sequence's weight list is 1 on the arms and 0 on the legs, so accumulating it at layer weight 1
/// replaces the arms and leaves the legs on the run. A layering that ignored the list would replace
/// the entire pose and freeze the player mid-stride — which looks exactly like the defect it was
/// built to fix.
///
/// <c>s2 &lt;= 0.0f</c> is skipped outright, and that is not the same as blending by zero: the
/// engine never touches the bone, so nothing rounds and nothing renormalises.
/// </remarks>
public sealed class PoseLayerConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    [Test]
    public void Build_WithALayerWeightedOffABone_LeavesThatBoneOnTheBasePose()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            baseAt: 10f,
            layerAt: 90f,

            // Bone 0 fully layered, bone 1 not at all — the run/reload split in miniature.
            boneWeights: [1f, 0f]);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 1).ShouldBe(
            10f,
            Tolerance,
            "seqdesc.weight(i) of zero means the engine skips the bone entirely, which is how a " +
            "reload plays on the arms while the legs keep running");
    }

    /// <remarks>
    /// **The control for the test above**, and without it a layering that did nothing at all would
    /// pass. Bone 0 carries a weight of one, so the layer must win there completely:
    /// <c>s1 = 1 - s2</c> is zero, and <c>pos1 = pos1 * 0 + pos2 * 1</c>.
    /// </remarks>
    [Test]
    public void Build_WithALayerFullyWeightedOnABone_ReplacesThatBone()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 90f, boneWeights: [1f, 0f]);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(
            90f, Tolerance, "a bone at full layer weight takes the layer's position outright");
    }

    /// <remarks>
    /// **Half a layer is half the distance**, which is the position half of <c>SlerpBones</c>:
    /// <c>pos1[i] = pos1[i] * s1 + pos2[i] * s2</c>. An implementation that treated any non-zero
    /// weight as "replace" would pass both tests above and fail this one, and a gesture fading out
    /// would pop rather than blend.
    /// </remarks>
    [Test]
    public void Build_WithAHalfWeightedLayer_BlendsHalfWay()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(baseAt: 10f, layerAt: 90f, boneWeights: [0.5f, 0f]);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(50f, Tolerance, "pos1 * (1 - s2) + pos2 * s2 at s2 = 0.5");
    }

    /// <remarks>
    /// **The layer's own weight multiplies the per-bone one** — <c>pS2[i] = s * seqdesc.weight(i)</c>
    /// — so a layer at weight 0.5 over a bone weighted 0.5 lands a quarter of the way. Two factors
    /// that are only distinguishable when they differ, which is why neither test above uses a layer
    /// weight other than one.
    /// </remarks>
    [Test]
    public void Build_WithAHalfWeightLayerOverAHalfWeightBone_BlendsAQuarterOfTheWay()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            baseAt: 10f, layerAt: 90f, boneWeights: [0.5f, 0f], layerWeight: 0.5f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(30f, Tolerance, "s = 0.5 times seqdesc.weight = 0.5 is s2 = 0.25");
    }

    /// <remarks>
    /// **A weight above one is clamped, not extrapolated.** <c>AccumulateLayers</c> does it before
    /// accumulating — <c>if (fWeight &gt; 1) fWeight = 1;</c>
    /// (<c>c_baseanimatingoverlay.cpp:369</c>) — and <c>SlerpBones</c> clamps again on entry
    /// (<c>bone_setup.cpp:1386</c>). Valve clamps twice; a layer that extrapolated would throw the
    /// bone past the layer's own pose.
    /// </remarks>
    [Test]
    public void Build_WithALayerWeightAboveOne_ClampsToTheLayersOwnPose()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            baseAt: 10f, layerAt: 90f, boneWeights: [1f, 0f], layerWeight: 4f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(90f, Tolerance, "fWeight > 1 is clamped, never extrapolated");
    }

    /// <remarks>
    /// **A layer of zero weight is skipped entirely**, which <c>AccumulateLayers</c> tests before
    /// it does anything: <c>if (fWeight &gt; 0)</c>. This is the control that separates "the layer
    /// applied and happened to change nothing" from "the layer was not applied", and it is also the
    /// common case — most slots are empty most of the time.
    /// </remarks>
    [Test]
    public void Build_WithAZeroWeightLayer_ChangesNothing()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = Posed(
            baseAt: 10f, layerAt: 90f, boneWeights: [1f, 1f], layerWeight: 0f);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(10f, Tolerance, "a zero-weight layer is not accumulated at all");
    }

    /// <remarks>
    /// **Layers accumulate onto the RESULT of the last, not onto the original base.** The engine
    /// walks <c>layer[j]</c> for ascending <c>j</c> — which is <c>m_nOrder</c> — and every
    /// <c>AccumulatePose</c> reads and writes the same <c>pos</c>/<c>q</c> arrays
    /// (<c>c_baseanimatingoverlay.cpp:372</c>).
    ///
    /// **The second layer is PARTIAL on purpose, and the first version of this test was worthless
    /// because it was not.** With both layers at weight one, <c>s1 = 1 - s2</c> is zero and
    /// whatever they blend against is multiplied away — so "onto the running result" and "onto the
    /// original base" predict the identical 90 and the test cannot fail. Sabotaged and measured:
    /// blending every layer against a saved copy of the base left all seven green.
    ///
    /// At half weight the two differ decisively. Accumulating in sequence gives
    /// <c>0.5 × 50 + 0.5 × 90 = 70</c>; against the original base it would give
    /// <c>0.5 × 10 + 0.5 × 90 = 50</c>.
    /// </remarks>
    [Test]
    public void Build_WithASecondPartialLayer_BlendsAgainstTheFirstsResult()
    {
        BoneAccessor into = new(2);

        SkeletonPose pose = new(Bones(), Sampler(baseAt: 10f, layers: [50f, 90f]))
        {
            Sequence = 0,
            Layers =
            [
                new PoseLayer(1, 0, 0f, 1f, [1f, 0f]),
                new PoseLayer(2, 0, 0f, 0.5f, [1f, 0f]),
            ],
        };

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(2));

        XOf(into, 0).ShouldBe(
            70f,
            Tolerance,
            "the second layer blends against what the first produced (0.5 x 50 + 0.5 x 90), not " +
            "against the original base pose, which would give 50");
    }

    /// <summary>A pose whose base sits at one X and whose single layer sits at another.</summary>
    private static SkeletonPose Posed(
        float baseAt, float layerAt, IReadOnlyList<float> boneWeights, float layerWeight = 1f) =>
        new(Bones(), Sampler(baseAt, [layerAt]))
        {
            Sequence = 0,
            Layers = [new PoseLayer(1, 0, 0f, layerWeight, boneWeights)],
        };

    /// <summary>
    /// An animation sampler that answers a different X per sequence: sequence 0 is the base, and
    /// sequence <c>n</c> is the <c>n - 1</c>th layer.
    /// </summary>
    private static Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>>
        Sampler(float baseAt, IReadOnlyList<float> layers) =>
        (sequence, _, _, _) =>
        {
            float x = sequence == 0 ? baseAt : layers[sequence - 1];

            return
            [
                new StudioBonePose(0, (x, 0f, 0f), (0f, 0f, 0f, 1f)),
                new StudioBonePose(1, (x, 0f, 0f), (0f, 0f, 0f, 1f)),
            ];
        };

    /// <summary>Two root bones at the origin, so the built matrix carries the pose and nothing else.</summary>
    /// <remarks>
    /// **Both parentless on purpose.** A child's built matrix is its parent's concatenated with its
    /// own, so a bone with a parent reports a number that is partly about the other bone — and a
    /// test measuring one bone's blend would be reading two.
    /// </remarks>
    private static IReadOnlyList<StudioBone> Bones() =>
    [
        new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone("other", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
    ];

    /// <summary>The X translation of a built bone matrix.</summary>
    private static float XOf(BoneAccessor bones, int bone) => bones.BoneForWrite(bone)[3];
}
