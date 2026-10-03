using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Valve's fog arithmetic and where the view takes its inputs from.
/// </summary>
/// <remarks>
/// **Implemented 2026-10-02 (B139 closed).** The history below is kept because it is why the
/// equations are held as citations: the parity of OUR arithmetic is FogConstantsTests (exact
/// constants) and FogRenderTests (pixels through the real shader).
///
/// **The conformance sweep turned this file into a gap report, which is what it always was.**
/// Its four tests quoted <c>common_fxc.h</c> and <c>fogcontroller.cpp</c> and asserted the
/// arithmetic in local helper functions — arithmetic written in the test, transcribed from Valve,
/// compared against itself. Nothing in <c>Tf2DemoSalvage.Viewer3D</c> was ever involved.
///
/// **It is not involved because there is nothing to involve.** <c>SceneFog</c> is decoded per
/// tick, retained on the timeline, and read by no production code anywhere — the only consumers of
/// <c>DemoTimeline.FogSamples</c> and <c>FogAt</c> in the entire repository are tests. Filed as
/// **B139**.
///
/// That is the fourth instance of the pattern in
/// <c>docs/memory/output-level-assertion-or-it-is-not-done.md</c>: a value decoded, retained,
/// unit-tested and never read. <c>m_flPlaybackRate</c> was the third, and every animation played at
/// rate 1 for as long as it lasted.
///
/// **So the citations stay and the arithmetic goes.** The equations below are the SPECIFICATION for
/// an unimplemented feature, which is a legitimate thing for a conformance test to hold (D45) — but
/// only if it says so, and only if something fails when the feature arrives without its parity
/// check. The gap assertion at the end is that trigger.
/// </remarks>
public sealed class FogConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Fog_TheEquations_AreRangeFogThenASquaredBlend()
    {
        // **Kept as citations rather than as assertions on transcribed arithmetic.** The previous
        // version computed `Squared(0.5f).ShouldBe(0.25f)` against a helper defined in the same
        // file, which tests that squaring squares.
        //
        // What each line is for, when this is built:
        //
        //   lerp( vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor )
        //       range fog SQUARES the factor before the lerp, and Valve says why in a comment
        //       beside it. A linear blend reads as haze rather than as Source's fog.
        //
        //   saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) )
        //       maxdensity clamps BEFORE the saturate, so a controller asking for 0.6 never
        //       reaches full fog however far away the surface is. Clamping after would ignore it.
        //
        //   const float flFogStartOverRange
        //       the first fog constant is start/(end-start), NOT the start distance, despite the
        //       macro `g_FogEndOverRange` twelve lines away suggesting otherwise. Feeding it a
        //       distance puts the fog's onset in the wrong place by a factor of the range.
        string source = Sdk("src/materialsystem/stdshaders/common_ps_fxc.h");

        source.ShouldContain(
            "return lerp( vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor );",
            Case.Sensitive,
            "range fog squares the factor");

        source.ShouldContain(
            "squaring the factor will get the middle range mixing closer to hardware fog",
            Case.Sensitive,
            "and Valve states the reason rather than leaving it to be inferred");

        source.ShouldContain(
            "return saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) );",
            Case.Sensitive,
            "maxdensity clamps before the saturate");

        source.ShouldContain("const float flFogStartOverRange", Case.Sensitive);
        source.ShouldContain("#define g_FogEndOverRange", Case.Sensitive);
    }

    // **`Fog_NothingInThisRendererReadsTheDecodedFog_WhichIsB139` stood here** — the gap marker that
    // was to redden the moment SceneFog reached the renderer and be replaced by parity checks. It
    // reddened (2026-10-02, B139 closed); the checks that replace it are below and, for our pixels,
    // in FogRenderTests and FogConstantsTests.

    [Test]
    public void EnableWorldFog_TheMainView_TakesLinearFogFromTheLocalPlayersParams()
    {
        // **What the main view hands the material system** (viewrender.cpp:4708). Each line is a
        // value FogConstants must reproduce:
        //
        //   GetFogEnable  — `pFogParams->enable != false`; no params means no fog at all.
        //   GetFogColor   — colorPrimary in 0..255, then `VectorScale( pColor, 1.0f / 255.0f )`:
        //                   GAMMA space (Valve's own note says it should be converted to linear), so the shader API's
        //                   `g_LinearFogColor` (common_ps_fxc.h:45) is a conversion of it.
        //   FogStart/End  — the distances themselves; the shader API turns them into the
        //                   start/(end-start) and 1/(end-start) the pixel shader reads.
        //   FogMaxDensity — straight through.
        string view = Sdk("src/game/client/viewrender.cpp");

        view.ShouldContain("pFogParams = pbp->GetFogParams();", Case.Sensitive);
        view.ShouldContain("return pFogParams->enable != false;", Case.Sensitive);
        view.ShouldContain("VectorScale( pColor, 1.0f / 255.0f, pColor );", Case.Sensitive);
        view.ShouldContain("pRenderContext->FogMode( MATERIAL_FOG_LINEAR );", Case.Sensitive);
        view.ShouldContain("pRenderContext->FogMaxDensity( GetFogMaxDensity( pFogParams ) );", Case.Sensitive);

        // The fog parameter layout the pixel shader unpacks: x start-over-range, z max density,
        // w one over the range (common_ps_fxc.h:250, CalcRangeFog( flProjPosZ, fogParams.x,
        // fogParams.z, fogParams.w )).
        Sdk("src/materialsystem/stdshaders/common_ps_fxc.h").ShouldContain(
            "retVal = CalcRangeFog( flProjPosZ, fogParams.x, fogParams.z, fogParams.w );", Case.Sensitive);
    }

    [Test]
    public void Enable3dSkyboxFog_TheSkyView_ScalesItsDistancesByOneOverTheSkyboxScale()
    {
        // **The 3D skybox has fog of its own, and it is the PLAYER's, not a controller's**
        // (viewrender.cpp:4806): `m_skybox3d.fog` in `DT_Local`, with start and end divided by
        // `m_skybox3d.scale` because the sky room is drawn at its own miniature size. Drawing the
        // room through the world's fog — or none — is the seam the pairing note in
        // UnimplementedEffectConformanceTests warned of.
        string view = Sdk("src/game/client/viewrender.cpp");

        view.ShouldContain("scale = 1.0f / local->m_skybox3d.scale;", Case.Sensitive);
        view.ShouldContain("pRenderContext->FogStart( GetSkyboxFogStart() * scale );", Case.Sensitive);
        view.ShouldContain("pRenderContext->FogEnd( GetSkyboxFogEnd() * scale );", Case.Sensitive);
        view.ShouldContain("return !!local->m_skybox3d.fog.enable;", Case.Sensitive);
    }

    [Test]
    public void UpdateFogController_TheLocalPlayersHandle_ChoosesTheController()
    {
        // **The view's fog is the controller the local player's `m_PlayerFog.m_hCtrl` names**
        // (c_baseplayer.cpp:2802), sent in DT_Local (c_baseplayer.cpp:201) — not "the first
        // controller in the entity list". A player with no handle gets `enable = false`: no fog,
        // however many controllers the map has.
        string player = Sdk("src/game/client/c_baseplayer.cpp");

        player.ShouldContain("RecvPropEHandle( RECVINFO( m_PlayerFog.m_hCtrl ) ),", Case.Sensitive);
        player.ShouldContain("m_CurrentFog = *pFogParams;", Case.Sensitive);
        player.ShouldContain("m_CurrentFog.enable = false;", Case.Sensitive);
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
