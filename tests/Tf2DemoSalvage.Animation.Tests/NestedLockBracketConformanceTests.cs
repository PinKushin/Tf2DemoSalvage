using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A sequence's lock bracket encloses its own autolayers — `AccumulatePose`'s nesting (B625, B626).</summary>
/// <remarks>
/// **`CBoneSetup::AccumulatePose` (`bone_setup.cpp:2393-2455`)** opens `seq_ik` with `AddSequenceLocks` (`:2424-2429`),
/// applies the sequence, runs `AddSequenceLayers` (`:2449`) — each autolayer a nested `AccumulatePose` with its own local
/// `seq_ik` — and only then `SolveSequenceLocks` (`:2451-2454`). So a locking sequence pins its feet back AFTER its
/// autolayers have moved them, and a nested capture never overwrites its parent's. `AddSequenceLayers` has no weight test
/// (`:2209`) and `AccumulatePose` only clamps (`:2408`), so a weight-0 autolayer still brackets.
///
/// **The leg of <see cref="IkLockConformanceTests"/>**: a root and a three-link chain along +X with slack, tip at (20, 0, 0).
/// Every layer here samples only the ROOT, so the only thing that moves the foot is where a layer puts the root, and the
/// only thing that brings it back is a lock. Correct and broken orders predict different foot heights, never "some move".
/// </remarks>
public sealed class NestedLockBracketConformanceTests
{
    private const double Tolerance = 1e-3;

    private const int Everything = ~0;

    /// <remarks>
    /// **B625, the order.** A locking parent that moves nothing, then its non-locking child that carries the root 4 units
    /// up +Y. Nested, the parent's solve runs after the child and pins the tip back to 0. Closed straight after itself —
    /// the old order — the parent's solve is a no-op and the child carries the tip to 4.
    /// </remarks>
    [Test]
    public void Build_ALockingLayerWithANestedChild_SolvesAfterTheChild()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 0f, [2] = 4f });
        pose.Layers =
        [
            Layer(1, weight: 1f, locks: true, nested: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(0f, Tolerance, "the parent's bracket closes after its autolayer, so the foot is pinned back");
    }

    /// <remarks>
    /// **The control for the test above**: the same two layers with nothing nested. The parent's bracket closes before
    /// the child, so the child's move stands. Without this the test above could pass by pinning unconditionally.
    /// </remarks>
    [Test]
    public void Build_ALockingLayerWithNothingNested_LetsTheNextLayerMoveTheFoot()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 0f, [2] = 4f });
        pose.Layers =
        [
            Layer(1, weight: 1f, locks: true, nested: 0),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(4f, Tolerance, "a sibling after a closed bracket is outside it");
    }

    /// <remarks>
    /// **Each bracket holds its own capture.** The parent captures with the tip at 0 and moves the root to 2; its locking
    /// child captures with the tip at 2 and moves the root to 4. The child pins the tip to 2, then the parent pins it to 0.
    /// One shared store would let the child's capture overwrite the parent's, and the parent would pin to 2.
    /// </remarks>
    [Test]
    public void Build_TwoNestedLockingLayers_EachPinsToItsOwnCapture()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 2f, [2] = 4f });
        pose.Layers =
        [
            Layer(1, weight: 1f, locks: true, nested: 1),
            Layer(2, weight: 1f, locks: true),
        ];

        TipY(pose).ShouldBe(0f, Tolerance, "the outer solve restores what the OUTER capture saw");
        pose.AppliedLocks.ShouldBe(2, "both brackets solved, carried from the two instances that did it");
    }

    /// <remarks>
    /// **B625 on the main sequence.** The base locks and its one autolayer carries the root to 4. With `BaseNested` 1 the
    /// base's solve waits for it and pins the tip back to 0; the control, `BaseNested` 0, solves the base first and the
    /// autolayer's move stands.
    /// </remarks>
    [TestCase(1, 0f)]
    [TestCase(0, 4f)]
    public void Build_ALockingBaseWithItsAutolayers_ClosesAfterBaseNestedLayers(int baseNested, float expected)
    {
        SkeletonPose pose = Pose(rootY: new() { [2] = 4f });
        pose.Locks = [Lock()];
        pose.BaseNested = baseNested;
        pose.Layers = [Layer(2, weight: 1f, locks: false)];

        TipY(pose).ShouldBe(expected, Tolerance);
    }

    /// <remarks>
    /// **B626.** A locking autolayer at weight 0 whose child (weight 1, no locks) carries the root to 4. The engine still
    /// runs the zero-weight sequence's bracket, so the tip is pinned back to 0. Skipping the weight-0 layer whole — the old
    /// guard — leaves it at 4.
    /// </remarks>
    [Test]
    public void Build_AZeroWeightLockingLayer_StillBracketsItsChild()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 9f, [2] = 4f });
        pose.Layers =
        [
            Layer(1, weight: 0f, locks: true, nested: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(0f, Tolerance, "AccumulatePose has no weight test, so the bracket runs at weight 0");
    }

    /// <remarks>
    /// **The control for B626's weight half**: the zero-weight layer itself composes nothing. Its root sample is 9, so any
    /// composing at all would show; with no child and no lock the tip stays at the base's 0.
    /// </remarks>
    [Test]
    public void Build_AZeroWeightLayer_ComposesNothing()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 9f });
        pose.Layers = [Layer(1, weight: 0f, locks: false)];

        TipY(pose).ShouldBe(0f, Tolerance, "SlerpBones skips every bone at s2 <= 0");
    }

    /// <remarks>
    /// **B634: a local layer composes into its parent's OWN pose, which is then blended at the parent's weight.**
    /// `AccumulatePose` seeds `pos2` with `InitPose`, samples the parent into it, runs `AddLocalLayers( pos2, … 1.0 … )` and
    /// only then `SlerpBones( q, pos, …, q2, pos2, flWeight )` (`bone_setup.cpp:2433-2440`). Parent 1 at 0.5 puts the root at
    /// 4; its local layer 2 at 1 puts it at 8, inside `pos2`. Blended: 0.5 · 0 + 0.5 · 8 = 4. Flat — the layer composed onto
    /// the accumulator after its parent — gives 0.5 · 4 = 2 then 8.
    /// </remarks>
    [Test]
    public void Build_AHalfWeightLayerWithALocalLayer_BlendsItsOwnLayeredPose()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 4f, [2] = 8f });
        pose.Layers =
        [
            Layer(1, weight: 0.5f, locks: false, nested: 1, local: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(4f, Tolerance, "the local layer is inside the half-weight blend, not over it");
    }

    /// <remarks>
    /// **At weight 0 the parent's own pose is discarded, local layers and all**: `SlerpBones` skips every bone at
    /// `s2 &lt;= 0` (`bone_setup.cpp:2440`), so the local layer's 8 never reaches the accumulator and the tip stays at 0.
    /// </remarks>
    [Test]
    public void Build_AZeroWeightLayerWithALocalLayer_ComposesNeither()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 4f, [2] = 8f });
        pose.Layers =
        [
            Layer(1, weight: 0f, locks: false, nested: 1, local: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(0f, Tolerance);
    }

    /// <remarks>The control: the same two layers with `Local` 0 compose flat, and the second's 8 stands.</remarks>
    [Test]
    public void Build_AHalfWeightLayerWithNothingLocal_LetsTheNextLayerComposeOverIt()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 4f, [2] = 8f });
        pose.Layers =
        [
            Layer(1, weight: 0.5f, locks: false, nested: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(8f, Tolerance);
    }

    /// <remarks>
    /// **A local layer's bracket captures and solves on the parent's own pose, not on the accumulator.** Layer 3 moves the
    /// accumulator's root to 6. Parent 1 animates no bone, so its own pose is the bind pose (root 0); its local layer 2
    /// locks, captures the tip at 0 there, moves the root to 8 and pins the tip back to 0. Parent 1 weighs every bone at 1,
    /// so the blend takes its pose whole: tip 0. Captured on the accumulator instead, the lock would pin to 6.
    /// </remarks>
    [Test]
    public void Build_ALockingLocalLayer_PinsToWhatItsParentsOwnPoseHeld()
    {
        SkeletonPose pose = Pose(rootY: new() { [3] = 6f, [2] = 8f });
        pose.Layers =
        [
            Layer(3, weight: 1f, locks: false),
            Layer(1, weight: 1f, locks: false, nested: 1, local: 1) with { BoneWeights = [1f, 1f, 1f, 1f] },
            Layer(2, weight: 1f, locks: true),
        ];

        TipY(pose).ShouldBe(0f, Tolerance, "the lock captured on the parent's own bind-seeded pose");
    }

    /// <remarks>
    /// **Local scopes nest** (B634): `AddLocalLayers` → `AccumulatePose` → `AddLocalLayers` (`bone_setup.cpp:2439`,
    /// `:2284`), each into the `pos2` of the one above. Layer 1 (0.5, root 2) holds layer 2 (0.5, root 4) in its own pose,
    /// and 2 holds 3 (1, root 8) in its own. 2's pose: 4 then 8 = 8. Into 1's: 0.5 · 2 + 0.5 · 8 = 5. Into the
    /// accumulator: 0.5 · 0 + 0.5 · 5 = 2.5. Then layer 4 (0.5, root 10), after both scopes closed, composes onto the
    /// accumulator again: 0.5 · 2.5 + 0.5 · 10 = 6.25 — which reads only if the target came back from the inner pose.
    /// </remarks>
    [Test]
    public void Build_TwoNestedLocalScopes_BlendInnermostFirstAndReturnToTheAccumulator()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 2f, [2] = 4f, [3] = 8f, [4] = 10f });
        pose.Layers =
        [
            Layer(1, weight: 0.5f, locks: false, nested: 2, local: 2),
            Layer(2, weight: 0.5f, locks: false, nested: 1, local: 1),
            Layer(3, weight: 1f, locks: false),
            Layer(4, weight: 0.5f, locks: false),
        ];

        TipY(pose).ShouldBe(6.25f, Tolerance);
    }

    /// <remarks>
    /// **A locking parent whose whole subtree is local: the blend first, then the solve, both at one index.**
    /// `AccumulatePose` captures on `pos` (`bone_setup.cpp:2424-2429`), blends `pos2` in (`:2440`) and solves on `pos`
    /// last (`:2451-2454`). Layer 3 puts the accumulator's root at 6, so the parent captures the tip at 6; its own pose
    /// (root 4, then its local layer's 8) blends in whole on the root, and the solve pins the tip back to 6. Solved
    /// before the blend, the solve finds nothing to do and the blend leaves 8.
    /// </remarks>
    [Test]
    public void Build_ALockingLayerWhoseSubtreeIsAllLocal_SolvesAfterItsOwnPoseIsBlended()
    {
        SkeletonPose pose = Pose(rootY: new() { [3] = 6f, [1] = 4f, [2] = 8f });
        pose.Layers =
        [
            Layer(3, weight: 1f, locks: false),
            Layer(1, weight: 1f, locks: true, nested: 1, local: 1),
            Layer(2, weight: 1f, locks: false),
        ];

        TipY(pose).ShouldBe(6f, Tolerance, "the parent's bracket solves on the accumulator after its own pose is in");
    }

    /// <remarks>
    /// **A sparse local sample: a bone it does not name is the BIND pose in the parent's own pose, and is blended.**
    /// `CalcAnimation` writes every bone whose `seqdesc.weight` is above 0 — `q[i] = pbone->quat; pos[i] = pbone->pos;`
    /// for one with no track (`bone_setup.cpp:1110-1121`) — so `SlerpBones` at 0.5 pulls it halfway to bind. The knee
    /// layer first raises bone 2 to Y 6; the local parent names no bone and weighs every bone 1, so bone 2 goes to
    /// 0.5 · 6 + 0.5 · 0 = 3, and the tip with it.
    /// </remarks>
    [Test]
    public void Build_ASparseLocalSampleWithAWeightedUnnamedBone_PullsItHalfwayToBind()
    {
        SkeletonPose pose = Pose(rootY: []);
        pose.Layers =
        [
            Layer(Knee, weight: 1f, locks: false) with { BoneWeights = All },
            Layer(1, weight: 0.5f, locks: false, nested: 1, local: 1) with { BoneWeights = All },
            Layer(2, weight: 1f, locks: false),
        ];

        (float x, float y) = Tip(pose);

        y.ShouldBe(3f, Tolerance, "bone 2 halfway from 6 to its bind 0; untouched, it would stay at 6");
        x.ShouldBe(20f, Tolerance, "bind's X, 10, on both sides of the blend; an identity seed would halve it to 15");
    }

    /// <remarks>
    /// **A DELTA local parent seeds its unnamed bones with IDENTITY, not bind** (`bone_setup.cpp:1112-1116`,
    /// `if (animdesc.flags &amp; STUDIO_DELTA) { q[i].Init( 0, 0, 0, 1 ); pos[i].Init( 0, 0, 0 ); }`). The parent names only
    /// the root, +2 in Y, and adds at 1 over every bone. Identity elsewhere adds nothing, so the tip stays at X 20; a bind
    /// seed would add each bone's rest offset a second time and put it at 40. No shipped sequence reaches this (none of the
    /// 48 `STUDIO_LOCAL` sequences is delta).
    /// </remarks>
    [Test]
    public void Build_ADeltaLocalParent_SeedsItsUnnamedBonesWithIdentity()
    {
        SkeletonPose pose = Pose(rootY: new() { [1] = 2f });
        pose.Layers =
        [
            Layer(1, weight: 1f, locks: false, nested: 1, local: 1) with { BoneWeights = All, Delta = true },
            Layer(2, weight: 1f, locks: false),
        ];

        (float x, float y) = Tip(pose);

        x.ShouldBe(20f, Tolerance, "the rest leg alone, nothing added twice");
        y.ShouldBe(2f, Tolerance, "the root's delta");
    }

    /// <summary>A sequence that raises bone 2 to Y 6 and names nothing else.</summary>
    private const int Knee = 9;

    private static readonly float[] All = [1f, 1f, 1f, 1f];

    /// <summary>The leg, whose base (sequence 0) is the rest pose and whose layer sequences each place the root.</summary>
    /// <remarks>A sequence not in <paramref name="rootY"/> animates no bone at all.</remarks>
    private static SkeletonPose Pose(Dictionary<int, float> rootY) =>
        new(Bones(), (sequence, _, _, _) => Sample(sequence, rootY))
        {
            Sequence = 0,
            IkChains = Chains(),
        };

    private static StudioBonePose[] Sample(int sequence, Dictionary<int, float> rootY)
    {
        if (sequence == 0)
        {
            return Rest();
        }

        if (sequence == Knee)
        {
            return [new StudioBonePose(2, (10f, 6f, 5f), (0f, 0f, 0f, 1f))];
        }

        return rootY.TryGetValue(sequence, out float y) ? [new StudioBonePose(0, (0f, y, 0f), (0f, 0f, 0f, 1f))] : [];
    }

    private static PoseLayer Layer(int sequence, float weight, bool locks, int nested = 0, int local = 0) =>
        new(sequence, 0, 0f, weight, [1f, 0f, 0f, 0f], Locks: locks ? [Lock()] : null, Nested: nested, Local: local);

    private static StudioIkLock Lock() => new(Chain: 0, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0);

    private static IReadOnlyList<StudioIkChain> Chains() =>
    [
        new StudioIkChain(
            "leg",
            0,
            [new StudioIkLink(1, (0f, 0f, 0f)), new StudioIkLink(2, (0f, 0f, 0f)), new StudioIkLink(3, (0f, 0f, 0f))]),
    ];

    /// <summary>The bent leg of <see cref="IkLockConformanceTests"/>: reach 22.36, tip 20 from the root.</summary>
    private static StudioBonePose[] Rest() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(2, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(3, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
    ];

    private static StudioBone[] Bones()
    {
        StudioBonePose[] rest = Rest();
        int[] parents = [-1, 0, 1, 2];
        StudioBone[] bones = new StudioBone[rest.Length];

        for (int bone = 0; bone < rest.Length; bone++)
        {
            bones[bone] = new StudioBone(
                $"bone{bone}",
                parents[bone],
                rest[bone].Position,
                rest[bone].Rotation,
                new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: Everything);
        }

        return bones;
    }

    /// <summary>Builds the pose and reads the tip's Y off its bone matrix.</summary>
    private static float TipY(SkeletonPose pose) => Tip(pose).Y;

    /// <summary>Builds the pose and reads the tip's X and Y off its bone matrix.</summary>
    private static (float X, float Y) Tip(SkeletonPose pose)
    {
        BoneAccessor bones = new(4) { WritableBones = Everything, ReadableBones = Everything };
        pose.Build(Everything, 0d, bones, new BoneBitList(4));

        float[] tip = bones.BoneForWrite(3);

        return (tip[3], tip[7]);
    }
}
