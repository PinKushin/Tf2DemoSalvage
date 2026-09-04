using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The quaternion-interpolation rule reaches the skeleton, and only for bones that declare it
/// (B317).
/// </summary>
/// <remarks>
/// **`QuatInterpConformanceTests` proves the arithmetic and says nothing about whether anything runs
/// it.** It calls `QuatInterpBones.Build` directly with matrices it chose; nothing in it touches the
/// path that decides which bone is procedural, finds the rule in the model, or supplies the control
/// bone's matrices. That gap is where this project has shipped three no-ops with a green suite —
/// `m_flPlaybackRate` was decoded, retained, unit-tested and read by no production code at all.
///
/// **The count is taken from `SkeletonPose` itself**, incremented where the work happens, rather
/// than from how many bones a model declares. A number derived by a second route is free to be right
/// while the code does nothing, which is the fault
/// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md` collects.
/// </remarks>
public sealed class QuatInterpWiringTests
{
    [Test]
    public void Build_ForABoneDeclaringTheRule_RunsIt()
    {
        SkeletonPose pose = Posed(procedural: true);

        BoneAccessor into = new(3);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(3));

        pose.QuatInterpBonesBuilt.ShouldBe(1);

        // And it took the trigger's position, which is what says the rule RAN rather than that the
        // counter was incremented. Trigger zero wants (5, 0, 0) and the control matches it.
        into.BoneForWrite(2)[3].ShouldBe(5f, 1e-4d);
    }

    /// <remarks>
    /// **The control that `BONE_ALWAYS_PROCEDURAL` is read.** The engine's whole dispatch is inside
    /// a test of that flag on the bone (`bone_setup.cpp:4940`), so a
    /// wiring that ran the rule on any bone whose model happened to carry one would overwrite
    /// ordinary animated bones with an interpolated pose. Same model, same rule bytes, flag
    /// cleared — the bone must keep its animated transform.
    /// </remarks>
    [Test]
    public void Build_ForABoneWithoutTheProceduralFlag_LeavesItAlone()
    {
        SkeletonPose pose = Posed(procedural: false);

        BoneAccessor into = new(3);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(3));

        pose.QuatInterpBonesBuilt.ShouldBe(0);

        into.BoneForWrite(2)[3].ShouldBe(0f, 1e-4d, "the bone keeps its animated transform");
    }

    /// <remarks>
    /// **A pose with no model bytes must not throw and must not run.** The source is optional —
    /// every synthetic skeleton in this suite has none — so the guard is load-bearing rather than
    /// defensive.
    /// </remarks>
    [Test]
    public void Build_WithNoModelBytes_RunsNothing()
    {
        SkeletonPose pose = new(Bones(procedural: true), (_, _, _, _) => []);

        BoneAccessor into = new(3);

        pose.Build(boneMask: ~0, currentTime: 0d, into, new BoneBitList(3));

        pose.QuatInterpBonesBuilt.ShouldBe(0);
    }

    /// <summary>A three-bone skeleton whose third bone is driven by the second.</summary>
    private static SkeletonPose Posed(bool procedural) =>
        new(Bones(procedural), (_, _, _, _) => [])
        {
            JiggleSource = Model(),
        };

    /// <summary>
    /// Root, a control, and the bone the rule drives.
    /// </summary>
    /// <remarks>
    /// **The control is bone 1 and the driven bone is 2, so the control is built first.** Valve's
    /// dispatch reads the control's already-built world matrix from inside the same forward walk, so
    /// a skeleton ordering them the other way round would read an unbuilt matrix — which is a fact
    /// about model authoring rather than something to guard here.
    /// </remarks>
    private static IReadOnlyList<StudioBone> Bones(bool procedural) =>
    [
        new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone("bip_hand", 0, (0f, 0f, 0f), (0f, 0f, 0f, 1f), default, Flags: ~0),
        new StudioBone(
            "hlp_forearm",
            0,
            (0f, 0f, 0f),
            (0f, 0f, 0f, 1f),
            default,
            Flags: procedural ? ~0 : ~0 & ~StudioBoneFlags.AlwaysProcedural),
    ];

    /// <summary>Bytes of the fake header before the bone table.</summary>
    private const int HeaderSize = 256;

    /// <summary>
    /// <c>studiohdr_t</c> and <c>mstudiobone_t</c> offsets, mirrored because <c>StudioLayout</c> is
    /// internal to the content assembly.
    /// </summary>
    /// <remarks>
    /// **Duplicated deliberately and safely.** `StudioQuatInterpTests` in `Content.Tests` pins these
    /// against `StudioLayout` itself; here they only have to be right enough for the reader to find
    /// the rule, and if any of them drifts the reader returns null and both tests below fail loudly
    /// rather than quietly measuring nothing.
    /// </remarks>
    private const int HeaderBoneCountAt = 156;

    private const int HeaderBoneIndexAt = 160;

    private const int BoneStride = 216;

    private const int BoneParentAt = 4;

    private const int BoneProcedureTypeAt = 164;

    private const int BoneProcedureIndexAt = 168;

    /// <summary>
    /// A minimal model whose bone 2 carries one trigger wanting position (5, 0, 0).
    /// </summary>
    /// <remarks>
    /// **One trigger matching the identity**, so the control — unrotated, like every bone here —
    /// weighs it fully and the answer is that trigger's pose exactly. A prediction with no
    /// arithmetic in it, because this suite is about the wiring and the conformance suite is about
    /// the maths.
    /// </remarks>
    private static byte[] Model()
    {
        const int bones = 3;
        const int ruleStride = 12;
        const int triggerStride = 48;

        int table = HeaderSize;
        int after = table + (bones * BoneStride);

        byte[] file = new byte[after + ruleStride + triggerStride];

        Write(file, HeaderBoneCountAt, bones);
        Write(file, HeaderBoneIndexAt, table);

        int driven = table + (2 * BoneStride);

        Write(file, driven + BoneParentAt, 0);
        Write(file, driven + BoneProcedureTypeAt, StudioProcedureType.QuaternionInterpolate);
        Write(file, driven + BoneProcedureIndexAt, after - driven);

        Write(file, after, 1);           // control: bone 1
        Write(file, after + 4, 1);       // one trigger
        Write(file, after + 8, ruleStride);

        int trigger = after + ruleStride;

        WriteFloat(file, trigger, 1f);            // inv_tolerance
        WriteFloat(file, trigger + 16, 1f);       // trigger.w — the identity rotation
        WriteFloat(file, trigger + 20, 5f);       // pos.x
        WriteFloat(file, trigger + 44, 1f);       // quat.w — the identity rotation

        return file;
    }

    private static void Write(byte[] into, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(into.AsSpan(at), value);

    private static void WriteFloat(byte[] into, int at, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(into.AsSpan(at), value);
}
