namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a map may CUT the detail prop distances and may never raise them.
/// </summary>
/// <remarks>
/// **`CDetailObjectSystem::LevelInitPostEntity`, `detailobjectsystem.cpp:1524`.** An
/// `env_detail_controller` in the map's entity lump makes the engine write both cvars itself:
///
/// <code>
///   if ( GetDetailController() )
///   {
///       cl_detailfade.SetValue( MIN( m_flDefaultFadeStart, GetDetailController()->m_flFadeStartDist ) );
///       cl_detaildist.SetValue( MIN( m_flDefaultFadeEnd, GetDetailController()->m_flFadeEndDist ) );
///   }
///   else
///   {
///       // revert to default values if the map doesn't specify
///       cl_detailfade.SetValue( m_flDefaultFadeStart );
///       cl_detaildist.SetValue( m_flDefaultFadeEnd );
///   }
/// </code>
///
/// **The `MIN` is the whole mechanism and it runs one way**: a map cannot force grass onto a
/// machine whose owner turned it off, and the pair it is measured against was captured in `Init()`
/// before any map loaded — which is what makes a config value outlive the map that overrode it.
///
/// **Measured: no map TF2 ships carries one.** 0 of 234 installed maps, with `worldspawn` found in
/// 234 of 234 as the control — the `detail-controller` probe. The class is still live in the game
/// (the string is in both `tf/bin/x64/server.dll` and `client.dll`), so a community map that places
/// one by hand gets this behaviour, which is why the branch is transcribed rather than skipped.
/// </remarks>
public sealed class DetailControllerConformanceTests
{
    /// <remarks>
    /// **The direction that would be invisible if it were wrong the other way round.** A map asking
    /// for less than the config gets it, and the arithmetic downstream must agree — so the control
    /// is the same sprite under the config's own distance, which must still be drawn.
    /// </remarks>
    [Test]
    public void ForLevel_WithAMapAskingForLess_TakesTheMaps()
    {
        (float Distance, float Fade) live = DetailFade.ForLevel(1200f, 400f, (100f, 300f));

        live.Distance.ShouldBe(300f);
        live.Fade.ShouldBe(100f);

        // 150 units out is inside the map's 200-unit opaque core (300 − 100); 350 is past its
        // maximum altogether. The control is the same sprite under the config's own numbers, where
        // 350 units is still fully opaque — so "the map was obeyed" and "nothing happened" cannot
        // produce the same reading.
        DetailFade.For(live.Distance, live.Fade).Alpha(150f * 150f).ShouldBe((byte)255);
        DetailFade.For(live.Distance, live.Fade).Alpha(350f * 350f).ShouldBe((byte)0);
        DetailFade.For(1200f, 400f).Alpha(350f * 350f).ShouldBe((byte)255);
    }

    /// <remarks>
    /// **A map cannot raise the distance, and this is the half a `MAX` or a plain assignment would
    /// break silently** — on any machine whose config is Valve's default, a map asking for more
    /// looks identical either way until someone runs `low.cfg`.
    /// </remarks>
    [Test]
    public void ForLevel_WithAMapAskingForMore_KeepsTheConfigs()
    {
        DetailFade.ForLevel(1200f, 400f, (8000f, 9000f)).ShouldBe((1200f, 400f));
    }

    /// <remarks>
    /// The two are MIN'd independently, so a map may cut one and not the other. Mixed on purpose:
    /// a controller that raises the distance and lowers the fade.
    /// </remarks>
    [Test]
    public void ForLevel_WithAMapCuttingOnlyTheFade_LeavesTheDistanceAlone()
    {
        DetailFade.ForLevel(1200f, 400f, (50f, 9000f)).ShouldBe((1200f, 50f));
    }

    /// <remarks>
    /// **The `else` branch: no controller means the config's values, not "whatever was last set".**
    /// The engine re-sets both cvars on every level load precisely so the previous map's override
    /// does not leak into this one.
    /// </remarks>
    [Test]
    public void ForLevel_WithNoController_IsTheConfigUntouched()
    {
        DetailFade.ForLevel(8592f, 250f, null).ShouldBe((8592f, 250f));
    }

    /// <remarks>
    /// **A controller stating nothing states zero** — Valve's entity allocator zeroes the object
    /// (`baseentity.cpp:3814`) and only `KeyValue` writes these fields — so a bare controller draws
    /// no detail props at all, by exactly the route `cl_detaildist 0` takes.
    /// </remarks>
    [Test]
    public void ForLevel_WithABareController_DrawsNothing()
    {
        (float Distance, float Fade) live = DetailFade.ForLevel(1200f, 400f, (0f, 0f));

        live.ShouldBe((0f, 0f));
        DetailFade.For(live.Distance, live.Fade).Alpha(0f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// **`fademindist` becomes a WIDTH, and the crossing is Valve's.** A mapper reads the key as
    /// "where fading begins"; it is assigned to `cl_detailfade`, whose help text is "Distance
    /// across which detail props fade in". So `fademindist 700, fademaxdist 1000` does not fade
    /// from 700 to 1000 — it gives a 1000-unit maximum with a 700-unit band, fully opaque only
    /// inside 300. A transcription that "fixed" this would put the fade start at 700 instead, and
    /// this asserts the difference between the two readings rather than the numbers alone: at 500
    /// units Valve's reading is mid-fade and the tidied one is fully opaque.
    ///
    /// **The config's fade has to be 800 for the map's 700 to survive the `MIN` at all**, which is
    /// itself worth stating — under Valve's own default of 400 the crossing is invisible, because
    /// the config clamps the band before the mapper's number can express anything.
    /// </remarks>
    [Test]
    public void ForLevel_WithTheMappersDistances_FadesFromThreeHundredNotSevenHundred()
    {
        (float Distance, float Fade) live = DetailFade.ForLevel(1200f, 800f, (700f, 1000f));

        live.ShouldBe((1000f, 700f));

        DetailFade fade = DetailFade.For(live.Distance, live.Fade);

        fade.Alpha(299f * 299f).ShouldBe((byte)255);
        fade.Alpha(500f * 500f).ShouldBeLessThan((byte)255);
        fade.Alpha(500f * 500f).ShouldBeGreaterThan((byte)0);
        fade.Alpha(1000f * 1000f).ShouldBe((byte)0);

        // The reading this rules out: a band of `fademaxdist − fademindist` would leave 500 units
        // fully opaque.
        DetailFade.For(1000f, 300f).Alpha(500f * 500f).ShouldBe((byte)255);
    }
}
