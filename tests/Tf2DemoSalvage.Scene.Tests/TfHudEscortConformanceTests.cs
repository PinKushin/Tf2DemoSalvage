using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudEscort` (game/client/tf/tf_hud_escort.cpp), `CSimpleControlPoint` (tf_hud_escort.h:101) and `C_TeamTrainWatcher::OnDataChanged`.</summary>
public sealed class TfHudEscortConformanceTests
{
    /// <remarks>`ApplySchemeSettings` (:569-624).</remarks>
    [TestCase(3, false, true, 0, "if_team_blue")]
    [TestCase(2, false, true, 2, "if_team_red if_single_with_hills if_single_with_hills_red")]
    [TestCase(3, true, false, 0, "if_team_blue if_multiple_trains if_multiple_trains_blue if_multiple_trains_bottom")]
    [TestCase(3, true, true, 3, "if_team_blue if_multiple_trains if_multiple_trains_blue if_multiple_trains_top")]
    public void Conditions_ByTeamTrainsAndHills_AreValves(int team, bool multipleTrains, bool topPanel, int hills, string conditions) =>
        string.Join(' ', TfHudEscort.Conditions(team, multipleTrains, topPanel, hills)!).ShouldBe(conditions);

    /// <remarks>`CEscortStatusTeardrop::UpdateBarText` (:257-317), the local player on RED in a running round.</remarks>
    [TestCase(2, 0, false, 4, "#Team_Capture_OwnPoint")]
    [TestCase(0, 3, true, 4, "#Team_Blocking_Capture")]
    [TestCase(3, 2, false, 4, "#Team_Progress_Blocked")]
    [TestCase(0, 0, false, 4, "")]
    [TestCase(0, 0, false, 5, "#Team_Capture_NotNow")]
    [TestCase(2, 3, false, 4, "#Team_Capture_OwnPoint")]
    public void BarText_ByOwnerCapperAndRound_IsValves(int owner, int capping, bool blocked, int roundState, string key)
    {
        HudState state = new(true, true, 0, 125, true, Team: 2, LocalIndex: 1, RoundState: roundState);
        SceneControlPoint point = new(true, owner, capping, 0, false, blocked, 0f, 0f, default, default, default, default, default);

        TfEscortStatusTeardrop.BarText(state, point).ShouldBe(key);
    }

    /// <remarks>:225-235: the local team capping, unblocked, shows the capping image and no text; :248-251 a won round hides it.</remarks>
    [TestCase(4, true, true, false)]
    [TestCase(5, false, false, false)]
    public void SetupForPoint_TheLocalTeamCapping_ShowsCappingWithoutText(int roundState, bool shown, bool capping, bool text)
    {
        VguiPanel owner = new(null, "Owner");
        TfEscortStatusTeardrop teardrop = new(owner);
        SceneControlPoint point = new(true, 3, 2, 0, false, false, 0f, 0f, default, default, default, default, default);
        HudState state = new(true, true, 0, 125, true, Team: 2, LocalIndex: 1, RoundState: roundState,
            Objectives: new SceneObjectives([point], "", new System.Collections.Generic.Dictionary<int, string>()));

        teardrop.SetupForPoint(state, 0);

        (teardrop.Visible, teardrop.FindChildByName("Capping")!.Visible && shown, teardrop.FindChildByName("ProgressText")!.Visible && shown)
            .ShouldBe((shown, capping, text));
    }

    [Test]
    public void SetupForPoint_OffEveryPoint_HidesTheTeardrop()
    {
        TfEscortStatusTeardrop teardrop = new(new VguiPanel(null, "Owner")) { Visible = true };
        HudState state = new(true, true, 0, 125, true, Team: 2, LocalIndex: 1, Objectives: new SceneObjectives([], "", new System.Collections.Generic.Dictionary<int, string>()));

        teardrop.SetupForPoint(state, -1);

        teardrop.Visible.ShouldBeFalse();
    }

    /// <remarks>
    /// `CEscortHillPanel::OnTick` (:57-80): the first tick a cycle after the signal is added, then one fade per 750 ms on the
    /// hill the cart is on — a new fade replacing the last — and off it, alpha 64 at once.
    /// </remarks>
    [Test]
    public void Tick_OnAHillThenOff_FadesOncePerCycleThenSits64()
    {
        HudViewport viewport = new();
        TfEscortHillPanel hill = new(viewport, "hill_panel0") { OnHill = true };

        hill.Tick(0f);
        int beforeACycle = viewport.Animations.ActiveAnimationCount;
        hill.Tick(0.8f);
        hill.Tick(0.9f);
        int afterOneCycle = viewport.Animations.ActiveAnimationCount;
        hill.Tick(1.6f);
        int afterTwo = viewport.Animations.ActiveAnimationCount;
        hill.OnHill = false;
        hill.Tick(2.4f);

        (beforeACycle, afterOneCycle, afterTwo, hill.GetFloat("alpha")).ShouldBe((0, 1, 1, 64f));
    }

    [Test]
    public void Conditions_ForNoGameTeam_AreNone() =>
        TfHudEscort.Conditions(1, false, true, 0).ShouldBeNull();

    [TestCase(3, false, "../hud/cart_point_blue")]
    [TestCase(2, true, "../hud/cart_point_red_opaque")]
    [TestCase(0, false, "../hud/cart_point_neutral")]
    public void PointImage_ByOwner_IsValves(int owner, bool opaque, string image) =>
        TfHudEscort.PointImage(owner, opaque).ShouldBe(image);

    /// <remarks>tf_hud_objectivestatus.cpp:286: payload by game type 3 or HUD type 3, never with a CTF or control-point HUD.</remarks>
    [TestCase(3, 0, true)]
    [TestCase(2, 3, true)]
    [TestCase(3, 2, false)]
    [TestCase(2, 0, false)]
    public void EscortVisible_ByGameAndHudType_FollowsSetVisiblePanels(int gameType, int hudType, bool visible) =>
        TfHudObjectiveStatus.EscortVisible(new SceneGameRules(false, 0, false) { GameType = gameType, HudType = hudType }).ShouldBe(visible);

    [Test]
    public void EscortVisible_OnAHybridCtfCpMap_IsHidden() =>
        TfHudObjectiveStatus.EscortVisible(new SceneGameRules(false, 0, false) { GameType = 3, PlayingHybridCtfCp = true }).ShouldBeFalse();

    /// <remarks>`OnDataChanged` from a new watcher's -2/-1/-1: speed and progress both fire; the swoop starts as it moves.</remarks>
    [Test]
    public void Apply_ANewWatcherMoving_TakesItsStateAndStartsTheSwoop() =>
        new SceneTrainWatcher(3, 0.25f, 2, 5f, 3).Apply(SceneTrainWatcher.Created(3), default, 10f)
            .ShouldBe(new SceneEscort(0.25f, 2, 3, 5f, 10f));

    [Test]
    public void Apply_OnlyTheProgressChanged_KeepsTheSwoopStart()
    {
        SceneTrainWatcher before = new(3, 0.25f, 2, -1f, 3);
        SceneEscort escort = new(0.25f, 2, 3, 0f, 10f);

        (before with { Progress = 0.3f }).Apply(before, escort, 12f).ShouldBe(escort with { Progress = 0.3f });
    }
}
