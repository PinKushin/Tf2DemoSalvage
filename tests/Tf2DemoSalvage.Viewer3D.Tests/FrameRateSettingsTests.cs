using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The frame rate limit and vertical sync settings.
/// </summary>
/// <remarks>
/// **Both exist because the measurement contradicted the code.** The swap chain presents with a
/// sync interval of one, which asks for vsync — and the viewer was measured at about 600 frames a
/// second on a machine whose driver forces vsync off globally. A driver override outranks the
/// present call, so the only honest cap is one this program applies itself.
///
/// **300 was documented as "Source's own <c>fps_max</c> ceiling", and there is no such ceiling.**
/// The owner: *"there is no actual ceiling, nocap will run 1000 fps in the real game at certain
/// places in certain maps"*. The engine has a FLOOR and no cap — the string *"sv_cheats is 0 and
/// fps_max is being limited to a minimum of 30 (or set to 0)"* is in every period engine from 2007
/// to live. 300 is this viewer's own number and rests on the 600 measured above.
///
/// The lower values matter to whoever is recording: 24 for film cadence, 30 and 60 for ordinary
/// video.
///
/// **Open, and stated as open:** the owner thinks *"the older clients might have had the cap, but
/// modern downt"*. A ceiling would be a numeric clamp with no string to scan for, so this needs a
/// decompiler to settle and has not been settled. Recorded rather than resolved by assumption.
/// </remarks>
public sealed class FrameRateSettingsTests
{
    [Test]
    public void FrameRateLimit_ByDefault_IsThreeHundred()
    {
        // Not uncapped. At 600 frames a second the viewer does ten times the work for a display
        // that cannot show it, and every one of those frames allocates.
        new ViewerSettings().FrameRateLimit.ShouldBe(ViewerSettings.DefaultFrameRateLimit);
    }

    [Test]
    public void ShowFrameRate_ByDefault_IsOff()
    {
        // As `cl_showfps` has it. A meter nobody asked for is furniture on the map.
        new ViewerSettings().ShowFrameRate.ShouldBe(0);
    }

    [Test]
    [TestCase("cl_showfps 0", 0)]
    [TestCase("cl_showfps 1", 1)]
    [TestCase("cl_showfps 2", 2)]

    // **Every other non-zero value draws the UNSMOOTHED meter, which is what the game does rather
    // than what a range check would do.** `ShouldDraw` tests `cl_showfps.GetInt()` for truth and
    // `Paint` then asks `== 2`, so 3 and -1 both fall to the else branch. Refusing them would be
    // this viewer disagreeing with a config TF2 accepts, which defeats taking Valve's name at all.
    [TestCase("cl_showfps 3", 1)]
    [TestCase("cl_showfps -1", 1)]
    public void ShowFrameRate_ForACvarValue_MatchesHowThePanelReadsIt(string config, int expected)
    {
        ViewerSettings.Parse(config).ShowFrameRate.ShouldBe(expected);
    }

    [Test]
    public void ByDefault_VerticalSyncIsOff()
    {
        // The owner turns it off globally and dislikes the latency. A default that fights the
        // machine's own setting is a default that produces support questions.
        new ViewerSettings().VerticalSync.ShouldBeFalse();
    }

    [Test]
    [TestCase(24)]
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    [TestCase(300)]
    public void ARateAVideoMakerWouldWant_SurvivesAWriteAndRead(int rate)
    {
        // The round trip is the test: a setting that writes but does not read back is a setting
        // that silently reverts on the next launch.
        ViewerSettings written = new() { FrameRateLimit = rate, VerticalSync = true };

        ViewerSettings read = ViewerSettings.Parse(written.Write());

        read.FrameRateLimit.ShouldBe(rate);
        read.VerticalSync.ShouldBeTrue();
    }

    [Test]
    public void AnUncappedRate_IsAllowedAndMeansNoWaiting()
    {
        // Zero is uncapped, for benchmarking. Kept expressible rather than clamped away, because
        // measuring how fast the renderer CAN go is a real question - it is how the 600 was found.
        ViewerSettings read = ViewerSettings.Parse(new ViewerSettings { FrameRateLimit = 0 }.Write());

        read.FrameRateLimit.ShouldBe(0);
    }

    [Test]
    public void ANegativeRate_IsIgnoredRatherThanObeyed()
    {
        // A hand-edited file can say anything. A negative budget would make every frame overdue
        // and the cap a no-op, which looks exactly like the cap not working.
        ViewerSettings.Parse("fps_max -60").FrameRateLimit
            .ShouldBe(ViewerSettings.DefaultFrameRateLimit);
    }

    [Test]
    public void ARateBelowThirty_IsObeyedEvenThoughTheGameClampsIt()
    {
        // **A deliberate departure, and the reason it is safe is in the engine's own words.**
        // `engine.dll` carries "sv_cheats is 0 and fps_max is being limited to a minimum of 30 (or
        // set to 0)" -- present in every era from 2007 to live, checked by scanning each period
        // build. That floor is anti-cheat: a very low cap is an advantage in a live match.
        //
        // There is no match here, and 24 is film cadence -- exactly what someone cutting a frag
        // video wants. So the clamp is not reproduced (D82: small, and justified).
        ViewerSettings.Parse("fps_max 24").FrameRateLimit.ShouldBe(24);
    }
}
