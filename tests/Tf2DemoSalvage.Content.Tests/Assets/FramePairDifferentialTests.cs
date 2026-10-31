using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="StudioAnimation.PoseBetweenInto"/> against <see cref="FramePairReference"/>, the two-pass frame pair as it
/// stood before the single-pass rewrite: the same bits, every float, on every case either path can take.
/// </summary>
/// <remarks>
/// **A pure cost reduction, so the only correct answer is the old one, bit for bit.** The comparison is on each float's
/// bits rather than through <c>StudioBonePose.Equals</c>, which treats <c>-0</c> and <c>+0</c> as equal and would let a
/// merged Euler conversion through (a zero rotation scale turns one short into <c>+0</c> and another into <c>-0</c>).
///
/// The synthetic model is built to reach every branch of the old reader: both packed rotations, a raw position, the
/// run-length walk across blocks with <c>valid &lt; total</c>, a terminator, a missing channel, per-track and
/// whole-animation deltas, a fixed-alignment bone, a bone named twice, sections whose pair straddles a boundary and the
/// trailing last-frame section, sections sharing one chain, a one-frame animation, a blocked one and an empty one. The
/// real test then runs every player's animations both ways, unremapped and remapped onto its player skeleton, which is
/// the shape production calls.
/// </remarks>
public sealed class FramePairDifferentialTests
{
    private static readonly float[] Fractions =
        [-0.1f, 0f, 1e-6f, 0.0005f, 0.001f, 0.25f, 0.5f, 0.999f, 1f, 1.5f, float.NaN];

    private static readonly float[] RealFractions = [0f, 1e-6f, 0.5f, 1f];

    [Test]
    public void PoseBetweenInto_EveryCaseOfASyntheticModel_IsBitIdenticalToTheTwoPassReference()
    {
        byte[] model = Synthetic();
        List<StudioBone> bones = Skeleton();
        int[]?[] remaps = [null, [3, -1, 0, 0, 7, 9, 2, 1], [1, 0]];

        StudioPoseWorkspace workspace = new();
        FramePairReference.Lists lists = new();
        Tally tally = new();

        int animations = StudioAnimation.Count(model);

        animations.ShouldBe(9, "the control: every synthetic animation is declared");

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

    [Test]
    public void PoseBetweenInto_EveryPlayersAnimations_AreBitIdenticalToTheTwoPassReference()
    {
        string[] classes = ["scout", "soldier", "pyro", "demo", "heavy", "engineer", "medic", "sniper", "spy"];
        StudioPoseWorkspace workspace = new();
        FramePairReference.Lists lists = new();
        Tally tally = new();
        int models = 0;
        StringBuilder read = new();

        GameInstall.Require();

        foreach (string name in classes)
        {
            if (Read($"models/player/{name}_animations.mdl") is not { } model ||
                Read($"models/player/{name}.mdl") is not { } player)
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
    public void PoseBetweenInto_EveryOtherAnimatedModelTheFixturesName_IsBitIdenticalToTheTwoPassReference()
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

        foreach ((string path, string? basePath) in subjects)
        {
            byte[]? baseModel = basePath is null ? null : Read(basePath);

            if (Read(path) is not { } model || (basePath is not null && baseModel is null))
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

    [Test]
    public void PoseBetweenInto_ASecondCallIntoTheSameWorkspace_AllocatesNothing()
    {
        byte[] model = Synthetic();
        List<StudioBone> bones = Skeleton();
        int[] remap = [3, -1, 0, 0, 7, 9, 2, 1];
        StudioPoseWorkspace workspace = new();

        StudioAnimation.PoseBetweenInto(model, bones, null, 0, 2, 0.5f, workspace, 0);
        StudioAnimation.PoseBetweenInto(model, bones, remap, 0, 2, 0.5f, workspace, 1);
        StudioAnimation.PoseBetweenInto(model, bones, null, 2, 3, 0.5f, workspace, 2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        List<StudioBonePose> plain = StudioAnimation.PoseBetweenInto(model, bones, null, 0, 2, 0.5f, workspace, 0);
        StudioAnimation.PoseBetweenInto(model, bones, remap, 0, 2, 0.5f, workspace, 1);
        StudioAnimation.PoseBetweenInto(model, bones, null, 2, 3, 0.5f, workspace, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        plain.Count.ShouldBe(bones.Count, "the control: the call blended, so the whole path ran");
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
        IReadOnlyList<StudioBonePose> got =
            StudioAnimation.PoseBetweenInto(model, bones, remap, animation, frame, fraction, workspace, slot);

        tally.Compared++;

        if (want.Count == 0)
        {
            tally.Empty++;
        }
        else if (want.Count == bones.Count && fraction is > 0f or float.NaN)
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

    // ---- the synthetic model ----

    private const int Descriptions = 256;

    private sealed record Track(int Bone, int Flags, byte[] Payload);

    private sealed record Animation(int Frames, Track[][] Chains, int Flags = 0, int SectionFrames = 0, int Block = 0, int[]? SectionMap = null);

    /// <summary>Eight bones, each with its own rest, Euler, scales; bone 5 fixed-aligned, bone 7 with a zero roll scale.</summary>
    private static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, 8).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, (2f * bone) + 0.5f, -bone * 0.25f),
            Rotation: (0.1f * bone, 0.2f, 0.3f, 0.9f),
            PoseToBone: default,
            Euler: (0.1f * bone, -0.2f, 0.05f * bone),
            PositionScale: (0.01f, 0.02f, 0.03f * (bone + 1)),
            RotationScale: (bone == 7 ? 0f : 0.001f, 0.0005f * (bone + 1), 0.002f),
            Flags: bone == 5 ? StudioBoneFlags.FixedAlignment : 0,
            Alignment: bone == 5 ? (0f, 0f, -0.7071f, -0.7071f) : default)),
    ];

    private static byte[] Synthetic()
    {
        const int rawRot = StudioFlags.AnimationRawRotation;
        const int rawRot64 = StudioFlags.AnimationRawRotation64;
        const int rawPos = StudioFlags.AnimationRawPosition;
        const int animRot = StudioFlags.AnimationAnimatedRotation;
        const int animPos = StudioFlags.AnimationAnimatedPosition;
        const int delta = StudioFlags.AnimationDelta;

        Track[] mixed =
        [
            new(0, rawRot64 | rawPos, [.. Q64(0x8123_4567_89AB_CDEFUL), .. V48(1.5f, -2.25f, 3f)]),
            new(1, rawRot | animPos, [.. Q48(40000, 20000, 0x8000 | 9000), 0, 0, 0, 0, 0, 0]),
            new(2, animRot | animPos, Animated(
                [Rle((2, 3, [100, -200]), (1, 3, [300])), Rle((6, 6, [1, 2, 3, 4, 5, 6])), Rle((1, 6, [50]))],
                [Rle((3, 2, [7, 8, 9]), (1, 4, [10])), null, Rle((2, 2, [5, 6]), (0, 0, []))])),
            new(3, animRot | delta, Animated(
                [Rle((3, 6, [1000, -1000, 1000])), null, Rle((1, 1, [5]), (1, 5, [6]))], null)),
            new(4, 0, []),
            new(5, animRot, Animated(
                [Rle((6, 6, [0, 500, 1000, 1500, 2000, 2500])), Rle((1, 6, [-300])), Rle((2, 6, [30000, -30000]))],
                null)),
            new(7, animRot | delta, Animated(
                [Rle((6, 6, [5, -3, 0, 7, -7, 0])), Rle((1, 6, [100])), Rle((1, 6, [0]))], null)),
            new(2, rawPos, [.. V48(-4f, 0.125f, 9f)]),
        ];

        Track[] Section(short a, short b) =>
        [
            new(2, animRot | animPos, Animated(
                [Rle((5, 5, [a, (short)(a + 10), (short)(a + 20), (short)(a + 30), (short)(a + 40)])), Rle((1, 5, [b])), null],
                [Rle((2, 5, [b, a])), null, null])),
            new(6, rawRot, [.. Q48((ushort)(32768 + a), (ushort)(32768 - b), 16384)]),
        ];

        Animation[] animations =
        [
            new(6, [mixed]),
            new(4, [[
                new(1, animRot, Animated([Rle((4, 4, [10, 20, 30, 40])), Rle((1, 4, [7])), null], null)),
                new(4, rawPos, [.. V48(0.5f, 0.25f, 0.125f)]),
                new(6, 0, []),
            ]], Flags: StudioFlags.SequenceDelta),
            new(10, [Section(100, 1), Section(200, 2), Section(300, 3), Section(400, 4)], SectionFrames: 4),
            new(10, [Section(500, 5)], SectionFrames: 4, SectionMap: [0, 0, 0, 0]),
            new(1, [mixed]),
            new(6, [mixed], Block: 1),
            new(6, [[new(200, rawPos, [.. V48(1f, 1f, 1f)])]]),
            new(0, [mixed]),
            new(3, [Section(600, 6)], SectionFrames: 4),
        ];

        byte[] file = new byte[16384];
        int data = Descriptions + (animations.Length * StudioLayout.AnimationStride);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderAnimationCountOffset), animations.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderAnimationIndexOffset), Descriptions);

        for (int index = 0; index < animations.Length; index++)
        {
            Animation animation = animations[index];
            int at = Descriptions + (index * StudioLayout.AnimationStride);

            Write32(file, at + StudioLayout.AnimationFlagsOffset, animation.Flags);
            Write32(file, at + StudioLayout.AnimationFrameCountOffset, animation.Frames);
            Write32(file, at + StudioLayout.AnimationBlockOffset, animation.Block);

            int[] chains = new int[animation.Chains.Length];

            for (int chain = 0; chain < chains.Length; chain++)
            {
                chains[chain] = data;
                data = WriteChain(file, data, animation.Chains[chain]);
            }

            if (animation.SectionFrames == 0)
            {
                Write32(file, at + StudioLayout.AnimationDataOffset, chains[0] - at);
                continue;
            }

            int table = data;
            int entries = (animation.Frames / animation.SectionFrames) + 2;

            Write32(file, at + StudioLayout.AnimationSectionFramesOffset, animation.SectionFrames);
            Write32(file, at + StudioLayout.AnimationSectionIndexOffset, table - at);
            Write32(file, at + StudioLayout.AnimationDataOffset, chains[0] - at);

            for (int section = 0; section < entries; section++)
            {
                int chain = animation.SectionMap?[Math.Min(section, animation.SectionMap.Length - 1)] ??
                    Math.Min(section, chains.Length - 1);

                Write32(file, table + (section * StudioLayout.AnimationSectionStride), 0);
                Write32(
                    file,
                    table + (section * StudioLayout.AnimationSectionStride) + StudioLayout.AnimationSectionDataOffset,
                    chains[chain] - at);
            }

            data = table + (entries * StudioLayout.AnimationSectionStride) + 8;
        }

        return file;
    }

    private static int WriteChain(byte[] file, int cursor, Track[] tracks)
    {
        for (int entry = 0; entry < tracks.Length; entry++)
        {
            Track track = tracks[entry];
            int size = 4 + track.Payload.Length;
            bool last = entry == tracks.Length - 1;

            file[cursor] = (byte)track.Bone;
            file[cursor + 1] = (byte)track.Flags;
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(cursor + 2), last ? (short)0 : (short)size);
            track.Payload.CopyTo(file, cursor + 4);

            cursor += size;
        }

        return cursor + 16;
    }

    private static void Write32(byte[] file, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), value);

    /// <summary>A run-length channel: each block is <c>valid</c>, <c>total</c>, then the valid values.</summary>
    private static byte[] Rle(params (int Valid, int Total, short[] Values)[] blocks)
    {
        List<byte> bytes = [];

        foreach ((int valid, int total, short[] values) in blocks)
        {
            bytes.Add((byte)valid);
            bytes.Add((byte)total);

            foreach (short value in values)
            {
                bytes.Add((byte)(value & 0xFF));
                bytes.Add((byte)((value >> 8) & 0xFF));
            }
        }

        return [.. bytes];
    }

    /// <summary>
    /// Animated channels as the reader lays them out: the rotation table, the position table after it when both are
    /// animated, then the streams. A null channel has offset zero, which reads as zero.
    /// </summary>
    private static byte[] Animated(byte[]?[]? rotation, byte[]?[]? position)
    {
        int tables = (rotation is null ? 0 : 6) + (position is null ? 0 : 6);
        List<byte> bytes = [.. new byte[tables]];

        void Table(byte[]?[] channels, int tableAt)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (channels[channel] is not { } stream)
                {
                    continue;
                }

                BinaryPrimitives.WriteInt16LittleEndian(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes)[(tableAt + (channel * 2))..],
                    (short)(bytes.Count - tableAt));

                bytes.AddRange(stream);
            }
        }

        if (rotation is not null)
        {
            Table(rotation, 0);
        }

        if (position is not null)
        {
            Table(position, rotation is null ? 0 : 6);
        }

        return [.. bytes];
    }

    private static byte[] Q48(ushort x, ushort y, ushort z)
    {
        byte[] bytes = new byte[6];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, x);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), y);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), z);

        return bytes;
    }

    private static byte[] Q64(ulong packed)
    {
        byte[] bytes = new byte[8];

        BinaryPrimitives.WriteUInt64LittleEndian(bytes, packed);

        return bytes;
    }

    private static byte[] V48(float x, float y, float z)
    {
        byte[] bytes = new byte[6];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, BitConverter.HalfToUInt16Bits((Half)x));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), BitConverter.HalfToUInt16Bits((Half)y));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), BitConverter.HalfToUInt16Bits((Half)z));

        return bytes;
    }

    private static byte[]? Read(string path)
    {
        foreach (string name in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string? archive = GameInstall.Find(name);

            if (archive is not null && VpkArchive.Open(archive).ReadFile(path) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
