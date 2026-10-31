using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The split of <c>Accumulate</c> inside the roots split (2026-10-31 accumulate-parts record).</summary>
/// <remarks>
/// **Controls with a known cost, as in <see cref="RootSetupPartsTests"/>.** Each slow step spins for a different, known
/// time through a list the step itself reads: the base pose's indexer (read only by the base's overlay, so the seed), the
/// lock list's indexer (read only by <c>IkLocks</c>), the sampled layer's enumerator (taken only by the compose pass) and
/// the sample itself. Lower bounds on wall time only; the upper side is <see cref="RootSetupParts.AccumulateOther"/> ≥ 0,
/// which fails when a sub-part swallows another's span, because the same ticks are then subtracted twice.
/// </remarks>
public sealed class AccumulatePartsTests
{
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    private const int Everything = ~0;

    /// <remarks>
    /// One layer that locks a chain: each base read 10 ms (only the sparse base's overlay reads it), each lock read 5 ms
    /// (only the capture before the sample and the solve after the compose read it), compose 20 ms, sample 30 ms. The
    /// lists count their own reads, so each part's floor is reads × spin: a solve left unstamped falls short of it by the
    /// solve's reads, and a compose stamped from before the sample reads the 30 ms twice and drives the residual negative.
    /// </remarks>
    [Test]
    public void Build_WithASlowSeedLockComposeAndSample_CarriesEachIntoItsOwnSubPart()
    {
        SlowList<StudioBonePose> basePose = new([Rest(0)], indexTicks: 10 * Millisecond);
        SlowList<StudioIkLock> locks = new([Lock()], indexTicks: 5 * Millisecond);
        SkeletonPose pose = Pose(
            basePose: basePose,
            layerPose: new SlowList<StudioBonePose>([Rest(0), Rest(1), Rest(2)], enumerateTicks: 20 * Millisecond),
            layerTicks: 30 * Millisecond);
        pose.IkChains = [Chain()];
        pose.Layers = [Layer(weight: 1f) with { Locks = locks }];

        RootSetupParts parts = Built(pose);

        (basePose.Reads, locks.Reads).ShouldBe((1, 4), "the control: the overlay reads once; capture once, solve three times");
        parts.Seed.ShouldBeGreaterThanOrEqualTo(basePose.Reads * 10 * Millisecond);
        parts.Locks.ShouldBeGreaterThanOrEqualTo(locks.Reads * 5 * Millisecond);
        parts.Compose.ShouldBeGreaterThanOrEqualTo(20 * Millisecond);
        parts.Sampling.ShouldBeGreaterThanOrEqualTo(30 * Millisecond);
        parts.AccumulateOther.ShouldBeGreaterThanOrEqualTo(0, "a sub-part counted a span twice");
        (parts.Seed + parts.Locks + parts.Compose + parts.AccumulateOther).ShouldBe(parts.Accumulate - parts.Sampling);
    }

    /// <remarks>
    /// **The base's own bracket splits the seed in two**: the rest seed, the capture, the overlay, the solve. The overlay
    /// spins 10 ms after the capture; every lock read spins 20 ms. A seed stamp that ran on through the capture counts the
    /// capture in both seed and locks, and the residual goes negative.
    /// </remarks>
    [Test]
    public void Build_WithBaseLocksAndNoLayers_SplitsTheSeedAroundTheBracket()
    {
        SlowList<StudioBonePose> basePose = new([Rest(0)], indexTicks: 10 * Millisecond);
        SlowList<StudioIkLock> locks = new([Lock()], indexTicks: 20 * Millisecond);
        SkeletonPose pose = Pose(basePose: basePose);
        pose.IkChains = [Chain()];
        pose.Locks = locks;

        RootSetupParts parts = Built(pose);

        (basePose.Reads, locks.Reads).ShouldBe((1, 4), "the control: the overlay reads once; capture once, solve three times");
        parts.Seed.ShouldBeGreaterThanOrEqualTo(basePose.Reads * 10 * Millisecond);
        parts.Locks.ShouldBeGreaterThanOrEqualTo(locks.Reads * 20 * Millisecond);
        (parts.Samples, parts.Compose).ShouldBe((0L, 0L));
        parts.AccumulateOther.ShouldBeGreaterThanOrEqualTo(0, "a sub-part counted a span twice");
    }

    /// <remarks>
    /// **One enumerator for the layer loop and one per sampled layer**: two weighted layers and one at zero weight, which is
    /// not sampled, so three. The control: a pose with no layers and no locks returns before any loop and takes none.
    /// </remarks>
    [Test]
    public void Build_WithTwoWeightedLayersAndOneAtZero_TakesThreeEnumerators()
    {
        SkeletonPose layered = Pose();
        layered.Layers = [Layer(weight: 1f), Layer(weight: 0.5f), Layer(weight: 0f)];
        SkeletonPose bare = Pose();

        (Built(layered).Enumerators, Built(bare).Enumerators).ShouldBe((3L, 0L));
    }

    /// <remarks>
    /// **A child's accumulate is not a root's.** The child's layer composes over a 10 ms enumerator; every sub-part of the
    /// roots stays exactly where it was. The control: the child's own build carries the compose and the two enumerators.
    /// </remarks>
    [Test]
    public void SetupBones_AChildWithALayer_AddsNoAccumulatePartToTheRoots()
    {
        BoneFrameCounter clock = new();
        AnimatingEntity parent = new(Pose(), clock);
        SkeletonPose childPose = Pose(layerPose: new SlowList<StudioBonePose>([Rest(0)], enumerateTicks: 10 * Millisecond));
        childPose.Layers = [Layer(weight: 1f)];
        AnimatingEntity child = new(childPose, clock) { Follows = parent };

        parent.SetupBones(Everything, 0d).ShouldBeTrue();
        RootSetupParts before = clock.Meter.RootParts;

        child.SetupBones(Everything, 0d).ShouldBeTrue();
        RootSetupParts added = clock.Meter.RootParts.Since(before);

        childPose.LastBuild.Compose.ShouldBeGreaterThanOrEqualTo(10 * Millisecond);
        childPose.LastBuild.Enumerators.ShouldBe(2);
        (added.Seed, added.Locks, added.Compose, added.Enumerators).ShouldBe((0L, 0L, 0L, 0L));
    }

    private static RootSetupParts Built(SkeletonPose pose)
    {
        BoneAccessor bones = new(Bones) { WritableBones = Everything, ReadableBones = Everything };
        pose.Build(Everything, 0d, bones, new BoneBitList(Bones));
        return pose.LastBuild;
    }

    private const int Bones = 3;

    /// <summary>A three-bone chain whose base sequence (0) and layer sequences (anything else) return the lists given.</summary>
    private static SkeletonPose Pose(
        IReadOnlyList<StudioBonePose>? basePose = null,
        IReadOnlyList<StudioBonePose>? layerPose = null,
        long layerTicks = 0) =>
        new([Bone(0, -1), Bone(1, 0), Bone(2, 1)], (sequence, _, _, _) =>
        {
            if (sequence == 0)
            {
                return basePose ?? [];
            }

            Spin(layerTicks);
            return layerPose ?? [Rest(0)];
        });

    private static PoseLayer Layer(float weight) => new(1, 0, 0f, weight, [1f, 1f, 1f]);

    private static StudioIkChain Chain() =>
        new("rfoot", 0, [new StudioIkLink(0, (0f, 0f, 1f)), new StudioIkLink(1, (0f, 0f, 1f)), new StudioIkLink(2, (0f, 0f, 1f))]);

    private static StudioIkLock Lock() => new(Chain: 0, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0);

    private static StudioBonePose Rest(int bone) => new(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));

    private static StudioBone Bone(int index, int parent) =>
        new(
            Name: $"bone{index}",
            Parent: parent,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
            Flags: Everything);

    private static void Spin(long ticks)
    {
        long until = Stopwatch.GetTimestamp() + ticks;

        while (Stopwatch.GetTimestamp() < until)
        {
            // A known cost, spent where the real work spends it.
        }
    }

    /// <summary>A list whose indexer and enumerator each cost a known time, so the step that reads it is known.</summary>
    private sealed class SlowList<T>(T[] items, long indexTicks = 0, long enumerateTicks = 0) : IReadOnlyList<T>
    {
        public int Count => items.Length;

        /// <summary>How many times the indexer ran: the floor of the part that reads it is this many spins.</summary>
        public int Reads { get; private set; }

        public T this[int index]
        {
            get
            {
                Reads++;
                Spin(indexTicks);
                return items[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            Spin(enumerateTicks);
            return ((IEnumerable<T>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
