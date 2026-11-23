using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A packed model's vertices rebuilt from what the set keeps, for a decal on a skinned model (finding 80).</summary>
/// <remarks>
/// **Why it exists.** `Device3D` kept its own copy of every skinned model's packed vertices for one reader, the decal a
/// bullet leaves on a player. That was 623 MB on f12. The set already keeps each model's corners and its packing order,
/// and the engine's decal projects onto the model's own mesh (`CStudioRender::AddDecal`). Rebuilt on demand, the copy can
/// go. The rebuild must be the packing exactly, so the oracle is the list the set packed before it let it go.
/// </remarks>
public sealed class PackedVerticesTests
{
    private const string Path = "models/player/test.mdl";

    /// <summary>Four corners in two materials, interleaved, so the packing reorders them; distinct values everywhere.</summary>
    private static PropModels.ModelFrames Skinned(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 2f, 3f, 0.1f, 0.2f, MaterialIndex: 5, NormalX: 1f) { Weights = (1f, 0f, 0f), Bones = (2, 0, 0), TangentX = 0.5f },
                    new(4f, 5f, 6f, 0.3f, 0.4f, MaterialIndex: 7, NormalY: 1f) { Weights = (0.5f, 0.5f, 0f), Bones = (1, 3, 0), TangentW = -1f },
                    new(7f, 8f, 9f, 0.5f, 0.6f, MaterialIndex: 5, NormalZ: 1f) { Weights = (0.25f, 0.75f, 0f), Bones = (4, 1, 0) },
                    new(1.5f, 2.5f, 3.5f, 0.7f, 0.8f, MaterialIndex: 7) { Weights = (1f, 0f, 0f), Bones = (6, 0, 0) },
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);

    [Test]
    public void PackedVertices_AfterTheSetLetsItsCopyGo_AreTheVerticesItPacked()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache([Path]);
        List<WorldVertex> packed = [.. models.Vertices];
        models.Uploaded();
        models.ReleaseUploaded();

        models.Vertices.ShouldBeEmpty("the control: the set's own copy is gone");
        packed.Select(vertex => vertex.X).ShouldBe([1f, 7f, 4f, 1.5f], "the control: packing reordered the corners");
        models.PackedVertices(Path).ShouldBe(packed);
    }

    /// <remarks>
    /// **A corner's face edge survives the packing** (B803): a brush entity's surfaces are packed like a studio model's, and
    /// `mat_wireframe 3` outlines only the edges that are its polygon's (`engine.dll` `FUN_1800e6780`). Corners 0 and 2 say
    /// false here, and the packing puts them in buffer slots 0 and 1.
    /// </remarks>
    [Test]
    public void Precache_CornersMarkedAsFaceEdges_PackTheMarkInTheirOrder()
    {
        PropModels.ModelFrames model = Skinned(Path);
        PropModels.ModelFrames marked =
            model with { Geometry = [[.. model.Geometry[0].Select((corner, index) => corner with { FaceEdge = index is 1 or 3 })]] };
        EntityModelSet models = new() { Geometry = _ => marked };

        models.Precache([Path]);

        models.Vertices.Select(vertex => vertex.FaceEdge).ShouldBe([false, false, true, true]);
        models.Vertices.Select(vertex => vertex.X).ShouldBe([1f, 7f, 4f, 1.5f], "the control: packing reordered the corners");
    }

    /// <remarks>
    /// **Reserved once for the whole call, then filled** (finding 80: the list's growth by doubling was about 1.45 GB of
    /// large-object garbage over f12's precache). Three models of four corners: grown model by model, the list ends at a
    /// capacity of 16 for 12 vertices. `VertexBytes` is the list's capacity in bytes, filled or not, so it reads the
    /// reserve.
    /// </remarks>
    [Test]
    public void Precache_ThreeModels_ReservesExactlyTheVerticesItPacks()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache(["models/a.mdl", "models/b.mdl", "models/c.mdl"]);

        models.Vertices.Count.ShouldBe(12, "the control: all three models packed");
        models.VertexBytes.ShouldBe(12L * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldVertex>());
    }

    /// <remarks>
    /// **Rebuilt once per model struck, not once per decal** (finding 80: rebuilding per decal was about 40 MB of
    /// large-object garbage per 20 s of f12). A second decal on the same model gets the same list; a level's end lets
    /// it go with the model.
    /// </remarks>
    [Test]
    public void PackedVertices_ASecondDecalOnTheSameModel_IsTheListTheFirstGot()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache([Path]);
        IReadOnlyList<WorldVertex>? first = models.PackedVertices(Path);

        models.PackedVertices(Path).ShouldBeSameAs(first);
    }

    /// <remarks>
    /// **A model panel's flexed mesh is spread over the order the set packed** (B779): the panels reach the set as their
    /// <see cref="Hud.IMdlCache"/>, and a face in any other order moves the wrong vertices. Packing groups the corners by
    /// material, so buffer vertices 0..3 carry corners 0, 2, 1, 3; a model never packed answers none.
    /// </remarks>
    [Test]
    public void PackedOrder_APackedModel_IsTheCornerEachBufferVertexCarries()
    {
        Hud.IMdlCache models = new EntityModelSet { Geometry = Skinned };

        ((EntityModelSet)models).Precache([Path]);

        models.PackedOrder(Path).ShouldBe([0, 2, 1, 3]);
        models.PackedOrder("models/never.mdl").ShouldBeNull();
    }

    /// <remarks>
    /// **A panel's set-up loads its model in the call** (B779 review 15; `InitializeAsClientEntity`, basemodelpanel.cpp:407):
    /// a model nothing packed is packed by the ask, with its order. The control: the lookup alone found nothing.
    /// </remarks>
    [Test]
    public void LoadMdl_AModelNothingPacked_PacksItInTheCall()
    {
        EntityModelSet models = new() { Geometry = Skinned };
        models.FindMdl(Path).ShouldBeNull("the control: nothing packed it");

        models.LoadMdl(Path).ShouldNotBeNull();

        models.FindMdl(Path).ShouldNotBeNull();
        models.PackedOrder(Path).ShouldBe([0, 2, 1, 3]);
    }

    /// <remarks>
    /// **A panel's load packs that model alone** (B779 review 17 item 3): the engine's load brings one model in
    /// (`InitializeAsClientEntity`, basemodelpanel.cpp:407; `FindMDL`, mdlpanel.cpp:185). The static props pack once a level,
    /// after the moment's own props, at the first `Add` (finding 83 item 1) — a HUD event's load in a skip must not run
    /// that first, ahead of the moment. Here a level with one static prop: the panel's load packs its model and neither
    /// the static prop nor its lighting. The control: the moment's `Add` after it packs the static prop and lights it.
    /// </remarks>
    [Test]
    public void LoadMdl_ALevelWithStaticProps_PacksOnlyTheAskedModel()
    {
        const string still = "models/props/still.mdl";
        int lit = 0;
        EntityModelSet models = new()
        {
            Geometry = Skinned,
            StaticProps = [new SceneProp(7, still, ScenePropTrack.Classify(still), default)],
            StaticLightingPrecached = _ => lit++,
        };

        models.LoadMdl(Path).ShouldNotBeNull();

        (models.FindMdl(still), lit).ShouldBe((null, 0));

        models.Add([]);
        (models.FindMdl(still) is not null, lit).ShouldBe((true, 1), "the control: the moment's first Add packs the statics");
    }

    /// <remarks>
    /// **A model that fails to load is read once a level** (B779 review 15): `LoadMdl` packs through `PackCounted` (review
    /// 17), which remembers the failure until the level's end, so a panel asking every paint does not reach the loader again.
    /// The control: the first ask read it.
    /// </remarks>
    [Test]
    public void LoadMdl_AModelThatFailsToLoad_ReadsItOnce()
    {
        int reads = 0;
        EntityModelSet models = new() { Geometry = path => { reads++; return null; } };

        models.LoadMdl(Path).ShouldBeNull();
        int first = reads;
        models.LoadMdl(Path).ShouldBeNull();

        first.ShouldBe(1, "the control: the first ask read it");
        reads.ShouldBe(1);
    }

    /// <remarks>
    /// **A panel's model is packed again on the next level** (B779 review 2 item 1, now through the load in the call): the
    /// HUD outlives a level (`ViewerPresenter.ClearMap` reaches <see cref="EntityModelSet.LevelShutdown"/>), and a panel
    /// that keeps its path asks again at its next paint, so the next map packs it again with its order. The controls: the
    /// first level packed it, and the shutdown forgot it.
    /// </remarks>
    [Test]
    public void LoadMdl_AfterALevelShutdown_PacksTheModelAgain()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.LoadMdl(Path);
        models.PackedOrder(Path).ShouldNotBeNull("the control: the first level packed it");

        models.LevelShutdown();
        models.PackedOrder(Path).ShouldBeNull("the control: the shutdown forgot it");

        models.LoadMdl(Path).ShouldNotBeNull();
        models.PackedOrder(Path).ShouldBe([0, 2, 1, 3]);
    }

    /// <remarks>
    /// **A panel's ask costs nothing per paint once answered** (B779 review 3 item 3, now through the load in the call): a
    /// CMDL panel reads its model on every paint, so a packed model, a remembered failure and a path the packer refuses (no
    /// studio model: `CanDraw`, review 16 item 2) each answer without allocating. Least over eight asks after the first.
    /// The controls: the first asks packed one and failed the other two.
    /// </remarks>
    [Test]
    public void LoadMdl_APackedModelARememberedFailureAndARefusedPath_AllocateNothing()
    {
        const string missing = "models/missing.mdl";
        const string sprite = "sprites/glow01.vmt";
        EntityModelSet models = new() { Geometry = path => path == Path ? Skinned(path) : null };
        long least = long.MaxValue;

        models.LoadMdl(Path).ShouldNotBeNull("the control: packed");
        models.LoadMdl(missing).ShouldBeNull("the control: failed");
        models.LoadMdl(sprite).ShouldBeNull("the control: refused");

        for (int run = 0; run < 8; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            models.LoadMdl(Path);
            models.LoadMdl(missing);
            models.LoadMdl(sprite);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
    }

    /// <remarks>
    /// **A failure remembered before the level's loader is bound does not outlive the bind** (B779 review 20 item 5): a read
    /// between a level's end and the next map's `Geometry` asks a loader that has nothing (`NoGeometry`), and that answer is
    /// the gap's, not the file's. Binding the next loader forgets it, as B363's `Precache` forgets an empty entry. The
    /// controls: the gap's ask failed, and the loader alone has the model.
    /// </remarks>
    [Test]
    public void LoadMdl_AFailureBeforeTheLoaderIsBound_LoadsOnceItIs()
    {
        EntityModelSet models = new();

        models.LoadMdl(Path).ShouldBeNull("the control: nothing bound yet");
        int generation = models.LoaderGeneration;

        models.Geometry = Skinned;

        models.LoaderGeneration.ShouldBe(generation + 1);
        models.LoadMdl(Path).ShouldNotBeNull();
    }

    /// <remarks>
    /// **A refused path is not a model** (B779 review 18 item 6): remembered apart from the packed set, so `Count` and
    /// `Paths` — the "entity models: N packed" line and the device's upload — see the models alone. The control: the
    /// refused path is answered null.
    /// </remarks>
    [Test]
    public void LoadMdl_ARefusedPath_IsNotCountedAsAModel()
    {
        const string sprite = "sprites/glow01.vmt";
        EntityModelSet models = new() { Geometry = Skinned };

        int reads = 0;
        models.Geometry = path => { reads++; return Skinned(path); };
        models.LoadMdl(Path).ShouldNotBeNull("a real model still loads");
        int count = models.Count;
        int readsBefore = reads;
        models.LoadMdl(sprite).ShouldBeNull("the control");
        models.LoadMdl(sprite).ShouldBeNull();

        (models.Count, models.Paths.Contains(sprite), reads).ShouldBe((count, false, readsBefore), "refused without asking the loader");
        models.LoadMdl("models/other.mdl").ShouldNotBeNull("another real model still loads after the refusal");
    }
}
