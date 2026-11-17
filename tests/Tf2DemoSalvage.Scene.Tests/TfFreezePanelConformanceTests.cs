using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFFreezePanel` (game/client/tf/tf_hud_freezepanel.cpp) and the client-side events that drive it (B748).</summary>
/// <remarks>
/// The recorder is entity 1, killed by entity 2 "Killer", a BLU soldier on 75 of 200; entity 3 "Engie" is a RED engineer
/// whose sentry is entity 40. A 640 × 480 screen, and a `.res` with the controls the code finds.
/// </remarks>
public sealed class TfFreezePanelConformanceTests
{
    [Test]
    public void Advance_EnteringTheFreezeCam_ShowsThePanelNamingTheObserverTarget()
    {
        // `PostDataUpdate`: `!m_bWasFreezeFraming && GetObserverMode() == OBS_MODE_FREEZECAM` fires `show_freezepanel` with
        // `killer = target->entindex()` (c_baseplayer.cpp:885-910).
        FreezeEvents events = new();
        events.Advance(Recorder(1f, ObserverModes.DeathCam), reset: false).ShouldBeEmpty();

        SceneGameEvent shown = events.Advance(Recorder(2f, ObserverModes.FreezeCam), reset: false).ShouldHaveSingleItem();

        (shown.Name, shown.GetInt("killer")).ShouldBe(("show_freezepanel", 2));
    }

    [Test]
    public void Advance_TheTravelTimeAfterEntry_StartsTheFreezeCamOnce()
    {
        // `CalcFreezeCamView`: `flCurTime >= spec_freeze_traveltime && !m_bSentFreezeFrame` fires `freezecam_started` and
        // sets the flag (c_baseplayer.cpp:1719-1729). 0.4 s.
        FreezeEvents events = new();
        events.Advance(Recorder(2f, ObserverModes.FreezeCam), reset: false);

        events.Advance(Recorder(2.39f, ObserverModes.FreezeCam), reset: false).ShouldBeEmpty();
        events.Advance(Recorder(2.4f, ObserverModes.FreezeCam), reset: false).ShouldHaveSingleItem().Name.ShouldBe("freezecam_started");
        events.Advance(Recorder(3f, ObserverModes.FreezeCam), reset: false).ShouldBeEmpty();
    }

    [Test]
    public void Advance_LeavingTheFreezeCam_HidesThePanel()
    {
        // `m_bWasFreezeFraming && GetObserverMode() != OBS_MODE_FREEZECAM` fires `hide_freezepanel` (:920-926).
        FreezeEvents events = new();
        events.Advance(Recorder(2f, ObserverModes.FreezeCam), reset: false);

        events.Advance(Recorder(7f, ObserverModes.None), reset: false).ShouldHaveSingleItem().Name.ShouldBe("hide_freezepanel");
    }

    [Test]
    public void HandleGameEvent_ShowByALivingPlayer_NamesHimAsTheKiller()
    {
        TfFreezePanel panel = Shown(killer: 2);

        panel.Visible.ShouldBeTrue();
        panel.FreezeLabel!.Text.ShouldBe("Killed by");
        Base(panel).DialogVariable("killername").ShouldBe("Killer");
    }

    [Test]
    public void HandleGameEvent_ShowByADeadPlayer_SaysHeIsDead()
    {
        // `if ( !pKiller->IsAlive() ) SetText( "#FreezePanel_Killer_Dead" )` (:394-397), and his health is 0 (:363-366),
        // which `SetHealth` shows as no number at all (tf_hud_playerstatus.cpp:840-860).
        TfFreezePanel panel = Shown(killer: 2, players: [Self(), Killer() with { LifeState = 2 }]);

        panel.FreezeLabel!.Text.ShouldBe("Killed by (dead)");
        panel.KillerHealth!.DialogVariable("Health").ShouldBe(string.Empty);
    }

    [Test]
    public void HandleGameEvent_ShowByALivingPlayer_ShowsHisHealth()
    {
        // `SetHealth( pKiller->GetHealth(), pKiller->GetMaxHealth(), m_Shared.GetMaxBuffedHealth() )` (:362-369).
        TfFreezePanel panel = Shown(killer: 2);

        panel.KillerHealth!.Building.ShouldBeFalse();
        panel.KillerHealth.DialogVariable("Health").ShouldBe("75");
    }

    [Test]
    public void HandleGameEvent_ShowByTheRecordersNemesis_SaysSoAndShowsTheNemesisLine()
    {
        // On the client `pTFPlayerKiller->m_Shared.IsPlayerDominated( local )` is the local player's own
        // `m_bPlayerDominatingMe[ killer ]` (tf_player_shared.cpp:7737-7751).
        TfFreezePanel panel = Shown(killer: 2, players: [Self() with { DominatingMe = System.UInt128.One << 2 }, Killer()]);

        panel.FreezeLabel!.Text.ShouldBe("Your nemesis");
        panel.NemesisSubPanel!.Visible.ShouldBeTrue();
        panel.NemesisSubPanel.DialogVariable("nemesisname").ShouldBe("NEMESIS");
    }

    [Test]
    public void HandleGameEvent_AnOrdinaryKill_HidesTheNemesisLine()
    {
        // The control for the one above: no domination, no line (`ShowNemesisPanel( pchNemesisText != NULL )`, :663).
        Shown(killer: 2).NemesisSubPanel!.Visible.ShouldBeFalse();
    }

    [Test]
    public void HandleGameEvent_ADeathThatWasARevenge_ShowsTheRevengeLine()
    {
        // `player_death` on the local player with `TF_DEATH_REVENGE` sets `SHOW_REVENGE` (:239-242), whose line needs no
        // domination (:640-642).
        TfFreezePanel panel = Built();
        Fire(panel, "player_death", new Dictionary<string, object?> { ["userid"] = 101, ["death_flags"] = 0x0004 });
        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 });

        panel.NemesisSubPanel!.DialogVariable("nemesisname").ShouldBe("REVENGE");
    }

    [Test]
    public void HandleGameEvent_ShowByASentry_NamesTheBuilderAndTheBuilding()
    {
        // `pKiller->IsBaseObject()` (:492-540): the builder's name, `#FreezePanel_KillerObject`, and `GetStatusName()`.
        TfFreezePanel panel = Shown(killer: 40);

        panel.FreezeLabel!.Text.ShouldBe("Killed by the");
        Base(panel).DialogVariable("killername").ShouldBe("Engie");
        Base(panel).DialogVariable("objectkiller").ShouldBe("Sentry Gun");
        panel.KillerIndex.ShouldBe(3, "m_iKillerIndex becomes the builder (:498)");
        panel.KillerHealth!.Building.ShouldBeTrue();
    }

    [Test]
    public void HandleGameEvent_ShowByABlueKiller_PaintsTheBlueBackground()
    {
        // "use the killer's team for the background color" (:580-584).
        Shown(killer: 2).Background!.ImageName.ShouldBe("../hud/color_panel_blu");
        Shown(killer: 40).Background!.ImageName.ShouldBe("../hud/color_panel_red", "the control: the RED engineer's sentry");
    }

    [Test]
    public void HandleGameEvent_ShowAfterTheRoundWasWon_SitsFiftyUnitsHigher()
    {
        // `if ( TFGameRules()->RoundHasBeenWon() ) SetPos( xp, m_iYBase - YRES(50) )` (:344-351); YRES(50) is 50 at 480.
        TfFreezePanel panel = Built();
        int baseY = panel.Y;

        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(1f) with { RoundState = 5 });

        panel.Y.ShouldBe(baseY - 50);
    }

    [Test]
    public void HandleGameEvent_HideFreezePanel_HidesIt()
    {
        TfFreezePanel panel = Shown(killer: 2);

        Fire(panel, "hide_freezepanel", []);

        panel.Visible.ShouldBeFalse();
    }

    [Test]
    public void OnThink_TheSnapshotDelayAfterTheFreezeCamStarted_ShowsTheScreenshotPromptWithItsKey()
    {
        // `freezecam_started` calls `ShowSnapshotPanelIn( 1.25 )` (:256), and `OnThink` shows it once that passes while the
        // panel is up (:938-949), with `Key_LookupBinding( "screenshot" )` in `#TF_freezecam_snapshot` (:997-1015).
        TfFreezePanel panel = Shown(killer: 2);
        Fire(panel, "freezecam_started", [], State(2f));
        HudViewport viewport = (HudViewport)panel.Parent!;

        viewport.SetState(State(3.2f));
        panel.Think();
        panel.ScreenshotPanel!.Visible.ShouldBeFalse("1.2 s on, not yet");

        viewport.SetState(State(3.3f));
        panel.Think();
        panel.ScreenshotPanel.Visible.ShouldBeTrue();
        panel.ScreenshotPanel.DialogVariable("text").ShouldBe("Press F5 to save");
    }

    [Test]
    public void OnThink_NoKeyBoundToScreenshot_KeepsThePromptHidden()
    {
        // `if ( key == NULL ... ) bShow = false` (:999-1003).
        TfFreezePanel panel = Shown(killer: 2);
        Fire(panel, "freezecam_started", [], State(2f));
        HudViewport viewport = (HudViewport)panel.Parent!;

        viewport.SetState(State(3.3f) with { KeyLookupBinding = static _ => null });
        panel.Think();

        panel.ScreenshotPanel!.Visible.ShouldBeFalse();
    }

    [Test]
    public void Advance_AFreezeCamWithNoObserverTarget_NeverStartsIt()
    {
        // `CalcFreezeCamView` falls to `CalcDeathCamView` with no target (c_baseplayer.cpp:1654-1659), before the line that
        // fires `freezecam_started` — so no prompt follows.
        FreezeEvents events = new();
        events.Advance(Recorder(2f, ObserverModes.FreezeCam) with { ObserverTarget = 0 }, reset: false);

        events.Advance(Recorder(3f, ObserverModes.FreezeCam) with { ObserverTarget = 0 }, reset: false).ShouldBeEmpty();
    }

    [Test]
    public void HandleGameEvent_ShowByTheWorld_GivesThePlainLabelAndNoHealth()
    {
        // `if ( pKiller )` holds for the world, entity 0: its health 0 of 0, and the final `else` of the label (:568-578).
        // After a player's card, so a stale label or health would show.
        TfFreezePanel panel = Shown(killer: 2, players: [Self(), Killer() with { LifeState = 2 }]);

        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 0 });

        panel.FreezeLabel!.Text.ShouldBe("Killed by");
        panel.KillerHealth!.DialogVariable("Health").ShouldBe(string.Empty);
        panel.Background!.ImageName.ShouldBe("../hud/color_panel_red", "the world's team is not BLU");
    }

    [Test]
    public void HandleGameEvent_ShowByASentryWithABuilder_ShowsTheAvatarPanel()
    {
        // `m_pAvatar->SetVisible( true )` with an owner (:502-506), hidden without one.
        Shown(killer: 40).FindChildByName("FreezePanelBase")!.FindChildByName("AvatarImage")!.Visible.ShouldBeTrue();
    }

    [Test]
    public void HandleGameEvent_AHideBeforeThePromptsTime_KeepsItsTimerForAReshow()
    {
        // `Hide` (:887-903) leaves `m_flShowSnapshotReminderAt` alone, so a card shown again before 1.25 s still prompts.
        TfFreezePanel panel = Shown(killer: 2);
        Fire(panel, "freezecam_started", [], State(2f));
        Fire(panel, "hide_freezepanel", []);
        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(2.5f));
        HudViewport viewport = (HudViewport)panel.Parent!;

        viewport.SetState(State(3.3f));
        panel.Think();

        panel.ScreenshotPanel!.Visible.ShouldBeTrue();
    }

    /// <remarks>
    /// **A second after the freeze cam starts, a callout over each spawned gib on screen** (B752): `ShowCalloutsIn( 1.0 )`
    /// (:255), then `OnThink` runs `UpdateCallout` once while the panel is up (:929-936). Callout naming is by the gib's place
    /// in `m_hSpawnedGibs` (`UpdateForGib`, :1294-1309): 0 the head, 1 a foot. The view's matrix maps a world (x, y) straight
    /// onto the 640 × 480 screen, so a gib at (300, 200) points the arrow there and the callout's top left is 25 and 50 short
    /// of it (`TestAndAddCallout`, :690-699).
    /// </remarks>
    [Test]
    public void OnThink_ASecondAfterTheFreezeCamStarted_CallsOutEachGibOnScreen()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f)), (1, new(450f, 200f, 0f))], null, null));

        panel.Callouts.Count.ShouldBe(2);
        (panel.Callouts[0].X, panel.Callouts[0].Y, panel.Callouts[0].Wide, panel.Callouts[0].Tall).ShouldBe((275, 150, 100, 50));
        panel.Callouts[0].Label!.Text.ShouldBe("Head");
        panel.Callouts[1].Label!.Text.ShouldBe("Foot");
    }

    /// <remarks>Not before the second has passed: at 3.3 the freeze cam has run 0.9 of it (started at 2.4).</remarks>
    [Test]
    public void OnThink_BeforeTheSecondIsUp_CallsOutNothing()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f))], null, null), now: 3.3f);

        panel.Callouts.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A callout that would overlap one already placed is dropped** (:710-719), strictly: boxes that only touch are clear
    /// (`QuickBoxIntersectTest`, mathlib.h:1398-1408). 50 to the right overlaps the first's 100-wide box; 100 only touches.
    /// </remarks>
    [Test]
    public void OnThink_TwoGibsWhoseCalloutsOverlap_CallsOutTheFirstOnly()
    {
        TfFreezePanel overlapping = Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f)), (1, new(350f, 200f, 0f))], null, null));
        TfFreezePanel touching = Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f)), (1, new(400f, 200f, 0f))], null, null));

        overlapping.Callouts.Count.ShouldBe(1);
        touching.Callouts.Count.ShouldBe(2, "the control: touching is not intersecting");
    }

    /// <remarks>
    /// **On screen means strictly inside every edge, and above the bottom forty** (:702): `iX &gt; 0`, `iY &gt; 0`,
    /// `iRight &lt; ScreenWidth()`, `iBottom &lt; ScreenHeight() - YRES(40)`. The callout's top left is the point less (25, 50) and
    /// it is 100 by 50, so each edge falls between two neighbouring pixels. Each point is half a pixel in, so the projection's
    /// float rounding cannot carry it across the truncation.
    /// </remarks>
    [TestCase(26.5f, 200.5f, 1, TestName = "OnThink_AGibOnePixelInsideTheLeftEdge_IsCalledOut")]
    [TestCase(25.5f, 200.5f, 0, TestName = "OnThink_AGibOnTheLeftEdge_IsNotCalledOut")]
    [TestCase(300.5f, 51.5f, 1, TestName = "OnThink_AGibOnePixelInsideTheTopEdge_IsCalledOut")]
    [TestCase(300.5f, 50.5f, 0, TestName = "OnThink_AGibOnTheTopEdge_IsNotCalledOut")]
    [TestCase(564.5f, 200.5f, 1, TestName = "OnThink_AGibOnePixelInsideTheRightEdge_IsCalledOut")]
    [TestCase(565.5f, 200.5f, 0, TestName = "OnThink_AGibOnTheRightEdge_IsNotCalledOut")]
    [TestCase(300.5f, 439.5f, 1, TestName = "OnThink_AGibOnePixelAboveTheBottomForty_IsCalledOut")]
    [TestCase(300.5f, 440.5f, 0, TestName = "OnThink_AGibOnTheBottomForty_IsNotCalledOut")]
    public void OnThink_AGibByAnEdge_IsCalledOutOnlyStrictlyInside(float x, float y, int callouts)
    {
        Calling(At(x, y)).Callouts.Count.ShouldBe(callouts);
    }

    /// <remarks>**Behind the view is nowhere** (`GetVectorInHudSpace` answers false when w is under 0.001, cdll_util.cpp:565-571).</remarks>
    [Test]
    public void OnThink_AGibBehindTheView_IsNotCalledOut()
    {
        Calling(At(300f, 200f), w: -1f).Callouts.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The freeze panel's box is its background's place inside its parent** (:767-769): `FreezePanelBG` sits at (0, 0) of
    /// `FreezePanelBase`, 79 × 43, so a callout over the screen's top left corner is refused although the panel is drawn at
    /// (10, 300). The control is a gib further right.
    /// </remarks>
    [Test]
    public void OnThink_ACalloutOverTheBackgroundsOwnPlace_IsNotCalledOut()
    {
        Calling(At(60f, 80f)).Callouts.ShouldBeEmpty();
        Calling(At(160f, 80f)).Callouts.Count.ShouldBe(1, "the control: clear of (0, 0, 79, 43)");
    }

    /// <remarks>
    /// **Each size is the screen's, truncated where the engine truncates** (:691-702, :736): at 1366 × 768 the offsets are
    /// `(int)XRES(25)` 53 and `(int)YRES(50)` 80, and the callout is `XRES(100)` 213.4 by `XRES(50)` 106.7, both of the
    /// width, kept as 213 and 106. A gib at (600, 400) puts the callout at (547, 320).
    /// </remarks>
    [Test]
    public void OnThink_OnAWideScreen_ScalesAndTruncatesAsTheEngineDoes()
    {
        TfFreezePanelCallout callout = Calling(At(600f, 400f), wide: 1366, tall: 768).Callouts.ShouldHaveSingleItem();

        (callout.X, callout.Y, callout.Wide, callout.Tall).ShouldBe((547, 320, 213, 106));
    }

    /// <remarks>
    /// **Past the named four, a gib says how many callouts came before it, counting only those placed** (:811-812, :1310-1324):
    /// three gibs at places 5, 6 and 7, after one at place 9 that is off screen, read "There", "And there", "And there too".
    /// </remarks>
    [Test]
    public void OnThink_GibsPastTheNamedFour_CountOnlyTheCalloutsPlaced()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources(
            [(9, new(-100f, 200f, 0f)), (5, new(100f, 200f, 0f)), (6, new(250f, 200f, 0f)), (7, new(400f, 200f, 0f))], null, null));

        (panel.Callouts[0].Label!.Text, panel.Callouts[1].Label!.Text, panel.Callouts[2].Label!.Text)
            .ShouldBe(("There", "And there", "And there too"));
    }

    /// <remarks>
    /// **The fifth place names a random organ** (:1305-1308), and the same callouts name the same one however much was
    /// played before them. *Interpolated:* the engine's draw comes from the client's global stream.
    /// </remarks>
    [Test]
    public void OnThink_AGibAtTheFifthPlace_NamesTheSameOrganEachTime()
    {
        string first = Calling(new FreezeCalloutSources([(4, new(300f, 200f, 0f))], null, null)).Callouts.ShouldHaveSingleItem().Label!.Text;
        TfFreezePanel again = Calling(new FreezeCalloutSources([(4, new(300f, 200f, 0f))], null, null));
        Fire(again, "hide_freezepanel", []);
        Fire(again, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(4f));
        Fire(again, "freezecam_started", [], State(4.4f));
        Think(again, new FreezeCalloutSources([(4, new(300f, 200f, 0f))], null, null), 5.5f);

        first.ShouldStartWith("Organ");
        again.Callouts.ShouldHaveSingleItem().Label!.Text.ShouldBe(first);
    }

    /// <remarks>
    /// **The ragdoll after the gibs** (:788-857): with both at one point, the gib takes the place and the ragdoll's callout
    /// overlaps it and is refused.
    /// </remarks>
    [Test]
    public void OnThink_AGibAndTheRagdollAtOnePoint_CallsOutTheGib()
    {
        Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f))], new(300f, 200f, 0f), null))
            .Callouts.ShouldHaveSingleItem().Label!.Text.ShouldBe("Head");
    }

    /// <remarks>
    /// **Placed once a freeze cam** (:929-936): the timer is cleared by the pass that places them, so a later think adds
    /// none. And **`Show` clears a pending one** (:871): a card shown again before the second is up places nothing.
    /// </remarks>
    [Test]
    public void OnThink_TheCalloutTimer_RunsOncePerFreezeCamAndAShowClearsIt()
    {
        TfFreezePanel placed = Calling(At(300f, 200f));
        Think(placed, At(450f, 200f), 4f);

        TfFreezePanel reshown = Built();
        Fire(reshown, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(1f));
        Fire(reshown, "freezecam_started", [], State(2.4f));
        Fire(reshown, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(2.5f));
        Think(reshown, At(300f, 200f), 3.5f);

        placed.Callouts.Count.ShouldBe(1, "a second pass would have added the gib at 450");
        reshown.Callouts.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A label wider than its control widens it, its background and the callout by the same amount, and a callout pushed
    /// past the right edge moves left with its arrow moved right** (`UpdateForGib`, :1327-1367). The torso's text here is
    /// wider than the test label's 80.
    /// </remarks>
    [Test]
    public void OnThink_ALabelWiderThanItsControl_WidensTheCalloutAndKeepsItOnScreen()
    {
        TfFreezePanelCallout middle = Calling(new FreezeCalloutSources([(3, new(300f, 200f, 0f))], null, null)).Callouts.ShouldHaveSingleItem();
        TfFreezePanelCallout edge = Calling(new FreezeCalloutSources([(3, new(560f, 200f, 0f))], null, null)).Callouts.ShouldHaveSingleItem();
        (int text, _) = middle.Label!.GetContentSize();
        int grown = text - 80;

        grown.ShouldBeGreaterThan(0, "the control: the text is wider than the control");
        (middle.Label.Wide, middle.FindChildByName("CalloutBG")!.Wide, middle.Wide, middle.X).ShouldBe((text, 80 + grown, 100 + grown, 275));
        (edge.X + edge.Wide).ShouldBe(640, "pushed left to the screen's edge");
        edge.FindChildByName("ArrowIcon")!.X.ShouldBe(20 + (535 - edge.X));
    }

    /// <remarks>**Only what the view can see** (:724-726): a world trace from the gib to `MainViewOrigin()` that stops short.</remarks>
    [Test]
    public void OnThink_AGibBehindAWall_IsNotCalledOut()
    {
        Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f))], null, static (_, _) => 0.5f)).Callouts.ShouldBeEmpty();
        Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f))], null, static (_, _) => 1f)).Callouts.Count.ShouldBe(1, "the control");
    }

    /// <remarks>**The ragdoll after the gibs, named as one** (:838-850, `UpdateForRagdoll`, :1374-1380).</remarks>
    [Test]
    public void OnThink_ARagdollOnScreen_IsCalledOutAsTheRagdoll()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources([], new(300f, 200f, 0f), null));

        panel.Callouts.ShouldHaveSingleItem().Label!.Text.ShouldBe("Your body");
    }

    /// <remarks>
    /// **Past the four named places, a gib is counted** (:1310-1324): its index is 4 or more and it says how many callouts
    /// came before it. Index 5 after one placed is "Callout2".
    /// </remarks>
    [Test]
    public void OnThink_AGibPastTheNamedFour_SaysHowManyCameBefore()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources([(0, new(150f, 200f, 0f)), (5, new(450f, 200f, 0f))], null, null));

        panel.Callouts[1].Label!.Text.ShouldBe("And there");
    }

    /// <remarks>**`Hide` deletes every callout** (:887-893): they are the viewport's children and go with the panel.</remarks>
    [Test]
    public void HandleGameEvent_HidingThePanel_DeletesItsCallouts()
    {
        TfFreezePanel panel = Calling(new FreezeCalloutSources([(0, new(300f, 200f, 0f))], null, null));
        TfFreezePanelCallout callout = panel.Callouts.ShouldHaveSingleItem();

        Fire(panel, "hide_freezepanel", []);

        panel.Callouts.ShouldBeEmpty();
        callout.Parent.ShouldBeNull();
    }

    /// <remarks>
    /// **The sources are the local player's, in the freeze cam** (B752): his spawned gibs and his ragdoll's root
    /// (`GetSpawnedGibs`, `GetRepresentativeRagdoll`, :757-758). Outside the freeze cam none are gathered.
    /// </remarks>
    [Test]
    public void Of_TheRecorderGibbedInTheFreezeCam_GathersHisPieces()
    {
        SceneRagdoll gibbed = new(100, 0, 3, SceneTeams.Red, 0f, 0f, 0f, true, false, false, false, 10, 500, PlayerIndex: 1);
        Dictionary<int, System.Numerics.Vector3> centres = new() { [RagdollProps.GibEntityIndex(gibbed, 0)] = new(1f, 2f, 3f) };
        HudState freezing = Recorder(5f, ObserverModes.FreezeCam);

        FreezeCalloutSources sources = FreezeCalloutSources.Of(freezing, 300, [gibbed], centres, centres, null, null).ShouldNotBeNull();

        sources.Gibs.ShouldBe([(0, new System.Numerics.Vector3(1f, 2f, 3f))]);
        sources.Ragdoll.ShouldBeNull("a gibbed corpse has no ragdoll");
        FreezeCalloutSources.Of(Recorder(5f, ObserverModes.DeathCam), 300, [gibbed], centres, centres, null, null)
            .ShouldBeNull("the control: not in the freeze cam");
    }

    /// <remarks>A body is called out at its simulated root; with neither gibs nor a simulated body there is nothing.</remarks>
    [Test]
    public void Of_TheRecordersSimulatedBody_GivesItsRoot()
    {
        SceneRagdoll body = new(100, 0, 3, SceneTeams.Red, 0f, 0f, 0f, false, false, false, false, 10, 500, PlayerIndex: 1);
        Dictionary<int, System.Numerics.Vector3> roots = new() { [RagdollProps.FirstCorpseEntityIndex] = new(4f, 5f, 6f) };
        HudState freezing = Recorder(5f, ObserverModes.FreezeCam);

        FreezeCalloutSources.Of(freezing, 300, [body], roots, roots, null, null).ShouldNotBeNull().Ragdoll
            .ShouldBe(new System.Numerics.Vector3(4f, 5f, 6f));
        FreezeCalloutSources.Of(freezing, 300, [body], roots, new Dictionary<int, System.Numerics.Vector3>(), null, null)
            .ShouldBeNull("the control: not yet simulated");
    }

    /// <summary>
    /// The panel shown, the freeze cam started at 2.4, and the viewport's think at <paramref name="now"/> with these sources
    /// and a view whose matrix maps a world (x, y) to the screen pixel (x, y).
    /// </summary>
    private static TfFreezePanel Calling(FreezeCalloutSources sources, float now = 3.5f, int wide = 640, int tall = 480, float w = 1f)
    {
        TfFreezePanel panel = Built(wide, tall);
        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = 2 }, State(1f));
        Fire(panel, "freezecam_started", [], State(2.4f));
        Think(panel, sources, now, wide, tall, w);

        return panel;
    }

    /// <summary>The viewport's think at a time, with these sources and the pixel matrix.</summary>
    private static void Think(TfFreezePanel panel, FreezeCalloutSources sources, float now, int wide = 640, int tall = 480, float w = 1f)
    {
        HudViewport viewport = (HudViewport)panel.Parent!;

        viewport.SetState(State(now) with
        {
            WorldToScreen = PixelMatrix(wide, tall, w),
            FreezeCallouts = () => sources,
            View = new FreeCamera { Origin = (320f, 240f, 100f) },
        });
        panel.Think();
    }

    /// <summary>
    /// A matrix `GetVectorInHudSpace` turns into screen pixel (x, y) for world (x, y) on a <paramref name="wide"/> by
    /// <paramref name="tall"/> screen; <paramref name="w"/> below 0.001 puts every point behind the view.
    /// </summary>
    private static float[] PixelMatrix(int wide, int tall, float w) =>
    [
        2f / wide, 0f, 0f, 0f,
        0f, -2f / tall, 0f, 0f,
        0f, 0f, 0f, 0f,
        -1f * w, 1f * w, 0f, w,
    ];

    /// <summary>One gib at place 0 at a screen point.</summary>
    private static FreezeCalloutSources At(float x, float y) => new([(0, new(x, y, 0f))], null, null);

    private static VguiEditablePanel Base(TfFreezePanel panel) => (VguiEditablePanel)panel.FindChildByName("FreezePanelBase")!;

    private static HudState Recorder(float now, int mode) =>
        State(now) with { ObserverMode = mode, ObserverTarget = 2 };

    private static HudState State(float now) =>
        new(true, true, 0, 0, false, CurTime: now, Team: 2, LocalIndex: 1, Players: [Self(), Killer(), Engineer()],
            Buildings: [new SceneBuilding(40) { Health = 150, MaxHealth = 216, ObjectType = SceneBuilding.Sentrygun, Team = 2, BuilderEntityIndex = 3 }],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Killer", [3] = "Engie" },
            KeyLookupBinding: static command => command == "screenshot" ? "F5" : null);

    private static ScenePlayer Self() => new(1, 0f, 0f, 0f, 2, 0, 3) { LifeState = 2 };

    private static ScenePlayer Killer() => new(2, 0f, 0f, 0f, 3, 75, 3) { EntityHealth = 75, MaxHealth = 200, MaxHealthForBuffing = 200 };

    private static ScenePlayer Engineer() => new(3, 0f, 0f, 0f, 2, 125, 9) { EntityHealth = 125, MaxHealth = 125 };

    private static TfFreezePanel Shown(int killer, IReadOnlyList<ScenePlayer>? players = null)
    {
        TfFreezePanel panel = Built();
        HudState state = State(1f);

        Fire(panel, "show_freezepanel", new Dictionary<string, object?> { ["killer"] = killer }, players is null ? state : state with { Players = players });

        return panel;
    }

    private static void Fire(TfFreezePanel panel, string name, Dictionary<string, object?> fields, HudState? state = null)
    {
        HudState at = state ?? State(1f);
        Dictionary<int, PlayerInfo> roster = new() { [1] = new PlayerInfo("Me", 101, string.Empty, 1, false, false) };

        panel.HandleGameEvent(new HudGameEvent(new SceneGameEvent(0, name, fields, roster), at.CurTime, at.Players ?? [], 1, default, string.Empty, 0), at);
    }

    private static TfFreezePanel Built(int wide = 640, int tall = 480)
    {
        Dictionary<string, byte[]> files = new()
        {
            [TfFreezePanel.ResFile] = Encoding.UTF8.GetBytes("""
                "Resource/UI/FreezePanel_Basic.res"
                {
                    "FreezePanelBase"
                    {
                        "ControlName" "EditablePanel" "fieldName" "FreezePanelBase" "xpos" "10" "ypos" "300" "wide" "250" "tall" "100"
                        "FreezePanelBG" { "ControlName" "CTFImagePanel" "fieldName" "FreezePanelBG" "wide" "79" "tall" "43" "image" "../hud/color_panel_brown" }
                        "FreezeLabel" { "ControlName" "CExLabel" "fieldName" "FreezeLabel" "wide" "170" "tall" "19" "labelText" "#FreezePanel_Killer" }
                        "FreezePanelHealth" { "ControlName" "CTFFreezePanelHealth" "fieldName" "FreezePanelHealth" "wide" "71" "tall" "35" }
                        "FreezeLabelKiller" { "ControlName" "CExLabel" "fieldName" "FreezeLabelKiller" "xpos" "0" "ypos" "20" "wide" "120" "tall" "19" "labelText" "%killername%" }
                        "AvatarImage" { "ControlName" "CAvatarImagePanel" "fieldName" "AvatarImage" "xpos" "49" "ypos" "74" "visible" "0" }
                        "NemesisSubPanel"
                        {
                            "ControlName" "EditablePanel" "fieldName" "NemesisSubPanel" "ypos" "30" "wide" "306" "tall" "38"
                            "NemesisPanelBG" { "ControlName" "ScalableImagePanel" "fieldName" "NemesisPanelBG" "wide" "161" "tall" "30" }
                            "NemesisIcon" { "ControlName" "ImagePanel" "fieldName" "NemesisIcon" "wide" "18" "tall" "18" }
                            "NemesisLabel" { "ControlName" "CExLabel" "fieldName" "NemesisLabel" "wide" "112" "tall" "18" "labelText" "%nemesisname%" }
                            "NemesisLabel2" { "ControlName" "Label" "fieldName" "NemesisLabel2" "wide" "112" "tall" "18" "labelText" "%nemesisadvice%" }
                        }
                    }
                    "ScreenshotPanel"
                    {
                        "ControlName" "EditablePanel" "fieldName" "ScreenshotPanel" "wide" "166" "tall" "40" "visible" "0"
                        "ScreenshotLabel" { "ControlName" "Label" "fieldName" "ScreenshotLabel" "wide" "133" "tall" "12" "labelText" "%text%" }
                    }
                }
                """),

            // TF2's own controls, made visible and given a width so a label's text is kept as drawn.
            [TfFreezePanelCallout.ResFile] = Encoding.UTF8.GetBytes("""
                "Resource/UI/FreezePanelCallout.res"
                {
                    "CalloutBG" { "ControlName" "CTFImagePanel" "fieldName" "CalloutBG" "xpos" "12" "ypos" "13" "wide" "80" "tall" "24" }
                    "ArrowIcon" { "ControlName" "ImagePanel" "fieldName" "ArrowIcon" "xpos" "20" "ypos" "35" "wide" "10" "tall" "10" }
                    "CalloutLabel" { "ControlName" "CExLabel" "fieldName" "CalloutLabel" "xpos" "12" "ypos" "15" "wide" "80" "tall" "20" "labelText" "#FreezePanel_Callout" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Callout_Head"] = "Head",
            ["Callout_Foot"] = "Foot",
            ["Callout_Ragdoll"] = "Your body",
            ["Callout_Torso"] = "The torso, a long label",
            ["Callout_Organ2"] = "Organ 2",
            ["Callout_Organ3"] = "Organ 3",
            ["Callout_Organ4"] = "Organ 4",
            ["Callout_Organ5"] = "Organ 5",
            ["Callout_Organ6"] = "Organ 6",
            ["FreezePanel_Callout"] = "There",
            ["FreezePanel_Callout2"] = "And there",
            ["FreezePanel_Callout3"] = "And there too",
            ["FreezePanel_Killer"] = "Killed by",
            ["FreezePanel_Killer_Dead"] = "Killed by (dead)",
            ["FreezePanel_Nemesis"] = "Your nemesis",
            ["FreezePanel_Nemesis_Dead"] = "Your nemesis (dead)",
            ["FreezePanel_KillerObject"] = "Killed by the",
            ["FreezePanel_KillerObject_Dead"] = "Killed by the (dead)",
            ["TF_FreezeNemesis"] = "NEMESIS",
            ["TF_NewNemesis"] = "NEW NEMESIS",
            ["TF_GotRevenge"] = "REVENGE",
            ["TF_freezecam_snapshot"] = "Press %s1 to save",
            ["TF_Object_Sentry"] = "Sentry Gun",
        };

        // A font, so a label measures its text: `TextRecorder` gives each character 10.
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""Scheme { Colors { } Borders { } Fonts { "Default" { "1" { "name" "Arial" "tall" "12" } } } }"""),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiSchemeFonts fonts = VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), _ => null, "english", tall);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, tall), scheme.Find("Fonts")!, wide, tall, "english", fonts)
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = wide, Tall = tall, Context = context };
        TfFreezePanel panel = new(viewport);

        panel.PerformApplySchemeSettings(context);
        viewport.SetState(State(1f));

        return panel;
    }
}
