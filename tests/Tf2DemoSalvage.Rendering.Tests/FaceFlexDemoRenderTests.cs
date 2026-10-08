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
/// A face changing on a real recording, through the production path to the pixel (B513): the scene builds the soldier's
/// instance — pose, bones and the flex stream — and the vertices drawn are the ones the device uploads.
/// </summary>
/// <remarks>
/// **The subject**: tf2-2026-pub-pov-clean (lcor), soldier 9, whose <c>taunt_laugh</c> scene starts at tick 1074
/// (<c>scene-wire</c>). Its <c>EXPRESSION</c> events run <c>player\soldier\emotion\emotion</c> from 0.019 s and four
/// <c>phonemes_strong</c> settings from 0.42 s (<c>flex scenes</c>). The control for the picture is the same draw
/// repeated; the control for the stream is the same soldier before the scene, whose face is the model's resting one.
/// </remarks>
public sealed class FaceFlexDemoRenderTests
{
    private const string Demo = "tf2-2026-pub-pov-clean.dem";
    private const string SoldierModel = "models/player/soldier.mdl";
    private const int Soldier = 9;
    private const int TauntStart = 1074;
    private const int Size = 128;

    private static readonly StrongBox<DemoTimeline?> Timeline = new();

    private static readonly StrongBox<GameContent?> Content = new();

    [Test]
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
            int[] rest = Picture(target, assets, models, soldier, null, yaw);

            again.ShouldBe(flexed, "the control: one draw repeated is the same picture");

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

    /// <summary>The soldier's instance at a tick, built by the scene from the demo with the production loader.</summary>
    private static (MapAssets Assets, EntityModelSet Models, ModelInstance Soldier) Posed(int tick)
    {
        string demo = CommittedDemo.RequireLocal(Demo);
        GameInstall.Require();

        DemoTimeline timeline = Timeline.Value ??= DemoTimeline.Build(File.ReadAllBytes(demo));
        TimelineMoments moments = new(timeline);
        MapAssets assets = MapCache.Load(entityModels: [SoldierModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        GameContent content = Content.Value ??= GameContent.Open(GameInstall.Require(), NullLoggerFactory.Instance);

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = content.Weapons,
            Appearance = new PlayerAppearances(NullLogger.Instance) { Timeline = timeline, Game = content }
                .Ensure(NoAppearance.Instance),
        };
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        timeline.PlayersAt(tick, players);
        timeline.PropsAt(tick, props);

        MomentInfo info = new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: false,
            Followed: -1,
            EyeCamera: null,
            IntervalPerTick: timeline.IntervalPerTick,
            ViewmodelFieldOfView: 54f,
            Recorder: timeline.RecorderEntityIndex)
        {
            ServerTime = moments.ServerTimeAt(tick),
        };

        scene.Build(players, props, info);
        scene.Pose(info);

        ModelInstance soldier = scene.Instances.Single(
            instance => instance.EntityIndex == Soldier &&
                string.Equals(instance.ModelPath, SoldierModel, StringComparison.OrdinalIgnoreCase));

        return (assets, models, soldier);
    }

    /// <summary>The head from one side, close up, every pixel's channel sum.</summary>
    private static int[] Picture(
        OffscreenTarget target, MapAssets assets, EntityModelSet models, ModelInstance soldier, float[]? face, float yaw)
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
            flex: face);

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
