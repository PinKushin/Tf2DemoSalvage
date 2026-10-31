using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="StudioPoseBlend.BlendInto"/> against <see cref="DensePoseBlendReference"/>, the expand-then-blend as it
/// stood before the dense-pose change: the same bits, every float, for every shape of input.
/// </summary>
/// <remarks>
/// **A pure cost reduction, so the only correct answer is the old one, bit for bit.** A grid blend's inputs are
/// already dense (one entry per bone, in bone order) whenever the frame blend ran, and re-expanding them reproduces
/// them; Valve's <c>BlendBones</c> (<c>bone_setup.cpp:1531</c>) blends two dense arrays with no expand step. The change
/// may only skip work, never move a bit, so the comparison is on each float's bits (a <c>-0</c> or a NaN payload
/// would pass <c>Equals</c>).
///
/// **Every shape the shortcut must refuse is fed in as a control**: sparse, out of order, a bone named twice, one
/// entry too many or too few, empty, and a dense list behind an interface the blend cannot see through. Each class
/// is counted, so a run where everything quietly took the old path cannot pass.
/// </remarks>
public sealed class DensePoseBlendDifferentialTests
{
    private static readonly float[] Weights = [-0.1f, 0f, 1e-6f, 0.25f, 0.5f, 1f, 1.5f, float.NaN];

    private static readonly float[] RealWeights = [0f, 1e-6f, 0.5f, 1f, 1.5f, float.NaN];

    [Test]
    public void BlendInto_EveryShapeOfASyntheticInput_IsBitIdenticalToTheExpandingReference()
    {
        List<StudioBone> bones = Skeleton();
        Dictionary<string, int> reached = [];
        Tally tally = new();
        List<StudioBonePose> into = [];
        List<StudioBonePose> scratch = [];

        (string Shape, IReadOnlyList<StudioBonePose> Pose)[] shapes = Shapes(bones.Count);

        foreach ((string firstShape, IReadOnlyList<StudioBonePose> first) in shapes)
        {
            foreach ((string secondShape, IReadOnlyList<StudioBonePose> second) in shapes)
            {
                foreach (float s in Weights)
                {
                    foreach (bool additive in new[] { false, true })
                    {
                        string label = $"{firstShape} with {secondShape}";

                        reached[firstShape] = reached.GetValueOrDefault(firstShape) + 1;
                        Compare(bones, first, second, s, additive, into, scratch, tally, label);
                    }
                }
            }
        }

        tally.Mismatches.ShouldBe(0, tally.First);

        foreach ((string shape, _) in shapes)
        {
            reached.GetValueOrDefault(shape).ShouldBeGreaterThan(0, $"the control: the {shape} input was compared");
        }

        tally.BothDense.ShouldBeGreaterThan(0, "the control: two dense inputs were compared");
        tally.OneDense.ShouldBeGreaterThan(0, "the control: one dense and one other input were compared");
        tally.NeitherDense.ShouldBeGreaterThan(0, "the control: two inputs that need expanding were compared");
    }

    /// <remarks>
    /// **The shape production calls**: each player's own animations sampled between two frames (dense) and on a frame
    /// (sparse), paired as two grid corners, blended, then blended again with a third corner onto that result, which
    /// is the grid's second blend.
    /// </remarks>
    [Test]
    public void BlendInto_EveryPlayersAnimationsAsGridCorners_AreBitIdenticalToTheExpandingReference()
    {
        string[] classes = ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];
        StudioPoseWorkspace workspace = new();
        Tally tally = new();
        List<StudioBonePose> into = [];
        List<StudioBonePose> onto = [];
        List<StudioBonePose> scratch = [];
        int models = 0;
        StringBuilder read = new();

        GameInstall.Require();

        VpkArchive[] archives = Archives();

        foreach (string name in classes)
        {
            if (Read(archives, $"models/player/{name}_animations.mdl") is not { } model)
            {
                continue;
            }

            models++;

            int before = tally.Compared;
            IReadOnlyList<StudioBone> bones = StudioBones.Read(model);
            int count = StudioAnimation.Count(model);

            for (int animation = 0; animation + 2 < count; animation += 2)
            {
                bool additive = StudioAnimation.IsDelta(model, animation);
                int frames = StudioAnimation.Frames(model, animation);

                foreach (int frame in new[] { 0, frames / 2 })
                {
                    foreach (float fraction in new[] { 0f, 0.5f })
                    {
                        List<StudioBonePose> first =
                            StudioAnimation.PoseBetweenInto(model, bones, null, animation, frame, fraction, workspace, 0);
                        List<StudioBonePose> second =
                            StudioAnimation.PoseBetweenInto(model, bones, null, animation + 1, frame, fraction, workspace, 1);
                        List<StudioBonePose> third =
                            StudioAnimation.PoseBetweenInto(model, bones, null, animation + 2, frame, fraction, workspace, 2);

                        foreach (float s in RealWeights)
                        {
                            string label = $"{name} animation {animation}, frame {frame}, fraction {fraction}, s {s}";

                            IReadOnlyList<StudioBonePose> pair =
                                Compare(bones, first, second, s, additive, into, scratch, tally, label);

                            Compare(bones, pair, third, 1f - s, additive, onto, scratch, tally, label + ", second blend");
                        }
                    }
                }
            }

            read.Append(name).Append(' ').Append(tally.Compared - before).Append("; ");
        }

        TestContext.Out.WriteLine(
            $"compared {tally.Compared} ({read}) both dense {tally.BothDense}, one dense {tally.OneDense}, neither " +
            $"{tally.NeitherDense}");

        models.ShouldBe(classes.Length, "the control: every class's animations were read");
        tally.Mismatches.ShouldBe(0, tally.First);
        tally.BothDense.ShouldBeGreaterThan(10_000, "the control: real dense corners were compared, not a handful");
        tally.NeitherDense.ShouldBeGreaterThan(1_000, "the control: real sparse corners were compared too");
    }

    /// <remarks>Warmed up first, as <c>FramePairDifferentialTests</c> does: the first calls size the lists.</remarks>
    [Test]
    public void BlendInto_ASecondBlendOfDenseInputs_AllocatesNothing()
    {
        List<StudioBone> bones = Skeleton();
        List<StudioBonePose> first = [.. Dense(bones.Count, 1f)];
        List<StudioBonePose> second = [.. Dense(bones.Count, 2f)];
        List<StudioBonePose> into = [];
        List<StudioBonePose> scratch = [];

        for (int pass = 0; pass < 64; pass++)
        {
            StudioPoseBlend.BlendInto(bones, first, second, 0.5f, additive: false, into, scratch);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        StudioPoseBlend.BlendInto(bones, first, second, 0.5f, additive: false, into, scratch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        into.Count.ShouldBe(bones.Count, "the control: the blend ran");
    }

    private static List<StudioBonePose> Compare(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> first,
        IReadOnlyList<StudioBonePose> second,
        float s,
        bool additive,
        List<StudioBonePose> into,
        List<StudioBonePose> scratch,
        Tally tally,
        string label)
    {
        StudioBonePose[] want = DensePoseBlendReference.Blend(bones, first, second, s, additive);
        bool firstDense = IsDense(first, bones.Count);
        bool secondDense = IsDense(second, bones.Count);

        IReadOnlyList<StudioBonePose> got =
            StudioPoseBlend.BlendInto(bones, first, second, s, additive, into, scratch);

        tally.Compared++;

        if (firstDense && secondDense)
        {
            tally.BothDense++;
        }
        else if (firstDense || secondDense)
        {
            tally.OneDense++;
        }
        else
        {
            tally.NeitherDense++;
        }

        if (Diff(got, want) is { } difference)
        {
            tally.Mismatches++;
            tally.First ??= $"{label}, additive {additive}: {difference}";
        }

        return into;
    }

    /// <summary>The test's own reading of "dense", from the list as the test sees it: one entry per bone, in order.</summary>
    private static bool IsDense(IReadOnlyList<StudioBonePose> pose, int bones)
    {
        if (pose.Count != bones || pose is not (List<StudioBonePose> or StudioBonePose[]))
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

    private static string? Diff(IReadOnlyList<StudioBonePose> got, StudioBonePose[] want)
    {
        if (got.Count != want.Length)
        {
            return $"count {got.Count}, want {want.Length}";
        }

        for (int entry = 0; entry < want.Length; entry++)
        {
            if (!SameBits(got[entry], want[entry]))
            {
                return $"entry {entry}: {got[entry]}, want {want[entry]}";
            }
        }

        return null;
    }

    private static bool SameBits(StudioBonePose g, StudioBonePose w) =>
        g.Bone == w.Bone &&
        Bits(g.Position.X) == Bits(w.Position.X) && Bits(g.Position.Y) == Bits(w.Position.Y) &&
        Bits(g.Position.Z) == Bits(w.Position.Z) && Bits(g.Rotation.X) == Bits(w.Rotation.X) &&
        Bits(g.Rotation.Y) == Bits(w.Rotation.Y) && Bits(g.Rotation.Z) == Bits(w.Rotation.Z) &&
        Bits(g.Rotation.W) == Bits(w.Rotation.W);

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    /// <summary>Eight bones, each its own rest; bone 5 fixed-aligned.</summary>
    private static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, 8).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, (2f * bone) + 0.5f, -bone * 0.25f),
            Rotation: (0.1f * bone, 0.2f, 0.3f, 0.9f),
            PoseToBone: default,
            Flags: bone == 5 ? StudioBoneFlags.FixedAlignment : 0)),
    ];

    /// <summary>One entry per bone in order, with awkward values: NaN, a negative zero, a denormal, a large one.</summary>
    private static StudioBonePose[] Dense(int bones, float seed) =>
    [
        .. Enumerable.Range(0, bones).Select(bone => new StudioBonePose(
            bone,
            (bone == 2 ? float.NaN : seed * bone, bone == 3 ? -0f : -seed, bone == 4 ? 1e-40f : seed * 1e6f),
            (bone == 6 ? -0f : 0.1f * seed, -0.5f * bone, bone == 1 ? float.NaN : 0.25f, bone % 2 == 0 ? -0.8f : 0.8f))),
    ];

    /// <summary>Every shape an input can take: the ones the shortcut takes and every one it must refuse.</summary>
    private static (string Shape, IReadOnlyList<StudioBonePose> Pose)[] Shapes(int bones)
    {
        StudioBonePose[] dense = Dense(bones, 1.5f);
        StudioBonePose[] other = Dense(bones, -3f);

        return
        [
            ("dense array", dense),
            ("dense list", (List<StudioBonePose>)[.. other]),
            ("dense behind an interface", new ReadOnlyCollection<StudioBonePose>([.. other])),
            ("sparse", (List<StudioBonePose>)[other[1], other[4], other[6]]),
            ("out of order", (List<StudioBonePose>)[.. other.Reverse()]),
            ("a bone named twice", (List<StudioBonePose>)[.. other[..3], other[2] with { Position = (9f, 9f, 9f) }, .. other[4..]]),
            ("one too many", (List<StudioBonePose>)[.. other, new StudioBonePose(bones, (1f, 1f, 1f), (0f, 0f, 0f, 1f))]),
            ("one too few", (List<StudioBonePose>)[.. other[..^1]]),
            ("a negative bone", (List<StudioBonePose>)[.. other[..^1], other[^1] with { Bone = -1 }]),
            ("empty", (List<StudioBonePose>)[]),
        ];
    }

    private sealed class Tally
    {
        public int Compared { get; set; }

        public int BothDense { get; set; }

        public int OneDense { get; set; }

        public int NeitherDense { get; set; }

        public int Mismatches { get; set; }

        public string? First { get; set; }
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
