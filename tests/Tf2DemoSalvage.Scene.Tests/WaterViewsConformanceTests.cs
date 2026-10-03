using System.Text;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The engine's water views — which of them run and with what flags — transcribed from
/// <c>game/client/viewrender.cpp</c> (B62).
/// </summary>
/// <remarks>
/// **Written off the SDK before anything implemented it.** Every expectation names the line it was read from:
/// <c>DetermineWaterRenderInfo</c> (<c>:2488-2647</c>), <c>DrawWorldAndEntities</c> (<c>:2652-2718</c>), the view
/// classes' <c>Setup</c>/<c>Draw</c> (<c>:5705-6249</c>), <c>PushView</c> (<c>:5295-5372</c>), and the draw-list
/// flags (<c>:696-749</c>, <c>ivrenderview.h:36-58</c>). The material's own defaults are <c>water.cpp</c>'s
/// <c>SHADER_INIT_PARAMS</c>, which run before the view reads them.
/// </remarks>
public sealed class WaterViewsConformanceTests
{
    private static WaterMaterialParameters Material(
        bool translucent = false,
        bool forceCheap = false,
        bool forceExpensive = true,
        bool reflect = true,
        bool refract = true,
        bool reflectEntities = false) =>
        new(translucent, forceCheap, forceExpensive, reflect, refract, reflectEntities);

    private static WaterRenderInfo Determine(
        WaterMaterialParameters? material, float distance = 0f, float cheapEnd = 0.1f, WaterConVars? cvars = null) =>
        WaterRenderInfo.Determine(material, distance, cheapEnd, cvars ?? WaterConVars.Defaults);

    private static VmtMaterial Vmt(string body) => VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Water\"\n{\n" + body + "\n}"));

    // ---- water.cpp SHADER_INIT_PARAMS: what the view sees after the shader initialised the material ----

    [Test]
    public void From_ForceExpensiveUndeclared_DefaultsToOneOnPc()
    {
        // water.cpp: "By default, we're force expensive on dx9.  NO WE DON'T!!!!" — and then sets it to 1 off the X360.
        WaterMaterialParameters read = WaterMaterialParameters.From(Vmt("\"$refracttexture\" \"_rt_WaterRefraction\""));

        read.ForceExpensive.ShouldBeTrue();
        read.ForceCheap.ShouldBeFalse();
        read.RefractTexture.ShouldBeTrue();
        read.ReflectTexture.ShouldBeFalse();
    }

    [Test]
    public void From_BothForcesDeclared_DropsExpensive()
    {
        // water.cpp: if( FORCEEXPENSIVE && FORCECHEAP ) FORCEEXPENSIVE = 0.
        WaterMaterialParameters read = WaterMaterialParameters.From(Vmt("\"$forcecheap\" 1\n\"$forceexpensive\" 1"));

        read.ForceCheap.ShouldBeTrue();
        read.ForceExpensive.ShouldBeFalse();
    }

    [Test]
    public void From_ExpensiveDeclaredZero_IsNotForced()
    {
        WaterMaterialParameters read = WaterMaterialParameters.From(
            Vmt("\"$forceexpensive\" 0\n\"$reflecttexture\" \"_rt_WaterReflection\"\n\"$reflectentities\" 1"));

        read.ForceExpensive.ShouldBeFalse();
        read.ReflectTexture.ShouldBeTrue();
        read.ReflectEntities.ShouldBeTrue();
    }

    // ---- DetermineWaterRenderInfo, viewrender.cpp:2488 ----

    [Test]
    public void Determine_NoVisibleFogVolume_IsCheapOpaqueAndDrawsNoSurface()
    {
        // :2493-2504 — the defaults, returned untouched when there is no fog volume.
        Determine(null).ShouldBe(new WaterRenderInfo(
            CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false, DrawWaterSurface: false, OpaqueWater: true));
    }

    [Test]
    public void Determine_MatDrawWaterOff_IsCheapAndNotOpaque()
    {
        // :2508-2513.
        Determine(Material(), cvars: WaterConVars.Defaults with { MatDrawWater = false }).ShouldBe(new WaterRenderInfo(
            CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false, DrawWaterSurface: false, OpaqueWater: false));
    }

    [Test]
    public void Determine_ExpensiveWithBothTextures_ReflectsAndRefractsAtAnyDistance()
    {
        // :2578-2587 local reflection; :2606 the LOD exit is skipped by bLocalReflection; :2617-2627.
        Determine(Material(), distance: 5000f).ShouldBe(new WaterRenderInfo(
            CheapWater: false, Refract: true, Reflect: true, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false));
    }

    [Test]
    public void Determine_RefractOnlyBeyondTheLodEnd_IsCheapAndOpaque()
    {
        // :2606 — without a local reflection, distance ≥ m_flCheapWaterEndDistance returns before refraction.
        Determine(Material(reflect: false), distance: 0.1f, cheapEnd: 0.1f).ShouldBe(new WaterRenderInfo(
            CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: true));
    }

    [Test]
    public void Determine_RefractOnlyInsideTheLodEnd_Refracts()
    {
        Determine(Material(reflect: false), distance: 999f, cheapEnd: 1000f).ShouldBe(new WaterRenderInfo(
            CheapWater: false, Refract: true, Reflect: false, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false));
    }

    [Test]
    public void Determine_ForceCheap_IsCheapEvenWithAReflection()
    {
        // :2552-2558 then :2606 "|| bForceCheap".
        Determine(Material(forceCheap: true, forceExpensive: false)).CheapWater.ShouldBeTrue();
    }

    [Test]
    public void Determine_ReflectTextureWithoutExpensive_DoesNotReflect()
    {
        // :2578 — !bForceExpensive turns the local reflection off.
        WaterRenderInfo info = Determine(Material(forceExpensive: false), distance: 0f, cheapEnd: 1000f);

        info.Reflect.ShouldBeFalse();
        info.Refract.ShouldBeTrue();
    }

    [Test]
    public void Determine_ConVarForcesExpensive_OverAMaterialThatDeclaresZero()
    {
        // :2518 then :2562 — "bForceExpensive || $forceexpensive".
        Determine(Material(forceExpensive: false), cvars: WaterConVars.Defaults with { ForceExpensive = true })
            .Reflect.ShouldBeTrue();
    }

    [Test]
    public void Determine_ReflectionConVarOff_DoesNotReflect()
    {
        Determine(Material(), cvars: WaterConVars.Defaults with { DrawReflection = false }, cheapEnd: 1000f)
            .Reflect.ShouldBeFalse();
    }

    [Test]
    public void Determine_RefractionConVarOff_StaysOpaque()
    {
        // :2611-2614 — no refraction, so the :2621 "can be seen through" never clears m_bOpaqueWater.
        WaterRenderInfo info = Determine(Material(), cvars: WaterConVars.Defaults with { DrawRefraction = false });

        info.Refract.ShouldBeFalse();
        info.OpaqueWater.ShouldBeTrue();
        info.CheapWater.ShouldBeFalse();
    }

    [Test]
    public void Determine_TranslucentMaterial_IsNotOpaque()
    {
        // :2541.
        Determine(Material(translucent: true, reflect: false), distance: 9f, cheapEnd: 1f).OpaqueWater.ShouldBeFalse();
    }

    [Test]
    public void Determine_ReflectEntities_FromTheMaterialOrTheConVar()
    {
        // :2628-2639.
        Determine(Material(reflectEntities: true)).ReflectEntities.ShouldBeTrue();
        Determine(Material(), cvars: WaterConVars.Defaults with { ForceReflectEntities = true }).ReflectEntities.ShouldBeTrue();
        Determine(Material()).ReflectEntities.ShouldBeFalse();
    }

    // ---- the views, DrawWorldAndEntities :2674-2717 ----

    private static readonly WaterFrame Above = new(
        EyeInFogVolume: false, DrawSkybox: true, WaterHeight: 64f, ViewIntersectsWater: false);

    [Test]
    public void Plan_CheapWaterOpaque_IsOneSimpleViewOfTheEyesSide()
    {
        // :2689 CSimpleWorldView; :5710-5741.
        WaterRenderInfo info = Determine(Material(reflect: false), distance: 9f, cheapEnd: 1f);

        WaterView only = WaterViews.Plan(info, Above, mainClear: ViewClearFlags.Depth).ShouldHaveSingleItem();

        only.Kind.ShouldBe(WaterViewKind.Simple);
        only.Draw.ShouldBe(
            ViewDrawFlags.DrawEntities | ViewDrawFlags.RenderAboveWater | ViewDrawFlags.RenderWater | ViewDrawFlags.DrawSkybox);
        only.Clear.ShouldBe(ViewClearFlags.Depth);
        only.Fog.ShouldBe(WaterViewFog.World);
        only.Clip.ShouldBe(HeightClip.None);
    }

    [Test]
    public void Plan_CheapWaterEyeUnder_DrawsUnderwaterInWaterFogWithoutSky()
    {
        // :5724-5726, :5738 (no skybox from inside), :5770-5781 (clear to the volume's fog colour).
        WaterRenderInfo info = Determine(Material(reflect: false), distance: 9f, cheapEnd: 1f);

        WaterView only = WaterViews.Plan(info, Above with { EyeInFogVolume = true }, ViewClearFlags.Depth).ShouldHaveSingleItem();

        only.Draw.ShouldBe(ViewDrawFlags.DrawEntities | ViewDrawFlags.RenderUnderWater | ViewDrawFlags.RenderWater);
        only.Clear.ShouldBe(ViewClearFlags.Depth | ViewClearFlags.Color);
        only.Fog.ShouldBe(WaterViewFog.Volume);
        only.ClearToFogColor.ShouldBeTrue();
    }

    [Test]
    public void Plan_CheapWaterIntersectingTheNearPlane_DrawsBothSides()
    {
        // :5718-5722 "have to draw both sides if we can see both."
        WaterRenderInfo info = Determine(Material(reflect: false), distance: 9f, cheapEnd: 1f);

        WaterViews.Plan(info, Above with { ViewIntersectsWater = true }, ViewClearFlags.Depth)[0].Draw
            .ShouldBe(ViewDrawFlags.DrawEntities | ViewDrawFlags.RenderUnderWater | ViewDrawFlags.RenderAboveWater |
                      ViewDrawFlags.RenderWater | ViewDrawFlags.DrawSkybox);
    }

    [Test]
    public void Plan_AboveExpensive_IsReflectionThenRefractionThenMain()
    {
        // :5908-5949 — the order AddViewToScene draws them in.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above, ViewClearFlags.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.Reflection, WaterViewKind.Refraction, WaterViewKind.Main]);

        // :5974-5988 — no entities unless $reflectentities.
        views[0].Draw.ShouldBe(ViewDrawFlags.RenderReflection | ViewDrawFlags.ClipZ | ViewDrawFlags.ClipBelow |
                               ViewDrawFlags.RenderAboveWater | ViewDrawFlags.DrawSkybox);
        views[0].Clear.ShouldBe(ViewClearFlags.Depth);
        views[0].Fog.ShouldBe(WaterViewFog.World);
        views[0].Target.ShouldBe(WaterViewTarget.Reflection);

        // :5308-5316, :5297-5305 — DF_CLIP_BELOW renders above the height, and no fudge-up spreads it DOWN by 2.
        views[0].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        // :6037-6041.
        views[1].Draw.ShouldBe(ViewDrawFlags.RenderRefraction | ViewDrawFlags.ClipZ | ViewDrawFlags.RenderUnderWater |
                               ViewDrawFlags.FudgeUp | ViewDrawFlags.DrawEntities);
        views[1].Clear.ShouldBe(ViewClearFlags.Color | ViewClearFlags.Depth);
        views[1].Fog.ShouldBe(WaterViewFog.VolumeHeight);
        views[1].ClearToFogColor.ShouldBeTrue();
        views[1].Target.ShouldBe(WaterViewTarget.Refraction);
        views[1].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 66f));

        // :5869-5889 — refraction is on, so the main view leaves the under-water world to it.
        views[2].Draw.ShouldBe(ViewDrawFlags.RenderAboveWater | ViewDrawFlags.DrawEntities | ViewDrawFlags.DrawSkybox |
                               ViewDrawFlags.RenderWater);
        views[2].Clear.ShouldBe(ViewClearFlags.Depth);
        views[2].Target.ShouldBe(WaterViewTarget.BackBuffer);
        views[2].Clip.ShouldBe(HeightClip.None);
    }

    [Test]
    public void Plan_AboveReflectingEntities_ReflectionDrawsThem()
    {
        Determine(Material(reflectEntities: true)).ShouldSatisfyAllConditions(info =>
            WaterViews.Plan(info, Above, ViewClearFlags.Depth)[0].Draw.HasFlag(ViewDrawFlags.DrawEntities).ShouldBeTrue());
    }

    [Test]
    public void Plan_AboveRefractionIntersectingTheNearPlane_ClipsMainBelowAndAddsTheIntersectionView()
    {
        // :5924, :5939-5944 (hardware clip planes), :5958-5961, :6085, :6094-6098.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above with { ViewIntersectsWater = true }, ViewClearFlags.Depth)];

        views.Select(view => view.Kind).ShouldBe(
            [WaterViewKind.Reflection, WaterViewKind.Refraction, WaterViewKind.Main, WaterViewKind.Intersection]);

        views[2].Draw.HasFlag(ViewDrawFlags.ClipZ | ViewDrawFlags.ClipBelow).ShouldBeTrue();
        views[2].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        views[3].Draw.ShouldBe(ViewDrawFlags.RenderUnderWater | ViewDrawFlags.ClipZ | ViewDrawFlags.DrawEntities);
        views[3].Fog.ShouldBe(WaterViewFog.VolumeHeight);
        views[3].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 62f));
        views[3].Target.ShouldBe(WaterViewTarget.BackBuffer);
    }

    [Test]
    public void Plan_AboveReflectOnlyNoSkybox_ClearsColourAndDrawsTheUnderwaterSideItself()
    {
        // :5886-5889 (translucent, no refraction → DF_RENDER_UNDERWATER) and :5927-5930 (no refraction, no skybox).
        WaterRenderInfo info = Determine(Material(translucent: true), cvars: WaterConVars.Defaults with { DrawRefraction = false });
        WaterView[] views = [.. WaterViews.Plan(info, Above with { DrawSkybox = false }, ViewClearFlags.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.Reflection, WaterViewKind.Main]);
        views[1].Draw.ShouldBe(ViewDrawFlags.RenderAboveWater | ViewDrawFlags.DrawEntities | ViewDrawFlags.RenderWater |
                               ViewDrawFlags.RenderUnderWater);
        views[1].Clear.ShouldBe(ViewClearFlags.Depth | ViewClearFlags.Color);
    }

    [Test]
    public void Plan_UnderExpensive_IsRefractionIntoTheBackBufferThenMain()
    {
        // :6167-6183; :6206-6215; :6247-6248 the copy out.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above with { EyeInFogVolume = true }, ViewClearFlags.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.UnderRefraction, WaterViewKind.UnderMain]);

        views[0].Draw.ShouldBe(ViewDrawFlags.ClipZ | ViewDrawFlags.ClipBelow | ViewDrawFlags.RenderAboveWater |
                               ViewDrawFlags.DrawEntities | ViewDrawFlags.DrawSkybox | ViewDrawFlags.ClipSkybox);
        views[0].Clear.ShouldBe(ViewClearFlags.Depth | ViewClearFlags.Color);
        views[0].Target.ShouldBe(WaterViewTarget.BackBufferCopiedToRefraction);
        views[0].Fog.ShouldBe(WaterViewFog.World);
        views[0].ClearToFogColor.ShouldBeTrue();
        views[0].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        // :6133-6147 — hardware clip, so DF_CLIP_Z; refraction is on, so no DF_RENDER_ABOVEWATER.
        views[1].Draw.ShouldBe(ViewDrawFlags.FudgeUp | ViewDrawFlags.RenderUnderWater | ViewDrawFlags.DrawEntities |
                               ViewDrawFlags.ClipZ | ViewDrawFlags.RenderWater);
        views[1].Clear.ShouldBe(ViewClearFlags.Depth);
        views[1].Fog.ShouldBe(WaterViewFog.Volume);
        views[1].ClearToFogColor.ShouldBeFalse();
        views[1].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 66f));
    }

    [Test]
    public void Plan_UnderWithoutRefraction_ClearsToTheVolumesFogColour()
    {
        // :6173-6179.
        WaterRenderInfo info = Determine(Material(translucent: true), cvars: WaterConVars.Defaults with { DrawRefraction = false });
        WaterView only = WaterViews.Plan(info, Above with { EyeInFogVolume = true }, ViewClearFlags.Depth).ShouldHaveSingleItem();

        only.Kind.ShouldBe(WaterViewKind.UnderMain);
        only.ClearToFogColor.ShouldBeTrue();
        only.Draw.HasFlag(ViewDrawFlags.RenderAboveWater).ShouldBeTrue();
    }

    [Test]
    public void Plan_MatClipZOff_DisablesEveryHeightClip()
    {
        // :5308 — "( m_DrawFlags & DF_CLIP_Z ) && mat_clipz.GetBool()".
        WaterRenderInfo info = Determine(Material());

        WaterViews.Plan(info, Above, ViewClearFlags.Depth, WaterConVars.Defaults with { MatClipZ = false })
            .ShouldAllBe(view => view.Clip == HeightClip.None);
    }

    // ---- BuildEngineDrawWorldListFlags :696 and the sort groups ivrenderview.h:50 ----

    [TestCase(ViewDrawFlags.RenderAboveWater, new[] { 0, 2 })]
    [TestCase(ViewDrawFlags.RenderUnderWater, new[] { 1, 2 })]
    [TestCase(ViewDrawFlags.RenderWater, new[] { 3 })]
    [TestCase(ViewDrawFlags.RenderAboveWater | ViewDrawFlags.RenderUnderWater | ViewDrawFlags.RenderWater, new[] { 0, 1, 2, 3 })]
    [TestCase(ViewDrawFlags.DrawEntities, new int[0])]
    public void SortGroups_ForADrawFlagSet_AreTheEnginesWorldListGroups(ViewDrawFlags flags, int[] groups) =>
        Enumerable.Range(0, VisibleWorld.SortGroups).Where(group => WaterViews.DrawsSortGroup(flags, group)).ShouldBe(groups);

    // ---- AdjustView :5265-5285, the mirrored camera ----

    [Test]
    public void Reflect_AnEye_MirrorsOriginAcrossTheWaterAndNegatesPitchAndRoll()
    {
        ((float X, float Y, float Z) origin, (float Pitch, float Yaw, float Roll) angles) =
            WaterViews.Reflect((10f, 20f, 100f), (30f, 45f, 5f), waterHeight: 64f);

        origin.ShouldBe((10f, 20f, 28f));
        angles.ShouldBe((-30f, 45f, -5f));
    }
}
