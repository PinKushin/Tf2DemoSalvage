using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFTeamStatus` and `CTFTeamStatusPlayerPanel` (game/client/tf/tf_teamstatus.cpp).</summary>
public sealed class TfTeamStatusConformanceTests
{
    private const int Red = 2;
    private const int Blue = 3;

    /// <remarks>:200-227 over tf_shareddefs.cpp:1902-1946: a team mate's class, grey once dead; a dead enemy's class when killed.</remarks>
    [TestCase(Blue, 3, true, true, 0, "class_portraits/soldier_blue_alpha")]
    [TestCase(Blue, 3, false, true, 0, "class_portraits/soldier_blue_grey_alpha")]
    [TestCase(Red, 6, true, true, 0, "class_portraits/heavy_alpha")]
    [TestCase(Red, 0, false, false, 6, "class_portraits/heavy_grey_alpha")]
    [TestCase(Red, 0, true, false, 6, "class_portraits/silhouette_alpha")]
    [TestCase(Red, -1, true, true, 0, "hud_connecting")]
    public void Portrait_ByClassLifeAndTeam_PicksValvesImage(int team, int playerClass, bool alive, bool sameTeam, int deadClass, string image) =>
        TfTeamStatusPlayerPanel.Portrait(team, playerClass, alive, sameTeam, deadClass).ShouldBe(image);

    /// <remarks>`RemapValClamped( iTeamCount, 6, 12, 4, 1 )`, truncated (:452): 9 players is halfway, 2.5, so 2.</remarks>
    [TestCase(4, 4)]
    [TestCase(6, 4)]
    [TestCase(9, 2)]
    [TestCase(12, 1)]
    [TestCase(16, 1)]
    public void Gap_ByTeamSize_RemapsSixToTwelve(int count, int gap) =>
        TfTeamStatus.Gap(count, 4, 1).ShouldBe(gap);

    /// <remarks>tf_teamstatus.cpp:100-119: a spy feigning death, or a ghost in hell on a Halloween map, is dead and feigned.</remarks>
    [TestCase(8, 1 << 13, false, false, true)]
    [TestCase(1, 1 << 13, false, true, false)]
    [TestCase(1, 0, true, false, true)]
    [TestCase(1, 0, false, true, false)]
    public void LifeState_FeigningOrAGhostInHell_IsDeadAndFeigned(int playerClass, int conditions, bool hellGhost, bool alive, bool feigned)
    {
        ScenePlayer player = new(5, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: playerClass,
            Conditions: hellGhost ? new PlayerConditions(0, 0, 1 << (PlayerConditions.HalloweenGhostMode - 64), 0, 0) : new PlayerConditions(conditions, 0, 0, 0, 0));
        HudState state = new(true, true, 0, 125, true,
            Rules: new SceneGameRules(false, 0, false) { Present = true, PlayersInHell = hellGhost, MapHolidayType = TfHolidays.Halloween });

        TfTeamStatusPlayerPanel.LifeState(state, player, playerClass, resourceAlive: true).ShouldBe((alive, feigned));
    }

    /// <remarks>
    /// The ghost needs all three: in hell, Halloween active, and the ghost condition; holidays forced off rule it out. A map with
    /// no holiday is not tried here: the calendar is the watcher's clock, so its answer is the date's (the calendar is tested
    /// directly below).
    /// </remarks>
    [TestCase(false, TfHolidays.Halloween, "0")]
    [TestCase(true, TfHolidays.Halloween, "1")]
    public void LifeState_AGhostWithoutHellOrHalloween_StaysAlive(bool inHell, int mapHoliday, string forceOff)
    {
        ScenePlayer ghost = new(5, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: 1,
            Conditions: new PlayerConditions(0, 0, 1 << (PlayerConditions.HalloweenGhostMode - 64), 0, 0));
        HudState state = new(true, true, 0, 125, true,
            Rules: new SceneGameRules(false, 0, false) { Present = true, PlayersInHell = inHell, MapHolidayType = mapHoliday },
            ConVars: new HudConVars(name => name == "tf_force_holidays_off" ? forceOff : null));

        TfTeamStatusPlayerPanel.LifeState(state, ghost, 1, resourceAlive: true).ShouldBe((true, false));
    }

    /// <remarks>
    /// `BIsCvarIndicatingHolidayIsActive( value, kHoliday_Halloween )` (tf_gamerules.cpp:997): Halloween, HalloweenOrFullMoon or
    /// HalloweenOrFullMoonOrValentines; a forced full moon (8) is not Halloween.
    /// </remarks>
    [TestCase("2", true)]
    [TestCase("9", true)]
    [TestCase("10", true)]
    [TestCase("8", false)]
    [TestCase("0", false)]
    public void IsHalloweenActive_ByForcedHoliday_IsValves(string forced, bool active) =>
        TfHolidays.IsHalloweenActive(name => name == "tf_forced_holiday" ? forced : null, 0, new System.DateTime(2026, 6, 1, 0, 0, 0, System.DateTimeKind.Local)).ShouldBe(active);

    /// <remarks>`g_Holiday_Halloween( "10-01", "11-08" )`: inclusive to the second at the start, ending at 11-08 00:00:00.</remarks>
    [TestCase(9, 30, false)]
    [TestCase(10, 1, true)]
    [TestCase(11, 8, true)]
    [TestCase(11, 9, false)]
    public void IsHalloweenActive_ByCalendar_IsValves(int month, int day, bool active) =>
        TfHolidays.IsHalloweenActive(_ => null, 0, new System.DateTime(2026, month, day, 0, 0, 0, System.DateTimeKind.Local)).ShouldBe(active);
}
