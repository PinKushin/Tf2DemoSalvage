using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The model set forgets a level's content when the level goes — the outside audit's finding 1.
/// </summary>
/// <remarks>
/// **`EntityModelSet` lives for the process; a map's models do not.** Its caches are keyed by
/// model PATH, and two facts make a path map-scoped rather than global: an inline brush model is
/// `*N`, a run of faces in one particular BSP, so `*3` on the next map is entirely different
/// geometry under the same name; and the loader consults the map's own pak before the archives
/// (`pak.ReadFile(file) ?? archives.Read(file)`), so a map can override any stock path for its own
/// duration. A cache that survives the level serves map A's door — or map A's custom override, or
/// map A's "this path is missing" — on map B.
///
/// **The engine flushes exactly this**: `CModelLoader` unloads the world and its brush models with
/// the map, and level transition unloads unreferenced models — and at this viewer's level
/// shutdown, everything is unreferenced. The next map's load repacks what it needs, which is where
/// that cost belongs (D129's load screen, not mid-play).
/// </remarks>
public sealed class ModelLevelLifecycleTests
{
    private const string Door = "*3";

    private static EntityModelSet Models() => new();

    private static PropModels.ModelFrames Triangle(float x) =>
        new(
            [
                new PropVertex[]
                {
                    new(x, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(x, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(x, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);

    private static SceneProp Prop(string model) =>
        new(1, model, ScenePropTrack.Classify(model), new ScenePose());

    /// <remarks>
    /// **The reused-name case, which is every door on every second map.** Map A's `*3` is packed;
    /// the level goes; map B names `*3` too. Without the flush, `Add` early-returns on the cached
    /// key and B's door draws as A's — same path, different building.
    /// </remarks>
    [Test]
    public void LevelShutdown_TheSamePathOnTheNextMap_ServesTheNextMapsGeometry()
    {
        EntityModelSet models = Models();

        models.Add([Prop(Door)], _ => Triangle(x: 1f));

        models.LevelShutdown();

        models.Add([Prop(Door)], _ => Triangle(x: 99f));

        models.Vertices.Count.ShouldBe(
            3, "one triangle; its blend target rides inside the same vertices");
        models.Vertices[0].X.ShouldBe(99f, "the geometry must be map B's, not map A's");
    }

    /// <remarks>
    /// **The cached-absence case.** A path that fails on map A is remembered as empty so the loader
    /// is not asked sixty times a second — correct within a level, and a permanent hole across
    /// levels: map B ships the file and would never load it.
    /// </remarks>
    [Test]
    public void LevelShutdown_APathThatFailedOnMapA_LoadsOnMapB()
    {
        EntityModelSet models = Models();

        models.Add([Prop(Door)], _ => null);

        models.Batches(Door).ShouldBeEmpty("map A does not have it");

        models.LevelShutdown();

        models.Add([Prop(Door)], _ => Triangle(x: 5f));

        models.Batches(Door).ShouldNotBeEmpty("map B ships it, and the failure must not be remembered");
    }

    /// <remarks>
    /// **The wiring, not the rule.** The three tests around this prove `LevelShutdown` works when
    /// called; this proves the level teardown CALLS it — `MomentScene.LevelShutdownPreEntity` is
    /// what `LevelSystems.Shutdown` walks, and a flush nothing invokes is the no-op this project
    /// keeps cataloguing (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
    /// </remarks>
    [Test]
    public void LevelShutdownPreEntity_OnTheScene_ReachesTheModelSet()
    {
        EntityModelSet models = Models();

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        models.Add([Prop(Door)], _ => Triangle(x: 1f));

        scene.LevelShutdownPreEntity();

        models.Vertices.ShouldBeEmpty("the scene's level teardown must flush the model set");
    }

    /// <remarks>
    /// **A stationary wearer's lighting is sampled once, not once per item per frame** (the
    /// outside audit's finding 6). The worn branch reads the wearer's light point and asked the
    /// sampler EVERY frame — a code comment had already named the missing cache as a defect
    /// (B189) — where `ModelLighting.For`'s contract is the engine's: a model at the identical
    /// point cannot have changed brightness, so the sample is keyed on the entity and the exact
    /// point and re-taken only when the point moves.
    /// </remarks>
    [Test]
    public void Instances_AStationaryWearerAcrossTwoFrames_SamplesWornLightingOnce()
    {
        EntityModelSet models = Models();

        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            new(7, "models/player/scout.mdl",
                ScenePropTrack.Classify("models/player/scout.mdl"),
                new ScenePose { X = 500f }),
            new(40, "models/player/items/hat.mdl",
                ScenePropTrack.Classify("models/player/items/hat.mdl"),
                new ScenePose(), AttachedTo: 7, BoneMerged: true),
        ];

        models.Add(props, _ => Triangle(x: 1f));

        int sampled = 0;

        PointLighting Count(float x, float y, float z)
        {
            sampled++;

            return PointLighting.None;
        }

        models.Instances(props, instances, Count);

        int first = sampled;

        first.ShouldBeGreaterThan(0, "the first frame must sample, or the control proves nothing");

        models.Instances(props, instances, Count);

        sampled.ShouldBe(first, "nothing moved, so the second frame re-samples nothing");
    }

    /// <remarks>
    /// **The POINT, not the count — this is the assertion the cache tests cannot make.** A
    /// bone-merged item's own pose is the map origin, and sampling there was cached too, so a
    /// counting test passes whichever point is used. The variable is WHERE: every quantity — the
    /// cube and, crucially, the SUN, which the old downstream override never covered — must be
    /// sampled where the wearer stands, and nothing may be sampled at the origin.
    /// </remarks>
    [Test]
    public void Instances_ABoneMergedItem_IsLitAndSunnedAtItsWearersPoint()
    {
        EntityModelSet models = Models();

        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            new(7, "models/player/scout.mdl",
                ScenePropTrack.Classify("models/player/scout.mdl"),
                new ScenePose { X = 500f, Y = 320f, Z = 64f }),
            new(40, "models/player/items/hat.mdl",
                ScenePropTrack.Classify("models/player/items/hat.mdl"),
                new ScenePose(), AttachedTo: 7, BoneMerged: true),
        ];

        models.Add(props, _ => Triangle(x: 1f));

        List<(float X, float Y, float Z)> litAt = [];
        List<(float X, float Y, float Z)> sunnedAt = [];

        models.Instances(
            props,
            instances,
            (x, y, z) =>
            {
                litAt.Add((x, y, z));

                return PointLighting.None;
            },
            (x, y, z) =>
            {
                sunnedAt.Add((x, y, z));

                return null;
            });

        static bool AtTheWearer((float X, float Y, float Z) point) =>
            MathF.Abs(point.X - 500f) < 0.5f && MathF.Abs(point.Y - 320f) < 0.5f;

        static bool AtTheOrigin((float X, float Y, float Z) point) =>
            MathF.Abs(point.X) < 0.5f && MathF.Abs(point.Y) < 0.5f && MathF.Abs(point.Z) < 0.5f;

        litAt.Exists(AtTheWearer)
            .ShouldBeTrue("the hat's cube must be sampled where its wearer stands");

        sunnedAt.Exists(AtTheWearer)
            .ShouldBeTrue("the hat's SUN must be sampled there too - the old override never covered it");

        litAt.Exists(AtTheOrigin)
            .ShouldBeFalse("nothing may be lit at the map origin, where a merged item's own pose sits");

        sunnedAt.Exists(AtTheOrigin)
            .ShouldBeFalse("nor traced for sky visibility from it");
    }

    /// <remarks>
    /// The control for the cache above: a wearer who MOVED must be re-sampled, because the leaf
    /// under him changed. A cache keyed on the entity alone would pass the stationary test and
    /// light a walking player by wherever he stood first.
    /// </remarks>
    [Test]
    public void Instances_AWearerWhoMoved_IsSampledAgain()
    {
        EntityModelSet models = Models();

        List<ModelInstance> instances = [];

        static SceneProp[] At(float x) =>
        [
            new(7, "models/player/scout.mdl",
                ScenePropTrack.Classify("models/player/scout.mdl"),
                new ScenePose { X = x }),
            new(40, "models/player/items/hat.mdl",
                ScenePropTrack.Classify("models/player/items/hat.mdl"),
                new ScenePose(), AttachedTo: 7, BoneMerged: true),
        ];

        models.Add(At(500f), _ => Triangle(x: 1f));

        int sampled = 0;

        PointLighting Count(float x, float y, float z)
        {
            sampled++;

            return PointLighting.None;
        }

        models.Instances(At(500f), instances, Count);

        int first = sampled;

        models.Instances(At(900f), instances, Count);

        sampled.ShouldBeGreaterThan(first, "the wearer moved, so his light must be re-sampled");
    }

    /// <remarks>
    /// **The growth case.** Every load packs into one vertex list; without the flush the list only
    /// ever grows, so a session that walks a playlist accumulates every map it has ever shown.
    /// Three cycles of the same map must cost what one costs.
    /// </remarks>
    [Test]
    public void LevelShutdown_TheSameMapLoadedThreeTimes_DoesNotGrowThePackedSet()
    {
        EntityModelSet models = Models();

        models.Add([Prop(Door)], _ => Triangle(x: 1f));

        int first = models.Vertices.Count;

        for (int reload = 0; reload < 2; reload++)
        {
            models.LevelShutdown();

            models.Add([Prop(Door)], _ => Triangle(x: 1f));
        }

        models.Vertices.Count.ShouldBe(first, "a reload replaces the packed set, never extends it");
    }
}
