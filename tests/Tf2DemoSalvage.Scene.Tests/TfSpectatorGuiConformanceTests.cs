using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTFSpectatorGUI` (tf_spectatorgui.cpp) and `CTFPlayerPanel` (tf_playerpanel.cpp), B516: which layout a SourceTV demo's
/// spectator GUI loads, when it shows, and what each tournament player card says and where it sits.
/// </summary>
/// <remarks>
/// Every expected value is read from the published client: `InTournamentGUI` (:1060), `GetResFile` (:1037),
/// `RecalculatePlayerPanels` (:1071) with `g_ClassDefinesRemap` (tf_classmenu.cpp:1391), `UpdatePlayerPanels` (:1144), and
/// `CTFPlayerPanel::Update` (tf_playerpanel.cpp:81-298). The `.res` here is hand-written with round numbers so each
/// position is arithmetic: 640x480 is proportional scale 1.
/// </remarks>
public sealed class TfSpectatorGuiConformanceTests
{
    private const int Scout = 1;
    private const int Soldier = 3;
    private const int Medic = 5;

    [TestCase(false, false, -1, false, "Resource/UI/Spectator.res")]
    [TestCase(true, false, -1, false, "Resource/UI/Spectator.res")]
    [TestCase(true, true, -1, false, "Resource/UI/SpectatorTournament.res")]
    [TestCase(true, true, 2, false, "Resource/UI/Spectator.res")]
    [TestCase(true, false, -1, true, "Resource/UI/SpectatorTournament.res")]
    public void ResFile_ByTournamentSpecGuiCompetitiveAndMvm_IsInTournamentGuisChoice(
        bool tournament, bool specGui, int matchGroup, bool mvm, string expected)
    {
        HudState state = State(tournament, specGui) with { Rules = new SceneGameRules(mvm, 0, false) { MatchGroup = matchGroup } };

        TfSpectatorGui.ResFile(state).ShouldBe(expected);
    }

    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    public void IsShown_ByHltvAndMatchSummary_IsTheGameNewmapRule(bool hltv, bool summary, bool expected)
    {
        HudState state = State(true, true) with { IsHltv = hltv, Rules = new SceneGameRules(false, 0, false) { ShowMatchSummary = summary } };

        TfSpectatorGui.IsShown(state).ShouldBe(expected);
    }

    [TestCase(false, true, false, true)]
    [TestCase(false, false, false, false)]
    [TestCase(false, true, true, false)]
    public void IsShown_APovRecorderTheServerOpenedItFor_ShowsUntilTheMatchSummary(bool hltv, bool opened, bool summary, bool expected)
    {
        // `ShowViewPortPanel( "specgui", ... )` (player.cpp:2294, tf_player.cpp:13992) reaches a POV recorder as a VGUIMenu.
        HudState state = State(true, true) with
        {
            IsHltv = hltv,
            SpectatorGuiShown = opened,
            Rules = new SceneGameRules(false, 0, false) { ShowMatchSummary = summary },
        };

        TfSpectatorGui.IsShown(state).ShouldBe(expected);
    }

    [Test]
    public void RecalculatePlayerPanels_AnStvDemoWithNoLocalSlotFound_StillListsBothTeams()
    {
        // The local player on SourceTV is the SourceTV client, team 1 — even where the timeline found no slot for it, which
        // leaves `HudState.Team` 0 and would otherwise fail `iTeam != iLocalTeam && iLocalTeam != TEAM_SPECTATOR`.
        (TfSpectatorGui gui, HudState state) = Built();

        Frame(gui, state with { HasLocalPlayer = false, Team = 0, LocalIndex = 0 });

        gui.PlayerPanels.Count(panel => panel.Visible).ShouldBe(3);
    }

    [Test]
    public void TakeNewPlayerPanels_AfterCardsAreMade_IsTrueOnceThenFalse()
    {
        (TfSpectatorGui gui, HudState state) = Built();

        gui.Update(state);

        gui.TakeNewPlayerPanels().ShouldBeTrue();
        gui.TakeNewPlayerPanels().ShouldBeFalse();
    }

    [Test]
    public void RecalculatePlayerPanels_ForAnStvSpectator_OrdersByClassMenuAndPlacesBluAsTeam1()
    {
        (TfSpectatorGui gui, HudState state) = Built();

        Frame(gui, state);

        // Scout, then soldier, then medic — the class menu's order, not the entity order (medic 2, soldier 3, scout 4).
        gui.PlayerPanels.Select(panel => panel.PlayerIndex).ShouldBe([4, 3, 2]);

        // A spectator's team1 is BLU: base 10 + n * 0 across, 100 + n * 30 down; spec keys 6, 5. RED is team2 at x 500, key 7.
        TfPlayerPanel scout = gui.PlayerPanels[0];
        TfPlayerPanel soldier = gui.PlayerPanels[1];
        TfPlayerPanel medic = gui.PlayerPanels[2];

        (scout.X, scout.Y, scout.SpecIndex).ShouldBe((10, 100, 6));
        (medic.X, medic.Y, medic.SpecIndex).ShouldBe((10, 130, 5));
        (soldier.X, soldier.Y, soldier.SpecIndex).ShouldBe((500, 100, 7));
        gui.PlayerPanels.ShouldAllBe(panel => panel.Visible);
    }

    [Test]
    public void Update_ADeadPlayerAndAChargedMedic_ShowRespawnSecondsChargeAndPortraits()
    {
        (TfSpectatorGui gui, HudState state) = Built();

        Frame(gui, state);

        TfPlayerPanel soldier = gui.PlayerPanels.Single(panel => panel.PlayerIndex == 3);
        TfPlayerPanel medic = gui.PlayerPanels.Single(panel => panel.PlayerIndex == 2);

        // Dead: respawn at server time 20, now 12.5 — `(int)( 20 - 12.5 )` is 7. **The portrait is NOT greyed**: `Reset` sets
        // `m_bPrevAlive = false` and only a CHANGE of alive sets the draw colour (:168), so a card first filled for a player
        // who is already dead keeps the image's own white until he lives and dies again.
        soldier.DialogVariable("respawntime").ShouldBe("7 s");
        soldier.ClassImage!.ImageName.ShouldBe("class_portraits/soldier_alpha");
        soldier.ClassImage.DrawColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
        soldier.DialogVariable("playername").ShouldBe("Sol");

        medic.DialogVariable("chargeamount").ShouldBe("45%");
        medic.DialogVariable("respawntime").ShouldBe(string.Empty);
        medic.ClassImage!.ImageName.ShouldBe("class_portraits/medic_blue_alpha");
        medic.ClassImage.DrawColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Test]
    public void Update_WithTheSpecGuiConVarOff_MakesNoPlayerPanels()
    {
        (TfSpectatorGui gui, HudState state) = Built();

        gui.Update(State(true, false) with { IsHltv = true, ScoreboardPlayers = state.ScoreboardPlayers, CurTime = 1f });

        gui.PlayerPanels.ShouldBeEmpty();
    }

    // `GetMapDisplayName` (tf_gamerules.cpp:18974): a shipped map's own name (:264, :300); else `_final` cut at the end
    // (:19007), `workshop/` and `.ugc<digits>` dropped (:19022), the mode prefix stripped (:19048), `_` to space, upper-cased
    // ASCII only (`Q_strupr`). **The `workshop/` cases are the function's, not the spectator GUI's:** its caller passes
    // `Q_FileBase( GetLevelName() )`, which has already dropped the folder — see
    // `Update_AWorkshopLevel_KeepsItsUgcSuffixAsTheEngineDoes` for what that caller really shows.
    [TestCase("cp_badlands", "Badlands")]
    [TestCase("CP_Badlands", "Badlands")]
    [TestCase("pl_enclosure_final", "Enclosure")]
    [TestCase("cp_process_final", "PROCESS")]
    [TestCase("cp_process_f12", "PROCESS F12")]
    [TestCase("koth_product_final1", "PRODUCT")]
    [TestCase("cp_final_stand", "FINAL STAND")]
    [TestCase("cp_x_final2", "X FINAL2")]
    [TestCase("workshop/cp_example.ugc12345", "EXAMPLE")]
    [TestCase("workshop/cp_example.ugc12a45", "EXAMPLE.UGC12A45")]
    [TestCase("workshop/koth_example.ugc1", "KOTH EXAMPLE", Description = "koth_ is tested on the name before workshop/ is dropped")]
    [TestCase("ctf_turbine", "TURBINE")]
    [TestCase("mvm_decoy", "DECOY")]
    [TestCase("ultiduo_baloo", "ULTIDUO BALOO")]
    [TestCase("CP_CAFé_final", "CAFé", Description = "Q_strlower/Q_strupr touch ASCII letters only")]
    public void MapDisplayName_ALevel_IsGetMapDisplayNamesAnswer(string level, string expected) =>
        TfMapDisplayName.Of(level).ShouldBe(expected);

    [Test]
    public void Update_OnAnStvDemo_SetsTheMapLabelFromTheLevelNotTheResPlaceholder()
    {
        // `UpdateKeyLabels` (tf_spectatorgui.cpp:685-709): `#Spec_Map` over `GetMapDisplayName( Q_FileBase( GetLevelName() ) )`.
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.MapLabel.Text.ShouldBe("map: cp_bridge", "the control: the .res placeholder before any update");

        gui.Update(state);

        gui.MapLabel.Text.ShouldBe("Map: PROCESS F12");
    }

    [Test]
    public void Update_OnAnStvDemo_SetsTheHltvTipAndTheAutoDirectorHintWithItsKey()
    {
        // `#Tip_HLTV` into `%tip%` (:472-479); `#TF_Spectator_AutoDirector` with `%strafe%` bound (:506-540).
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.Update(state);

        ((VguiLabel)gui.FindChildByName("TipLabel")!).Text.ShouldBe("You are watching SourceTV.");
        gui.ClassOrTeamLabel.Text.ShouldBe("Press [ ALT ] for Auto Director");
    }

    [Test]
    public void Update_AWorkshopLevel_KeepsItsUgcSuffixAsTheEngineDoes()
    {
        // `Q_FileBase( "maps/workshop/cp_example.ugc12345.bsp" )` is `cp_example.ugc12345` (:692): the folder is gone before
        // `GetMapDisplayName` sees it, so its workshop branch never fires from here and the `.ugc` digits stay.
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.Update(state with { LevelBaseName = "cp_example.ugc12345" });

        gui.MapLabel.Text.ShouldBe("Map: EXAMPLE.UGC12345");
    }

    [Test]
    public void Update_ANewLevelOrNewBindings_RebuildTheCachedTexts()
    {
        // The engine rebuilds each string every frame; this port keeps them until an input changes, so a change must reach
        // the label — the map on a new level, the keys on a new binding table (a config reload replaces the table).
        (TfSpectatorGui gui, HudState state) = BuiltPlain();
        Dictionary<string, string> rebound = new() { ["attack"] = "mouse3", ["strafe"] = "shift" };

        gui.Update(state);
        gui.Update(state with { LevelBaseName = "cp_badlands", KeyLookupBinding = rebound.GetValueOrDefault });

        gui.MapLabel.Text.ShouldBe("Map: Badlands");
        gui.CycleTargetFwdKeyLabel.Text.ShouldBe("[MOUSE3]");
        gui.ClassOrTeamLabel.Text.ShouldBe("Press [ SHIFT ] for Auto Director");
    }

    [Test]
    public void Update_TheTip_IsRefreshedEveryTenSecondsAndAfterASeekBack()
    {
        // `m_flNextTipChangeTime = curtime + 10.0f` (:490); a seek back is caught as the player panels' timer is (:333).
        (TfSpectatorGui gui, HudState state, Dictionary<string, string> strings) = BuiltPlainWithStrings();
        VguiLabel tip = (VguiLabel)gui.FindChildByName("TipLabel")!;

        gui.Update(state with { CurTime = 12.5f });
        strings["Tip_HLTV"] = "second";

        gui.Update(state with { CurTime = 22.4f });
        tip.Text.ShouldBe("You are watching SourceTV.", "within ten seconds the tip stands");

        gui.Update(state with { CurTime = 22.5f });
        tip.Text.ShouldBe("second", "at ten seconds it is read again");

        strings["Tip_HLTV"] = "third";
        gui.Update(state with { CurTime = 1f });
        tip.Text.ShouldBe("third", "a seek back does not wait for the old clock");
    }

    [Test]
    public void Update_OnAnStvDemo_BindsTheCameraKeyLabels()
    {
        // `#TF_Spectator_SwitchCamModeKey` "[%jump%]" and the two cycle keys (:589, :626, :670), bound through
        // `UTIL_ReplaceKeyBindings` — the `.res` placeholders `#TF_Spectator_ClassOrTeamKey` replaced. The switch label is
        // null from the constructor (:97) and found from the `.res` in `ApplySchemeSettings` (:217), so it is driven.
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.Update(state);

        gui.SwitchCamModeKeyLabel!.Text.ShouldBe("[SPACE]");
        gui.CycleTargetFwdKeyLabel.Text.ShouldBe("[MOUSE1]");
        gui.CycleTargetRevKeyLabel.Text.ShouldBe("[MOUSE2]");
    }

    [Test]
    public void Update_APovSpectatorTeamRecorder_AsksToChangeTeam()
    {
        // `GetTeamNumber() == TEAM_SPECTATOR` outside arena (:510): `#TF_Spectator_ChangeTeam`, `%changeteam%` bound.
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.Update(state with { IsHltv = false, SpectatorGuiShown = true, HasLocalPlayer = true, Team = 1 });

        gui.ClassOrTeamLabel.Text.ShouldBe("Press [ COMMA ] to Change Team");
    }

    [Test]
    public void Update_ADeadPlayerUnderForceCameraNone_HidesTheCycleKeysAndTheirCaptions()
    {
        // `bSuppressCycle` (:595): a player on a team under `mp_forcecamera 2` (`OBS_ALLOW_NONE`).
        (TfSpectatorGui gui, HudState state) = BuiltPlain();
        Dictionary<string, string> server = new() { ["mp_forcecamera"] = "2" };

        gui.Update(state with
        {
            IsHltv = false,
            SpectatorGuiShown = true,
            HasLocalPlayer = true,
            Team = SceneTeams.Red,
            ConVars = state.ConVars with { Find = server.GetValueOrDefault },
        });

        gui.CycleTargetFwdKeyLabel.Visible.ShouldBeFalse();
        gui.FindChildByName("CycleTargetFwdLabel")!.Visible.ShouldBeFalse();
        gui.SwitchCamModeKeyLabel!.Visible.ShouldBeFalse();
        gui.ClassOrTeamLabel.Text.ShouldBe("Press [ PERIOD ] to Change Class");
    }

    [Test]
    public void Update_APlayerUnderForceCameraTeam_HidesOnlyTheSwitchKey()
    {
        // `mp_forcecamera 1` (`OBS_ALLOW_TEAM`, the default): the switch key hides (:562), the cycle keys do not (:595).
        (TfSpectatorGui gui, HudState state) = BuiltPlain();

        gui.Update(state with { IsHltv = false, SpectatorGuiShown = true, HasLocalPlayer = true, Team = SceneTeams.Red });

        gui.SwitchCamModeKeyLabel!.Visible.ShouldBeFalse();
        gui.FindChildByName("SwitchCamModeLabel")!.Visible.ShouldBeFalse();
        gui.CycleTargetFwdKeyLabel.Visible.ShouldBeTrue();
        gui.CycleTargetFwdKeyLabel.Text.ShouldBe("[MOUSE1]");
    }

    [Test]
    public void Update_APlayerUnderFadeToBlack_HidesTheCycleKeysToo()
    {
        // `mp_fadetoblack` alone suppresses both, under `mp_forcecamera 0` (`OBS_ALLOW_ALL`) (:562, :595).
        (TfSpectatorGui gui, HudState state) = BuiltPlain();
        Dictionary<string, string> server = new() { ["mp_forcecamera"] = "0", ["mp_fadetoblack"] = "1" };
        HudState playing = state with
        {
            IsHltv = false,
            SpectatorGuiShown = true,
            HasLocalPlayer = true,
            Team = SceneTeams.Blu,
            ConVars = state.ConVars with { Find = server.GetValueOrDefault },
        };

        gui.Update(playing);

        gui.CycleTargetRevKeyLabel.Visible.ShouldBeFalse();
        gui.FindChildByName("CycleTargetRevLabel")!.Visible.ShouldBeFalse();
        gui.SwitchCamModeKeyLabel!.Visible.ShouldBeFalse();

        server["mp_fadetoblack"] = "0";
        gui.Update(playing);

        gui.CycleTargetRevKeyLabel.Visible.ShouldBeTrue("the control: with neither set, all show");
        gui.SwitchCamModeKeyLabel.Visible.ShouldBeTrue();
    }

    [Test]
    public void Update_UnderHudMinmode_LeavesTheMapLabelAsLoaded()
    {
        // `cl_hud_minmode` skips the key labels and the map (:558) — the `.res`'s `visible_minmode 0` hides it instead.
        (TfSpectatorGui gui, HudState state) = BuiltPlain();
        Dictionary<string, string> client = new() { ["cl_hud_minmode"] = "1" };

        gui.Update(state with { ConVars = state.ConVars with { Client = client.GetValueOrDefault } });

        gui.MapLabel.Text.ShouldBe("map: cp_bridge");
        gui.ClassOrTeamLabel.Text.ShouldBe("Press [ ALT ] for Auto Director", "the hint is above the minmode test");
    }

    /// <summary>
    /// The viewport's frame as the game runs it: the panel's `Update` (which makes the cards), a layout pass (which applies
    /// their scheme, so `classimage` is found and `Reset` runs), then the next update (:1144) filling them in.
    /// </summary>
    private static void Frame(TfSpectatorGui gui, HudState state)
    {
        HudViewport viewport = (HudViewport)gui.Parent!;

        gui.Update(state);
        VguiLayout.SolveTraverse(viewport, viewport.Context!);
        gui.UpdatePlayerPanels(state);
    }

    private static HudState State(bool tournament, bool specGui)
    {
        Dictionary<string, string> server = new() { ["mp_tournament"] = tournament ? "1" : "0" };
        Dictionary<string, string> client = new() { ["cl_use_tournament_specgui"] = specGui ? "1" : "0" };

        return new HudState(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 0, Alive: false)
        {
            ConVars = new HudConVars(server.GetValueOrDefault, client.GetValueOrDefault),
        };
    }

    /// <summary>
    /// The stock `Spectator.res`'s labels as shipped — their placeholder texts kept — over a SourceTV recording of
    /// cp_process_f12, with the strings `valve_english.txt` and `tf_english.txt` give and four keys bound.
    /// </summary>
    private static (TfSpectatorGui Gui, HudState State) BuiltPlain()
    {
        (TfSpectatorGui gui, HudState state, _) = BuiltPlainWithStrings();

        return (gui, state);
    }

    /// <summary><see cref="BuiltPlain"/>, with the localisation table to change under it.</summary>
    private static (TfSpectatorGui Gui, HudState State, Dictionary<string, string> Strings) BuiltPlainWithStrings()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["Resource/UI/Spectator.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/Spectator.res"
                {
                    "specgui" { }
                    "topbar" { "tall" "84" }
                    "MapLabel" { "ControlName" "CExLabel" "fieldName" "MapLabel" "labelText" "map: cp_bridge" }
                    "ClassOrTeamLabel" { "ControlName" "CExLabel" "fieldName" "ClassOrTeamLabel" "labelText" "#TF_Spectator_ChangeTeam" }
                    "SwitchCamModeKeyLabel" { "ControlName" "CExLabel" "fieldName" "SwitchCamModeKeyLabel" "labelText" "#TF_Spectator_ChangeTeam" }
                    "SwitchCamModeLabel" { "ControlName" "CExLabel" "fieldName" "SwitchCamModeLabel" "labelText" "#TF_Spectator_SwitchCamMode" }
                    "CycleTargetFwdKeyLabel" { "ControlName" "CExLabel" "fieldName" "CycleTargetFwdKeyLabel" "labelText" "#TF_Spectator_ClassOrTeamKey" }
                    "CycleTargetFwdLabel" { "ControlName" "CExLabel" "fieldName" "CycleTargetFwdLabel" "labelText" "#TF_Spectator_CycleTargetFwd" }
                    "CycleTargetRevKeyLabel" { "ControlName" "CExLabel" "fieldName" "CycleTargetRevKeyLabel" "labelText" "#TF_Spectator_ClassOrTeamKey" }
                    "CycleTargetRevLabel" { "ControlName" "CExLabel" "fieldName" "CycleTargetRevLabel" "labelText" "#TF_Spectator_CycleTargetRev" }
                    "TipLabel" { "ControlName" "CExLabel" "fieldName" "TipLabel" "labelText" "%tip%" }
                }
                """),
        };
        Dictionary<string, string> strings = new()
        {
            ["Spec_Map"] = "Map: %s1",
            ["Tip_HLTV"] = "You are watching SourceTV.",
            ["TF_Spectator_ChangeTeam"] = "Press [ %changeteam% ] to Change Team",
            ["TF_Spectator_ChangeClass"] = "Press [ %changeclass% ] to Change Class",
            ["TF_Spectator_AutoDirector"] = "Press [ %strafe% ] for Auto Director",
            ["TF_Spectator_SwitchCamModeKey"] = "[%jump%]",
            ["TF_Spectator_SwitchCamMode"] = "Switch Camera Mode",
            ["TF_Spectator_CycleTargetFwdKey"] = "[%attack%]",
            ["TF_Spectator_CycleTargetFwd"] = "Cycle Targets (fwd)",
            ["TF_Spectator_CycleTargetRevKey"] = "[%attack2%]",
            ["TF_Spectator_CycleTargetRev"] = "Cycle Targets (rev)",
        };
        Dictionary<string, string> keys = new()
        {
            ["jump"] = "space", ["attack"] = "mouse1", ["attack2"] = "mouse2", ["changeteam"] = "comma", ["changeclass"] = "period",
            ["strafe"] = "alt",
        };
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudState state = State(true, false) with
        {
            IsHltv = true,
            HasLocalPlayer = false,
            CurTime = 12.5f,
            LevelBaseName = "cp_process_f12",
            KeyLookupBinding = keys.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        viewport.SetState(state);

        TfSpectatorGui gui = new(viewport);

        gui.PerformApplySchemeSettings(context);
        return (gui, state, strings);
    }

    private static (TfSpectatorGui Gui, HudState State) Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["Resource/UI/SpectatorTournament.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/SpectatorTournament.res"
                {
                    "specgui"
                    {
                        "team1_player_base_x" "10"
                        "team1_player_base_y" "100"
                        "team1_player_delta_x" "0"
                        "team1_player_delta_y" "30"
                        "team2_player_base_x" "500"
                        "team2_player_base_y" "100"
                        "team2_player_delta_x" "0"
                        "team2_player_delta_y" "30"
                        "playerpanels_kv"
                        {
                            "wide" "100"
                            "tall" "28"
                            "classimage" { "ControlName" "CTFClassImage" "fieldName" "classimage" "wide" "20" "tall" "20" }
                        }
                    }
                    "topbar" { "tall" "50" }
                }
                """),
        };
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
        };
        HudState state = State(true, true) with
        {
            IsHltv = true,
            Team = 1,
            LocalIndex = 1,
            CurTime = 12.5f,
            ServerTime = 12.5f,
            Names = new Dictionary<int, string> { [2] = "Med", [3] = "Sol", [4] = "Sco" },
            ScoreboardPlayers =
            [
                new SceneScoreboardPlayer(1) { Connected = true, Team = 1, PlayerClass = 0 },
                new SceneScoreboardPlayer(2) { Connected = true, Team = SceneTeams.Blu, PlayerClass = Medic, Alive = true, Health = 150, MaxHealth = 150, ChargeLevel = 45 },
                new SceneScoreboardPlayer(3) { Connected = true, Team = SceneTeams.Red, PlayerClass = Soldier, Alive = false, NextRespawnTime = 20f },
                new SceneScoreboardPlayer(4) { Connected = true, Team = SceneTeams.Blu, PlayerClass = Scout, Alive = true, Health = 125, MaxHealth = 125 },
            ],
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        viewport.SetState(state);

        TfSpectatorGui gui = new(viewport);

        gui.PerformApplySchemeSettings(context);
        return (gui, state);
    }
}
