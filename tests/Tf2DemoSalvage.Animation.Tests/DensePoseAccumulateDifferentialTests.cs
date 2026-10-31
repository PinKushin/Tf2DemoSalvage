using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <see cref="SkeletonPose.Build"/> against <see cref="AccumulatePathReference"/>, its accumulate path as it stood
/// before the dense-pose change: every bone matrix the same to the bit, and the same bones marked written.
/// </summary>
/// <remarks>
/// **A pure cost reduction, so the only correct answer is the old one, bit for bit.** Valve's pose stays dense from
/// <c>InitPose</c> to <c>BuildTransformations</c> (<c>bone_setup.cpp:1782</c>, <c>AccumulatePose</c> :2393,
/// <c>SlerpBones</c> :1373, <c>CalcBoneAdj</c> :2462 in place); ours re-expanded a pose that was already dense in
/// <c>Accumulate</c>, again in <c>Adjust</c>, and indexed it again for <c>_overrideOf</c>. Skipping that work must not
/// move a bit.
///
/// **The sampler hands every call back in ONE reused list**, as production's workspace does, so a change that read the
/// base pose after the first layer had overwritten it would differ here. Every input class is counted as a control:
/// a dense and a sparse base, layers of each kind and weight, base and layer locks, controllers, the barrel, a partial
/// mask, bones the merge already wrote, and every shape the dense shortcut must refuse.
/// </remarks>
public sealed class DensePoseAccumulateDifferentialTests
{
    private static readonly float[] LayerWeights = [0f, 1e-6f, 0.5f, 1f, 1.5f, float.NaN];

    private static readonly float[] WeightPattern = [1f, 0f, 0.5f, 1e-7f, 2f, float.NaN, -1f, 1f, 0.25f];

    [Test]
    public void Build_EveryScenarioOverRealAnimatedModels_IsBitIdenticalToTheExpandingReference()
    {
        Tally tally = new();
        StringBuilder read = new();

        GameInstall.Require();

        VpkArchive[] archives = Archives();
        int subjects = 0;

        foreach (string name in new[] { "scout", "heavy", "engineer", "spy" })
        {
            if (Read(archives, $"models/player/{name}.mdl") is not { } player ||
                Read(archives, $"models/player/{name}_animations.mdl") is not { } animations)
            {
                continue;
            }

            subjects++;

            IReadOnlyList<StudioBone> bones = StudioBones.Read(player);
            IReadOnlyList<StudioBone> animationBones = StudioBones.Read(animations);
            int[] remap = StudioBones.Remap(animationBones, bones);
            Subject subject = new(
                name,
                bones,
                animations,
                animationBones,
                remap,
                StudioIkChains.Read(player),
                LocksOf(animations),
                Controllers: [],
                ControllerValues: []);

            int before = tally.Compared;

            RunScenarios(subject, tally);
            read.Append(name).Append(' ').Append(tally.Compared - before).Append("; ");
        }

        if (Read(archives, "models/buildables/sentry3.mdl") is { } sentry)
        {
            subjects++;

            IReadOnlyList<StudioBone> bones = StudioBones.Read(sentry);

            // The shipped sentry declares no bone controllers (seen when this test was first run), so three are put on
            // its own bones, one of each kind `CalcBoneAdj` switches on, beside any the file does declare.
            IReadOnlyList<StudioBoneController> controllers =
            [
                .. StudioBoneControllers.Read(sentry),
                new(Math.Min(1, bones.Count - 1), StudioBoneController.RotateX, -45f, 45f, 0),
                new(Math.Min(2, bones.Count - 1), StudioBoneController.TranslateY, 0f, 6f, 1),
                new(Math.Min(3, bones.Count - 1), StudioBoneController.RotateZ, 10f, 370f, 2),
            ];
            Subject subject = new(
                "sentry3",
                bones,
                sentry,
                bones,
                Remap: null,
                Chains: [],
                Locks: [],
                controllers,
                ControllerValues: [0f, 0.3f, 1f, 1.7f, float.NaN, 0.5f]);

            int before = tally.Compared;

            RunScenarios(subject, tally);
            read.Append("sentry3 ").Append(tally.Compared - before).Append("; ");
        }

        TestContext.Out.WriteLine($"compared {tally.Compared} ({read}) {tally}");

        subjects.ShouldBe(5, "the control: every subject model was read from the install");
        tally.Mismatches.ShouldBe(0, tally.First);
        tally.AssertReached();
    }

    [Test]
    public void Build_EveryShapeOfASyntheticPose_IsBitIdenticalToTheExpandingReference()
    {
        List<StudioBone> bones = Skeleton();
        Tally tally = new();

        foreach ((string shape, StudioBonePose[] pose, bool listed) in Shapes(bones.Count))
        {
            foreach ((string layerShape, StudioBonePose[] layerPose, bool layerListed) in Shapes(bones.Count))
            {
                foreach (bool withLayer in new[] { false, true })
                {
                    foreach (bool withControllers in new[] { false, true })
                    {
                        Sampler Make() => new(
                            (sequence, _, _) => sequence == 0 ? (pose, listed) : (layerPose, layerListed), bones.Count);

                        Setup setup = new(
                            Layers: withLayer ? [new(1, 0, 0f, 0.75f, WeightPattern, Delta: false)] : [],
                            Controllers: withControllers
                                ?
                                [
                                    new(3, StudioBoneController.RotateY, -30f, 60f, 0),
                                    new(5, StudioBoneController.TranslateZ, 0f, 4f, 1),
                                    new(6, StudioBoneController.TranslateX, 0f, 4f, 1),
                                ]
                                : [],
                            ControllerValues: [0.4f, 0.9f]);

                        Compare(
                            $"synthetic base {shape}, layer {layerShape}, layer {withLayer}, controllers {withControllers}",
                            bones,
                            Make,
                            setup,
                            ~0,
                            preWritten: [],
                            tally);

                        tally.SeenShapes.Add(shape);
                    }
                }
            }
        }

        tally.Mismatches.ShouldBe(0, tally.First);

        foreach ((string shape, _, _) in Shapes(bones.Count))
        {
            tally.SeenShapes.ShouldContain(shape, $"the control: the {shape} base was compared");
        }

        tally.DenseBase.ShouldBeGreaterThan(0, "the control: a dense base was compared");
        tally.OtherBase.ShouldBeGreaterThan(0, "the control: a base the shortcut must refuse was compared");
    }

    private static void RunScenarios(Subject subject, Tally tally)
    {
        int count = StudioAnimation.Count(subject.File);
        int[] deltas = [.. Enumerable.Range(0, count).Where(a => StudioAnimation.IsDelta(subject.File, a)).Take(6)];
        int[] plain = [.. Enumerable.Range(0, count).Where(a => !StudioAnimation.IsDelta(subject.File, a)).Take(40)];
        int[] masks =
        [
            ~0,
            StudioBoneFlags.UsedByAnything,
            subject.Bones.Count > 1 ? subject.Bones[1].Flags & StudioBoneFlags.UsedByAnything : 0,
        ];
        int scenario = 0;

        foreach (int animation in plain.Where((_, at) => at % 4 == 0))
        {
            int frames = Math.Max(1, StudioAnimation.Frames(subject.File, animation));

            foreach (float fraction in new[] { 0f, 0.5f })
            {
                foreach (float layerWeight in LayerWeights)
                {
                    scenario++;

                    int layerAnimation = deltas.Length > 0 ? deltas[scenario % deltas.Length] : animation;
                    int overAnimation = plain[(scenario + 1) % plain.Length];
                    bool post = scenario % 2 == 0;
                    bool withLocks = scenario % 3 == 0 && subject.Locks.Count > 0;
                    bool layerLocks = scenario % 3 == 1 && subject.Locks.Count > 0;
                    float[] boneWeights = [.. Enumerable.Range(0, subject.Bones.Count)
                        .Select(bone => scenario % 2 == 0 ? 1f : WeightPattern[bone % WeightPattern.Length])];

                    PoseLayer[] layers = (scenario % 5) switch
                    {
                        0 => [],
                        1 => [new(overAnimation, 0, fraction, layerWeight, boneWeights, Delta: false)],
                        2 => [new(layerAnimation, 0, fraction, layerWeight, boneWeights, Delta: true, Post: post)],
                        3 => [new(layerAnimation, 0, fraction, layerWeight, boneWeights, Delta: true, Post: !post,
                                  Locks: layerLocks ? subject.Locks : null)],
                        _ =>
                        [
                            new(overAnimation, 1, fraction, layerWeight, boneWeights, Delta: false),
                            new(layerAnimation, 1, fraction, 1f, boneWeights, Delta: true, Post: post),
                        ],
                    };

                    int barrel = scenario % 4 == 0 ? Math.Min(2, subject.Bones.Count - 1) : -1;
                    IReadOnlyList<float>? placement = scenario % 2 == 1
                        ? [0f, -1f, 0f, 10f, 1f, 0f, 0f, -4f, 0f, 0f, 1f, 2.5f]
                        : null;

                    Setup setup = new(
                        Sequence: animation,
                        Frame: frames / 2,
                        FrameFraction: fraction,
                        Layers: layers,
                        Chains: subject.Chains,
                        Locks: withLocks ? subject.Locks : [],
                        Controllers: subject.Controllers,
                        ControllerValues: subject.ControllerValues.Count > 0
                            ? [subject.ControllerValues[scenario % subject.ControllerValues.Count], 0.25f, 0.8f]
                            : [],
                        Barrel: barrel,
                        Placement: placement);

                    Compare(
                        $"{subject.Name} animation {animation}, fraction {fraction}, layer weight {layerWeight}, scenario {scenario}",
                        subject.Bones,
                        subject.Sampler,
                        setup,
                        masks[scenario % masks.Length],
                        preWritten: scenario % 6 == 5 ? [0, Math.Min(3, subject.Bones.Count - 1)] : [],
                        tally);

                    tally.Layers += layers.Length;
                    tally.Delta += layers.Count(layer => layer.Delta);
                    tally.Override += layers.Count(layer => !layer.Delta);
                    tally.LayerLocks += layerLocks && scenario % 5 == 3 ? 1 : 0;
                    tally.PartialMask += masks[scenario % masks.Length] != ~0 ? 1 : 0;
                    tally.PreWritten += scenario % 6 == 5 ? 1 : 0;
                }
            }
        }
    }

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

        AccumulatePathReference reference = new(
            bones, (sequence, frame, fraction, _) => referenceSampler.Sample(sequence, frame, fraction));
        SkeletonPose production = new(
            bones, (sequence, frame, fraction, _) => productionSampler.Sample(sequence, frame, fraction));

        setup.ApplyTo(reference);
        setup.ApplyTo(production);

        BoneAccessor want = new(bones.Count);
        BoneAccessor got = new(bones.Count);
        BoneBitList wantWritten = new(bones.Count);
        BoneBitList gotWritten = new(bones.Count);

        // A merged wearer's bones, as `alreadyWritten` would hold them: the same matrix on both sides.
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

        // The inert values the reference leaves out, checked rather than assumed.
        production.JiggleSource.ShouldBeNull();
        production.IkErrors.Count.ShouldBe(0);
        production.SolvedChains.ShouldBe(0);
        production.DuckJumpOffset.ShouldBe(0f);
        production.RootReflection.ShouldBeNull();
        (production.HeadScale, production.TorsoScale, production.HandScale).ShouldBe((1f, 1f, 1f));

        tally.Compared++;

        if (productionSampler.FirstWasDense)
        {
            tally.DenseBase++;
        }
        else
        {
            tally.OtherBase++;
        }

        tally.Locks += production.AppliedLocks > 0 ? 1 : 0;
        tally.Barrel += production.SpunBarrels > 0 ? 1 : 0;
        tally.Controllers += production.Controllers.Count > 0 && production.BoneControllers.Count > 0 ? 1 : 0;

        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (gotWritten.IsMarked(bone) != wantWritten.IsMarked(bone))
            {
                tally.Mismatch($"{label}: bone {bone} written {gotWritten.IsMarked(bone)}, want {wantWritten.IsMarked(bone)}");
                return;
            }

            if (!wantWritten.IsMarked(bone))
            {
                tally.Skipped++;
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

    /// <summary>
    /// A sampler that answers every call in the SAME list, as <c>StudioPoseWorkspace</c> does: a pose read after the
    /// next call has overwritten it reads the wrong bones.
    /// </summary>
    /// <param name="answer">The pose for a sequence, frame and fraction, and whether it arrives as a plain list.</param>
    /// <param name="bones">The skeleton's bone count, for the test's own reading of "dense".</param>
    private sealed class Sampler(
        Func<int, int, float, (IReadOnlyList<StudioBonePose> Pose, bool Listed)> answer, int bones)
    {
        private readonly List<StudioBonePose> _reused = [];
        private int _calls;

        public bool FirstWasDense { get; private set; }

        public IReadOnlyList<StudioBonePose> Sample(int sequence, int frame, float fraction)
        {
            (IReadOnlyList<StudioBonePose> pose, bool listed) = answer(sequence, frame, fraction);

            _reused.Clear();
            _reused.AddRange(pose);

            if (_calls++ == 0)
            {
                FirstWasDense = listed && IsDense(_reused, bones);
            }

            // A list the shortcut cannot see through, for the shape that must take the old path for that reason.
            return listed ? _reused : new ReadOnlyCollection<StudioBonePose>([.. _reused]);
        }

        private static bool IsDense(List<StudioBonePose> pose, int expected)
        {
            if (pose.Count != expected)
            {
                return false;
            }

            for (int entry = 0; entry < pose.Count; entry++)
            {
                if (pose[entry].Bone != entry)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Everything a build reads off the entity, applied the same way to both sides.</summary>
    private sealed record Setup(
        int Sequence = 0,
        int Frame = 0,
        float FrameFraction = 0f,
        IReadOnlyList<PoseLayer>? Layers = null,
        IReadOnlyList<StudioIkChain>? Chains = null,
        IReadOnlyList<StudioIkLock>? Locks = null,
        IReadOnlyList<StudioBoneController>? Controllers = null,
        IReadOnlyList<float>? ControllerValues = null,
        int Barrel = -1,
        IReadOnlyList<float>? Placement = null)
    {
        private static readonly (float X, float Y, float Z, float W) Spun = (0.1f, 0.2f, -0.3f, 0.927f);

        public void ApplyTo(AccumulatePathReference target)
        {
            target.Sequence = Sequence;
            target.Frame = Frame;
            target.FrameFraction = FrameFraction;
            target.Layers = Layers ?? [];
            target.IkChains = Chains ?? [];
            target.Locks = Locks ?? [];
            target.Controllers = Controllers ?? [];
            target.BoneControllers = ControllerValues ?? [];
            target.BarrelBone = Barrel;
            target.BarrelRotation = Spun;
            target.EntityTransform = Placement;
        }

        public void ApplyTo(SkeletonPose target)
        {
            target.Sequence = Sequence;
            target.Frame = Frame;
            target.FrameFraction = FrameFraction;
            target.Layers = Layers ?? [];
            target.IkChains = Chains ?? [];
            target.Locks = Locks ?? [];
            target.Controllers = Controllers ?? [];
            target.BoneControllers = ControllerValues ?? [];
            target.BarrelBone = Barrel;
            target.BarrelRotation = Spun;
            target.EntityTransform = Placement;
        }
    }

    private sealed record Subject(
        string Name,
        IReadOnlyList<StudioBone> Bones,
        byte[] File,
        IReadOnlyList<StudioBone> AnimationBones,
        int[]? Remap,
        IReadOnlyList<StudioIkChain> Chains,
        IReadOnlyList<StudioIkLock> Locks,
        IReadOnlyList<StudioBoneController> Controllers,
        IReadOnlyList<float> ControllerValues)
    {
        public Sampler Sampler() => new((animation, frame, fraction) => (Pose(animation, frame, fraction), true), Bones.Count);

        /// <summary>
        /// One animation as production samples it: the frame, renumbered onto the skeleton, and when the fraction is
        /// above zero the blend toward the next frame over the whole skeleton (dense).
        /// </summary>
        public IReadOnlyList<StudioBonePose> Pose(int animation, int frame, float fraction)
        {
            IReadOnlyList<StudioBonePose> at = Renumbered(StudioAnimation.Pose(File, AnimationBones, animation, frame));

            if (fraction <= 0f || at.Count == 0)
            {
                return at;
            }

            IReadOnlyList<StudioBonePose> next =
                Renumbered(StudioAnimation.Pose(File, AnimationBones, animation, frame + 1));

            return next.Count == 0
                ? at
                : StudioPoseBlend.Blend(Bones, at, next, fraction, StudioAnimation.IsDelta(File, animation));
        }

        private IReadOnlyList<StudioBonePose> Renumbered(IReadOnlyList<StudioBonePose> pose) =>
            Remap is null
                ? pose
                :
                [
                    .. pose
                        .Where(moved => moved.Bone >= 0 && moved.Bone < Remap.Length && Remap[moved.Bone] >= 0)
                        .Select(moved => moved with { Bone = Remap[moved.Bone] }),
                ];
    }

    private sealed class Tally
    {
        public int Compared { get; set; }

        public int Mismatches { get; private set; }

        public string? First { get; private set; }

        public int DenseBase { get; set; }

        public int OtherBase { get; set; }

        public int Layers { get; set; }

        public int Delta { get; set; }

        public int Override { get; set; }

        public int Locks { get; set; }

        public int LayerLocks { get; set; }

        public int Controllers { get; set; }

        public int Barrel { get; set; }

        public int PartialMask { get; set; }

        public int PreWritten { get; set; }

        public int Skipped { get; set; }

        public HashSet<string> SeenShapes { get; } = [];

        public void Mismatch(string what)
        {
            Mismatches++;
            First ??= what;
        }

        public void AssertReached()
        {
            DenseBase.ShouldBeGreaterThan(0, "the control: a dense base pose was compared");
            OtherBase.ShouldBeGreaterThan(0, "the control: a sparse base pose was compared");
            Delta.ShouldBeGreaterThan(0, "the control: a delta layer was compared");
            Override.ShouldBeGreaterThan(0, "the control: an overriding layer was compared");
            Locks.ShouldBeGreaterThan(0, "the control: a build applied IK locks");
            LayerLocks.ShouldBeGreaterThan(0, "the control: a layer carried its own locks");
            Controllers.ShouldBeGreaterThan(0, "the control: a build ran bone controllers");
            Barrel.ShouldBeGreaterThan(0, "the control: a build spun a barrel");
            PartialMask.ShouldBeGreaterThan(0, "the control: a build used a mask narrower than everything");
            PreWritten.ShouldBeGreaterThan(0, "the control: a build found bones already written");
            Skipped.ShouldBeGreaterThan(0, "the control: some bone was skipped by the mask or the merge");
        }

        public override string ToString() =>
            $"dense base {DenseBase}, other base {OtherBase}, layers {Layers} (delta {Delta}, override {Override}), " +
            $"locks applied {Locks}, layer locks {LayerLocks}, controllers {Controllers}, barrel {Barrel}, " +
            $"partial mask {PartialMask}, pre-written {PreWritten}, skipped bones {Skipped}";
    }

    private const int SyntheticBones = 8;

    /// <summary>Eight bones in a chain; bone 5 fixed-aligned; bone 6 used by nothing, so a controller on it is refused.</summary>
    private static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, SyntheticBones).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, (2f * bone) + 0.5f, -bone * 0.25f),
            Rotation: (0.1f * bone, 0.2f, 0.3f, 0.9f),
            PoseToBone: default,
            Flags: (bone == 5 ? StudioBoneFlags.FixedAlignment : 0) | (bone == 6 ? 0 : StudioBoneFlags.UsedByAnything))),
    ];

    /// <summary>Every shape a pose can take, and whether it arrives as a list the shortcut can see through.</summary>
    private static (string Shape, StudioBonePose[] Pose, bool Listed)[] Shapes(int bones)
    {
        StudioBonePose[] dense =
        [
            .. Enumerable.Range(0, bones).Select(bone => new StudioBonePose(
                bone,
                (bone == 2 ? float.NaN : 1.5f * bone, bone == 3 ? -0f : -1f, bone == 4 ? 1e-40f : 2e6f),
                (bone == 6 ? -0f : 0.15f, -0.5f * bone, bone == 1 ? float.NaN : 0.25f, bone % 2 == 0 ? -0.8f : 0.8f))),
        ];

        return
        [
            ("dense", dense, true),
            ("dense behind an interface", dense, false),
            ("sparse", [dense[1], dense[4], dense[6]], true),
            ("out of order", [.. dense.Reverse()], true),
            ("a bone named twice", [.. dense[..3], dense[2] with { Position = (9f, 9f, 9f) }, .. dense[4..]], true),
            ("one too many", [.. dense, new StudioBonePose(bones, (1f, 1f, 1f), (0f, 0f, 0f, 1f))], true),
            ("one too few", [.. dense[..^1]], true),
            ("a negative bone", [.. dense[..^1], dense[^1] with { Bone = -1 }], true),
            ("empty", [], true),
        ];
    }

    /// <summary>The first sequences of the animation model that lock a chain, as a lock list to hold.</summary>
    private static IReadOnlyList<StudioIkLock> LocksOf(byte[] animations)
    {
        int sequences = StudioSequences.Read(animations).Count;

        for (int sequence = 0; sequence < sequences; sequence++)
        {
            if (StudioIkLocks.Read(animations, sequence) is { Count: > 0 } locks)
            {
                return locks;
            }
        }

        return [];
    }

    private static VpkArchive[] Archives() =>
    [
        .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(GameInstall.Find)
            .OfType<string>()
            .Select(VpkArchive.Open),
    ];

    private static byte[]? Read(VpkArchive[] archives, string path)
    {
        foreach (VpkArchive archive in archives)
        {
            if (archive.ReadFile(path) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
