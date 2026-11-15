using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A jiggle bone steps on the real-time clock, not on demo time (B615).</summary>
/// <remarks>
/// **`BuildJiggleTransformations( i, gpGlobals->realtime, … )`** (<c>c_baseanimating.cpp:1586</c>): the springs integrate
/// wall-clock time, which a pause, a demo speed or a seek does not stop. Here demo time is held at one value for every
/// build while real time advances a hundredth of a second each; a flexible bone pointing along world X, all stiffness
/// zero, falls under gravity only if the clock it steps on moves. Read on demo time it never moves.
/// </remarks>
public sealed class JiggleClockConformanceTests
{
    /// <remarks>
    /// **The control is the same bone on a held clock**, which still moves: a step under a thousandth is clamped up to
    /// one (`jigglebones.cpp`, `deltaT`), so twenty held builds integrate 0.02 s where twenty running ones integrate
    /// 0.2 s. Free fall goes as the square of the time, so the running tip must fall well below the held one.
    /// </remarks>
    [Test]
    public void Build_WithDemoTimeHeldAndRealTimeRunning_StepsTheJiggleBoneOnRealTime()
    {
        float running = ForwardZ(realTimeRuns: true);
        float held = ForwardZ(realTimeRuns: false);

        held.ShouldBeLessThanOrEqualTo(0f, "the control: a held clock steps a thousandth a build");
        running.ShouldBeLessThan(held * 10f, "real time ran ten times as far, so the tip fell far further");
    }

    /// <summary>The bone's forward axis's world Z after twenty builds at a demo time held at 5 s.</summary>
    private static float ForwardZ(bool realTimeRuns)
    {
        SkeletonPose pose = new(Bones(), (_, _, _) => []) { JiggleSource = Model() };
        BoneAccessor into = new(1);

        for (int frame = 0; frame <= 20; frame++)
        {
            pose.RealTime = realTimeRuns ? frame * 0.01d : 0d;
            pose.Build(boneMask: ~0, currentTime: 5d, into, new BoneBitList(1));
        }

        pose.JigglingBones.ShouldBe(1, "the control: the spring ran on the bone");

        // The forward axis is the matrix's third column; its world Z is element 10.
        return into.Bone(0)[10];
    }

    /// <summary>One bone, always procedural, turned so its own Z (its length) lies along world X.</summary>
    /// <remarks>Along world Z gravity would pull straight down its length and leave the direction unchanged.</remarks>
    private static IReadOnlyList<StudioBone> Bones() =>
    [
        new StudioBone("hair", -1, (0f, 0f, 0f), (0f, MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)), default, Flags: ~0),
    ];

    /// <summary>`studiohdr_t` and `mstudiobone_t` offsets, mirrored as `QuatInterpWiringTests` mirrors them.</summary>
    private const int HeaderSize = 256;

    private const int HeaderBoneCountAt = 156;
    private const int HeaderBoneIndexAt = 160;
    private const int BoneStride = 216;
    private const int BoneParentAt = 4;
    private const int BoneProcedureTypeAt = 164;
    private const int BoneProcedureIndexAt = 168;

    /// <summary>`STUDIO_PROC_JIGGLE`, `studio.h`.</summary>
    private const int ProcedureJiggle = 5;

    /// <summary>`mstudiojigglebone_t`: a flags int, then 34 floats.</summary>
    private const int JiggleStride = 35 * 4;

    /// <summary>A one-bone model whose bone is a flexible jiggle bone of length 10, tip mass 100, every spring zero.</summary>
    private static byte[] Model()
    {
        int bone = HeaderSize;
        int jiggle = bone + BoneStride;
        byte[] file = new byte[jiggle + JiggleStride];

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(HeaderBoneCountAt), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(HeaderBoneIndexAt), bone);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + BoneParentAt), -1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + BoneProcedureTypeAt), ProcedureJiggle);

        // `procindex` is relative to the bone (`studio.h:293`).
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + BoneProcedureIndexAt), jiggle - bone);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(jiggle), StudioJiggleFlags.Flexible);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(jiggle + 4), 10f);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(jiggle + 8), 100f);

        return file;
    }
}
