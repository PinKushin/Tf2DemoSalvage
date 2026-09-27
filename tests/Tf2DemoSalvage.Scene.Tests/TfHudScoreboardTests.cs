using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFClientScoreBoardDialog` (`tf_clientscoreboard.cpp`): its two player lists.</summary>
public sealed class TfHudScoreboardTests
{
    [Test]
    public void InitPlayerList_ThePlayersSection_HasEveryColumnInOrderWithValvesWidths()
    {
        VguiSectionedListPanel list = new(null, "RedPlayerList");

        TfClientScoreBoardDialog.InitPlayerList(list);

        list.ItemCount.ShouldBe(0);

        // Section 0 must accept a row so its columns exist; assert shape by adding one full row and
        // reading it back — the panel does not expose its column list directly.
        Dictionary<string, string> row = new(System.StringComparer.Ordinal)
        {
            ["medal"] = "0",
            ["avatar"] = "0",
            ["spacer"] = string.Empty,
            ["name"] = "Soldier",
            ["killstreak"] = "3",
            ["killstreak_image"] = "0",
            ["dominating"] = "0",
            ["nemesis"] = "0",
            ["score"] = "12",
            ["class"] = "3",
            ["ping"] = "40",
        };

        int itemId = list.AddItem(0, row);

        list.GetItemData(itemId).ShouldNotBeNull();
        list.GetItemSection(itemId).ShouldBe(0);
    }

    [Test]
    public void TfPlayerSortFunc_HigherScore_SortsFirst()
    {
        VguiSectionedListPanel list = new(null, "list");
        list.AddSection(0, "s");

        int low = list.AddItem(0, Row(playerIndex: 1, score: 5, connected: 2));
        int high = list.AddItem(0, Row(playerIndex: 2, score: 10, connected: 2));

        TfClientScoreBoardDialog.TfPlayerSortFunc(list, high, low).ShouldBeTrue();
        TfClientScoreBoardDialog.TfPlayerSortFunc(list, low, high).ShouldBeFalse();
    }

    [Test]
    public void TfPlayerSortFunc_EqualScoreButOneNotFullyConnected_PutsFullyConnectedFirst()
    {
        VguiSectionedListPanel list = new(null, "list");
        list.AddSection(0, "s");

        int connecting = list.AddItem(0, Row(playerIndex: 1, score: 0, connected: 0));
        int connected = list.AddItem(0, Row(playerIndex: 2, score: 0, connected: 2));

        TfClientScoreBoardDialog.TfPlayerSortFunc(list, connected, connecting).ShouldBeTrue();
        TfClientScoreBoardDialog.TfPlayerSortFunc(list, connecting, connected).ShouldBeFalse();
    }

    [Test]
    public void TfPlayerSortFunc_EqualScoreBothConnected_HigherPlayerIndexSortsFirst()
    {
        VguiSectionedListPanel list = new(null, "list");
        list.AddSection(0, "s");

        int low = list.AddItem(0, Row(playerIndex: 3, score: 7, connected: 2));
        int high = list.AddItem(0, Row(playerIndex: 9, score: 7, connected: 2));

        TfClientScoreBoardDialog.TfPlayerSortFunc(list, high, low).ShouldBeTrue();
        TfClientScoreBoardDialog.TfPlayerSortFunc(list, low, high).ShouldBeFalse();
    }

    [Test]
    public void UpdatePlayerList_TwoTeams_SplitsIntoRedAndBlueLists()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players =
        [
            new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true, TotalScore = 10, Ping = 20, PlayerClass = 1 },
            new(5) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = true, TotalScore = 5, Ping = 40, PlayerClass = 2 },
        ];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 0);

        dialog.PlayerListRed.ItemCount.ShouldBe(1);
        dialog.PlayerListBlue.ItemCount.ShouldBe(1);

        dialog.PlayerListRed.GetItemData(0)!["playerIndex"].ShouldBe("2");
        dialog.PlayerListRed.GetItemData(0)!["score"].ShouldBe("10");
        dialog.PlayerListBlue.GetItemData(0)!["playerIndex"].ShouldBe("5");
    }

    [Test]
    public void UpdatePlayerList_ANeitherConnectedNorValidSlot_IsSkipped()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players =
        [
            new(2) { Connected = false, Valid = false, Team = SceneTeams.Red, TotalScore = 10 },
        ];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 0);

        dialog.PlayerListRed.ItemCount.ShouldBe(0);
        dialog.PlayerListBlue.ItemCount.ShouldBe(0);
    }

    [Test]
    public void UpdatePlayerList_ANameFromTheRoster_FillsTheNameColumn()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players = [new(7) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = true }];
        Dictionary<int, string> names = new() { [7] = "demoman" };

        dialog.UpdatePlayerList(players, names, localPlayerIndex: 0);

        dialog.PlayerListBlue.GetItemData(0)!["name"].ShouldBe("demoman");
    }

    [Test]
    public void UpdatePlayerList_ADeadEnemyRedPlayer_GetsValvesDeadRedColour()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players = [new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = false }];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 99);

        dialog.PlayerListRed.GetItemFgColor(0).ShouldBe(((byte)135, (byte)83, (byte)83, (byte)255));
    }

    [Test]
    public void UpdatePlayerList_TheDeadLocalPlayerOnBlue_GetsValvesBrighterDeadBlueColour()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players = [new(4) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = false }];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 4);

        dialog.PlayerListBlue.GetItemFgColor(0).ShouldBe(((byte)123, (byte)153, (byte)187, (byte)255));
    }

    [Test]
    public void UpdateTeamInfo_ConnectedPlayersOnEachTeam_CountsEachList()
    {
        SceneScoreboardPlayer[] players =
        [
            new(1) { Connected = true, Valid = true, Team = SceneTeams.Red },
            new(2) { Connected = true, Valid = true, Team = SceneTeams.Red },
            new(3) { Connected = true, Valid = true, Team = SceneTeams.Blu },
            new(4) { Connected = false, Valid = false, Team = SceneTeams.Blu },
        ];

        (int blue, int red) = TfClientScoreBoardDialog.UpdateTeamInfo(players);

        blue.ShouldBe(1);
        red.ShouldBe(2);
    }

    private static Dictionary<string, string> Row(int playerIndex, int score, int connected) => new(System.StringComparer.Ordinal)
    {
        ["playerIndex"] = playerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["score"] = score.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["connected"] = connected.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
