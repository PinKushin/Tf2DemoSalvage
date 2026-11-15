using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="StudioAnimation.PoseBetweenInto"/> against <see cref="FramePairReference"/>, the engine's frame pair
/// transcribed apart from production (B620): the same bits, every float. Each outcome is reached and counted as a
/// control: empty, the frame alone, and the pair, inside one section, across a section's end and into the trailing one.
/// </summary>
/// <remarks>
/// **The engine is the answer, bit for bit.** The comparison is on each float's bits rather than through
/// <c>StudioBonePose.Equals</c>, which treats <c>-0</c> and <c>+0</c> as equal. Until B620 the reference was the
/// two-pass read the single pass replaced; it now transcribes <c>CalcBoneQuaternion</c>, <c>CalcBonePosition</c> and the
/// two-key <c>ExtractAnimValue</c>, which read the frame after from the frame's own section and blend inside each track.
///
/// The synthetic model is built to reach every branch of the old reader: both packed rotations, a raw position, the
/// run-length walk across blocks with <c>valid &lt; total</c>, a terminator, a missing channel, per-track and
/// whole-animation deltas, a fixed-alignment bone, a bone named twice, sections whose pair straddles a boundary and the
/// trailing last-frame section, a trailing section held in an external block (so the frame after the last regular one
/// reads nothing), sections sharing one chain, a one-frame animation, a blocked one and an empty one. The
/// real test then runs every player's animations both ways, unremapped and remapped onto its player skeleton, which is
/// the shape production calls.
/// </remarks>
public sealed class FramePairDifferentialTests
{
    /// <remarks>Either side of the engine's 0.001 by one float, so the threshold is pinned to the bit (B614 review).</remarks>
    private static readonly float[] Fractions =
        [-0.1f, 0f, 1e-6f, 0.0005f, MathF.BitDecrement(0.001f), 0.001f, MathF.BitIncrement(0.001f), 0.0015f, 0.25f, 0.5f,
            0.999f, 1f, 1.5f, float.NaN];

    private static readonly float[] RealFractions = [0f, 1e-6f, 0.5f, 1f];

    [Test]
    public void PoseBetweenInto_EveryCaseOfASyntheticModel_IsBitIdenticalToTheEnginesSample()
    {
        byte[] model = SyntheticStudioAnimation.Synthetic();
        List<StudioBone> bones = SyntheticStudioAnimation.Skeleton();
        int[]?[] remaps = [null, [3, -1, 0, 0, 7, 9, 2, 1], [1, 0]];

        StudioPoseWorkspace workspace = new();
        FramePairReference.Lists lists = new();
        Tally tally = new();

        int animations = StudioAnimation.Count(model);

        animations.ShouldBe(10, "the control: every synthetic animation is declared");

        foreach (int[]? remap in remaps)
        {
            for (int animation = -1; animation <= animations; animation++)
            {
                int frames = StudioAnimation.Frames(model, animation);

                foreach (int frame in FramesOf(frames, everyFrame: true))
                {
                    tally.CountSections(model, animation, frame);

                    foreach (float fraction in Fractions)
                    {
                        Compare(model, bones, remap, animation, frame, fraction, workspace, lists, tally);
                    }
                }
            }
        }

        tally.Mismatches.ShouldBe(0, tally.First);
        tally.Dense.ShouldBeGreaterThan(0, "the control: a blend was compared");
        tally.Sparse.ShouldBeGreaterThan(0, "the control: a single frame was compared");
        tally.Empty.ShouldBeGreaterThan(0, "the control: an empty pose was compared");
        tally.Straddling.ShouldBeGreaterThan(0, "the control: a pair across a section boundary was compared");
        tally.IntoTrailing.ShouldBeGreaterThan(0, "the control: a pair into the trailing last-frame section was compared");
        tally.WithinOne.ShouldBeGreaterThan(0, "the control: a pair inside one section was compared");
    }

    /// <remarks>
    /// **Valve blends two keys only when `s > 0.001f`** (B614): rotation at `bone_setup.cpp:408`, position at `:525`; at or
    /// below it each track reads the frame alone. So at a thousandth or less the pose is the frame's, the same list a
    /// fraction of zero gives, and just above it the blend. The case is the first synthetic frame whose blend at a half
    /// differs from the frame alone, the control that the frame and the next differ at all.
    /// </remarks>
    [TestCase(0.0005f, true, TestName = "PoseBetweenInto_AtHalfAThousandth_ReadsTheFrameAlone")]
    [TestCase(0.001f, true, TestName = "PoseBetweenInto_AtAThousandth_ReadsTheFrameAlone")]
    [TestCase(0.002f, false, TestName = "PoseBetweenInto_AtTwoThousandths_Blends")]
    public void PoseBetweenInto_AroundAThousandth_BlendsOnlyAboveIt(float fraction, bool alone)
    {
        byte[] model = SyntheticStudioAnimation.Synthetic();
        List<StudioBone> bones = SyntheticStudioAnimation.Skeleton();
        StudioPoseWorkspace workspace = new();
        (int animation, int frame) = (-1, -1);

        for (int each = 0; each < StudioAnimation.Count(model) && animation < 0; each++)
        {
            for (int at = 0; at + 1 < StudioAnimation.Frames(model, each); at++)
            {
                List<StudioBonePose> single = [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(bones), each, at, 0f, workspace, 0)];
                List<StudioBonePose> half = [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(bones), each, at, 0.5f, workspace, 0)];

                if (single.Count > 0 && !half.SequenceEqual(single))
                {
                    (animation, frame) = (each, at);
                    break;
                }
            }
        }

        animation.ShouldBeGreaterThanOrEqualTo(0, "the control: some frame and the next differ");

        List<StudioBonePose> frameAlone = [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(bones), animation, frame, 0f, workspace, 0)];
        List<StudioBonePose> asked = [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(bones), animation, frame, fraction, workspace, 0)];

        asked.SequenceEqual(frameAlone).ShouldBe(alone);
    }

    [Test]
    public void PoseBetweenInto_EveryPlayersAnimations_AreBitIdenticalToTheEnginesSample()
    {
        string[] classes = ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];
        StudioPoseWorkspace workspace = new();
        FramePairReference.Lists lists = new();
        Tally tally = new();
        int models = 0;
        StringBuilder read = new();

        GameInstall.Require();

        VpkArchive[] archives = Archives();

        foreach (string name in classes)
        {
            if (Read(archives, $"models/player/{name}_animations.mdl") is not { } model ||
                Read(archives, $"models/player/{name}.mdl") is not { } player)
            {
                continue;
            }

            models++;

            int before = tally.Compared;

            CompareEveryAnimation(model, player, workspace, lists, tally);
            read.Append(name).Append(' ').Append(tally.Compared - before).Append("; ");
        }

        TestContext.Out.WriteLine(
            $"compared {tally.Compared} ({read}) dense {tally.Dense}, sparse {tally.Sparse}, empty {tally.Empty}; " +
            $"pairs within a section {tally.WithinOne}, straddling {tally.Straddling}, into the trailing section " +
            $"{tally.IntoTrailing}");

        models.ShouldBe(classes.Length, "the control: every class's animations and skeleton were read");
        tally.Mismatches.ShouldBe(0, tally.First);
        tally.Dense.ShouldBeGreaterThan(10_000, "the control: real blends were compared, not a handful");
    }

    /// <remarks>
    /// **Every other animated model the existing fixtures name**: the player models' own animations, the first-person
    /// arms (the measured scene is first person) and their included animation models remapped onto them, the one old
    /// viewmodel still shipped, and the animated props and buildables. The fixtures also name
    /// <c>v_rocketlauncher</c>, <c>v_scattergun</c>, <c>v_watch</c> and <c>v_knife</c>, which the current install no
    /// longer carries. A file missing from the install fails the control rather than
    /// quietly shrinking what was compared.
    /// </remarks>
    [Test]
    public void PoseBetweenInto_EveryOtherAnimatedModelTheFixturesName_IsBitIdenticalToTheEnginesSample()
    {
        string[] classes = ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];
        (string Model, string? Base)[] subjects =
        [
            .. classes.Select(name => ($"models/player/{name}.mdl", (string?)null)),
            .. classes.Select(name => ($"models/weapons/c_models/c_{name}_arms.mdl", (string?)null)),
            .. classes.Select(name => (
                $"models/weapons/c_models/c_{name}_animations.mdl",
                (string?)$"models/weapons/c_models/c_{name}_arms.mdl")),
            ("models/weapons/v_models/v_rocketlauncher_soldier.mdl", null),
            ("models/props_gameplay/resupply_locker.mdl", null),
            ("models/props_gameplay/door_slide_door.mdl", null),
            ("models/props_gameplay/cap_point_base.mdl", null),
            ("models/props_well/main_entrance_door.mdl", null),
            ("models/buildables/sentry3.mdl", null),
            ("models/effects/cappoint_hologram.mdl", null),
            ("models/items/medkit_small.mdl", null),
        ];

        StudioPoseWorkspace workspace = new();
        FramePairReference.Lists lists = new();
        Tally tally = new();
        List<string> missing = [];
        StringBuilder read = new();

        GameInstall.Require();

        VpkArchive[] archives = Archives();

        foreach ((string path, string? basePath) in subjects)
        {
            byte[]? baseModel = basePath is null ? null : Read(archives, basePath);

            if (Read(archives, path) is not { } model || (basePath is not null && baseModel is null))
            {
                missing.Add(path);
                continue;
            }

            int before = tally.Compared;

            CompareEveryAnimation(model, baseModel, workspace, lists, tally);
            read.Append(path[(path.LastIndexOf('/') + 1)..]).Append(' ').Append(tally.Compared - before).Append("; ");
        }

        TestContext.Out.WriteLine(
            $"compared {tally.Compared} ({read}) dense {tally.Dense}, sparse {tally.Sparse}, empty {tally.Empty}; " +
            $"pairs within a section {tally.WithinOne}, straddling {tally.Straddling}, into the trailing section " +
            $"{tally.IntoTrailing}");

        missing.ShouldBeEmpty("the control: every named model was read from the install");
        tally.Mismatches.ShouldBe(0, tally.First);
        tally.Dense.ShouldBeGreaterThan(1_000, "the control: real blends were compared, not a handful");
    }

    /// <summary>Every animation of <paramref name="model"/>, unremapped and, given a base, remapped onto it.</summary>
    private static void CompareEveryAnimation(
        byte[] model, byte[]? baseModel, StudioPoseWorkspace workspace, FramePairReference.Lists lists, Tally tally)
    {
        IReadOnlyList<StudioBone> bones = StudioBones.Read(model);
        int[]?[] remaps = baseModel is null ? [null] : [null, StudioBones.Remap(bones, StudioBones.Read(baseModel))];

        foreach (int[]? remap in remaps)
        {
            for (int animation = 0; animation < StudioAnimation.Count(model); animation++)
            {
                foreach (int frame in FramesOf(StudioAnimation.Frames(model, animation), everyFrame: false))
                {
                    tally.CountSections(model, animation, frame);

                    foreach (float fraction in RealFractions)
                    {
                        Compare(model, bones, remap, animation, frame, fraction, workspace, lists, tally);
                    }
                }
            }
        }
    }

    /// <remarks>
    /// **Warmed up, then measured twice, and the SECOND measurement is the one asserted.** The first calls grow the
    /// workspace's lists, which is the one allocation the design allows. After that, tiered compilation can still
    /// patch a call site or allocate a stub on this thread the first time a method is promoted, and that has nothing
    /// to do with posing. A pass after the warm-up that is itself followed by a clean pass separates the two.
    /// </remarks>
    [Test]
    public void PoseBetweenInto_ASecondCallIntoTheSameWorkspace_AllocatesNothing()
    {
        const int warmUp = 64;

        byte[] model = SyntheticStudioAnimation.Synthetic();
        List<StudioBone> bones = SyntheticStudioAnimation.Skeleton();
        StudioRemap own = StudioRemap.Own(bones);
        StudioRemap remap = StudioRemap.Explicit(bones, [3, -1, 0, 0, 7, 9, 2, 1]);
        StudioPoseWorkspace workspace = new();

        for (int pass = 0; pass < warmUp; pass++)
        {
            ThreePaths(model, own, remap, workspace);
        }

        long first = GC.GetAllocatedBytesForCurrentThread();
        ThreePaths(model, own, remap, workspace);
        long second = GC.GetAllocatedBytesForCurrentThread();
        List<StudioBonePose> plain = ThreePaths(model, own, remap, workspace);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - second;

        TestContext.Out.WriteLine($"first measured pass {second - first} bytes, second {allocated}");
        allocated.ShouldBe(0L);
        plain.Count.ShouldBe(bones.Count, "the control: the call blended, so the whole path ran");
    }

    /// <summary>The pair in one section, through a remap, and across a section edge.</summary>
    private static List<StudioBonePose> ThreePaths(
        byte[] model, StudioRemap own, StudioRemap remap, StudioPoseWorkspace workspace)
    {
        List<StudioBonePose> plain = StudioAnimation.PoseBetweenInto(model, own, 0, 2, 0.5f, workspace, 0);

        StudioAnimation.PoseBetweenInto(model, remap, 0, 2, 0.5f, workspace, 1);
        StudioAnimation.PoseBetweenInto(model, own, 2, 3, 0.5f, workspace, 2);

        return plain;
    }

    private static void Compare(
        byte[] model,
        IReadOnlyList<StudioBone> bones,
        int[]? remap,
        int animation,
        int frame,
        float fraction,
        StudioPoseWorkspace workspace,
        FramePairReference.Lists lists,
        Tally tally)
    {
        int slot = tally.Compared % 3;

        IReadOnlyList<StudioBonePose> want =
            FramePairReference.PoseBetween(model, bones, remap, animation, frame, fraction, lists);
        StudioRemap group = remap is null ? StudioRemap.Own(bones) : StudioRemap.Explicit(bones, remap);
        IReadOnlyList<StudioBonePose> got =
            StudioAnimation.PoseBetweenInto(model, group, animation, frame, fraction, workspace, slot);

        tally.Compared++;

        // Classified by which branch of the engine's sample ran: none, the frame alone, or the pair.
        if (want.Count == 0)
        {
            tally.Empty++;
        }
        else if (fraction > 0.001f)
        {
            tally.Dense++;
        }
        else
        {
            tally.Sparse++;
        }

        if (Diff(got, want) is { } difference)
        {
            tally.Mismatches++;
            tally.First ??=
                $"animation {animation}, frame {frame}, fraction {fraction}, remap " +
                $"{(remap is null ? "none" : string.Join(',', remap))}: {difference}";
        }
    }

    private static string? Diff(IReadOnlyList<StudioBonePose> got, IReadOnlyList<StudioBonePose> want)
    {
        if (got.Count != want.Count)
        {
            return $"count {got.Count}, want {want.Count}";
        }

        for (int entry = 0; entry < want.Count; entry++)
        {
            StudioBonePose g = got[entry];
            StudioBonePose w = want[entry];

            float[] gotBits =
                [g.Position.X, g.Position.Y, g.Position.Z, g.Rotation.X, g.Rotation.Y, g.Rotation.Z, g.Rotation.W];
            float[] wantBits =
                [w.Position.X, w.Position.Y, w.Position.Z, w.Rotation.X, w.Rotation.Y, w.Rotation.Z, w.Rotation.W];

            if (g.Bone != w.Bone ||
                !gotBits.Select(BitConverter.SingleToInt32Bits).SequenceEqual(wantBits.Select(BitConverter.SingleToInt32Bits)))
            {
                return $"entry {entry}: {g}, want {w}";
            }
        }

        return null;
    }

    /// <summary>The frames to sample: every one around the ends and, for the real models, a stride between.</summary>
    private static SortedSet<int> FramesOf(int frames, bool everyFrame)
    {
        SortedSet<int> picked = [-1, 0, 1, frames - 2, frames - 1, frames, frames + 1];

        int step = everyFrame ? 1 : Math.Max(1, frames / 8);

        for (int frame = 0; frame < frames; frame += step)
        {
            picked.Add(frame);
        }

        // Around every multiple of a common section length, so a real sectioned animation's boundaries are read.
        for (int edge = 0; !everyFrame && edge < frames; edge += 30)
        {
            picked.Add(edge - 1);
            picked.Add(edge);
        }

        if (everyFrame)
        {
            picked.Add(-2);
            picked.Add(int.MaxValue);
        }

        return picked;
    }

    private sealed class Tally
    {
        public int Compared { get; set; }

        public int Dense { get; set; }

        public int Sparse { get; set; }

        public int Empty { get; set; }


        public int Mismatches { get; set; }

        public string? First { get; set; }

        public int WithinOne { get; private set; }

        public int Straddling { get; private set; }

        public int IntoTrailing { get; private set; }

        /// <summary>Which sections <paramref name="frame"/> and the frame after it read from, as the reader resolves them.</summary>
        public void CountSections(byte[] model, int animation, int frame)
        {
            (int frames, int sectionFrames, int sections, _, _) = StudioAnimation.Sectioning(model, animation);

            if (frames <= 0 || sectionFrames <= 0 || sections <= 0)
            {
                return;
            }

            int first = StudioAnimation.Section(frames, sectionFrames, Math.Clamp(frame, 0, frames - 1)).Section;
            int second = StudioAnimation.Section(frames, sectionFrames, Math.Clamp(frame + 1, 0, frames - 1)).Section;

            if (first == second)
            {
                WithinOne++;
            }
            else if (frames > sectionFrames && second == (frames / sectionFrames) + 1)
            {
                IntoTrailing++;
            }
            else
            {
                Straddling++;
            }
        }
    }

    /// <summary>The game's model archives, each opened once: an open reads the whole directory tree.</summary>
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
