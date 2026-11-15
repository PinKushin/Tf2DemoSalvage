using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The real-time clock reaches a jiggle bone through the scene (B615 review).</summary>
/// <remarks>
/// **The wiring half.** `JiggleClockConformanceTests` holds the skeleton stepping on its own `RealTime`; this holds the hops
/// that set it — `MomentInfo.RealTime` into `MomentScene.Build`, into `EntityModelSet.RealTime`, into each pose. A cabinet
/// whose one bone is a flexible jiggle bone along world X is posed twenty times at demo time 5 s, with real time either
/// running a hundredth a frame or held. Free fall goes as the square of the time, so running drops the tip far below
/// held, which still steps the clamped thousandth (`jigglebones.cpp`).
/// </remarks>
public sealed class MomentSceneJiggleClockTests
{
    [Test]
    public void Pose_WithDemoTimeHeldAndRealTimeRunning_StepsTheJiggleBoneOnRealTime()
    {
        float running = ForwardZ(realTimeRuns: true);
        float held = ForwardZ(realTimeRuns: false);

        held.ShouldBeLessThanOrEqualTo(0f, "the control: a held clock steps a thousandth a frame");
        running.ShouldBeLessThan(held * 10f, "real time ran ten times as far, so the tip fell far further");
    }

    /// <summary>The jiggle bone's forward axis's world Z after twenty posed frames at demo time 5 s.</summary>
    private static float ForwardZ(bool realTimeRuns)
    {
        EntityModelSet models = new() { Geometry = _ => Jiggling() };
        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());
        SceneProp cabinet = new(
            9,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = 0f },
            null);

        for (int frame = 0; frame <= 20; frame++)
        {
            MomentInfo info = new(5d, 5, false, null, null, 1f, 54f) { RealTime = realTimeRuns ? frame * 0.01d : 0d };

            scene.Build([], [cabinet], info);
            scene.Pose(info);
        }

        models.JigglingBones.ShouldBe(1, "the control: the scene posed the jiggle bone");

        (string _, IReadOnlyList<float[]> bones, int _, IReadOnlyList<(int Base, int Count)>? _) =
            models.SkinningOf(cabinet.EntityIndex).ShouldNotBeNull("the cabinet must be posed");

        // The forward axis is the bone matrix's third column; its world Z is element 10.
        return bones[0][10];
    }

    /// <summary>A one-bone model whose bone is a flexible jiggle bone of length 10, tip mass 100, every spring zero.</summary>
    private static PropModels.ModelFrames Jiggling()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(("idle", 0x0001));

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: model with
            {
                Models = [Bytes()],

                // Turned so its own Z, its length, lies along world X: along Z gravity pulls down its length and leaves
                // the direction unchanged.
                Bones =
                [
                    new StudioBone(
                        "hair",
                        -1,
                        (0f, 0f, 0f),
                        (0f, MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)),
                        new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                        Flags: ~0),
                ],
            });
    }

    /// <summary>The fixture's one-second animation, then a one-bone table whose bone carries the jiggle record.</summary>
    private static byte[] Bytes()
    {
        byte[] animation = AnimatedStudioBytes.OneSecondLoop(animations: 1);
        int bone = animation.Length;
        int jiggle = bone + BoneStride;
        byte[] file = new byte[jiggle + JiggleStride];

        animation.CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderBoneCountOffset), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(StudioLayout.HeaderBoneIndexOffset), bone);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + 4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + StudioLayout.BoneProcedureTypeOffset), StudioProcedureType.Jiggle);

        // `procindex` is relative to the bone (`studio.h:293`).
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(bone + StudioLayout.BoneProcedureIndexOffset), jiggle - bone);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(jiggle), StudioJiggleFlags.Flexible);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(jiggle + 4), 10f);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(jiggle + 8), 100f);

        return file;
    }

    private const int BoneStride = StudioLayout.BoneStride;

    private const int JiggleStride = StudioLayout.JiggleBoneStride;
}
