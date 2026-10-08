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
/// The model tangent on a real recording, through the production path to the pixel: the scene builds the spy's instance
/// — pose, bones, cloak bind — and the vertices drawn are the ones the device uploads (<c>EntityModelSet.Vertices</c>).
/// </summary>
/// <remarks>
/// **The subject**: serveme-627619-stv-2026-08-07 (lcor), RED spy 7, uncloaked at 63600 and cloaking from 63693 —
/// half way (0.3–0.8, <c>CorpusCloakAndOverlayTests</c>) at 63730. Each test draws the same instance twice, with the
/// uploaded tangents and with them stripped (w zero, "no frame"); the control is the same draw repeated, which must
/// agree to the byte. <c>ModelTangentRenderTests</c> and <c>CloakRenderTests</c> predict the per-pixel values on one
/// texel; this proves production's vertices carry the frame into the draw.
/// </remarks>
public sealed class ModelTangentDemoRenderTests
{
    private const string Demo = "serveme-627619-stv-2026-08-07.dem";
    private const string SpyModel = "models/player/spy.mdl";
    private const int Spy = 7;
    private const int Size = 128;

    /// <summary>The demo's timeline, built once for both tests: a minute each otherwise.</summary>
    private static readonly StrongBox<DemoTimeline?> Timeline = new();

    private static readonly StrongBox<GameContent?> Content = new();

    [Test]
    public void Render_ARealSpysBumpLitBody_TakesItsHighlightFromTheTangentFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        (MapAssets assets, EntityModelSet models, ModelInstance spy) = Posed(63600);

        spy.Cloak.Cloaking.ShouldBeFalse("the control: not yet cloaking");
        models.Vertices.Count(vertex => vertex.TangentW is 1f or -1f).ShouldBe(
            models.Vertices.Count, "every uploaded spy vertex carries the .vvd's frame");

        SunLight sun = new(1f, 1f, 1f, -0.5f, 0.6f, -0.62f);
        int[] framed = Picture(target, assets, models.Vertices, models, spy, sun, frame: null);
        int[] again = Picture(target, assets, models.Vertices, models, spy, sun, frame: null);
        int[] flat = Picture(target, assets, Stripped(models.Vertices), models, spy, sun, frame: null);

        int drawn = framed.Count(pixel => pixel > 0);
        int moved = Changed(framed, flat);

        TestContext.Out.WriteLine($"spy at 63600: {drawn} pixels drawn, {moved} change with the tangent frame");

        again.ShouldBe(framed, "the control: one draw repeated is the same picture");
        // Measured 2026-10-08: 409 drawn, 56 moved by more than 6/765 — the sun-facing side, where the bump shows.
        drawn.ShouldBeGreaterThan(300, "the spy is in view");
        moved.ShouldBeGreaterThan(drawn / 10, "the normal map shapes the lit body through the frame");
    }

    [Test]
    public void Render_ARealSpyHalfCloaked_RefractsAlongTheTangentFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        (MapAssets assets, EntityModelSet models, ModelInstance spy) = Posed(63730);

        spy.Cloak.Cloaking.ShouldBeTrue("the control: cloaking");
        spy.Cloak.SpyInvis.ShouldBeInRange(0.45f, 0.9f, "past 4/9: the cloak pass alone draws");

        int[] framed = Picture(target, assets, models.Vertices, models, spy, null, frame: Edge(assets));
        int[] again = Picture(target, assets, models.Vertices, models, spy, null, frame: Edge(assets));
        int[] turned = Picture(target, assets, Turned(models.Vertices), models, spy, null, frame: Edge(assets));
        int[] background = Background(target, assets);

        int covered = Changed(framed, background);
        int moved = Changed(framed, turned);

        TestContext.Out.WriteLine($"spy at 63730 (cloak {spy.Cloak.SpyInvis:0.##}): {covered} pixels refracted, {moved} move when T turns");

        again.ShouldBe(framed, "the control: one draw repeated is the same picture");
        // Measured 2026-10-08 at cloak 0.56: 391 refracted, 125 moved.
        covered.ShouldBeGreaterThan(300, "the cloaked spy warps the stripes behind him");
        moved.ShouldBeGreaterThan(covered / 10, "turning the tangent half a turn turns the warp");
    }

    /// <summary>The spy's instance at a tick, built by the scene from the demo with the production loader.</summary>
    private static (MapAssets Assets, EntityModelSet Models, ModelInstance Spy) Posed(int tick)
    {
        string demo = CommittedDemo.RequireLocal(Demo);
        GameInstall.Require();

        DemoTimeline timeline = Timeline.Value ??= DemoTimeline.Build(File.ReadAllBytes(demo));
        TimelineMoments moments = new(timeline);
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        GameContent content = Content.Value ??= GameContent.Open(GameInstall.Require(), NullLoggerFactory.Instance);

        // The class models and the weapon schema, as the presenter wires them (PlayerAppearances, LevelSystems).
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

        ModelInstance spy = scene.Instances.Single(
            instance => instance.EntityIndex == Spy && string.Equals(instance.ModelPath, SpyModel, StringComparison.OrdinalIgnoreCase));

        return (assets, models, spy);
    }

    /// <summary>The instance drawn from beside it, every pixel's channel sum.</summary>
    private static int[] Picture(
        OffscreenTarget target,
        MapAssets assets,
        IReadOnlyList<WorldVertex> vertices,
        EntityModelSet models,
        ModelInstance spy,
        SunLight? sun,
        Action<OffscreenTarget>? frame)
    {
        (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = spy.WorldBounds;
        (float X, float Y, float Z) centre = ((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

        float[] camera = new FreeCamera
        {
            Origin = (centre.X - 110f, centre.Y, centre.Z),
            Angles = (0f, 0f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        frame?.Invoke(target);

        target.DrawModelPose(
            vertices,
            models.AllFrames(SpyModel)[spy.Frame],
            camera,
            spy.Matrix,
            assets,
            light: new AmbientCube((0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f),
                (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f), (0.05f, 0.05f, 0.05f)),
            sun: sun,
            cloak: spy.Cloak,
            bones: spy.Bones);

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

    /// <summary>A frame for the cloak to copy: grey, with vertical stripes, so any sideways warp shows.</summary>
    private static Action<OffscreenTarget> Edge(MapAssets assets) => target =>
    {
        target.Clear(0.6f, 0.6f, 0.6f);

        int material = Enumerable.Range(0, assets.Materials.Count).First(
            at => assets.Materials[at].Name.EndsWith("spy/spy_red", StringComparison.OrdinalIgnoreCase));
        List<WorldVertex> stripes = [];

        for (float left = -1f; left < 1f; left += 0.25f)
        {
            WorldVertex Corner(float x, float y) => new(x, y, 0.99f, 0.5f, 0.5f, 0f, 0f, 1f) { NormalZ = -1f };

            stripes.AddRange(
            [
                Corner(left, -1f), Corner(left + 0.125f, 1f), Corner(left + 0.125f, -1f),
                Corner(left, -1f), Corner(left, 1f), Corner(left + 0.125f, 1f),
            ]);
        }

        float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

        target.DrawModelPose(stripes, [new WorldBatch(material, 0, stripes.Count)], identity, identity, assets, bothSides: true);
    };

    private static int[] Background(OffscreenTarget target, MapAssets assets)
    {
        Edge(assets)(target);

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

    private static List<WorldVertex> Stripped(IReadOnlyList<WorldVertex> vertices) =>
        [.. vertices.Select(vertex => vertex with { TangentW = 0f })];

    private static List<WorldVertex> Turned(IReadOnlyList<WorldVertex> vertices) =>
        [.. vertices.Select(vertex => vertex with { TangentX = -vertex.TangentX, TangentY = -vertex.TangentY, TangentZ = -vertex.TangentZ })];

    private static int Changed(int[] a, int[] b) => a.Zip(b).Count(pair => Math.Abs(pair.First - pair.Second) > 6);
}
