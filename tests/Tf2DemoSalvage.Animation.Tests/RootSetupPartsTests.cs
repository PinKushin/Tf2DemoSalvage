using System.Diagnostics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The split of a root's bone setup (2026-10-31 setup-bones read, follow-up 1): each part carried from where it ran.</summary>
/// <remarks>
/// **Controls with a known cost.** Each slow part spins for a different, known number of stopwatch ticks, so the part it
/// lands in must read at least that much, and a part it was attributed to by mistake reads near zero and fails. Only lower
/// bounds on wall time, because the gate runs assemblies side by side; the upper-side checks are the nesting invariants,
/// which hold exactly because the spans nest by construction.
/// </remarks>
public sealed class RootSetupPartsTests
{
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    private const int Everything = ~0;

    /// <remarks>
    /// The base sequence spins 10 ms, the one weighted layer 30 ms, and a zero-weight layer is not sampled at all (the
    /// engine's `if (fWeight > 0)`), so the count is exactly one. A base end-stamp taken after the layers would leave
    /// `Accumulate` below `Sampling`; a sample timed into the base would leave `Sampling` near zero.
    /// </remarks>
    [Test]
    public void Build_WithASlowBaseAndASlowLayer_CarriesEachIntoItsOwnPart()
    {
        SkeletonPose pose = Pose(baseTicks: 10 * Millisecond, layerTicks: 30 * Millisecond);
        pose.Layers = [Layer(sequence: 1, weight: 1f), Layer(sequence: 2, weight: 0f)];
        (BoneAccessor bones, BoneBitList written) = Storage();

        long started = Stopwatch.GetTimestamp();
        pose.Build(Everything, 0d, bones, written);
        long whole = Stopwatch.GetTimestamp() - started;

        RootSetupParts parts = pose.LastBuild;

        parts.Base.ShouldBeGreaterThanOrEqualTo(10 * Millisecond);
        parts.Sampling.ShouldBeGreaterThanOrEqualTo(30 * Millisecond);
        parts.Samples.ShouldBe(1);
        parts.Accumulate.ShouldBeGreaterThanOrEqualTo(parts.Sampling);
        parts.Blend.ShouldBe(0, "a pose does not time the blend; the entity does");
        parts.Sum.ShouldBeLessThanOrEqualTo(whole);
    }

    /// <remarks>
    /// **The blend is timed on every root call, the cache hits too**, because the root's ticks count those calls: two calls
    /// in one frame, one build, and the 20 ms blend spun on each must read at least 40 ms. The parts must fit inside the
    /// ticks the same calls recorded.
    /// </remarks>
    [Test]
    public void SetupBones_ARootCalledTwiceWithASlowBlend_RecordsTheBlendOfBothAndTheBuildsParts()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity root = new(Pose(baseTicks: 10 * Millisecond), clock) { BeforeBuild = () => Spin(20 * Millisecond) };

        root.SetupBones(Everything, 0d).ShouldBeTrue();
        root.SetupBones(Everything, 0d).ShouldBeTrue();

        RootSetupParts parts = clock.Meter.RootParts;

        clock.Meter.RootBuilds.ShouldBe(1);
        parts.Blend.ShouldBeGreaterThanOrEqualTo(40 * Millisecond);
        parts.Base.ShouldBeGreaterThanOrEqualTo(10 * Millisecond);
        parts.Sum.ShouldBeLessThanOrEqualTo(clock.Meter.RootTicks);
    }

    /// <remarks>
    /// **A child's build is not a root's part.** The parent is built first; the child, whose base spins 10 ms, then asks
    /// for it (a cache hit) and builds itself. Every build part of the roots stays exactly where it was, and the child's
    /// ticks hold the spin — the control that the child's build did run.
    /// </remarks>
    [Test]
    public void SetupBones_AChildOverABuiltParent_AddsNoBuildPartToTheRoots()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity parent = new(Pose(), clock);
        AnimatingEntity child = new(Pose(baseTicks: 10 * Millisecond), clock) { Follows = parent };

        parent.SetupBones(Everything, 0d).ShouldBeTrue();
        RootSetupParts before = clock.Meter.RootParts;

        child.SetupBones(Everything, 0d).ShouldBeTrue();
        RootSetupParts added = clock.Meter.RootParts.Since(before);

        (clock.Meter.ChildBuilds, clock.Meter.RootBuilds).ShouldBe((1, 1));
        clock.Meter.ChildTicks.ShouldBeGreaterThanOrEqualTo(10 * Millisecond);
        (added.Base, added.Accumulate, added.Sampling, added.Samples, added.Adjust, added.Loop, added.Ik, added.Finish)
            .ShouldBe((0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L));
        (added.Seed, added.Capture, added.Solve, added.Compose, added.Enumerators).ShouldBe((0L, 0L, 0L, 0L, 0L));
    }

    /// <remarks>
    /// **`Since` and `Add` are hand-written field lists**, and nothing else reaches them with distinct values: the Scene
    /// charge compares `Since` against itself, and the children's cases see only zeros. Every input, difference and sum
    /// here is distinct, so a field taken from its neighbour, or written to its neighbour's place, cannot pass.
    /// </remarks>
    [Test]
    public void SinceAndAdd_EveryFieldDistinct_WorkFieldByField()
    {
        RootSetupParts later = new(101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167);
        RootSetupParts earlier = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

        later.Since(earlier).ShouldBe(new RootSetupParts(100, 101, 104, 105, 108, 121, 124, 129, 130, 139, 140, 145, 150, 153));
        later.Add(earlier).ShouldBe(new RootSetupParts(102, 105, 110, 113, 118, 133, 138, 145, 148, 159, 162, 169, 176, 181));
    }

    /// <summary>A one-bone skeleton whose base sequence (0) and layer sequences (anything else) spin for a known time.</summary>
    private static SkeletonPose Pose(long baseTicks = 0, long layerTicks = 0) =>
        new([Bone()], (sequence, _, _, _) =>
        {
            Spin(sequence == 0 ? baseTicks : layerTicks);
            return [];
        });

    private static PoseLayer Layer(int sequence, float weight) => new(sequence, 0, 0f, weight, [1f]);

    private static (BoneAccessor Bones, BoneBitList Written) Storage()
    {
        BoneAccessor bones = new(1) { WritableBones = Everything, ReadableBones = Everything };
        return (bones, new BoneBitList(1));
    }

    private static void Spin(long ticks)
    {
        long until = Stopwatch.GetTimestamp() + ticks;

        while (Stopwatch.GetTimestamp() < until)
        {
            // A known cost, spent where the real work spends it.
        }
    }

    private static StudioBone Bone() =>
        new(
            Name: "root",
            Parent: -1,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
            Flags: Everything);
}
