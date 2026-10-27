using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`cl_threaded_bone_setup`'s pre-pass (finding 105 A7): the roots posed ahead of the draw loop draw the same bones.</summary>
/// <remarks>
/// **Two scenes over the same props, the pre-pass off in one and on in the other**, and every drawn instance's bones compared
/// bit for bit, frame by frame (A5). Six players of twenty bones each wear two bone-merged items and one carries a weapon:
/// the roots are the players, the children are what stays on the draw. A twenty-bone prop hung on a player WITHOUT a merge
/// is a move child, which the engine's `!GetMoveParent()` keeps off the list (`c_baseanimating.cpp:2897`), so it counts
/// against an exact pre-posed total. The corpse case (a ragdoll, excluded too) is held at the entity, by
/// `AnimatingEntityTests.Meter_RootsEitherSideOfTheThreshold_CountOnlyTheSixteenBoneOneWithNoRagdollEligible`.
/// </remarks>
public sealed class EntityModelSetPrePoseTests
{
    private const int Players = 6;
    private const int FrameCount = 5;

    private static readonly string[] PlayerBones = [.. Enumerable.Range(0, 20).Select(bone => bone == 0 ? "root" : $"b{bone}")];

    [Test]
    public void Instances_RootsPrePosedAheadOfTheDraw_DrawTheSameBonesAsSerial()
    {
        (EntityModelSet serial, List<ModelInstance> serialDrawn) = Scene(threaded: false);
        (EntityModelSet prePosed, List<ModelInstance> prePosedDrawn) = Scene(threaded: true);

        for (int frame = 0; frame < FrameCount; frame++)
        {
            SceneProp[] props = Props(frame);
            double seconds = frame / 66d;

            serial.Instances(props, serialDrawn, seconds: seconds);
            prePosed.Instances(props, prePosedDrawn, seconds: seconds);

            prePosedDrawn.Count.ShouldBe(serialDrawn.Count, $"frame {frame}");
            prePosed.Posed.ShouldBe(serial.Posed, $"frame {frame}");

            for (int at = 0; at < serialDrawn.Count; at++)
            {
                (prePosedDrawn[at].ModelPath, prePosedDrawn[at].EntityIndex).ShouldBe((serialDrawn[at].ModelPath, serialDrawn[at].EntityIndex));
                Bytes(prePosedDrawn[at].Bones).ShouldBe(Bytes(serialDrawn[at].Bones), $"frame {frame}, {serialDrawn[at].ModelPath}");
            }
        }

        // **The control that the pre-pass ran at all**: from the third frame each player is posed by it — the engine's -1 mask
        // is the one before the last rollover (`c_baseanimating.cpp:2827`), so the first two frames ask for nothing — and
        // nothing else ever is: not the items, the weapon or the move child. Off, it builds none.
        prePosed.PrePosed.ShouldBe(Players * (FrameCount - 2));
        serial.PrePosed.ShouldBe(0);
        prePosed.BuildsDrawing.ShouldBe(serial.BuildsDrawing);
    }

    [TestCase("", false, TestName = "Parse_NoThreadedBoneSetup_IsOffAsTheEngineShipsIt")]
    [TestCase("cl_threaded_bone_setup 1\n", true, TestName = "Parse_ThreadedBoneSetupOne_IsOn")]
    [TestCase("cl_threaded_bone_setup 0.5\n", false, TestName = "Parse_ThreadedBoneSetupAHalf_IsOffAsGetIntTruncatesIt")]
    public void Parse_ThreadedBoneSetup_IsReadAsTheEnginesGetBool(string config, bool expected) =>
        ViewerSettings.Parse(config).ThreadedBoneSetup.ShouldBe(expected);

    private static (EntityModelSet Models, List<ModelInstance> Drawn) Scene(bool threaded)
    {
        EntityModelSet models = new() { ThreadedBoneSetup = threaded };

        models.Add(Props(0), Loaded);
        return (models, []);
    }

    private static SceneProp[] Props(int frame)
    {
        List<SceneProp> props = [];

        for (int player = 0; player < Players; player++)
        {
            int index = player + 1;

            props.Add(Prop("models/player/demo.mdl", index, x: (100f * index) + (7f * frame), yaw: (13f * frame) + index));
            props.Add(Prop("models/player/items/hat.mdl", 100 + index, attachedTo: index) with { BoneMerged = true });
            props.Add(Prop("models/player/items/medal.mdl", 200 + index, attachedTo: index) with { BoneMerged = true });
        }

        props.Add(Prop("models/weapons/c_models/c_rocketlauncher.mdl", 300, attachedTo: 1) with { BoneMerged = true });

        // A move child with a skeleton of its own: a root to the bone layer, a move child to the engine's list.
        props.Add(Prop("models/props/hung.mdl", 400, x: 4f, attachedTo: 2));

        return [.. props];
    }

    private static PropModels.ModelFrames Loaded(string path) => path switch
    {
        // Sixteen bones, so only being a CHILD keeps an item off the list, not its size.
        _ when path.Contains("items", StringComparison.Ordinal) => Skinned(["root", "b3", .. Enumerable.Range(0, 14).Select(bone => $"own_{bone}")]),
        _ when path.Contains("weapons", StringComparison.Ordinal) => Skinned("b5", "barrel"),
        _ when path.Contains("hung", StringComparison.Ordinal) => Skinned([.. PlayerBones.Select(bone => "hung_" + bone)]),
        _ => Skinned(PlayerBones),
    };

    private static PropModels.ModelFrames Skinned(params string[] bones) =>
        new(
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
            Skinned: SyntheticSkinnedModel.WithBones(bones));

    private static SceneProp Prop(string model, int entity, float x = 0f, float yaw = 0f, int? attachedTo = null) =>
        new(entity, model, ScenePropTrack.Classify(model), new ScenePose { X = x, Yaw = yaw, Scale = 1f }, attachedTo);

    /// <summary>Every bone float's bits, so a difference in the last place fails.</summary>
    private static int[] Bytes(IReadOnlyList<float[]>? bones) =>
        bones is null ? [] : [.. bones.SelectMany(bone => bone).Select(BitConverter.SingleToInt32Bits)];
}
