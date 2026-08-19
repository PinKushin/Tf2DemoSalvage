using System;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// How the engine declares and evaluates a material proxy, from its own source.
/// </summary>
/// <remarks>
/// **The arithmetic already had parity tests; the CONTRACT did not.** <c>MaterialProxyTests</c>
/// checks the scroll and sine formulas against Valve's, and that is worth having — but it says
/// nothing about which argument names a VMT writes, what a missing one defaults to, or when a proxy
/// runs. Those are the parts an implementation gets wrong while every formula test stays green.
///
/// **Writing them found a divergence in code that already had a passing test.**
/// <c>ASineWithNoPeriod_HoldsStillRatherThanDividingByZero</c> asserts that a period of zero returns
/// the maximum, reasoning that a material naming no period is not asking to oscillate. Valve
/// disagrees in one line: <c>if (flSinePeriod == 0) flSinePeriod = 1;</c>. The implementation and
/// its test were written together and agreed with each other rather than with the engine.
/// </remarks>
public sealed class MaterialProxyConformanceTests
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
    public void MaterialProxy_Evaluation_RunsOnBindNotOnAClock()
    {
        // **The question "does it need to be per frame" has a precise answer, and it is no.**
        // IMaterialProxy has three methods and none of them is a tick:
        //
        //     virtual bool Init( IMaterial* pMaterial, KeyValues *pKeyValues ) = 0;
        //     virtual void OnBind( void * ) = 0;
        //     virtual void Release() = 0;
        //
        // A proxy is evaluated when its material is BOUND for a draw. So a material drawn twice in
        // a frame evaluates twice, and one that is off screen evaluates not at all — the cost is
        // proportional to what is drawn rather than to what the map contains.
        //
        // That matters for where this belongs: the equivalent of OnBind here is the per-batch
        // material upload, not a frame loop, and putting it in a frame loop would evaluate proxies
        // for materials nothing is drawing.
        string header = Sdk("src/public/materialsystem/imaterialproxy.h");

        header.ShouldContain("virtual void OnBind( void * ) = 0;");
        header.ShouldNotContain("OnFrame");
        header.ShouldNotContain("Update(");
    }

    [Test]
    public void MaterialProxy_ASinePeriodOfZero_BecomesOne()
    {
        // **The divergence this file was written to catch.** mathproxy.cpp:408:
        //
        //     if (flSinePeriod == 0)
        //         flSinePeriod = 1;
        //
        // Not "hold at the maximum", which is what this project did. The two differ on every
        // material that omits sinePeriod — the engine oscillates it once a second and this held it
        // at a constant.
        Sdk("src/game/client/mathproxy.cpp").ShouldContain("flSinePeriod = 1;");

        // A period of zero must therefore behave exactly as a period of one.
        MaterialProxies.Sine(0.25d, period: 0f, minimum: 0.2f, maximum: 0.9f)
            .ShouldBe(
                MaterialProxies.Sine(0.25d, period: 1f, minimum: 0.2f, maximum: 0.9f),
                0.0001f,
                "the engine substitutes a period of one rather than stopping the oscillation");

        // And at a quarter of a second into a one-second period the sine is at its peak, which is
        // a value the "hold at maximum" implementation also returns — so the check above is done
        // at a time where the two DIFFER as well.
        MaterialProxies.Sine(0.75d, period: 0f, minimum: 0.2f, maximum: 0.9f)
            .ShouldBe(0.2f, 0.0001f, "three quarters through, a sine is at its minimum");
    }

    [Test]
    public void MaterialProxy_TheSineRange_IsMappedAsValveWritesIt()
    {
        // Valve maps to [0,1] first and then onto [min,max] (mathproxy.cpp:412-413):
        //
        //     flValue = ( sin( 2.0f * M_PI * (curtime - offset) / period ) * 0.5f ) + 0.5f;
        //     flValue = ( flSineMax - flSineMin ) * flValue + flSineMin;
        //
        // Algebraically that is midpoint + half-span * sin, which is how this project writes it —
        // asserted here so the equivalence is recorded rather than rediscovered, and so a future
        // simplification cannot quietly change it.
        string source = Sdk("src/game/client/mathproxy.cpp");

        source.ShouldContain("* 0.5f ) + 0.5f");
        source.ShouldContain("( flSineMax - flSineMin ) * flValue + flSineMin");

        // At t = 0 with no offset, sin is 0, so the mapped value is the MIDPOINT — not the minimum,
        // which is the reading a "starts at min and rises" intuition gives.
        MaterialProxies.Sine(0d, period: 2f, minimum: 0f, maximum: 1f)
            .ShouldBe(0.5f, 0.0001f, "a sine starts at its midpoint, not at its minimum");
    }

    [Test]
    public void MaterialProxy_ASineTimeOffset_IsNotImplemented()
    {
        // `timeOffset`, default 0, subtracted from curtime before the division
        // (mathproxy.cpp:393 and 412). It shifts the phase, which is how two materials running the
        // same proxy are kept from pulsing in lockstep.
        //
        // Not implemented here. Recorded as a named gap rather than left for someone to discover
        // when two capture points breathe in unison.
        string source = Sdk("src/game/client/mathproxy.cpp");

        source.ShouldContain("\"timeOffset\", 0.0f");
        source.ShouldContain("gpGlobals->curtime - flSineTimeOffset");
    }

    [Test]
    public void MaterialProxy_ArgumentNamesAndDefaults_AreValves()
    {
        // **The names a VMT actually writes, and what a missing one means.** These are the strings
        // an implementation matches against, so getting one wrong silently disables the proxy: a
        // material naming `sineperiod` against a reader expecting `period` gets the default and
        // oscillates at the wrong rate rather than failing.
        //
        // Matching is case-insensitive because KeyValues is, and TF2's own materials are
        // inconsistent — cappoint_logo_blue writes "Sineperiod" and "SineMax".
        string math = Sdk("src/game/client/mathproxy.cpp");

        math.ShouldContain("\"sinePeriod\", 1.0f");
        math.ShouldContain("\"sineMax\", 1.0f");
        math.ShouldContain("\"sineMin\", 0.0f");

        string scroll = Sdk("src/game/client/texturescrollmaterialproxy.cpp");

        scroll.ShouldContain("\"textureScrollVar\"");
        scroll.ShouldContain("\"textureScrollRate\", 1.0f");
        scroll.ShouldContain("\"textureScrollAngle\", 0.0f");
        scroll.ShouldContain("\"textureScale\", 1.0f");
    }

    [Test]
    public void MaterialProxy_ResultVar_NamesTheDestinationExceptForScroll()
    {
        // **Two different conventions, and mixing them up disables one of the two proxies.**
        //
        // A maths proxy derives from CResultProxy, which reads `resultVar`
        // (functionproxy.cpp:112). A texture scroll does NOT — it reads `textureScrollVar`
        // (texturescrollmaterialproxy.cpp:54), because what it writes is a matrix rather than a
        // number and it is not a CResultProxy at all.
        Sdk("src/game/client/functionproxy.cpp")
            .ShouldContain("pKeyValues->GetString( \"resultVar\" )");

        Sdk("src/game/client/texturescrollmaterialproxy.cpp")
            .ShouldContain("pKeyValues->GetString( \"textureScrollVar\" )");
    }

    [Test]
    public void MaterialProxy_AResultVar_MayNameOneVectorComponent()
    {
        // `resultVar` accepts an array subscript — `$color[1]` writes only green
        // (functionproxy.cpp:118-130). An implementation that treats the whole string as a name
        // finds no variable called "$color[1]" and does nothing, which is the silent-disable
        // failure again.
        //
        // Recorded because TF2 materials use it, and because the parse is not obvious: the
        // subscript is stripped and the remainder is the variable.
        string source = Sdk("src/game/client/functionproxy.cpp");

        source.ShouldContain("if (strchr(pResult, '['))");
        source.ShouldContain("m_ResultVecComp = strtol( pArray, &pIEnd, 10 );");
    }

    /// <summary>Reads an SDK file, or fails loudly.</summary>
    private static string Sdk(string path) =>
        SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
}
