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
