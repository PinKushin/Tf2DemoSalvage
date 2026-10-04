using System;
using System.Linq;
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

        WaterView only = WaterViews.Plan(info, Above, mainClear: ViewClears.Depth).ShouldHaveSingleItem();

        only.Kind.ShouldBe(WaterViewKind.Simple);
        only.Draw.ShouldBe(
            ViewDraws.DrawEntities | ViewDraws.RenderAboveWater | ViewDraws.RenderWater | ViewDraws.DrawSkybox);
        only.Clear.ShouldBe(ViewClears.Depth);
        only.Fog.ShouldBe(WaterViewFog.World);
        only.Clip.ShouldBe(HeightClip.None);
    }

    [Test]
    public void Plan_CheapWaterEyeUnder_DrawsUnderwaterInWaterFogWithoutSky()
    {
        // :5724-5726, :5738 (no skybox from inside), :5770-5781 (clear to the volume's fog colour).
        WaterRenderInfo info = Determine(Material(reflect: false), distance: 9f, cheapEnd: 1f);

        WaterView only = WaterViews.Plan(info, Above with { EyeInFogVolume = true }, ViewClears.Depth).ShouldHaveSingleItem();

        only.Draw.ShouldBe(ViewDraws.DrawEntities | ViewDraws.RenderUnderWater | ViewDraws.RenderWater);
        only.Clear.ShouldBe(ViewClears.Depth | ViewClears.Color);
        only.Fog.ShouldBe(WaterViewFog.Volume);
        only.ClearToFogColor.ShouldBeTrue();
    }

    [Test]
    public void Plan_CheapWaterIntersectingTheNearPlane_DrawsBothSides()
    {
        // :5718-5722 "have to draw both sides if we can see both."
        WaterRenderInfo info = Determine(Material(reflect: false), distance: 9f, cheapEnd: 1f);

        WaterViews.Plan(info, Above with { ViewIntersectsWater = true }, ViewClears.Depth)[0].Draw
            .ShouldBe(ViewDraws.DrawEntities | ViewDraws.RenderUnderWater | ViewDraws.RenderAboveWater |
                      ViewDraws.RenderWater | ViewDraws.DrawSkybox);
    }

    [Test]
    public void Plan_AboveExpensive_IsReflectionThenRefractionThenMain()
    {
        // :5908-5949 — the order AddViewToScene draws them in.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above, ViewClears.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.Reflection, WaterViewKind.Refraction, WaterViewKind.Main]);

        // :5974-5988 — no entities unless $reflectentities.
        views[0].Draw.ShouldBe(ViewDraws.RenderReflection | ViewDraws.ClipZ | ViewDraws.ClipBelow |
                               ViewDraws.RenderAboveWater | ViewDraws.DrawSkybox);
        views[0].Clear.ShouldBe(ViewClears.Depth);
        views[0].Fog.ShouldBe(WaterViewFog.World);
        views[0].Target.ShouldBe(WaterViewTarget.Reflection);

        // :5308-5316, :5297-5305 — DF_CLIP_BELOW renders above the height, and no fudge-up spreads it DOWN by 2.
        views[0].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        // :6037-6041.
        views[1].Draw.ShouldBe(ViewDraws.RenderRefraction | ViewDraws.ClipZ | ViewDraws.RenderUnderWater |
                               ViewDraws.FudgeUp | ViewDraws.DrawEntities);
        views[1].Clear.ShouldBe(ViewClears.Color | ViewClears.Depth);
        views[1].Fog.ShouldBe(WaterViewFog.VolumeHeight);
        views[1].ClearToFogColor.ShouldBeTrue();
        views[1].Target.ShouldBe(WaterViewTarget.Refraction);
        views[1].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 66f));

        // :5869-5889 — refraction is on, so the main view leaves the under-water world to it.
        views[2].Draw.ShouldBe(ViewDraws.RenderAboveWater | ViewDraws.DrawEntities | ViewDraws.DrawSkybox |
                               ViewDraws.RenderWater);
        views[2].Clear.ShouldBe(ViewClears.Depth);
        views[2].Target.ShouldBe(WaterViewTarget.BackBuffer);
        views[2].Clip.ShouldBe(HeightClip.None);
    }

    [Test]
    public void Plan_AboveReflectingEntities_ReflectionDrawsThem()
    {
        Determine(Material(reflectEntities: true)).ShouldSatisfyAllConditions(info =>
            WaterViews.Plan(info, Above, ViewClears.Depth)[0].Draw.HasFlag(ViewDraws.DrawEntities).ShouldBeTrue());
    }

    [Test]
    public void Plan_AboveRefractionIntersectingTheNearPlane_ClipsMainBelowAndAddsTheIntersectionView()
    {
        // :5924, :5939-5944 (hardware clip planes), :5958-5961, :6085, :6094-6098.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above with { ViewIntersectsWater = true }, ViewClears.Depth)];

        views.Select(view => view.Kind).ShouldBe(
            [WaterViewKind.Reflection, WaterViewKind.Refraction, WaterViewKind.Main, WaterViewKind.Intersection]);

        views[2].Draw.HasFlag(ViewDraws.ClipZ | ViewDraws.ClipBelow).ShouldBeTrue();
        views[2].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        views[3].Draw.ShouldBe(ViewDraws.RenderUnderWater | ViewDraws.ClipZ | ViewDraws.DrawEntities);
        views[3].Fog.ShouldBe(WaterViewFog.VolumeHeight);
        views[3].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 62f));
        views[3].Target.ShouldBe(WaterViewTarget.BackBuffer);
    }

    [Test]
    public void Plan_AboveReflectOnlyNoSkybox_ClearsColourAndDrawsTheUnderwaterSideItself()
    {
        // :5886-5889 (translucent, no refraction → DF_RENDER_UNDERWATER) and :5927-5930 (no refraction, no skybox).
        WaterRenderInfo info = Determine(Material(translucent: true), cvars: WaterConVars.Defaults with { DrawRefraction = false });
        WaterView[] views = [.. WaterViews.Plan(info, Above with { DrawSkybox = false }, ViewClears.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.Reflection, WaterViewKind.Main]);
        views[1].Draw.ShouldBe(ViewDraws.RenderAboveWater | ViewDraws.DrawEntities | ViewDraws.RenderWater |
                               ViewDraws.RenderUnderWater);
        views[1].Clear.ShouldBe(ViewClears.Depth | ViewClears.Color);
    }

    [Test]
    public void Plan_UnderExpensive_IsRefractionIntoTheBackBufferThenMain()
    {
        // :6167-6183; :6206-6215; :6247-6248 the copy out.
        WaterView[] views = [.. WaterViews.Plan(Determine(Material()), Above with { EyeInFogVolume = true }, ViewClears.Depth)];

        views.Select(view => view.Kind).ShouldBe([WaterViewKind.UnderRefraction, WaterViewKind.UnderMain]);

        views[0].Draw.ShouldBe(ViewDraws.ClipZ | ViewDraws.ClipBelow | ViewDraws.RenderAboveWater |
                               ViewDraws.DrawEntities | ViewDraws.DrawSkybox | ViewDraws.ClipSkybox);
        views[0].Clear.ShouldBe(ViewClears.Depth | ViewClears.Color);
        views[0].Target.ShouldBe(WaterViewTarget.BackBufferCopiedToRefraction);
        views[0].Fog.ShouldBe(WaterViewFog.World);
        views[0].ClearToFogColor.ShouldBeTrue();
        views[0].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderAbove, 62f));

        // :6133-6147 — hardware clip, so DF_CLIP_Z; refraction is on, so no DF_RENDER_ABOVEWATER.
        views[1].Draw.ShouldBe(ViewDraws.FudgeUp | ViewDraws.RenderUnderWater | ViewDraws.DrawEntities |
                               ViewDraws.ClipZ | ViewDraws.RenderWater);
        views[1].Clear.ShouldBe(ViewClears.Depth);
        views[1].Fog.ShouldBe(WaterViewFog.Volume);
        views[1].ClearToFogColor.ShouldBeFalse();
        views[1].Clip.ShouldBe(new HeightClip(HeightClipMode.RenderBelow, 66f));
    }

    [Test]
    public void Plan_UnderWithoutRefraction_ClearsToTheVolumesFogColour()
    {
        // :6173-6179.
        WaterRenderInfo info = Determine(Material(translucent: true), cvars: WaterConVars.Defaults with { DrawRefraction = false });
        WaterView only = WaterViews.Plan(info, Above with { EyeInFogVolume = true }, ViewClears.Depth).ShouldHaveSingleItem();

        only.Kind.ShouldBe(WaterViewKind.UnderMain);
        only.ClearToFogColor.ShouldBeTrue();
        only.Draw.HasFlag(ViewDraws.RenderAboveWater).ShouldBeTrue();
    }

    [Test]
    public void Plan_MatClipZOff_DisablesEveryHeightClip()
    {
        // :5308 — "( m_DrawFlags & DF_CLIP_Z ) && mat_clipz.GetBool()".
        WaterRenderInfo info = Determine(Material());

        WaterViews.Plan(info, Above, ViewClears.Depth, WaterConVars.Defaults with { MatClipZ = false })
            .ShouldAllBe(view => view.Clip == HeightClip.None);
    }

    // ---- BuildEngineDrawWorldListFlags :696 and the sort groups ivrenderview.h:50 ----

    [TestCase(ViewDraws.RenderAboveWater, new[] { 0, 2 })]
    [TestCase(ViewDraws.RenderUnderWater, new[] { 1, 2 })]
    [TestCase(ViewDraws.RenderWater, new[] { 3 })]
    [TestCase(ViewDraws.RenderAboveWater | ViewDraws.RenderUnderWater | ViewDraws.RenderWater, new[] { 0, 1, 2, 3 })]
    [TestCase(ViewDraws.DrawEntities, new int[0])]
    public void SortGroups_ForADrawFlagSet_AreTheEnginesWorldListGroups(ViewDraws flags, int[] groups) =>
        Enumerable.Range(0, VisibleWorld.SortGroups).Where(group => WaterViews.DrawsSortGroup(flags, group)).ShouldBe(groups);

    // ---- the view's LOD distances: C_WaterLODControl.cpp:49, WaterLODControl.cpp:66, viewrender.cpp:937 ----

    [TestCase("{ \"classname\" \"water_lod_control\" }", 1000f, 2000f)]
    [TestCase("{ \"classname\" \"water_lod_control\" \"cheapwaterstartdistance\" \"300\" \"cheapwaterenddistance\" \"900\" }", 300f, 900f)]
    public void WaterLod_FromTheMapsEntities_IsTheEntitys(string entities, float start, float end) =>
        Content.Bsp.BspEntities.WaterLod(Content.Bsp.BspEntities.Parse(Encoding.ASCII.GetBytes(entities)))
            .ShouldBe((start, end));

    [Test]
    public void WaterLod_WithNoEntity_IsNothing() =>
        Content.Bsp.BspEntities.WaterLod(Content.Bsp.BspEntities.Parse(Encoding.ASCII.GetBytes(string.Empty)))
            .ShouldBeNull();

    [Test]
    public void WaterLodSession_AMapWithoutTheEntity_KeepsThePreviousMaps()
    {
        // m_flCheapWaterEndDistance is set by the constructor (viewrender.cpp:937-938) and by
        // SetCheapWaterEndDistance alone (:2962) — from C_WaterLODControl or r_cheapwaterend. Nothing resets it at a
        // level change (LevelShutdown, :950, does not), so the view keeps the last map's values for the session.
        WaterLodSession session = new();

        session.Current.ShouldBe((0f, 0.1f));
        session.EnterMap((300f, 900f));
        session.Current.ShouldBe((300f, 900f));
        session.EnterMap(null);
        session.Current.ShouldBe((300f, 900f));
    }

    // ---- SetFogVolumeState: engine.dll 0x1800e0cd0, through IVRenderView slot 30 (0x18012e9d0) ----

    private static MapWater Fogged(string fog) => MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
        "\"Water\"\n{\n" + fog + "\n\"$fogcolor\" \"{51 43 13}\"\n\"$fogstart\" -100\n\"$fogend\" 400\n}")), null);

    [Test]
    public void VolumeFog_HeightFog_IsLinearBelowTheSurfaceWithTheMaterialsFog()
    {
        // FogMode 2 (LINEAR_BELOW_FOG_Z) for height fog, SetFogZ the volume's surfaceZ, FogColor3fv $fogcolor,
        // FogStart $fogstart, FogEnd $fogend, FogMaxDensity 1.0 (0x18035c93c).
        VolumeFog fog = WaterViews.VolumeFogFor(Fogged("\"$fogenable\" 1"), surfaceZ: 64f, useHeightFog: true).ShouldNotBeNull();

        fog.ShouldBe(new VolumeFog(true, 64f, (51f / 255f, 43f / 255f, 13f / 255f), -100f, 400f, 1f));
    }

    [Test]
    public void VolumeFog_FromTheEye_IsPlainLinear() =>
        WaterViews.VolumeFogFor(Fogged("\"$fogenable\" 1"), 64f, useHeightFog: false)!.Value.HeightFog.ShouldBeFalse();

    [Test]
    public void VolumeFog_WithoutFogEnable_IsNone()
    {
        // 0x1800e0d98: `$fogenable` is read as an int and zero turns fog off — undeclared reads zero.
        WaterViews.VolumeFogFor(Fogged(string.Empty), 64f, true).ShouldBeNull();
    }

    [Test]
    public void VolumeFog_WithWaterFogDisabled_IsNone() =>
        // 0x1800e0da2: fog_enable_water_fog, default "1" (0x18035e7a4).
        WaterViews.VolumeFogFor(Fogged("\"$fogenable\" 1"), 64f, true, fogEnableWaterFog: false).ShouldBeNull();

    // ---- DoesViewPlaneIntersectWater, viewrender.cpp:2741 ----

    private static readonly (float X, float Y, float Z)[] NearPlaneAcross =
        [(-10f, -10f, 60f), (-10f, 10f, 60f), (10f, -10f, 70f), (10f, 10f, 70f)];

    [Test]
    public void ViewPlaneIntersectsWater_NoWaterData_IsFalse() =>
        WaterViews.ViewPlaneIntersectsWater(NearPlaneAcross, 64f, -1, (_, _) => true).ShouldBeFalse();

    [Test]
    public void ViewPlaneIntersectsWater_PlaneWhollyAboveThePlusFudge_IsFalse()
    {
        // :2776-2788 — `worldPos.z - fudge < waterZ` with fudge 7: 71.1 is above, 70.9 is not.
        (float X, float Y, float Z)[] above = [.. NearPlaneAcross.Select(corner => (corner.X, corner.Y, 71.1f))];
        (float X, float Y, float Z)[] within = [.. NearPlaneAcross.Select(corner => (corner.X, corner.Y, 70.9f))];

        WaterViews.ViewPlaneIntersectsWater(above, 64f, 0, (_, _) => true).ShouldBeFalse();
        WaterViews.ViewPlaneIntersectsWater(within, 64f, 0, (_, _) => true).ShouldBeTrue();
    }

    [Test]
    public void ViewPlaneIntersectsWater_Straddling_AsksTheVolumeWithTheBoundsGrownBySeven()
    {
        // :2790-2796.
        ((float, float, float) Min, (float, float, float) Max) asked = default;

        WaterViews.ViewPlaneIntersectsWater(NearPlaneAcross, 64f, 0, (min, max) =>
        {
            asked = (min, max);
            return false;
        }).ShouldBeFalse();

        asked.ShouldBe(((-17f, -17f, 53f), (17f, 17f, 77f)));
    }

    [Test]
    public void NearPlane_AFreeCamera_IsItsCornersAtTheNearDistance()
    {
        // The four (±1, ±1, 0) corners the engine unprojects (:2762-2765), taken from the camera that made the matrix.
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f), FieldOfView = 90f, NearZ = 7f, Aspect = 1f };

        WaterViews.NearPlane(camera).Select(corner => (MathF.Round(corner.X, 3), MathF.Round(corner.Y, 3), MathF.Round(corner.Z, 3)))
            .OrderBy(corner => corner.Item2).ThenBy(corner => corner.Item3)
            .ShouldBe([(7f, -7f, -7f), (7f, -7f, 7f), (7f, 7f, -7f), (7f, 7f, 7f)]);
    }

    // ---- the $bumptransform proxy chain and the animated normal map (ctf_2fort's water/water_2fort.vmt) ----

    private const string TwoFortProxies =
        "\"$normalmap\" \"water/tfwater001_normal\"\n\"$bumpframe\" 0\n\"$temp\" \"[0 0]\"\n\"$curr\" 0.0\n\"$curr2\" 0.0\n" +
        "\"Proxies\"\n{\n" +
        "\"AnimatedTexture\"\n{\n\"animatedtexturevar\" \"$normalmap\"\n\"animatedtextureframenumvar\" \"$bumpframe\"\n\"animatedtextureframerate\" 30.00\n}\n" +
        "\"Sine\"\n{\n\"sineperiod\" \"24\"\n\"sinemin\" -0.5\n\"sinemax\" 0.5\n\"resultVar\" \"$curr\"\n}\n" +
        "\"Sine\"\n{\n\"sineperiod\" \"16\"\n\"sinemin\" 0.5\n\"sinemax\" -0.5\n\"resultVar\" \"$curr2\"\n}\n" +
        "\"Equals\"\n{\n\"srcVar1\" \"$curr2\"\n\"resultVar\" \"$temp[0]\"\n}\n" +
        "\"Equals\"\n{\n\"srcVar1\" \"$curr\"\n\"resultVar\" \"$temp[1]\"\n}\n" +
        "\"TextureTransform\"\n{\n\"translateVar\" \"$temp\"\n\"resultVar\" \"$bumptransform\"\n}\n" +
        "\"WaterLOD\"\n{\n\"dummy\" 0\n}\n}";

    [Test]
    public void BumpTransformAt_TheTwoFortChain_TranslatesBySineOutputs()
    {
        // CTextureTransformProxy::OnBind (matrixproxy.cpp:75): with only translateVar, T(translate) · T(c) · T(−c).
        MapWater water = MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Water\"\n{\n" + TwoFortProxies + "\n}")), null);
        const double seconds = 5.0;

        TextureTransform transform = water.BumpTransformAt(seconds);

        transform.Row0.W.ShouldBe(MaterialProxies.Sine(seconds, 16f, 0.5f, -0.5f), 1e-5f);
        transform.Row1.W.ShouldBe(MaterialProxies.Sine(seconds, 24f, -0.5f, 0.5f), 1e-5f);
        transform.Row0.X.ShouldBe(1f, 1e-6f);
        transform.Row1.Y.ShouldBe(1f, 1e-6f);
    }

    [Test]
    public void BumpTransformAt_NoProxy_IsTheDeclaredTransform() =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Water\"\n{\n\"$bumptransform\" \"center .5 .5 scale 2 2 rotate 0 translate 0 0\"\n}")), null)
            .BumpTransformAt(3.0).Row0.X.ShouldBe(2f, 1e-6f);

    [Test]
    public void NormalFrameAt_AnAnimatedNormalMap_AdvancesAtItsRate()
    {
        // AnimatedTexture on $normalmap at 30 frames a second; Water binds NORMALMAP with BUMPFRAME (water.cpp:296).
        MapWater water = MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"Water\"\n{\n" + TwoFortProxies + "\n}")), null);

        water.NormalFrameAt(0.5, frames: 24).ShouldBe(MaterialProxies.AnimationFrame(0.5, 30f, 24));
        water.NormalFrameAt(0.5, frames: 1).ShouldBe(0);
    }

    // ---- m_pFogVolumeMaterial: engine.dll 0x1800e01bc → 0x1800dffd0 ----

    [Test]
    public void FogVolumeMaterial_FromInside_IsTheBottomMaterial()
    {
        MapWater surface = MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$bottommaterial\" \"water/under.vmt\"\n}")), null);
        Content.Bsp.BspWaterVolume[] volumes = [new(64f, 0f, 3, SurfaceTexdata: 1)];
        MapWater?[] waters = [null, surface, null];
        string[] names = ["dev/a", "water/top", "WATER/UNDER"];

        WaterViews.FogVolumeMaterial(new FogVolumeInfo(0, 5, false, 0f, 64f, 3), volumes, waters, names).ShouldBe(1);
        WaterViews.FogVolumeMaterial(new FogVolumeInfo(0, 5, true, 0f, 64f, 3), volumes, waters, names).ShouldBe(2);
        WaterViews.FogVolumeMaterial(FogVolumeInfo.None(0f), volumes, waters, names).ShouldBe(-1);
    }

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
