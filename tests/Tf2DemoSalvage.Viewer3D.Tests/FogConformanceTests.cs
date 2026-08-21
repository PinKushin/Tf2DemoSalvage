using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Distance fog, specified from the SDK before any of it is built.
/// </summary>
/// <remarks>
/// **Fog is not a material parameter — it is networked, per tick, by an entity.**
/// <c>CFogController</c>'s send table (<c>fogcontroller.cpp:78</c>) carries start, end, colour,
/// maximum density and a set of lerp targets, so a demo records the fog changing as the map's
/// triggers fire. That makes it the first thing here whose inputs come from the DEMO rather than
/// from the map's assets.
///
/// **Two things about the arithmetic are silent if missed**, and one of them is a naming trap in
/// Valve's own header.
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
    public void Fog_TheBlendFactor_IsSquaredBeforeTheLerp()
    {
        // **The factor is squared, and Valve says why on the line itself**
        // (common_ps_fxc.h:290):
        //
        //     pixelFogFactor = saturate( pixelFogFactor );
        //     return lerp( vShaderColor.rgb, vFogColor.rgb, pixelFogFactor * pixelFogFactor );
        //         //squaring the factor will get the middle range mixing closer to hardware fog
        //
        // Missing the square makes fog far too strong through the whole middle distance — at the
        // halfway point it is 0.5 of the fog colour instead of 0.25 — while both ends stay correct,
        // so the map still fades to fog at the far plane and looks merely hazy rather than wrong.
        //
        // **Height fog does NOT square it**, which is the same file four lines down. So this is a
        // property of range fog rather than of fog, and an implementation that squares everywhere
        // is wrong for water.
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
            "return lerp( vShaderColor.rgb, vFogColor.rgb, saturate( pixelFogFactor ) );",
            Case.Sensitive,
            "height fog does not square, so the square belongs to range fog specifically");

        // The arithmetic at the midpoint, which is where the two differ most.
        Squared(0.5f).ShouldBe(0.25f, 1e-6f);

        static float Squared(float factor) => factor * factor;
    }

    [Test]
    public void Fog_TheRangeFactor_IsClampedByMaxDensityBeforeSaturating()
    {
        // `CalcRangeFog`, common_ps_fxc.h:232 — the whole computation in one line:
        //
        //     return saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) );
        //
        // **The `min` comes BEFORE the `saturate`**, which matters because `$fogmaxdensity` is how a
        // mapper stops distant geometry disappearing entirely. Clamping after saturating would make
        // a max density above 1 meaningless and one below 1 apply only at the far end; clamping
        // first caps the whole curve.
        //
        // The controller sends the density (`m_fog.maxdensity`), so it is per-demo data rather than
        // a constant to assume.
        string source = Sdk("src/materialsystem/stdshaders/common_ps_fxc.h");

        source.ShouldContain(
            "return saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) );",
            Case.Sensitive);

        // At a density of 0.6, a fully fogged distance stops at 0.6 rather than 1.
        Range(10000f, start: 0f, end: 1000f, maxDensity: 0.6f).ShouldBe(0.6f, 1e-6f);
        Range(500f, start: 0f, end: 1000f, maxDensity: 0.6f).ShouldBe(0.5f, 1e-6f);

        static float Range(float depth, float start, float end, float maxDensity)
        {
            float range = 1f / (end - start);

            return Math.Clamp(Math.Min(maxDensity, (depth * range) - (start * range)), 0f, 1f);
        }
    }

    [Test]
    public void Fog_TheFirstParameter_IsStartOverRangeDespiteItsMacroName()
    {
        // **A naming conflict inside Valve's own header, and the arithmetic settles it.**
        // `CalcRangeFog`'s parameter is `flFogStartOverRange`:
        //
        //     saturate( min( flFogMaxDensity, (flProjPosZ * flFogOORange) - flFogStartOverRange ) )
        //
        // while the macro for the same slot, twelve lines below, is:
        //
        //     #define g_FogEndOverRange   g_FogParams.x
        //
        // They cannot both be right. Standard linear fog is `(z - start) / (end - start)`, and the
        // expression above is `z/range - x`; for those to agree, **x must be start/range**. Putting
        // end/range there instead shifts the whole curve by `(end - start)/range`, which is exactly
        // 1 — so fog would begin fully opaque at the camera and clear at the far plane. Backwards,
        // and unmistakable once drawn, which is the only mercy in it.
        //
        // Recorded because a reader who trusts the macro name writes the wrong one and the
        // conflicting evidence is twelve lines away in the same file.
        string source = Sdk("src/materialsystem/stdshaders/common_ps_fxc.h");

        source.ShouldContain("const float flFogStartOverRange", Case.Sensitive);
        source.ShouldContain("#define g_FogEndOverRange", Case.Sensitive);

        // The arithmetic, both ways round, at the fog's own start distance where the factor must be
        // exactly zero.
        const float start = 500f;
        const float end = 2000f;
        float range = 1f / (end - start);

        (((start * range) - (start * range)) is 0f).ShouldBeTrue("start/range gives 0 at the start");

        ((start * range) - (end * range)).ShouldBe(-1f, 1e-6f, "end/range gives -1 there instead");
    }

    [Test]
    public void Fog_TheControllerNetworksItsParameters_SoADemoCarriesThem()
    {
        // **This is the first drawn feature whose inputs come from the demo rather than the map.**
        // CFogController, fogcontroller.cpp:78, sends start, end, colour, maximum density and a
        // full set of lerp targets — so fog that changes mid-round is recorded and replayable.
        //
        // The wire names are what `SENDINFO_STRUCTELEM` produces, which is the struct path itself:
        // `m_fog.start`, not `start`. That is the same trap as `wire-names-are-strings` — a decoder
        // looking for the C++ field name finds nothing.
        //
        // Measured on the committed corpus: 3 of 10 demos carry the class at all — the 2009
        // badlands POV and both 2011 koth_viaduct recordings — so fog is verifiable but not
        // everywhere, and a demo without it should draw no fog rather than a default.
        string source = Sdk("src/game/server/fogcontroller.cpp");

        source.ShouldContain("IMPLEMENT_SERVERCLASS_ST_NOBASE( CFogController, DT_FogController )");
        source.ShouldContain("SendPropFloat( SENDINFO_STRUCTELEM( m_fog.start ), 0, SPROP_NOSCALE )");
        source.ShouldContain("SendPropFloat( SENDINFO_STRUCTELEM( m_fog.end ), 0, SPROP_NOSCALE )");
        source.ShouldContain("SendPropFloat( SENDINFO_STRUCTELEM( m_fog.maxdensity ), 0, SPROP_NOSCALE )");
        source.ShouldContain("SendPropInt( SENDINFO_STRUCTELEM( m_fog.colorPrimary ), 32, SPROP_UNSIGNED )");

        source.ShouldContain(
            "SendPropInt( SENDINFO_STRUCTELEM( m_fog.enable ), 1, SPROP_UNSIGNED )",
            Case.Sensitive,
            "one bit, and a map with a controller can still have fog switched off");
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
