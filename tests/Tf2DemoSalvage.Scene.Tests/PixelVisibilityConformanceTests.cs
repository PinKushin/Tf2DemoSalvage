using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `PixelVisibility_FractionVisible` — the occlusion query a glow and a beam's halo are dimmed by (B378, B521).
/// </summary>
/// <remarks>
/// *Read from published source*, `game/client/c_pixel_visibility.cpp`. The fraction is a count of proxy pixels that pass
/// the depth test over the count the proxy covers, read back a frame later and approached at a rate:
///
/// <code>
/// float target = (float)pixels / (float)pixelsPossible;
/// target = (target >= 0.95f) ? 1.0f : (target &lt; 0.0f) ? 0.0f : target;
/// float rate = gpGlobals->frametime * fadeTimeInv;
/// m_brightnessTarget = Approach( target, m_brightnessTarget, rate ); // fade in / out
/// ...
/// return m_brightnessTarget * m_clipFraction;
/// </code>
///
/// The proxy is `PixelVisibility_DrawProxy` (`:52-166`): a view-facing quad pulled toward the eye by the proxy size.
/// </remarks>
public sealed class PixelVisibilityConformanceTests
{
    private const float Height = 1000f;

    /// <summary>An eye at the origin looking down +X, 90 degrees across a square viewport: 1000 / distance pixels a unit.</summary>
    private static PixelVisibilityView View()
    {
        FreeCamera camera = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f), FieldOfView = 90f, Aspect = 1f };

        return PixelVisibilityView.From(camera, Height);
    }

    private static GlowQuery Lamp(float distance = 100f, float size = 8f) => new(7, new Vector3(distance, 0f, 0f), size, 1f);

    /// <remarks>
    /// `view_beams.cpp:2266-2271`: "HACKHACK: heuristic to estimate proxy size" —
    /// <c>size = 1.0f + (pbeam->m_fHaloScale * pbeam->m_fWidth / pbeam->m_fEndWidth); size = clamp( size, 1.0f, 8.0f );</c>.
    /// </remarks>
    [TestCase(60f, 64f, 64f, 8f)]
    [TestCase(0f, 64f, 64f, 1f)]
    [TestCase(3f, 10f, 20f, 2.5f)]
    public void HaloProxySize_FromTheBeamsOwnWidths_IsOnePlusTheScaledRatioClamped(float haloScale, float width, float endWidth, float expected) =>
        PixelVisibility.HaloProxySize(haloScale, width, endWidth).ShouldBe(expected);

    [TestCase(50, 100, 0.5f)]
    [TestCase(95, 100, 1f)]
    [TestCase(94, 100, 0.94f)]
    [TestCase(0, 100, 0f)]
    [TestCase(10, 0, 0f)]
    public void Target_FromTheTwoCounts_IsTheirRatioAndWholeFromNinetyFivePercent(int pixels, int possible, float expected) =>
        PixelVisibility.Target(pixels, possible).ShouldBe(expected, 1e-6f);

    /// <remarks>
    /// `origin -= dir * forwardScale`, then `scale *= sqrt2` (0.707106781) and the four corners at
    /// `origin ± CurrentViewUp() * scale / proxyAspect ± CurrentViewRight() * scale`.
    /// </remarks>
    [Test]
    public void Proxy_OfALampAhead_IsAViewFacingQuadPulledTowardTheEyeByItsSize()
    {
        (PixelVisibilityProxy proxy, float clip) = PixelVisibility.Proxy(Lamp(), View()).ShouldNotBeNull();

        const float Half = 8f * 0.707106781f;

        // Right is -Y at zero angles, so "up minus right" is +Y.
        Near(proxy.First, new Vector3(92f, Half, Half));
        Near(proxy.Second, new Vector3(92f, -Half, Half));
        Near(proxy.Third, new Vector3(92f, -Half, -Half));
        Near(proxy.Fourth, new Vector3(92f, Half, -Half));
        clip.ShouldBe(1f);
    }

    [Test]
    public void Proxy_WithAnAspect_DividesTheHeightOnly()
    {
        (PixelVisibilityProxy proxy, _) = PixelVisibility.Proxy(Lamp() with { ProxyAspect = 2f }, View()).ShouldNotBeNull();

        Near(proxy.First, new Vector3(92f, 8f * 0.707106781f, 4f * 0.707106781f));
    }

    /// <remarks>
    /// `MIN_PROXY_PIXELS = 5.0f`: "make the radius larger to ensure a minimum screen space size of the proxy geometry".
    /// At 1000 units this view draws one pixel a unit, so a 2-unit proxy becomes 5 units — and it is still pulled
    /// forward by 2, "don't expand this with distance to fit pixels or the sprite will poke through".
    /// </remarks>
    [Test]
    public void Proxy_SmallerThanFivePixels_GrowsAcrossTheViewAndNotTowardTheEye()
    {
        (PixelVisibilityProxy proxy, _) = PixelVisibility.Proxy(Lamp(1000f, 2f), View()).ShouldNotBeNull();

        Near(proxy.First, new Vector3(998f, 5f * 0.707106781f, 5f * 0.707106781f));
    }

    private static void Near(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).ShouldBeLessThan(1e-3f, $"{actual} against {expected}");

    /// <remarks>`if ( ScreenTransform( verts[i+1], screen[i] ) ) return -1;` — a corner behind the eye issues no query.</remarks>
    [Test]
    public void Proxy_BehindTheEye_IsNotDrawn() =>
        PixelVisibility.Proxy(Lamp(-100f), View()).ShouldBeNull();

    /// <remarks>"compute the ratio of the area not clipped by the frustum to total area".</remarks>
    [Test]
    public void Proxy_HalfOffTheSideOfTheScreen_ClipsToHalf()
    {
        // At 90 degrees the screen's edge is the diagonal y = x, and pulling the proxy toward the eye keeps it there.
        GlowQuery lamp = new(7, new Vector3(100f, 100f, 0f), 8f, 1f);

        (_, float clip) = PixelVisibility.Proxy(lamp, View()).ShouldNotBeNull();

        clip.ShouldBe(0.5f, 1e-3f);
    }

    /// <remarks>`m_frameIssued == -1`: nothing to read, `return m_brightnessTarget * m_clipFraction`, which is zero.</remarks>
    [Test]
    public void FractionVisible_BeforeAnyQueryWasIssued_IsZero()
    {
        PixelVisibility system = new();

        system.BeginFrame(1f, static _ => (100, 100));

        system.FractionVisible(Lamp()).ShouldBe(0f);
    }

    [Test]
    public void FractionVisible_TheFrameAfterItsQuery_ApproachesTheTargetByFrameTimeOverFadeTime()
    {
        PixelVisibility system = new();
        PixelVisibilityView view = View();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(Lamp());
        system.EndView(view).Count.ShouldBe(1);

        // PIXELVIS_DEFAULT_FADE_TIME is 0.0625: a quarter of it moves a quarter of the way.
        system.BeginFrame(0.0625f / 4f, static _ => (80, 100));

        system.FractionVisible(Lamp()).ShouldBe(0.25f, 1e-6f);
        system.Settled.ShouldBeFalse();
    }

    [Test]
    public void FractionVisible_OnceItHasArrived_IsTheTargetAndSettled()
    {
        PixelVisibility system = new();
        PixelVisibilityView view = View();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(Lamp());
        system.EndView(view);

        system.BeginFrame(1f, static _ => (80, 100));

        system.FractionVisible(Lamp()).ShouldBe(0.8f, 1e-6f);
        system.Settled.ShouldBeTrue();
    }

    /// <remarks>`if ( !m_wasQueriedThisFrame )` — a second ask in one frame does not fade twice.</remarks>
    [Test]
    public void FractionVisible_AskedTwiceInAFrame_FadesOnce()
    {
        PixelVisibility system = new();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(Lamp());
        system.EndView(View());

        system.BeginFrame(0.0625f / 4f, static _ => (100, 100));
        system.FractionVisible(Lamp());

        system.FractionVisible(Lamp()).ShouldBe(0.25f, 1e-6f);
    }

    /// <remarks>`return m_brightnessTarget * m_clipFraction` — the frustum's share multiplies what the query found.</remarks>
    [Test]
    public void FractionVisible_OfAProxyBehindTheEye_IsZeroWhateverWasCounted()
    {
        PixelVisibility system = new();
        GlowQuery lamp = Lamp();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(lamp);
        system.EndView(View());
        system.BeginFrame(1f, static _ => (100, 100));
        system.FractionVisible(lamp).ShouldBe(1f);

        // The camera turns its back: the proxy is not issued and the clip fraction is zero.
        FreeCamera turned = new() { Origin = (0f, 0f, 0f), Angles = (0f, 180f, 0f), FieldOfView = 90f, Aspect = 1f };

        system.EndView(PixelVisibilityView.From(turned, Height)).ShouldBeEmpty();
        system.BeginFrame(1f, static _ => (100, 100));

        system.FractionVisible(lamp).ShouldBe(0f);
    }

    /// <remarks>
    /// `CPixelVisSet::IsActive`: `(gpGlobals->framecount - frameIssued) > 1 ? false : true`, and `EndScene` frees a set
    /// that is not — so a glow not asked about for two frames starts again from zero.
    /// </remarks>
    [Test]
    public void FractionVisible_AfterTwoFramesUnasked_StartsAgainFromZero()
    {
        PixelVisibility system = new();
        PixelVisibilityView view = View();
        Func<int, (int Pixels, int Possible)?> all = static _ => (100, 100);

        system.BeginFrame(0f, all);
        system.FractionVisible(Lamp());
        system.EndView(view);
        system.BeginFrame(1f, all);
        system.FractionVisible(Lamp()).ShouldBe(1f);
        system.EndView(view);

        system.BeginFrame(1f, all);
        system.EndView(view).Count.ShouldBe(1, "a set asked about last frame is still active, and still queried");
        system.BeginFrame(1f, all);
        system.EndView(view).Count.ShouldBe(1, "freed at the END of the second unasked frame, after its view");
        system.BeginFrame(1f, all);
        system.EndView(view).ShouldBeEmpty();

        system.FractionVisible(Lamp()).ShouldBe(0f);
    }

    /// <remarks>
    /// `GetFractionVisible` (`c_pixel_visibility.cpp:565-570`): <c>if ( !m_hwCanTestGlows || building_cubemaps.GetBool() )
    /// return GlowSightDistance( params.position, true ) > 0 ? 1.0f : 0.0f;</c> — and <c>m_hwCanTestGlows</c> is
    /// <c>r_dopixelvisibility.GetBool() &amp;&amp; fastqueries &amp;&amp; engine->GetDXSupportLevel() >= 80</c> as
    /// `LevelInitPreEntity` found it (`:522`). A config with `r_dopixelvisibility 0` gets the line trace, all or nothing.
    /// </remarks>
    [TestCase(true, 1f)]
    [TestCase(false, 0f)]
    public void FractionVisible_WithPixelVisibilityOffAtLevelInit_IsTheLineOfSight(bool inSight, float expected)
    {
        PixelVisibility system = new();

        system.LevelInit(doPixelVisibility: false);
        system.BeginFrame(1f, static _ => (100, 100), _ => inSight);

        system.FractionVisible(Lamp()).ShouldBe(expected);
        system.EndView(View()).ShouldBeEmpty("no query is issued for a set that was never made");
    }

    /// <remarks>
    /// **The switch is read twice and only one reading is latched.** `m_hwCanTestGlows` is the level's; `EndView`
    /// asks <c>PixelVisibility_IsAvailable()</c>, which reads the cvar now (`:585`, `:837-841`). Turned off mid-level,
    /// the glows keep asking their queries and no query is issued again — they hold what was last counted.
    /// </remarks>
    [Test]
    public void EndView_WithPixelVisibilityTurnedOffMidLevel_IssuesNothingAndTheFractionHolds()
    {
        PixelVisibility system = new();
        PixelVisibilityView view = View();

        system.LevelInit(doPixelVisibility: true);
        system.BeginFrame(0f, static _ => null, static _ => false);
        system.FractionVisible(Lamp());
        system.EndView(view);
        system.BeginFrame(1f, static _ => (80, 100), static _ => false);
        system.FractionVisible(Lamp()).ShouldBe(0.8f, 1e-6f);

        system.DoPixelVisibility = false;

        system.EndView(view).ShouldBeEmpty();
        system.BeginFrame(1f, static _ => (80, 100), static _ => false);
        system.FractionVisible(Lamp()).ShouldBe(0.8f, 1e-6f, "the query path, not the sight line, which says hidden");
    }

    /// <remarks>`LevelShutdownPostEntity` purges both lists (`:549-563`): a new level's glows start from nothing.</remarks>
    [Test]
    public void LevelInit_AfterGlowsWereSeen_ForgetsThem()
    {
        PixelVisibility system = new();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(Lamp());
        system.EndView(View());
        system.BeginFrame(1f, static _ => (100, 100));
        system.FractionVisible(Lamp()).ShouldBe(1f);

        system.LevelInit(doPixelVisibility: true);
        system.BeginFrame(1f, static _ => (100, 100));

        system.FractionVisible(Lamp()).ShouldBe(0f);
    }

    [Test]
    public void EndView_WithTwoLamps_IssuesOneProxyEachUnderItsEntity()
    {
        PixelVisibility system = new();

        system.BeginFrame(0f, static _ => null);
        system.FractionVisible(Lamp());
        system.FractionVisible(Lamp(200f) with { Entity = 9 });

        IReadOnlyList<PixelVisibilityProxy> proxies = system.EndView(View());

        proxies.Count.ShouldBe(2);
        proxies[0].Key.ShouldBe(7);
        proxies[1].Key.ShouldBe(9);
    }
}
