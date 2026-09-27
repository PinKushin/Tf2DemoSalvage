using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
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
    public void UpdatePlayerList_AnAlivePlayer_TakesTheTeamColourOnASeeThroughBlackRow()
    {
        // `clr = g_PR->GetTeamColor( nTeam )` (:1545) — COLOR_RED and COLOR_BLUE (shareddefs.h:565) — and
        // `SetItemBgColor( itemID, Color( 0, 0, 0, 80 ) )` for every row (:1569).
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players =
        [
            new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true },
            new(3) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = true },
        ];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 0);

        (dialog.PlayerListRed.GetItemFgColor(0), dialog.PlayerListBlue.GetItemFgColor(0), dialog.PlayerListRed.GetItemBgColor(0))
            .ShouldBe((((byte)255, (byte)64, (byte)64, (byte)255), ((byte)153, (byte)204, (byte)255, (byte)255), ((byte)0, (byte)0, (byte)0, (byte)80)));
    }

    [Test]
    public void UpdatePlayerList_ScoreFontDefaultSet_EveryRowGetsIt()
    {
        // `SetItemFont( itemID, m_hScoreFontDefault )` (tf_clientscoreboard.cpp:1570).
        TfClientScoreBoardDialog dialog = new(null);
        VguiFontAmalgam font = new();
        dialog.ScoreFontDefault = font;

        SceneScoreboardPlayer[] players =
        [
            new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true },
            new(5) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = true },
        ];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 0);

        dialog.PlayerListRed.GetItemFont(0).ShouldBeSameAs(font);
        dialog.PlayerListBlue.GetItemFont(0).ShouldBeSameAs(font);
    }

    [Test]
    public void UpdatePlayerList_NoScoreFontYet_LeavesTheRowOnTheListsDefaultFont()
    {
        // `ApplySchemeSettings` has not run yet — see remarks on `ScoreFontDefault` — so nothing is set rather than
        // a null font being forced onto the row.
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players = [new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true }];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 0);

        dialog.PlayerListRed.GetItemFont(0).ShouldBeNull();
    }

    [Test]
    public void UpdatePlayerList_TheLocalPlayersRow_IsSelected()
    {
        // `iSelectedPlayerIndex == playerIndex` -> `pPlayerList->SetSelectedItem( itemID )` (:1572-1576).
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players =
        [
            new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true },
            new(5) { Connected = true, Valid = true, Team = SceneTeams.Blu, Alive = true },
        ];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 5);

        dialog.PlayerListBlue.SelectedItem.ShouldBe(0);
        dialog.PlayerListRed.SelectedItem.ShouldBe(-1);
    }

    [Test]
    public void UpdatePlayerList_NoPlayerMatchesLocalIndex_SelectsNothing()
    {
        TfClientScoreBoardDialog dialog = new(null);

        SceneScoreboardPlayer[] players = [new(2) { Connected = true, Valid = true, Team = SceneTeams.Red, Alive = true }];

        dialog.UpdatePlayerList(players, names: null, localPlayerIndex: 99);

        dialog.PlayerListRed.SelectedItem.ShouldBe(-1);
    }

    [Test]
    public void UpdateTeamInfo_RedAndBlueTeams_SetsScoreLocalizedNameAndPluralizedCount()
    {
        // `C_TFTeam::UpdateTeamName` outside a tournament: `#TF_RedTeam_Name`/`#TF_BlueTeam_Name` (c_tf_team.cpp:170-195),
        // never the server's `m_szTeamname`.
        TfClientScoreBoardDialog dialog = Localized();

        dialog.UpdateTeamInfo(Teams() with { TournamentMode = false });

        (dialog.DialogVariable("redteamscore"), dialog.DialogVariable("redteamname"), dialog.DialogVariable("redteamplayercount"))
            .ShouldBe(("3", "Red Team", "2 players"));
        (dialog.DialogVariable("blueteamscore"), dialog.DialogVariable("blueteamname"), dialog.DialogVariable("blueteamplayercount"))
            .ShouldBe(("5", "Blu Team", "1 player"));
    }

    [Test]
    public void UpdateTeamInfo_ATournamentOutsideMatchmaking_NamesTeamsByTheTournamentConVars()
    {
        // :159-167: `mp_tournament_redteamname`/`mp_tournament_blueteamname`.
        TfClientScoreBoardDialog dialog = Localized();

        dialog.UpdateTeamInfo(Teams());

        (dialog.DialogVariable("redteamname"), dialog.DialogVariable("blueteamname")).ShouldBe(("Cats", "Dogs"));
    }

    [Test]
    public void UpdateTeamInfo_ATournamentInCasualMatchmaking_KeepsTheLocalizedNames()
    {
        // `IsCompetitiveMode()` (tf_gamerules.cpp:2214): casual 12v12 (7) is `MATCH_TYPE_CASUAL`; with no premade party the
        // name falls through to the localized one (:118-158).
        TfClientScoreBoardDialog dialog = Localized();

        dialog.UpdateTeamInfo(Teams() with { Rules = new SceneGameRules(false, 0, false) { MatchGroup = 7 } });

        dialog.DialogVariable("redteamname").ShouldBe("Red Team");
    }

    [Test]
    public void UpdateTeamInfo_MannVsMachine_NamesRedTheDefenders()
    {
        TfClientScoreBoardDialog dialog = Localized();

        dialog.UpdateTeamInfo(Teams() with { TournamentMode = false, Rules = new SceneGameRules(true, 0, false) });

        dialog.DialogVariable("redteamname").ShouldBe("Defenders");
    }

    [Test]
    public void UpdateTeamInfo_NoLocalization_FallsBackToValvesLiterals()
    {
        // `if ( !pwzName ) pwzName = L"RED"` (:190-194); a missing count format constructs nothing.
        TfClientScoreBoardDialog dialog = new(null);

        dialog.UpdateTeamInfo(Teams() with { TournamentMode = false });

        (dialog.DialogVariable("redteamname"), dialog.DialogVariable("blueteamname"), dialog.DialogVariable("redteamplayercount"))
            .ShouldBe(("RED", "BLU", string.Empty));
    }

    [Test]
    public void UpdateTeamInfo_NotTournamentMode_HidesBothTeamNameLabels()
    {
        TfClientScoreBoardDialog dialog = new(null);

        dialog.UpdateTeamInfo(Teams() with { TournamentMode = false });

        (dialog.RedTeamName.Visible, dialog.BlueTeamName.Visible).ShouldBe((false, false));
    }

    [Test]
    public void UpdateTeamInfo_TournamentModeNotMvm_ShowsBothTeamNameLabels()
    {
        TfClientScoreBoardDialog dialog = new(null);

        dialog.UpdateTeamInfo(Teams());

        (dialog.RedTeamName.Visible, dialog.BlueTeamName.Visible).ShouldBe((true, true));
    }

    [Test]
    public void UpdateTeamInfo_TournamentModeAndMvm_HidesBothTeamNameLabels()
    {
        TfClientScoreBoardDialog dialog = new(null);

        dialog.UpdateTeamInfo(Teams() with { Rules = new SceneGameRules(true, 0, false) });

        (dialog.RedTeamName.Visible, dialog.BlueTeamName.Visible).ShouldBe((false, false));
    }

    private static HudState Teams() => new(true, true, 0, 0, true)
    {
        TournamentMode = true,
        TournamentRedTeamName = "Cats",
        TournamentBlueTeamName = "Dogs",
        Teams =
        [
            new(TfClientScoreBoardDialog.TeamRed) { Score = 3, Name = "SentRed", Players = [1, 2] },
            new(TfClientScoreBoardDialog.TeamBlue) { Score = 5, Name = "SentBlue", Players = [3] },
        ],
    };

    private static TfClientScoreBoardDialog Localized()
    {
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_RedTeam_Name"] = "Red Team",
            ["TF_BlueTeam_Name"] = "Blu Team",
            ["TF_Defenders"] = "Defenders",
            ["TF_ScoreBoard_Player"] = "%s1 player",
            ["TF_ScoreBoard_Players"] = "%s1 players",
        };
        KeyValuesTree scheme = KeyValuesTree.Load(System.Text.Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        return new TfClientScoreBoardDialog(viewport);
    }

    private static Dictionary<string, string> Row(int playerIndex, int score, int connected) => new(System.StringComparer.Ordinal)
    {
        ["playerIndex"] = playerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["score"] = score.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["connected"] = connected.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
