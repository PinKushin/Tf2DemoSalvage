using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

using static Tf2DemoSalvage.Animation.Tests.DensePoseAccumulateDifferentialTests;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <see cref="SkeletonPose.Build"/> against <see cref="PoseBuildReference"/>, the path as it stood at <c>55484e1f</c>:
/// every bone matrix the same to the bit, the same bones written, and the same lock, IK and barrel counters.
/// </summary>
/// <remarks>
/// **The iklock-compose changes are pure cost reductions** (docs/measurements/2026-11-02-iklock-compose-cost.md), so the
/// only correct pose is the old one, bit for bit. What they touch is the shape of the loops and of the lock solve, so the
/// scenarios are the arrangements those loops walk: the main sequence's bracket over its own autolayers (B625), a local
/// subtree composed into its own pose (B634), a zero-weight parent whose local subtree is skipped, a delta local parent,
/// three nested locking layers, layers in the order B633 gives them, IK rules, bone controllers and a barrel.
///
/// **The sampler answers every call in one reused list**, as production's workspace does (the dense-pose differential's
/// `Sampler`), so a change that read a sample after the next call had overwritten it would differ here.
/// </remarks>
public sealed class PoseBuildDifferentialTests
{
    private const int Everything = ~0;

    /// <summary>Units: float round-trip noise on a travel of tens of units is far below this, a real change far above.</summary>
    private const float FurthestTolerance = 1e-3f;

    private static readonly float[] LayerWeights = [0f, 0.5f, 1f, 1.5f];

    [Test]
    public void Build_EveryArrangementOverRealPlayers_IsBitIdenticalToTheFrozenPath()
    {
        GameInstall.Require();

        VpkArchive[] archives = Archives();

        // The control before any skip: an installed game opens its archives and carries the scout.
        archives.ShouldNotBeEmpty("the control: an installed game opens its model archives");
        Read(archives, "models/player/scout.mdl").ShouldNotBeNull("the control: the reader finds the scout");

        Tally tally = new();
        StringBuilder read = new();

        foreach (string name in new[] { "scout", "heavy" })
        {
            byte[]? player = Read(archives, $"models/player/{name}.mdl");
            byte[]? animations = Read(archives, $"models/player/{name}_animations.mdl");

            if (player is null || animations is null)
            {
                Assert.Ignore($"models/player/{name}.mdl or its animations is not in this install");
                return;
            }

            IReadOnlyList<StudioBone> bones = StudioBones.Read(player);
            IReadOnlyList<StudioBone> animationBones = StudioBones.Read(animations);
            IReadOnlyList<StudioIkChain> chains = StudioIkChains.Read(player);
            Subject subject = new(
                name,
                bones,
                animations,
                animationBones,
                StudioBones.Remap(animationBones, bones),
                chains,
                LocksOf(animations),
                Controllers: [],
                ControllerValues: []);

            int before = tally.Compared;

            RunReal(subject, tally);
            read.Append(name).Append(' ').Append(tally.Compared - before).Append("; ");
        }

        TestContext.Out.WriteLine($"compared {tally.Compared} ({read}) {tally}");

        tally.Mismatches.ShouldBe(0, tally.First);
        tally.AssertReached(synthetic: false);
    }

    [Test]
    public void Build_EveryArrangementOverASyntheticPairOfLegs_IsBitIdenticalToTheFrozenPath()
    {
        StudioBone[] bones = Legs();
        Tally tally = new();
        int scenario = 0;

        // A straight base is what makes a main-sequence bracket's locks fail.
        foreach (int baseSequence in new[] { RestSequence, SparseBase, StraightLegs })
        {
            foreach (int shift in new[] { ShiftRoot, StraightLegs })
            {
                foreach (float weight in new[] { 0f, 0.5f, 1f })
                {
                    foreach (Arrangement arrangement in Arrangements(shift, KneeRaise, DeltaRoot, LegLocks(), weight, AllOf(bones.Length)))
                    {
                        scenario++;

                        Setup setup = new(
                            Sequence: baseSequence,
                            Arrangement: arrangement,
                            Chains: LegChains(),
                            IkErrors: scenario % 2 == 0 ? [Rule(chain: 0, bone: 3, (1f, 1f, 0f))] : [],
                            Controllers: scenario % 3 == 0 ? LegControllers() : [],
                            ControllerValues: [0.4f, 0.9f],
                            Barrel: scenario % 4 == 0 ? 5 : -1);

                        Compare(
                            $"legs base {baseSequence}, shift {shift}, weight {weight}, {arrangement.Name}",
                            bones,
                            () => new Sampler((sequence, _, _) => (LegSample(sequence), true), bones.Length),
                            setup,
                            Everything,
                            preWritten: scenario % 7 == 6 ? [4] : [],
                            tally);
                    }
                }
            }
        }

        TestContext.Out.WriteLine($"compared {tally.Compared}: {tally}");

        tally.Mismatches.ShouldBe(0, tally.First);
        tally.AssertReached(synthetic: true);
    }

    /// <remarks>
    /// **The layered path, with every loop the change rewrote in it, allocates nothing after a warm-up**: three nested
    /// locking layers inside the main sequence's bracket, a local subtree, bone controllers and an IK rule. Red at
    /// <c>55484e1f</c>, where each <c>foreach</c> over an interface boxed an enumerator (the setup-bones read's M6).
    ///
    /// **Not the barrel**: <c>SpinBarrel</c> builds a fresh list per call, which is a separate allocation the change does
    /// not touch. **Instrumentation can make this fail without production allocating** — a mutation run or a coverage
    /// collector records on the measuring thread — so a failure seen only under one is to be read as the instrument first.
    /// </remarks>
    [Test]
    public void Build_ASecondLayeredBuildWithLocksControllersAndIk_AllocatesNothing()
    {
        StudioBone[] bones = Legs();
        float[] all = AllOf(bones.Length);
        StudioIkLock[] locks = LegLocks();

        // Made once, so the sampler itself allocates nothing on the measured build.
        StudioBonePose[][] samples = [.. Enumerable.Range(0, SparseBase + 1).Select(LegSample)];
        SkeletonPose production = new(bones, (sequence, _, _, _) => samples[sequence])
        {
            Sequence = SparseBase,
            IkChains = LegChains(),
            Locks = locks,
            BaseNested = 5,
            Layers =
            [
                new(ShiftRoot, 0, 0f, 0.7f, all, Locks: locks, Nested: 2),
                new(KneeRaise, 0, 0f, 0.6f, all, Locks: locks, Nested: 1),
                new(DeltaRoot, 0, 0f, 1f, all, Delta: true, Locks: locks),
                new(ShiftRoot, 0, 0f, 0.8f, all, Nested: 1, Local: 1),
                new(KneeRaise, 0, 0f, 0.5f, all),
            ],
            Controllers = LegControllers(),
            BoneControllers = [0.4f, 0.9f],
            IkErrors = [Rule(chain: 0, bone: 3, (1f, 1f, 0f))],
        };
        BoneAccessor into = new(bones.Length);
        BoneBitList written = new(bones.Length);

        for (int pass = 0; pass < 64; pass++)
        {
            written.Clear();
            production.Build(Everything, 0d, into, written);
        }

        written.Clear();

        long before = GC.GetAllocatedBytesForCurrentThread();
        production.Build(Everything, 0d, into, written);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        written.IsMarked(bones.Length - 1).ShouldBeTrue("the control: the build ran to the last bone");
        production.AppliedLocks.ShouldBeGreaterThan(0, "the control: the lock brackets solved");
        production.SolvedChains.ShouldBe(1, "the control: the IK rule solved its chain");
    }

    private static void RunReal(Subject subject, Tally tally)
    {
        int count = StudioAnimation.Count(subject.File);
        int[] deltas = [.. Enumerable.Range(0, count).Where(a => StudioAnimation.IsDelta(subject.File, a)).Take(4)];
        int[] plain = [.. Enumerable.Range(0, count).Where(a => !StudioAnimation.IsDelta(subject.File, a)).Take(24)];
        int foot = subject.Chains.Count > 2 ? 2 : 0;
        int footBone = subject.Chains.Count > 0 ? subject.Chains[foot].Links[^1].Bone : 0;
        int[] masks = [Everything, StudioBoneFlags.UsedByAnything];
        StudioBoneController[] controllers =
        [
            new(Math.Min(1, subject.Bones.Count - 1), StudioBoneController.RotateX, -45f, 45f, 0),
            new(Math.Min(2, subject.Bones.Count - 1), StudioBoneController.TranslateY, 0f, 6f, 1),
            new(Math.Min(3, subject.Bones.Count - 1), StudioBoneController.RotateZ, 10f, 370f, 2),
        ];
        int scenario = 0;

        foreach (int animation in plain.Where((_, at) => at % 4 == 0))
        {
            int frames = Math.Max(1, StudioAnimation.Frames(subject.File, animation));

            foreach (float fraction in new[] { 0f, 0.5f })
            {
                int pa = plain[(animation + 1) % plain.Length];
                int pb = plain[(animation + 7) % plain.Length];
                int delta = deltas.Length > 0 ? deltas[animation % deltas.Length] : pb;
                float weight = LayerWeights[scenario % LayerWeights.Length];
                float[] boneWeights = [.. Enumerable.Range(0, subject.Bones.Count).Select(bone => bone % 5 == 4 ? 0.5f : 1f)];

                foreach (Arrangement arrangement in Arrangements(pa, pb, delta, subject.Locks, weight, boneWeights))
                {
                    scenario++;

                    Setup setup = new(
                        Sequence: animation,
                        Frame: frames / 2,
                        FrameFraction: fraction,
                        Arrangement: arrangement with
                        {
                            Layers = [.. arrangement.Layers.Select(layer => layer with { FrameFraction = fraction })],
                        },
                        Chains: subject.Chains,
                        IkErrors: scenario % 2 == 0 && subject.Chains.Count > 0 ? [Rule(foot, footBone, (1.5f, -1f, 2f))] : [],
                        Controllers: scenario % 3 == 0 ? controllers : [],
                        ControllerValues: [0.3f, 0.25f, 0.8f],
                        Barrel: scenario % 4 == 0 ? Math.Min(2, subject.Bones.Count - 1) : -1,
                        Placement: scenario % 2 == 1 ? [0f, -1f, 0f, 10f, 1f, 0f, 0f, -4f, 0f, 0f, 1f, 2.5f] : null);

                    Compare(
                        $"{subject.Name} animation {animation}, fraction {fraction}, weight {weight}, {arrangement.Name}",
                        subject.Bones,
                        subject.Sampler,
                        setup,
                        masks[scenario % masks.Length],
                        preWritten: scenario % 7 == 6 ? [0, Math.Min(3, subject.Bones.Count - 1)] : [],
                        tally);
                }
            }
        }
    }

    /// <summary>One arrangement of layers, with the main sequence's own locks and how many layers its bracket encloses.</summary>
    internal sealed record Arrangement(string Name, PoseLayer[] Layers, IReadOnlyList<StudioIkLock> BaseLocks, int BaseNested);

    /// <summary>Every arrangement the accumulate's loops walk, built from two plain sequences, a delta one and a lock list.</summary>
    private static Arrangement[] Arrangements(int pa, int pb, int delta, IReadOnlyList<StudioIkLock> locks, float weight, float[] bones) =>
    [
        new("nothing", [], BaseLocks: [], BaseNested: 0),
        new("base locks alone", [], BaseLocks: locks, BaseNested: 0),
        new(
            "the base bracket over two autolayers",
            [new(pa, 0, 0f, weight, bones, Locks: locks), new(delta, 0, 0f, 1f, bones, Delta: true)],
            BaseLocks: locks,
            BaseNested: 2),
        new(
            "a local subtree, then a locking nested child",
            [
                new(pa, 0, 0f, weight, bones, Nested: 2, Local: 1),
                new(pb, 0, 0f, 0.5f, bones),
                new(pb, 0, 0f, 0.8f, bones, Locks: locks),
            ],
            BaseLocks: [],
            BaseNested: 0),
        new(
            "a zero-weight locking parent skips its local child",
            [
                new(pa, 0, 0f, 0f, bones, Locks: locks, Nested: 2, Local: 1),
                new(pb, 0, 0f, 1f, bones),
                new(delta, 0, 0f, 1f, bones, Delta: true),
            ],
            BaseLocks: [],
            BaseNested: 0),
        new(
            "a delta local parent",
            [new(delta, 0, 0f, 1f, bones, Delta: true, Nested: 1, Local: 1), new(pa, 0, 0f, 0.5f, bones)],
            BaseLocks: [],
            BaseNested: 0),
        new(
            "three nested locking layers in the base bracket",
            [
                new(pa, 0, 0f, weight, bones, Locks: locks, Nested: 2),
                new(pb, 0, 0f, 0.6f, bones, Locks: locks, Nested: 1),
                new(delta, 0, 0f, 1f, bones, Delta: true, Post: true, Locks: locks),
            ],
            BaseLocks: locks,
            BaseNested: 3),
        new(
            "flat layers, newest first",
            [
                new(pb, 0, 0f, 0.3f, bones),
                new(pa, 0, 0f, weight, bones, Locks: locks),
                new(delta, 0, 0f, 0.9f, bones, Delta: true, Post: true),
            ],
            BaseLocks: [],
            BaseNested: 0),
        new(
            "a local subtree inside a local subtree",
            [
                new(pa, 0, 0f, 0.8f, bones, Nested: 2, Local: 2),
                new(pb, 0, 0f, weight, bones, Nested: 1, Local: 1),
                new(delta, 0, 0f, 1f, bones, Delta: true),
            ],
            BaseLocks: [],
            BaseNested: 0),
    ];

    private static void Compare(
        string label,
        IReadOnlyList<StudioBone> bones,
        Func<Sampler> sampler,
        Setup setup,
        int mask,
        int[] preWritten,
        Tally tally)
    {
        Sampler referenceSampler = sampler();
        Sampler productionSampler = sampler();

        PoseBuildReference reference = new(bones, (sequence, frame, fraction, _) => referenceSampler.Sample(sequence, frame, fraction));
        SkeletonPose production = new(bones, (sequence, frame, fraction, _) => productionSampler.Sample(sequence, frame, fraction));

        setup.ApplyTo(reference);
        setup.ApplyTo(production);

        BoneAccessor want = new(bones.Count);
        BoneAccessor got = new(bones.Count);
        BoneBitList wantWritten = new(bones.Count);
        BoneBitList gotWritten = new(bones.Count);

        foreach (int bone in preWritten)
        {
            float[] merged = [0.5f, 0f, 0.866f, 3f, 0f, 1f, 0f, -2f, -0.866f, 0f, 0.5f, 7f];

            merged.CopyTo(want.BoneForWrite(bone), 0);
            merged.CopyTo(got.BoneForWrite(bone), 0);
            wantWritten.Mark(bone);
            gotWritten.Mark(bone);
        }

        reference.Build(mask, want, wantWritten);
        production.Build(mask, 0d, got, gotWritten);

        productionSampler.Check();

        if (productionSampler.Written is { } written)
        {
            tally.Mismatch($"{label}: the build wrote into a pose it was handed: {written}");
            return;
        }

        // The inert values the reference leaves out, checked rather than assumed.
        production.JiggleSource.ShouldBeNull();
        production.DuckJumpOffset.ShouldBe(0f);
        production.RootReflection.ShouldBeNull();
        (production.HeadScale, production.TorsoScale, production.HandScale).ShouldBe((1f, 1f, 1f));

        tally.Compared++;

        (int Applied, int Moved, float Furthest, int Solved, int Spun) wantCounters =
            (reference.AppliedLocks, reference.LockEffect.Moved, reference.LockEffect.Furthest, reference.SolvedChains, reference.SpunBarrels);
        (int Applied, int Moved, float Furthest, int Solved, int Spun) gotCounters =
            (production.AppliedLocks, production.LockEffect.Moved, production.LockEffect.Furthest, production.SolvedChains, production.SpunBarrels);

        // `FurthestMove` within a tolerance, not to the bit: since the iklock-compose re-walk went, a lock's travel ends at
        // the solved matrix rather than at its local-space round trip (B243), a last-bits change of route.
        if (gotCounters.Applied != wantCounters.Applied || gotCounters.Moved != wantCounters.Moved ||
            MathF.Abs(gotCounters.Furthest - wantCounters.Furthest) > FurthestTolerance ||
            gotCounters.Solved != wantCounters.Solved || gotCounters.Spun != wantCounters.Spun)
        {
            tally.Mismatch($"{label}: counters (applied, moved, furthest, solved, spun) {gotCounters}, want {wantCounters}");
            return;
        }

        tally.Count(setup, wantCounters.Applied, wantCounters.Moved, wantCounters.Solved, wantCounters.Spun);

        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (gotWritten.IsMarked(bone) != wantWritten.IsMarked(bone))
            {
                tally.Mismatch($"{label}: bone {bone} written {gotWritten.IsMarked(bone)}, want {wantWritten.IsMarked(bone)}");
                return;
            }

            ReadOnlySpan<float> g = got.Bone(bone);
            ReadOnlySpan<float> w = want.Bone(bone);

            for (int cell = 0; cell < 12; cell++)
            {
                if (BitConverter.SingleToInt32Bits(g[cell]) != BitConverter.SingleToInt32Bits(w[cell]))
                {
                    tally.Mismatch($"{label}: bone {bone} cell {cell} {g[cell]:R}, want {w[cell]:R}");
                    return;
                }
            }
        }
    }

    /// <summary>Everything a build reads off the entity, applied the same way to both sides.</summary>
    private sealed record Setup(
        Arrangement Arrangement,
        IReadOnlyList<StudioIkChain> Chains,
        IReadOnlyList<(StudioIkRule Rule, Vector3 Position, Quaternion Rotation, float Weight)> IkErrors,
        IReadOnlyList<StudioBoneController> Controllers,
        IReadOnlyList<float> ControllerValues,
        int Sequence = 0,
        int Frame = 0,
        float FrameFraction = 0f,
        int Barrel = -1,
        IReadOnlyList<float>? Placement = null)
    {
        private static readonly (float X, float Y, float Z, float W) Spun = (0.1f, 0.2f, -0.3f, 0.927f);

        public void ApplyTo(PoseBuildReference target)
        {
            target.Sequence = Sequence;
            target.Frame = Frame;
            target.FrameFraction = FrameFraction;
            target.Layers = Arrangement.Layers;
            target.IkChains = Chains;
            target.Locks = Arrangement.BaseLocks;
            target.BaseNested = Arrangement.BaseNested;
            target.IkErrors = IkErrors;
            target.Controllers = Controllers;
            target.BoneControllers = ControllerValues;
            target.BarrelBone = Barrel;
            target.BarrelRotation = Spun;
            target.EntityTransform = Placement;
        }

        public void ApplyTo(SkeletonPose target)
        {
            target.Sequence = Sequence;
            target.Frame = Frame;
            target.FrameFraction = FrameFraction;
            target.Layers = Arrangement.Layers;
            target.IkChains = Chains;
            target.Locks = Arrangement.BaseLocks;
            target.BaseNested = Arrangement.BaseNested;
            target.IkErrors = IkErrors;
            target.Controllers = Controllers;
            target.BoneControllers = ControllerValues;
            target.BarrelBone = Barrel;
            target.BarrelRotation = Spun;
            target.EntityTransform = Placement;
        }
    }

    private sealed class Tally
    {
        public int Compared { get; set; }

        public int Mismatches { get; private set; }

        public string? First { get; private set; }

        private readonly HashSet<string> _arrangements = [];

        private int _applied;
        private int _moved;
        private int _solved;
        private int _spun;
        private int _controlled;
        private int _bothLocks;
        private int _failedLocks;

        public void Mismatch(string what)
        {
            Mismatches++;
            First ??= what;
        }

        public void Count(Setup setup, int applied, int moved, int solved, int spun)
        {
            _arrangements.Add(setup.Arrangement.Name);
            _applied += applied > 0 ? 1 : 0;
            _moved += moved > 0 ? 1 : 0;
            _solved += solved > 0 ? 1 : 0;
            _spun += spun > 0 ? 1 : 0;
            _controlled += setup.Controllers.Count > 0 ? 1 : 0;

            bool baseOnly = setup.Arrangement.BaseLocks.Count > 0 && setup.Arrangement.Layers.Length == 0;

            _bothLocks += baseOnly && applied == 2 ? 1 : 0;
            _failedLocks += baseOnly && applied == 0 ? 1 : 0;
        }

        public void AssertReached(bool synthetic)
        {
            _arrangements.Count.ShouldBe(9, $"the control: every arrangement was compared ({string.Join(", ", _arrangements)})");
            _applied.ShouldBeGreaterThan(0, "the control: a build applied IK locks");
            _moved.ShouldBeGreaterThan(0, "the control: a lock moved its effector, so Moved and FurthestMove are compared");
            _solved.ShouldBeGreaterThan(0, "the control: an IK rule solved a chain");
            _spun.ShouldBeGreaterThan(0, "the control: a build spun a barrel");
            _controlled.ShouldBeGreaterThan(0, "the control: a build ran bone controllers");

            if (synthetic)
            {
                _bothLocks.ShouldBeGreaterThan(0, "the control: both locks of one bracket solved");
                _failedLocks.ShouldBeGreaterThan(0, "the control: a bracket whose locks failed to solve");
            }
        }

        public override string ToString() =>
            $"arrangements {_arrangements.Count}, locks applied {_applied}, moved {_moved}, IK solved {_solved}, barrel {_spun}, " +
            $"controllers {_controlled}, both locks {_bothLocks}, failed locks {_failedLocks}";
    }

    private static (StudioIkRule Rule, Vector3 Position, Quaternion Rotation, float Weight) Rule(
        int chain, int bone, (float X, float Y, float Z) error) =>
        (new StudioIkRule(
            Type: StudioIkRuleType.Self,
            Chain: chain,
            Bone: bone,
            Slot: 0,
            Height: 0f,
            Radius: 0f,
            Floor: 0f,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            CompressedError: 0,
            FirstFrame: 0,
            ErrorIndex: 1,
            Start: 0f,
            Peak: 0f,
            Tail: 1f,
            End: 1f,
            Contact: 0f,
            Drop: 0f,
            Top: 0f,
            AttachmentName: 0),
         new Vector3(error.X, error.Y, error.Z),
         Quaternion.Identity,
         1f);

    // The legs: a root and two bent three-bone legs off it, reach 22.36 each, so a root shifted by 2 can be pinned. A
    // straightened leg is what `Studio_SolveIK` refuses (`l3 > (l1 + l2) * 0.9998`), so its locks fail.
    private const int RestSequence = 0;
    private const int ShiftRoot = 1;
    private const int StraightLegs = 2;
    private const int KneeRaise = 3;
    private const int DeltaRoot = 4;
    private const int SparseBase = 5;

    private static StudioBonePose[] LegRest() =>
    [
        new(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new(1, (0f, 3f, 0f), (0f, 0f, 0f, 1f)),
        new(2, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
        new(3, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
        new(4, (0f, -3f, 0f), (0f, 0f, 0f, 1f)),
        new(5, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
        new(6, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
    ];

    private static StudioBonePose[] LegSample(int sequence) => sequence switch
    {
        RestSequence => LegRest(),
        ShiftRoot => [new(0, (0f, 2f, 0f), (0f, 0f, 0.0998f, 0.995f))],
        StraightLegs =>
        [
            new(2, (11.18034f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(3, (11.18034f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(5, (11.18034f, 0f, 0f), (0f, 0f, 0f, 1f)),
            new(6, (11.18034f, 0f, 0f), (0f, 0f, 0f, 1f)),
        ],
        KneeRaise => [new(2, (10f, 6f, 5f), (0f, 0f, 0f, 1f)), new(5, (10f, -6f, 5f), (0.1f, 0f, 0f, 0.995f))],
        DeltaRoot => [new(0, (0f, 1f, 0.5f), (0f, 0.05f, 0f, 0.9987f))],
        SparseBase => [new(1, (0f, 3f, 0.5f), (0f, 0f, 0f, 1f)), new(5, (10f, 1f, 5f), (0f, 0f, 0f, 1f))],
        _ => [],
    };

    private static StudioBone[] Legs()
    {
        StudioBonePose[] rest = LegRest();
        int[] parents = [-1, 0, 1, 2, 0, 4, 5];

        return
        [
            .. rest.Select(pose => new StudioBone(
                $"bone{pose.Bone}",
                parents[pose.Bone],
                pose.Position,
                pose.Rotation,
                new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: StudioBoneFlags.UsedByAnything)),
        ];
    }

    private static StudioIkChain[] LegChains() =>
    [
        new("left", 0, [new StudioIkLink(1, (0f, 0f, 0f)), new StudioIkLink(2, (0f, 0f, 0f)), new StudioIkLink(3, (0f, 0f, 0f))]),
        new("right", 0, [new StudioIkLink(4, (0f, 0f, 0f)), new StudioIkLink(5, (0f, 0f, 0f)), new StudioIkLink(6, (0f, 0f, 0f))]),
    ];

    private static StudioIkLock[] LegLocks() =>
    [
        new(Chain: 0, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0),
        new(Chain: 1, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0),
    ];

    private static StudioBoneController[] LegControllers() =>
    [
        new(2, StudioBoneController.RotateY, -30f, 60f, 0),
        new(5, StudioBoneController.TranslateZ, 0f, 4f, 1),
    ];

    private static float[] AllOf(int bones) => [.. Enumerable.Repeat(1f, bones)];
}
