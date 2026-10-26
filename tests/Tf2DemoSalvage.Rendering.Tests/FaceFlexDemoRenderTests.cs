using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A face changing through the production path to the pixel (B513): the scene builds the soldier's instance — pose,
/// bones and the flex stream — and the vertices drawn are the ones the device uploads.
/// </summary>
/// <remarks>
/// **The moment is written down, not read from a recording** (D213): soldier 9 of tf2-2026-pub-pov-clean — the
/// recording's own player — whose <c>taunt_laugh</c> scene starts at tick 1074 and stops at 1398, over the idle scene he
/// has been in since 455, with the laugh's voice line, as `moment-dump tf2-2026-pub-pov-clean 1204 9` printed them on
/// 2026-10-11. The scene is the install's: its <c>EXPRESSION</c> events run <c>player\soldier\emotion\emotion</c> from
/// 0.019 s and four <c>phonemes_strong</c> settings from 0.42 s (<c>flex scenes</c>). The control for the picture is the
/// same draw repeated; the control for the stream is the same soldier before the scene, whose face is the model's
/// resting one.
/// </remarks>
public sealed class FaceFlexDemoRenderTests
{
    private const string SoldierModel = "models/player/soldier.mdl";
    private const int Soldier = 9;
    private const int TauntStart = 1074;
    private const int Size = 128;

    /// <summary><c>m_nPlayerCond</c> while he laughs: <c>TF_COND_TAUNTING</c> among its three bits.</summary>
    private const int LaughingConditions = 10485888;

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Instance_ASoldierLaughing_CarriesAFaceThatMovesItsVertices()
    {
        (_, _, ModelInstance before) = Posed(TauntStart - 30);
        (_, EntityModelSet models, ModelInstance laughing) = Posed(TauntStart + 130);

        float[] face = laughing.Flex.ShouldNotBeNull("two seconds into the laugh");
        IReadOnlyList<WorldBatch> batches = models.AllFrames(SoldierModel)[laughing.Frame];
        face.Length.ShouldBe(
            (batches.Max(batch => batch.FirstVertex + batch.VertexCount) - batches.Min(batch => batch.FirstVertex)) * 6,
            "one position and one normal delta per vertex of the model's buffer");

        // The control: the same soldier before the scene wears the resting face — every controller at zero in its own
        // range, which for a TF player is not the bind pose (ResetFlexWeights, c_tf_player.cpp:5291).
        (int restMoved, float restLargest) = Movement(before.Flex);
        (int moved, float largest) = Movement(face);

        TestContext.Out.WriteLine(
            $"soldier at {TauntStart + 130}: {moved} of {face.Length / 6} buffer vertices move, the furthest {largest:0.###} units; " +
            $"at rest {restMoved}, {restLargest:0.###}");

        largest.ShouldBeGreaterThan(restLargest + 0.25f, "a laugh opens the mouth by a visible amount");
        moved.ShouldBeGreaterThan(restMoved + 100);
    }

    private static (int Moved, float Largest) Movement(float[]? face)
    {
        float largest = 0f;
        int moved = 0;

        for (int at = 0; face is not null && at < face.Length; at += 6)
        {
            float length = MathF.Sqrt((face[at] * face[at]) + (face[at + 1] * face[at + 1]) + (face[at + 2] * face[at + 2]));
            largest = MathF.Max(largest, length);
            moved += length > 0.05f ? 1 : 0;
        }

        return (moved, largest);
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Render_ASoldierLaughing_DrawsADifferentFaceFromTheSameBonesWithoutIt()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        (MapAssets assets, EntityModelSet models, ModelInstance soldier) = Posed(TauntStart + 130);

        float[] face = soldier.Flex.ShouldNotBeNull();
        int best = 0;
        int drawn = 0;

        foreach (float yaw in (float[])[0f, 90f, 180f, 270f])
        {
            int[] flexed = Picture(target, assets, models, soldier, face, yaw);
            int[] again = Picture(target, assets, models, soldier, face, yaw);
            int[] ranged = Picture(target, assets, models, soldier, face, yaw, soldier.FlexRange.ShouldNotBeNull());
            int[] rest = Picture(target, assets, models, soldier, null, yaw);

            again.ShouldBe(flexed, "the control: one draw repeated is the same picture");

            // Sent as production sends it, only the flexed range (B597): the same picture as the whole stream.
            ranged.ShouldBe(flexed, "the flexed range draws what the whole stream draws");

            int changed = flexed.Zip(rest).Count(pair => Math.Abs(pair.First - pair.Second) > 6);

            TestContext.Out.WriteLine($"head from yaw {yaw}: {flexed.Count(pixel => pixel > 0)} drawn, {changed} change with the face");

            if (changed > best)
            {
                best = changed;
                drawn = flexed.Count(pixel => pixel > 0);
            }
        }

        drawn.ShouldBeGreaterThan(1000, "the head fills the view");
        best.ShouldBeGreaterThan(20, "the face the scene makes is on screen");
    }

    /// <summary>The soldier's instance at a tick, built by the scene from the written-down moment with the production loader.</summary>
    private static (MapAssets Assets, EntityModelSet Models, ModelInstance Soldier) Posed(int tick)
    {
        ScenePlayer soldier9 = new(
            EntityIndex: Soldier,
            X: 5913.6333f,
            Y: -2720.6206f,
            Z: 384.03125f,
            Team: 3,
            Health: 300,
            PlayerClass: 3,
            Yaw: 263.92963f,
            LifeState: 0,
            Flags: 131329,
            Conditions: new PlayerConditions(tick >= TauntStart ? LaughingConditions : 0, 0, 0, 0, 0),
            ClientSideAnimated: true);

        DemoTimeline timeline = SyntheticMatch.Timeline(
            [soldier9],
            scenes:
            [
                new SceneChoreography(455, 1061, "scenes/player/soldier/low/idleloop01.vcd", [Soldier]) { StoppedTick = 1457 },
                new SceneChoreography(TauntStart, 1073, "scenes/player/Soldier/low/taunt_laugh.vcd", [Soldier]) { StoppedTick = 1398 },
            ],
            sounds:
            [
                new SceneSound(
                    TauntStart, "vo/soldier_LaughLong03.mp3", 1084, Soldier, Channel: 2, Volume: 0.61417323f, SoundLevel: 95,
                    Pitch: 100, DelaySeconds: 0f, OriginX: 5912f, OriginY: -2720f, OriginZ: 424f),
            ]);

        MapAssets assets = MapCache.Load(entityModels: [SoldierModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = SyntheticMatch.Scene(models, SyntheticMatch.Appearance(timeline));

        // The soldier is the recording's own player; the moment is drawn in third person.
        MomentInfo info = SyntheticMatch.Info(timeline, tick, recorder: Soldier);

        scene.Build([soldier9], [], info);
        scene.Pose(info);

        ModelInstance soldier = scene.Instances.Single(
            instance => instance.EntityIndex == Soldier &&
                string.Equals(instance.ModelPath, SoldierModel, StringComparison.OrdinalIgnoreCase));

        return (assets, models, soldier);
    }

    /// <summary>The head from one side, close up, every pixel's channel sum.</summary>
    private static int[] Picture(
        OffscreenTarget target, MapAssets assets, EntityModelSet models, ModelInstance soldier, float[]? face, float yaw,
        FlexSlots? range = null)
    {
        (float minX, float minY, float _, float maxX, float maxY, float maxZ) = soldier.WorldBounds;
        (float X, float Y, float Z) head = ((minX + maxX) / 2f, (minY + maxY) / 2f, maxZ - 12f);
        float radians = yaw * MathF.PI / 180f;

        float[] camera = new FreeCamera
        {
            Origin = (head.X - (40f * MathF.Cos(radians)), head.Y - (40f * MathF.Sin(radians)), head.Z),
            Angles = (0f, yaw, 0f),
            Aspect = 1f,
        }.ToMatrix();

        // The model's own slice, rebased to zero, exactly as Device3D hands it to the renderer — the stream is in
        // that buffer's order.
        IReadOnlyList<WorldBatch> batches = models.AllFrames(SoldierModel)[soldier.Frame];
        int lowest = batches.Min(batch => batch.FirstVertex);
        int highest = batches.Max(batch => batch.FirstVertex + batch.VertexCount);
        List<WorldVertex> own = [.. Enumerable.Range(lowest - models.VertexBase, highest - lowest).Select(at => models.Vertices[at])];

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            own,
            [.. batches.Select(batch => batch with { FirstVertex = batch.FirstVertex - lowest })],
            camera,
            soldier.Matrix,
            assets,
            light: new AmbientCube((0.3f, 0.3f, 0.3f), (0.3f, 0.3f, 0.3f), (0.3f, 0.3f, 0.3f),
                (0.3f, 0.3f, 0.3f), (0.6f, 0.6f, 0.6f), (0.1f, 0.1f, 0.1f)),
            bones: soldier.Bones,
            flex: face,
            flexRange: range);

        int[] sums = new int[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                (int r, int g, int b) = target.PixelAt(x, y);
                sums[(y * Size) + x] = r + g + b;
            }
        }

        return sums;
    }
}
