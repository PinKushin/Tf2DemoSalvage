using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The main view's pruned list (B711, D251): its translucent pass walks pruned places, and each reaches the world list by
/// <c>m_pActualLeafIndex</c> (`viewrender.cpp:4307`, `:3574`).
/// </summary>
public sealed class ViewHalfMainPrunedTests
{
    [Test]
    public void MainActual_WithAPrunedList_IsThePlacesIndexInTheWorldList()
    {
        WaterPlan plan = new() { MainPruned = ([7, 3], [1, 4]) };

        plan.MainActual(0).ShouldBe(1);
        plan.MainActual(1).ShouldBe(4);
    }

    [Test]
    public void MainActual_PastThePrunedList_IsThePlaceAsItIs()
    {
        WaterPlan plan = new() { MainPruned = ([7, 3], [1, 4]) };

        plan.MainActual(5).ShouldBe(5, "a stale entity place reaches no other leaf's runs");
    }

    /// <remarks>
    /// **A cloak pass on screen reads the frame buffer** (`vertexlitgeneric_dx9.cpp:286-301`: `$cloakfactor` strictly inside
    /// (0, 1)), so a water view skips it (`viewrender.cpp:4614-4617`); fully cloaked or uncloaked it does not.
    /// </remarks>
    [TestCase(0.5f, true)]
    [TestCase(0f, false)]
    [TestCase(1f, false)]
    public void ReadsFrameBuffer_AtACloakFactor_IsTrueOnlyMidCloak(float factor, bool reads) =>
        Device3D.ReadsFrameBuffer(new ModelInstance("m.mdl", [], null, null, Cloak: new CloakBind(factor, 0f)), materialsNeed: false)
            .ShouldBe(reads);

    /// <remarks>
    /// **A sheen pass or a frame-buffer shader answers true with no per-frame test** (`vertexlitgeneric_dx9.cpp:286-301`):
    /// uncloaked, the model is still skipped in a water view.
    /// </remarks>
    /// <remarks>
    /// **The reflection fades a detail model from its mirrored origin** (`BuildDetailObjectRenderLists( origin )` after
    /// `AdjustView`; `EnumerateLeaf`, `detailobjectsystem.cpp:2739-2775`). `cl_detaildist` 1200, `cl_detailfade` 400: at 1000
    /// units the squared distance is 1,000,000, inside the band from 640,000 to 1,440,000, so 440,000 × 255 / 800,000 = 140.
    /// The control, 100 units away, is inside the fade start: 255.
    /// </remarks>
    [TestCase(0f, 140)]
    [TestCase(900f, 255)]
    public void ForView_AnEyeBelowTheModel_IsTheFadeAtThatDistance(float eyeZ, int expected) =>
        ((int)DetailModels.ForView(
            new Tf2DemoSalvage.Content.Bsp.BspDetailProp(
                (0f, 0f, 1000f), default, 0, 0, Tf2DemoSalvage.Content.Bsp.DetailPropType.Model, 0, 0, 0, 0, 1f),
            (0f, 0f, eyeZ),
            DetailFade.For(1200f, 400f)).Alpha).ShouldBe(expected);

    /// <remarks>
    /// **The materials asked are the ones the entity shows** (`IsUsingFBTexture( model, skin, body, … )`): a batch's skinref is
    /// swapped by the skin, and a body part's alternative not selected by the body is not shown.
    /// </remarks>
    [Test]
    public void AnyShownMaterial_SwappedBySkinAndGatedByBody_AsksTheShownMaterial()
    {
        // One part with two alternatives; the batch is alternative 1 of slot 3, whose material 3 the skin swaps for 7.
        WorldBatch[] batches = [new(MaterialIndex: 3, FirstVertex: 0, VertexCount: 3, BodyPart: 0, BodyModel: 1, MaterialSlot: 3)];
        (int Base, int Count)[] parts = [(1, 2)];
        Dictionary<int, int> skin = new() { [3] = 7 };

        WorldRenderer.AnyShownMaterial(batches, skin, parts, body: 1, material => material == 7).ShouldBeTrue("swapped and shown");
        WorldRenderer.AnyShownMaterial(batches, null, parts, body: 1, material => material == 7).ShouldBeFalse("its own material is 3");
        WorldRenderer.AnyShownMaterial(batches, skin, parts, body: 0, material => material == 7).ShouldBeFalse("body 0 hides it");
    }

    [Test]
    public void ReadsFrameBuffer_UncloakedWithAMaterialThatNeedsIt_IsTrue() =>
        Device3D.ReadsFrameBuffer(new ModelInstance("m.mdl", [], null, null), materialsNeed: true).ShouldBeTrue();

    /// <remarks>
    /// **Every water view interleaves its translucent world by its own pruned list** (`DrawTranslucentRenderables`,
    /// `viewrender.cpp:4547-4695`, B711): the monitor's views but the reflection walk the monitor's world list, whose
    /// translucent runs its draw installs, and its reflection its own cull's.
    /// </remarks>
    [TestCase(false, WaterViewKind.Refraction, true)]
    [TestCase(true, WaterViewKind.Refraction, true)]
    [TestCase(true, WaterViewKind.Reflection, true)]
    public void PrunedActual_AWaterViewOfAPlan_IsItsPrunedList(
        bool monitor, WaterViewKind kind, bool interleaves)
    {
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: true, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: false);
        WaterView view = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth).First(each => each.Kind == kind);
        WaterPlan plan = new() { Monitor = monitor };

        plan.WaterPruned.Add((view, [4, 2]));

        (plan.PrunedActual(view) is not null).ShouldBe(interleaves);
    }

    /// <remarks>
    /// **A water view draws the shadows cut on its own world list** (B702): the main view's views but the reflection draw
    /// its list, the monitor's the monitor's, and each reflection its own cull's — `RenderShadows` inside that view's
    /// `R_DrawWorldLists` (B711).
    /// </remarks>
    [TestCase(false, false, "World")]
    [TestCase(false, true, "Reflection")]
    [TestCase(true, false, "Monitor")]
    [TestCase(true, true, "MonitorReflection")]
    public void WaterViewShadows_AViewOfAPlan_IsTheListCutOnItsWorld(bool monitor, bool reflection, string list) =>
        WorldRenderer.WaterViewShadows(monitor, reflection).ToString().ShouldBe(list);

    /// <remarks>
    /// **The monitor's water views are collated first, the main view's after** (B711): `DrawOneMonitor` runs before the scene
    /// (`viewrender.cpp:2074-2081`) and its `DrawWorldAndEntities` draws its own water views. Each plan finds its views' entries
    /// from where its own begin; a monitor that does not draw this frame collates none, and its plan finds none.
    /// </remarks>
    [TestCase(true, 0, 2, new[] { 10, 11, 20 })]
    [TestCase(false, null, 0, new[] { 20 })]
    public void Collate_TheMonitorsAndTheMainViewsWaterViews_ListsTheMonitorsFirstAndKeepsWhereEachBegins(
        bool monitorDraws, int? monitorFrom, int mainFrom, int[] leaves)
    {
        WaterPlan monitor = new() { Monitor = true };
        WaterPlan main = new();
        List<RenderableView> collated = [new([99], default)];

        monitor.WaterRenderables.Add(new RenderableView([10], default));
        monitor.WaterRenderables.Add(new RenderableView([11], default));
        main.WaterRenderables.Add(new RenderableView([20], default));

        int monitors = WaterPlan.Collate(collated, monitor, monitorDraws, main);

        collated.Select(static view => view.Leaves[0]).ShouldBe(leaves);
        monitors.ShouldBe(monitorDraws ? 2 : 0);
        monitor.CollatedFrom.ShouldBe(monitorFrom);
        main.CollatedFrom.ShouldBe(mainFrom);
    }

    /// <remarks>
    /// **A renderable is collated at the first leaf of the view's list its box touches** (`BuildRenderablesList` walks the
    /// list in order, and `CollateRenderablesInLeaf` takes a renderable once a frame, `clientleafsystem.cpp:1574-1600`,
    /// `:1813`, B711): the monitor's force-drawn local player is placed so, and a box in no listed leaf is not collated.
    /// </remarks>
    [TestCase(new[] { 9, 3 }, 1)]
    [TestCase(new[] { 5 }, 0)]
    [TestCase(new[] { 7 }, -1)]
    public void FirstListedPlace_ABoxTouchingSomeLeaves_IsTheFirstOfThemInTheList(int[] touched, int place) =>
        Device3D.FirstListedPlace([5, 3, 9], new HashSet<int>(touched)).ShouldBe(place);

    /// <remarks>
    /// **A view collates the detail models of each listed leaf at that leaf's place** (`CollateRenderablesInLeaf`,
    /// `clientleafsystem.cpp:1718-1751`, B711): a leaf's own, in build order, and none from a leaf it does not list. A map
    /// without a tree has one place, which holds them all.
    /// </remarks>
    [Test]
    public void CollateDetailModels_TwoListedLeavesAndAnUnlistedOne_PlacesEachLeafsModelsAtItsPlace()
    {
        Dictionary<int, List<int>> byLeaf = new() { [3] = [0], [5] = [1, 2], [7] = [3] };
        List<(int Place, int Index)> into = [(9, 9)];

        Device3D.CollateDetailModels([5, 3], byLeaf, 4, everyAtZero: false, into);

        into.ShouldBe([(0, 1), (0, 2), (1, 0)]);

        Device3D.CollateDetailModels([0], byLeaf, 4, everyAtZero: true, into);

        into.ShouldBe([(0, 0), (0, 1), (0, 2), (0, 3)], "the control: one leaf holds every model");
    }

    /// <remarks>
    /// **One under-water overlay a frame, the last one set** (B711): `RenderView` shuts it down first (`viewrender.cpp:2030`),
    /// each `CUnderWaterView::Setup` sets it on the one `CViewRender` (`m_pMainView->SetWaterOverlayMaterial`, `:6115-6128`),
    /// the monitors draw before the main view (`:2074-2081`), and `DrawUnderwaterOverlay` draws it over the main screen after
    /// the viewmodels (`:2160-2162`). So a monitor under water lends the main screen its overlay unless the main view sets
    /// one; the control, a monitor that did not draw, sets none.
    /// </remarks>
    [TestCase("main", "monitor", true, "main")]
    [TestCase(null, "monitor", true, "monitor")]
    [TestCase(null, "monitor", false, null)]
    [TestCase(null, null, true, null)]
    public void FrameUnderwaterOverlay_TheMainAndTheMonitorsOwn_IsTheLastSet(
        string? main, string? monitor, bool monitorDrew, string? expected)
    {
        static ScreenOverlayMaterial? Overlay(string? name) =>
            name is null ? null : new ScreenOverlayMaterial(name, null, [], new Dictionary<string, (float, float, float)>());

        (Device3D.FrameUnderwaterOverlay(Overlay(main), Overlay(monitor), monitorDrew)?.Name).ShouldBe(expected);
    }

    [Test]
    public void MainActual_UnprunedAndAfterTheWaterPlanIsCleared_IsThePlaceItself()
    {
        ViewHalf half = new();
        half.Water.MainPruned = ([7, 3], [1, 4]);
        half.MonitorWater.MainPruned = ([7, 3], [1, 4]);

        half.ClearWater();

        half.Water.MainActual(1).ShouldBe(1);
        half.MonitorWater.MainActual(1).ShouldBe(1, "the monitor's plan is forgotten with the main one's");
    }
}
