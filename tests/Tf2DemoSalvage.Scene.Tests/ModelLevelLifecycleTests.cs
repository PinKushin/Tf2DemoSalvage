using System.Collections.Generic;

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
