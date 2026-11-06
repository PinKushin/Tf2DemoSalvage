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
}
