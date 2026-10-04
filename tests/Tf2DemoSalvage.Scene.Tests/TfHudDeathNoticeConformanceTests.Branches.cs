using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Every branch of `CTFHudDeathNotice::OnGameEvent` and `CHudBaseDeathNotice::FireGameEvent`/`Paint`, each pinned to the
/// value the engine computes (src/game/client/tf/tf_hud_deathnotice.cpp, hud_basedeathnotice.cpp, cited per test).
/// </summary>
public sealed partial class TfHudDeathNoticeConformanceTests
{
    private const string Font = """ "Default" { "1" { "name" "Arial" "tall" "12" } } """;

    /// <summary>The localised strings the branches below look up, each distinct so a swapped key shows.</summary>
    private static readonly Dictionary<string, string> MoreStrings = new()
    {
        ["Msg_Captured"] = "captured one",
        ["Msg_Defended"] = "defended",
        ["SpecialScore_Count"] = "score %s1",
        ["TeamLeader_Kill"] = "leader",
        ["Msg_PickedUpFlag"] = "picked up",
        ["Msg_PickedUpFlagHalloween2014"] = "picked up 2014",
        ["Msg_CapturedFlag"] = "capflag",
        ["Msg_CapturedFlagHalloween2014"] = "capflag 2014",
        ["Msg_DefendedFlag"] = "defflag",
        ["Msg_DefendedFlagHalloween2014"] = "defflag 2014",
        ["Msg_DefendedBomb"] = "defbomb",
        ["TF_Object_Dispenser"] = "Dispenser",
        ["TF_Object_Sentry"] = "Sentry",
        ["TF_object_Sapper"] = "Sapper",
        ["Humiliation_Kill"] = "hum",
        ["Humiliation_Kill_Arm"] = "arm",
        ["Humiliation_Kill_Slap"] = "slap",
        ["Humiliation_Count"] = "hum %s1",
        ["Msg_PasstimeBallGet"] = "got",
        ["Msg_PasstimeSteal"] = "stole",
        ["Msg_PasstimeScore"] = "scored",
        ["Msg_PasstimeScoreCount"] = "scored %s1",
        ["Msg_PasstimePassComplete"] = "passed",
        ["Msg_PasstimeInterception"] = "intercepted",
        ["Msg_PasstimeBlock"] = "blocked",
        ["TF_Balloonicorn"] = "Balloonicorn",
        ["Msg_Dominating_What"] = "dom?",
        ["Msg_Revenge"] = "REVENGE",
        ["Msg_Revenge_What"] = "rev?",
        ["DeathMsg_Suicide"] = "suicide",
        ["DeathMsg_AssistedSuicide"] = "assisted",
        ["DeathMsg_AssistedSuicide_Multiple"] = "assisted multi",
        ["TF_HALLOWEEN_EYEBALL_BOSS_DEATHCAM_NAME"] = "MONOCULUS",
        ["TF_HALLOWEEN_MERASMUS_DEATHCAM_NAME"] = "MERASMUS",
        ["TF_HALLOWEEN_SKELETON_DEATHCAM_NAME"] = "Skeleton",
        ["koth_slime_salmann"] = "Salmann",
        ["koth_krampus_boss"] = "Krampus",
        ["Kill_Streak"] = "ks%s1",
        ["Duck_Streak"] = "ds%s1",
        ["cp_a"] = "Alpha",
    };

    [Test]
    public void ListensFor_TheBaseAndTfInits_AreTheSeventeenEvents() =>
        TfHudDeathNotice.ListensFor.OrderBy(name => name, System.StringComparer.Ordinal).ShouldBe(
        [
            // hud_basedeathnotice.cpp:66–73, tf_hud_deathnotice.cpp:651–662.
            "duck_xp_level_up", "fish_notice", "fish_notice__arm", "object_destroyed", "pass_ball_blocked", "pass_ball_stolen",
            "pass_get", "pass_pass_caught", "pass_score", "player_death", "rd_robot_killed", "slap_notice", "special_score",
            "team_leader_killed", "teamplay_capture_blocked", "teamplay_flag_event", "teamplay_point_captured",
        ]);

    [Test]
    public void ApplySchemeSettings_NoResFile_TakesThePanelVariableDefaults()
    {
        // hud_basedeathnotice.h CPanelAnimationVars, tf_hud_deathnotice.cpp:634–638; proportional at 480 tall is unscaled.
        TfHudDeathNotice feed = Feed(Font);

        (feed.GetFloat("LineHeight"), feed.GetFloat("LineSpacing"), feed.GetFloat("CornerRadius"), feed.GetFloat("MaxDeathNotices"))
            .ShouldBe((16f, 4f, 3f, 4f));
        feed.GetBool("RightJustify").ShouldBeTrue();
        feed.GetFont("TextFont").ShouldNotBeNull("the default font name is \"Default\"");
        new[]
        {
            feed.GetColor("IconColor"), feed.GetColor("BaseBackgroundColor"), feed.GetColor("LocalBackgroundColor"),
            feed.GetColor("KillStreakBackgroundColor"), feed.GetColor("TeamBlue"), feed.GetColor("TeamRed"), feed.GetColor("PurpleText"),
            feed.GetColor("GreenText"), feed.GetColor("LocalPlayerColor"),
        }.ShouldBe(
        [
            ((byte)255, (byte)80, (byte)0, (byte)255), ((byte)46, (byte)43, (byte)42, (byte)220), ((byte)245, (byte)229, (byte)196, (byte)200),
            ((byte)224, (byte)223, (byte)219, (byte)200), ((byte)153, (byte)204, (byte)255, (byte)255), ((byte)255, (byte)64, (byte)64, (byte)255),
            ((byte)134, (byte)80, (byte)172, (byte)255), ((byte)112, (byte)176, (byte)74, (byte)255), ((byte)65, (byte)65, (byte)65, (byte)255),
        ]);
    }

    [Test]
    public void Clear_AfterAKill_EmptiesTheFeedAndStillDraws()
    {
        TfHudDeathNotice feed = Fed(Kill(12, 14));

        feed.Clear();

        (feed.Notices.Count, feed.ShouldDraw(default), feed.HiddenBits).ShouldBe((0, true, 0));
    }

    [Test]
    public void HandleGameEvent_DuckLevelUp_GoesToTheBannerAlone()
    {
        // tf_hud_deathnotice.cpp:761–766: AddStreakMsg and return, before the base adds a line.
        TfHudDeathNotice feed = Fed(Fire("duck_xp_level_up", new() { ["level"] = 3 }));

        feed.Notices.ShouldBeEmpty();
        feed.Streak!.CurrentStreakType.ShouldBe(TfStreakType.DuckLevelUp);
    }

    [Test]
    public void HandleGameEvent_NoticeTimeZero_AddsNothing()
    {
        // hud_basedeathnotice.cpp:406: `hud_deathnotice_time.GetFloat() == 0` returns.
        TfHudDeathNotice feed = Feed();

        Think(feed, "hud_deathnotice_time", "0");
        feed.HandleGameEvent(Kill(12, 14));

        feed.Notices.ShouldBeEmpty();
    }

    [Test]
    public void RetireExpiredDeathNotices_NoticeTimeTwo_KeepsTheRecordersLineTwiceAsLong()
    {
        // DeathNoticeItem::GetExpiryTime: created at 10, 2 s for others, 4 s with the local player involved.
        TfHudDeathNotice feed = Feed();

        Think(feed, "hud_deathnotice_time", "2");
        feed.HandleGameEvent(Kill(12, 14));
        feed.HandleGameEvent(Kill(12, 11));

        feed.RetireExpiredDeathNotices(12f);
        feed.Notices.Count.ShouldBe(2, "not yet past 12");
        feed.RetireExpiredDeathNotices(12.5f);
        feed.Notices.ShouldHaveSingleItem().VictimName.ShouldBe("Recorder");
        feed.RetireExpiredDeathNotices(14.5f);
        feed.Notices.ShouldBeEmpty();
    }

    [Test]
    public void HandleGameEvent_StreakDisplayTimeZero_ShowsNoBanner()
    {
        // tf_hud_deathnotice.cpp:1555: `cl_hud_killstreak_display_time.GetInt() <= 0` returns.
        TfHudDeathNotice feed = Feed();

        Think(feed, "cl_hud_killstreak_display_time", "0");
        feed.HandleGameEvent(Kill(12, 14, more: new() { ["kill_streak_total"] = 5 }));

        feed.Streak!.CurrentStreakCount.ShouldBe(0);
    }

    [TestCase(14, true, TestName = "ShouldShowDeathNotice_ASilentKillOfATeammate_IsHidden")]
    [TestCase(11, false, TestName = "ShouldShowDeathNotice_ASilentKillOfTheRecorder_IsShown")]
    [TestCase(13, false, TestName = "ShouldShowDeathNotice_ASilentKillOfAnEnemy_IsShown")]
    public void ShouldShowDeathNotice_SilentKill(int victim, bool hidden) =>
        // tf_hud_deathnotice.cpp:695–703.
        Fed(Kill(12, victim, more: new() { ["silent_kill"] = true })).Notices.Count.ShouldBe(hidden ? 0 : 1);

    [TestCase(12, 13, -1, 0, true, TestName = "ShouldShowDeathNotice_AnInvaderKilledByAnotherInMannVsMachine_IsHidden")]
    [TestCase(11, 13, -1, 0, false, TestName = "ShouldShowDeathNotice_AnInvaderKilledByTheRecorder_IsShown")]
    [TestCase(12, 13, 11, 0, false, TestName = "ShouldShowDeathNotice_AnInvaderKillAssistedByTheRecorder_IsShown")]
    [TestCase(12, 13, -1, 0x0200, false, TestName = "ShouldShowDeathNotice_AMinibossInvader_IsShown")]
    [TestCase(12, 14, -1, 0, false, TestName = "ShouldShowDeathNotice_ADefenderInMannVsMachine_IsShown")]
    public void ShouldShowDeathNotice_MannVsMachine(int attacker, int victim, int assister, int flags, bool hidden) =>
        // tf_hud_deathnotice.cpp:705–717: TF_TEAM_PVE_INVADERS is BLU.
        Fed(Kill(attacker, victim, more: new() { ["assister"] = assister, ["death_flags"] = flags }, rules: new SceneGameRules(true, 0, false)))
            .Notices.Count(notice => notice.VictimId == victim).ShouldBe(hidden ? 0 : 1);

    [Test]
    public void HandleGameEvent_AFeignedDeathOfTheRecorderWithNoEntity_IsNotShown()
    {
        // hud_basedeathnotice.cpp:438: `iLocalPlayerIndex == victim` returns even when the team test could not run.
        ScenePlayer[] players = Players().Where(player => player.EntityIndex != 1).ToArray();

        Fed(Kill(12, 11, more: new() { ["death_flags"] = 0x0020 }, players: players)).Notices.ShouldBeEmpty();
    }

    [Test]
    public void HandleGameEvent_TwoSpecialScores_StackIntoOneCountedLine()
    {
        // hud_basedeathnotice.cpp:735–753 and UseExistingNotice:102.
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Fire("special_score", new() { ["player"] = 1 }));
        feed.HandleGameEvent(Fire("special_score", new() { ["player"] = 1 }));

        DeathNoticeItem notice = feed.Notices.ShouldHaveSingleItem();
        (notice.KillerName, notice.KillerTeam, notice.LocalPlayerInvolved, notice.KillerId).ShouldBe(("Recorder", 2, true, 1));
        (notice.SpecialScore, notice.Crit, notice.Count, notice.InfoText).ShouldBe((true, false, 2, "score 2"));
        notice.IconDeath.ShouldBeNull("a special score skips the icon lookup (hud_basedeathnotice.cpp:793)");
    }

    [Test]
    public void HandleGameEvent_SpecialScoresByTwoPlayers_AreTwoLines()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Fire("special_score", new() { ["player"] = 2 }));
        feed.HandleGameEvent(Fire("special_score", new() { ["player"] = 3 }));

        feed.Notices.Select(notice => (notice.KillerName, notice.KillerTeam, notice.LocalPlayerInvolved)).ShouldBe([("Scout", 3, false), ("Medic", 3, false)]);
    }

    [Test]
    public void HandleGameEvent_ASpecialScoreByNoOne_IsUnnamedAndTeamless()
    {
        DeathNoticeItem notice = Fed(Fire("special_score", new() { ["player"] = 0 })).Notices[0];

        (notice.KillerName, notice.KillerTeam, notice.KillerId).ShouldBe((string.Empty, 0, 0));
    }

    [Test]
    public void HandleGameEvent_TeamLeaderKilled_NamesBothAndSkipsTheIcon()
    {
        // hud_basedeathnotice.cpp:755–789.
        DeathNoticeItem notice = Fed(Fire("team_leader_killed", new() { ["killer"] = 2, ["victim"] = 1 })).Notices[0];

        (notice.KillerName, notice.KillerTeam, notice.VictimName, notice.VictimTeam).ShouldBe(("Scout", 3, "Recorder", 2));
        (notice.LocalPlayerInvolved, notice.KillerId, notice.VictimId, notice.InfoText).ShouldBe((true, 2, 1, "leader"));
        (notice.Crit, notice.IconDeath).ShouldBe((false, null));
    }

    [Test]
    public void HandleGameEvent_TeamLeaderKilledByNoOne_IsUnnamedAndTeamless()
    {
        DeathNoticeItem notice = Fed(Fire("team_leader_killed", new() { ["killer"] = 0, ["victim"] = 0 })).Notices[0];

        (notice.KillerName, notice.KillerTeam, notice.VictimName, notice.VictimTeam, notice.LocalPlayerInvolved)
            .ShouldBe((string.Empty, 0, string.Empty, 0, false));
    }

    [Test]
    public void HandleGameEvent_TeamLeaderKilledByTheRecorder_InvolvesTheRecorder() =>
        Fed(Fire("team_leader_killed", new() { ["killer"] = 1, ["victim"] = 2 })).Notices[0].LocalPlayerInvolved.ShouldBeTrue();

    [TestCase(3, "d_bluedefend", false)]
    [TestCase(1, "d_reddefend", true)]
    public void HandleGameEvent_ACaptureBlock_NamesTheBlockerAndTheDefendIcon(int blocker, string icon, bool local)
    {
        // hud_basedeathnotice.cpp:633–646; tf_hud_deathnotice.cpp:1254–1272.
        DeathNoticeItem notice = Fed(Fire("teamplay_capture_blocked", new() { ["cpname"] = "#cp_a", ["blocker"] = blocker })).Notices[0];

        (notice.VictimName, notice.InfoText, notice.Icon, notice.LocalPlayerInvolved).ShouldBe(("Alpha", "defended", icon, local));
        notice.KillerTeam.ShouldBe(blocker == 1 ? 2 : 3);
    }

    [Test]
    public void HandleGameEvent_ACaptureByTheRecorderAlone_IsTheSingleTextAndInvolvesThem()
    {
        DeathNoticeItem notice = Fed(Fire("teamplay_point_captured", new() { ["cappers"] = "\u0001" })).Notices[0];

        // GetLocalizedControlPointName's default (hud_basedeathnotice.cpp:818).
        (notice.VictimName, notice.KillerName, notice.KillerTeam, notice.VictimTeam).ShouldBe(("Unnamed Control Point", "Recorder", 2, 0));
        (notice.InfoText, notice.LocalPlayerInvolved, notice.Icon).ShouldBe(("captured one", true, "d_redcapture"));
    }

    [Test]
    public void HandleGameEvent_ACaptureByAnUnassignedSlot_TakesNoTeamIcon()
    {
        // tf_hud_deathnotice.cpp:1264: a team outside the two game teams returns before the icon.
        DeathNoticeItem notice = Fed(Fire("teamplay_point_captured", new() { ["cappers"] = "\u0005" })).Notices[0];

        (notice.Icon, notice.IconDeath!.ShortName).ShouldBe((string.Empty, "d_skull_tf"));
    }

    [TestCase(1, 0, false, "picked up", "d_bluecapture")]
    [TestCase(1, 5, false, "picked up 2014", "d_bluecapture")]
    [TestCase(2, 0, false, "capflag", "d_bluecapture")]
    [TestCase(2, 5, false, "capflag 2014", "d_bluecapture")]
    [TestCase(3, 0, false, "defflag", "d_bluedefend")]
    [TestCase(3, 5, false, "defflag 2014", "d_bluedefend")]
    [TestCase(3, 0, true, "defbomb", "d_bluedefend")]
    public void HandleGameEvent_AFlagEvent_TakesItsKeyAndIcon(int type, int scenario, bool mannVsMachine, string text, string icon)
    {
        // hud_basedeathnotice.cpp:651–717 (HALLOWEEN_SCENARIO_DOOMSDAY is 5); tf_hud_deathnotice.cpp:1256–1272.
        DeathNoticeItem notice = Fed(Fire("teamplay_flag_event", new() { ["eventtype"] = type, ["player"] = 2 },
            rules: new SceneGameRules(mannVsMachine, scenario, false))).Notices.ShouldHaveSingleItem();

        (notice.InfoText, notice.Icon, notice.KillerName, notice.KillerTeam, notice.LocalPlayerInvolved).ShouldBe((text, icon, "Scout", 3, false));
    }

    [Test]
    public void HandleGameEvent_AFlagEventByTheRecorder_InvolvesThem() =>
        Fed(Fire("teamplay_flag_event", new() { ["eventtype"] = 1, ["player"] = 1 })).Notices[0].LocalPlayerInvolved.ShouldBeTrue();

    [TestCase(4, false, TestName = "HandleGameEvent_ADroppedFlag_IsNotShown")]
    [TestCase(1, true, TestName = "HandleGameEvent_AFlagEventInPlayerDestruction_IsNotShown")]
    public void HandleGameEvent_UnsupportedFlagEvent(int type, bool playerDestruction) =>
        // hud_basedeathnotice.cpp:654 and the switch's default.
        Fed(Fire("teamplay_flag_event", new() { ["eventtype"] = type, ["player"] = 2 }, rules: new SceneGameRules(false, 0, playerDestruction)))
            .Notices.ShouldBeEmpty();

    [Test]
    public void HandleGameEvent_AMapPlacedObject_IsNotShown() =>
        // hud_basedeathnotice.cpp:466: victim 0 removes the line.
        Fed(Kill(12, 99, name: "object_destroyed", more: new() { ["objecttype"] = 2 })).Notices.ShouldBeEmpty();

    [TestCase(0, "Dispenser (Pyro)")]
    [TestCase(1, "#TF_Object_Tele (Pyro)")]
    [TestCase(2, "Sentry (Pyro)")]
    [TestCase(3, "Sapper (Pyro)")]
    [TestCase(4, "Pyro")]
    [TestCase(-1, "Pyro")]
    public void HandleGameEvent_AnObjectDestroyed_IsNamedForItsTypeAndOwner(int type, string victim)
    {
        // tf_hud_deathnotice.cpp:43–49 and :935–968; an unlocalised token is copied as written.
        TfHudDeathNotice feed = Fed(Kill(12, 14, name: "object_destroyed", more: new() { ["objecttype"] = type, ["death_flags"] = 0x0001 }));

        feed.Notices.ShouldHaveSingleItem("an object destroyed adds no domination line").VictimName.ShouldBe(victim);
    }

    [Test]
    public void HandleGameEvent_AnAustraliumKill_GlowsWithTheAustraliumIcon()
    {
        // hud_basedeathnotice.cpp:493–497: TF_DEATH_AUSTRALIUM (0x0400) before DMG_CRITICAL.
        DeathNoticeItem notice = Fed(Kill(12, 14, more: new() { ["death_flags"] = 0x0400, ["damagebits"] = 1 << 20 })).Notices[0];

        (notice.Crit, notice.IconCritDeath!.ShortName).ShouldBe((true, "d_australium"));
    }

    [Test]
    public void HandleGameEvent_AStackedPlainKillAfterACrit_ClearsTheGlow()
    {
        // hud_basedeathnotice.cpp:503–507 resets the reused line.
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Kill(12, 14, more: new() { ["weaponid"] = 72, ["damagebits"] = 1 << 20 }));
        feed.HandleGameEvent(Kill(12, 14, more: new() { ["weaponid"] = 72 }));

        DeathNoticeItem notice = feed.Notices.ShouldHaveSingleItem();
        (notice.Crit, notice.IconCritDeath).ShouldBe((false, null));
        (notice.WeaponId, notice.KillerId, notice.VictimId).ShouldBe((72, 12, 14));
    }

    [TestCase(72, 14, 1, TestName = "UseExistingNotice_TheSameFishKillTwice_IsOneLine")]
    [TestCase(106, 14, 1, TestName = "UseExistingNotice_TheSameSlapTwice_IsOneLine")]
    [TestCase(92, 14, 1, TestName = "UseExistingNotice_TheSameThrowableTwice_IsOneLine")]
    [TestCase(93, 14, 1, TestName = "UseExistingNotice_TheSameGrenadeThrowableTwice_IsOneLine")]
    [TestCase(72, 13, 2, TestName = "UseExistingNotice_AFishKillOfAnotherVictim_IsASecondLine")]
    [TestCase(10, 14, 2, TestName = "UseExistingNotice_ANonStackingWeaponTwice_IsTwoLines")]
    public void UseExistingNotice_Stacking(int weapon, int secondVictim, int lines)
    {
        // tf_hud_deathnotice.cpp:1638–1663.
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Kill(12, 14, more: new() { ["weaponid"] = weapon }));
        feed.HandleGameEvent(Kill(12, secondVictim, more: new() { ["weaponid"] = weapon }));

        feed.Notices.Count.ShouldBe(lines);
    }

    [Test]
    public void UseExistingNotice_AFishKillByAnotherAttacker_IsASecondLine()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Kill(12, 14, more: new() { ["weaponid"] = 72 }));
        feed.HandleGameEvent(Kill(13, 14, more: new() { ["weaponid"] = 72 }));

        feed.Notices.Count.ShouldBe(2);
    }

    [Test]
    public void HandleGameEvent_ASuicide_IsSelfInflictedWithNoKillerName()
    {
        // hud_basedeathnotice.cpp:519–521.
        DeathNoticeItem notice = Fed(Kill(14, 14)).Notices[0];

        (notice.SelfInflicted, notice.KillerName, notice.Icon).ShouldBe((true, string.Empty, "d_scattergun"));
    }

    [Test]
    public void HandleGameEvent_AFallByAnotherPlayer_IsNotSelfInflicted()
    {
        DeathNoticeItem notice = Fed(Kill(12, 14, more: new() { ["damagebits"] = 1 << 5 })).Notices[0];

        (notice.SelfInflicted, notice.InfoText, notice.KillerName).ShouldBe((false, string.Empty, "Scout"));
    }

    [TestCase(0x0100, 0, "world", "maps/cp_test.bsp", "d_purgatory")]
    [TestCase(0, 1 << 4, "world", "maps/cp_test.bsp", "d_vehicle")]
    [TestCase(0, 0, "tracktrain", "maps/cp_test.bsp", "d_vehicle")]
    [TestCase(0, 0, "TrackTrain", "maps/cp_test.bsp", "d_vehicle")]
    [TestCase(0, 1 << 4, "world", "maps/PD_Galleria.bsp", "d_resurfacer")]
    [TestCase(0, 0, "world", "maps/cp_test.bsp", "d_world")]
    public void HandleGameEvent_AWorldDeath_TakesItsSpecialIcon(int flags, int damage, string weapon, string map, string icon)
    {
        // hud_basedeathnotice.cpp:523–548: purgatory, then fall, then vehicle or `d_tracktrain` (compared without case);
        // `Q_FileBase` and `Q_strlower` the map name for pd_galleria.
        DeathNoticeItem notice = Fed(Kill(0, 14, weapon, more: new() { ["death_flags"] = flags, ["damagebits"] = damage }, map: map)).Notices[0];

        notice.Icon.ShouldBe(icon);
    }

    [Test]
    public void HandleGameEvent_APurgatoryFall_TakesTheIconNotTheFallText()
    {
        DeathNoticeItem notice = Fed(Kill(0, 14, "world", more: new() { ["death_flags"] = 0x0100, ["damagebits"] = 1 << 5 })).Notices[0];

        (notice.Icon, notice.InfoText).ShouldBe(("d_purgatory", string.Empty));
    }

    [Test]
    public void HandleGameEvent_AFallUnderAVehicle_TakesTheFallTextNotTheVehicleIcon()
    {
        DeathNoticeItem notice = Fed(Kill(0, 14, "world", more: new() { ["damagebits"] = (1 << 5) | (1 << 4) })).Notices[0];

        (notice.Icon, notice.InfoText).ShouldBe(("d_world", "fell to a clumsy, painful death"));
    }

    [TestCase("fish_notice", 39, 0, "hum")]
    [TestCase("fish_notice__arm", 39, 0, "arm")]
    [TestCase("slap_notice", 80, 0, "slap")]
    [TestCase("fish_notice", 0, 0x0020, "hum")]
    [TestCase("fish_notice", 0, 0, "hum 1")]
    public void HandleGameEvent_AHumiliation_TakesItsText(string name, int custom, int flags, string text)
    {
        // tf_hud_deathnotice.cpp:1279–1310; a fish kill of a feigning spy is the kill text too.
        DeathNoticeItem notice = Fed(Kill(12, 13, name: name, more: new() { ["customkill"] = custom, ["death_flags"] = flags })).Notices[0];

        (notice.InfoText, notice.KillerName).ShouldBe((text, "Scout"));
    }

    [Test]
    public void HandleGameEvent_TwoStackedFishHits_CountTwo()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Kill(12, 13, name: "fish_notice", more: new() { ["weaponid"] = 72 }));
        feed.HandleGameEvent(Kill(12, 13, name: "fish_notice", more: new() { ["weaponid"] = 72 }));

        feed.Notices.ShouldHaveSingleItem().InfoText.ShouldBe("hum 2");
    }

    [Test]
    public void HandleGameEvent_AnAssistedHumiliation_JoinsTheNames() =>
        Fed(Kill(12, 14, name: "slap_notice", more: new() { ["assister"] = 13 })).Notices[0].KillerName.ShouldBe("Scout + Medic");

    [TestCase(12, "RED ROBOT", 2, false)]
    [TestCase(11, "BLUE ROBOT", 3, true)]
    public void HandleGameEvent_ARobotKilled_IsTheOtherTeamsRobot(int attacker, string victim, int victimTeam, bool local)
    {
        // tf_hud_deathnotice.cpp:1340–1358.
        DeathNoticeItem notice = Fed(Fire("rd_robot_killed", new() { ["attacker"] = attacker, ["weapon"] = "wrench" })).Notices[0];

        (notice.VictimName, notice.VictimTeam, notice.Icon, notice.LocalPlayerInvolved).ShouldBe((victim, victimTeam, "d_wrench", local));
        notice.KillerTeam.ShouldBe(attacker == 11 ? 2 : 3);
    }

    [Test]
    public void HandleGameEvent_PassGet_NamesTheOwner()
    {
        // tf_hud_deathnotice.cpp:1360–1383.
        DeathNoticeItem notice = Fed(Fire("pass_get", new() { ["owner"] = 1 })).Notices[0];

        (notice.InfoText, notice.KillerName, notice.KillerTeam, notice.LocalPlayerInvolved, notice.Icon).ShouldBe(("got", "Recorder", 2, true, "d_passtime_pass"));
    }

    [TestCase(2, 4, false)]
    [TestCase(1, 2, true)]
    [TestCase(2, 1, true)]
    public void HandleGameEvent_PassBallStolen_NamesBothSides(int attacker, int victim, bool local)
    {
        // tf_hud_deathnotice.cpp:1384–1410.
        DeathNoticeItem notice = Fed(Fire("pass_ball_stolen", new() { ["attacker"] = attacker, ["victim"] = victim })).Notices[0];

        (notice.InfoText, notice.LocalPlayerInvolved, notice.Icon).ShouldBe(("stole", local, "d_passtime_steal"));
        (notice.KillerTeam, notice.VictimTeam).ShouldBe((attacker == 1 || attacker == 4 ? 2 : 3, victim == 1 || victim == 4 ? 2 : 3));
    }

    [TestCase(2, 1, "scored", "d_passtime_score_blue", false)]
    [TestCase(1, 3, "scored 3", "d_passtime_score_red", true)]
    [TestCase(4, 2, "scored 2", "d_passtime_score_red", false)]
    public void HandleGameEvent_PassScore_CountsPointsAndTakesTheTeamIcon(int scorer, int points, string text, string icon, bool local)
    {
        // tf_hud_deathnotice.cpp:1411–1438.
        DeathNoticeItem notice = Fed(Fire("pass_score", new() { ["scorer"] = scorer, ["points"] = points })).Notices[0];

        (notice.InfoText, notice.Icon, notice.LocalPlayerInvolved).ShouldBe((text, icon, local));
    }

    [TestCase(2, 3, "Scout", "Medic", "passed", "d_passtime_pass", false)]
    [TestCase(2, 4, "Pyro", "Scout", "intercepted", "d_passtime_intercept", false)]
    [TestCase(1, 4, "Recorder", "Pyro", "passed", "d_passtime_pass", true)]
    [TestCase(4, 1, "Pyro", "Recorder", "passed", "d_passtime_pass", true)]
    public void HandleGameEvent_PassCaught_IsAPassOrAnInterception(int passer, int catcher, string killer, string victim, string text, string icon, bool local)
    {
        // tf_hud_deathnotice.cpp:1439–1488: the same team is a pass from passer to catcher, else the catcher intercepts.
        DeathNoticeItem notice = Fed(Fire("pass_pass_caught", new() { ["passer"] = passer, ["catcher"] = catcher })).Notices[0];

        (notice.KillerName, notice.VictimName, notice.InfoText, notice.Icon, notice.LocalPlayerInvolved).ShouldBe((killer, victim, text, icon, local));
        (notice.KillerTeam, notice.VictimTeam).ShouldBe((killer is "Recorder" or "Pyro" ? 2 : 3, victim is "Recorder" or "Pyro" ? 2 : 3));
    }

    [TestCase(2, 4, false)]
    [TestCase(1, 2, true)]
    [TestCase(2, 1, true)]
    public void HandleGameEvent_PassBallBlocked_NamesBlockerAndOwner(int blocker, int owner, bool local)
    {
        // tf_hud_deathnotice.cpp:1489–1513.
        DeathNoticeItem notice = Fed(Fire("pass_ball_blocked", new() { ["blocker"] = blocker, ["owner"] = owner })).Notices[0];

        (notice.InfoText, notice.Icon, notice.LocalPlayerInvolved).ShouldBe(("blocked", "d_ball", local));
        (notice.KillerTeam, notice.VictimTeam).ShouldBe((blocker == 1 || blocker == 4 ? 2 : 3, owner == 1 || owner == 4 ? 2 : 3));
    }

    [TestCase("aBalloonicorn", 12, "Scout + Balloonicorn")]
    [TestCase("cBalloonicorn", 12, "Balloonicorn + Scout")]
    [TestCase("bTF_Balloonicorn", 12, "Scout + Balloonicorn")]
    [TestCase("dTF_Balloonicorn", 12, "Balloonicorn + Scout")]
    [TestCase("bNoSuchToken", 12, "Scout + ")]
    [TestCase("zBalloonicorn", 12, "Scout")]
    [TestCase("aBalloonicorn", 14, "")]
    public void HandleGameEvent_APyroVisionFallbackAssister_IsReadByItsFirstByte(string fallback, int attacker, string killer)
    {
        // tf_hud_deathnotice.cpp:808–877 (EHorriblePyroVisionHack, tf_shareddefs.h:1867); never for a self-kill.
        DeathNoticeItem notice = Fed(Kill(attacker, 14, more: new() { ["assister_fallback"] = fallback }, vision: 1)).Notices[0];

        notice.KillerName.ShouldBe(killer);
    }

    [Test]
    public void HandleGameEvent_AFallbackAssisterWithoutPyroVision_IsIgnored() =>
        Fed(Kill(12, 14, more: new() { ["assister_fallback"] = "aBalloonicorn" }, vision: 2)).Notices[0].KillerName.ShouldBe("Scout");

    [Test]
    public void HandleGameEvent_AFallbackAssisterOnAnObjectKilledByItsOwner_StillShows()
    {
        // :816 — `bIsObjectDestroyed || killer != victim`.
        DeathNoticeItem notice = Fed(Kill(14, 14, name: "object_destroyed", more: new() { ["assister_fallback"] = "aBalloonicorn", ["objecttype"] = 2 }, vision: 1)).Notices[0];

        notice.KillerName.ShouldBe(" + Balloonicorn", "a self-destroyed object clears the killer name first (hud_basedeathnotice.cpp:521)");
    }

    [Test]
    public void HandleGameEvent_ARealAssisterUnderPyroVision_BeatsTheFallback() =>
        Fed(Kill(12, 14, more: new() { ["assister"] = 13, ["assister_fallback"] = "aBalloonicorn" }, vision: 1)).Notices[0].KillerName.ShouldBe("Scout + Medic");

    [Test]
    public void HandleGameEvent_TheRecorderAssisting_InvolvesThem() =>
        Fed(Kill(12, 14, more: new() { ["assister"] = 11 })).Notices[0].LocalPlayerInvolved.ShouldBeTrue();

    [TestCase(0x0002, 13, "is DOMINATING", "Medic")]
    [TestCase(0x0008, 13, "REVENGE", "Medic")]
    [TestCase(0x0004, -1, "REVENGE", "Scout")]
    public void HandleGameEvent_ARivalryFlag_AddsItsLine(int flags, int assister, string text, string killer)
    {
        // tf_hud_deathnotice.cpp:903–925; AddAdditionalMsg :1526.
        TfHudDeathNotice feed = Fed(Kill(12, 14, more: new() { ["death_flags"] = flags, ["assister"] = assister }));

        feed.Notices.Count.ShouldBe(2);
        (feed.Notices[1].InfoText, feed.Notices[1].KillerName, feed.Notices[1].KillerTeam, feed.Notices[1].VictimName, feed.Notices[1].VictimTeam)
            .ShouldBe((text, killer, 3, "Pyro", 2));
        (feed.Notices[1].CreationTime, feed.Notices[1].LocalPlayerInvolved).ShouldBe((10f, false));
    }

    [TestCase(0x0002, TestName = "HandleGameEvent_AnAssisterDominationWithNoAssister_AddsNothing")]
    [TestCase(0x0008, TestName = "HandleGameEvent_AnAssisterRevengeWithNoAssister_AddsNothing")]
    public void HandleGameEvent_AssisterFlagWithoutAssister(int flags) =>
        Fed(Kill(12, 14, more: new() { ["death_flags"] = flags })).Notices.Count.ShouldBe(1);

    [TestCase(0x0001, "dom?")]
    [TestCase(0x0004, "rev?")]
    public void HandleGameEvent_ARivalryUnderPyroVision_TakesTheWhatText(int flags, string text) =>
        Fed(Kill(12, 14, more: new() { ["death_flags"] = flags }, vision: 1)).Notices[1].InfoText.ShouldBe(text);

    [TestCase(0x0002, "Game.Domination")]
    [TestCase(0x0008, "Game.Revenge")]
    public void HandleGameEvent_AnAssisterRivalryByTheRecorder_PlaysItsSound(int flags, string sound)
    {
        Fed(Kill(12, 14, more: new() { ["death_flags"] = flags, ["assister"] = 11 }), out List<string> sounds);

        sounds.ShouldBe([sound]);
    }

    [Test]
    public void HandleGameEvent_ADominationLineWithTheRecorderAsVictim_InvolvesThem() =>
        Fed(Kill(12, 11, more: new() { ["death_flags"] = 0x0001 })).Notices[1].LocalPlayerInvolved.ShouldBeTrue();

    [TestCase(2, "sharp_dresser", 0, "d_sharp_dresser_backstab")]
    [TestCase(2, "knife", 0, "d_backstab")]
    [TestCase(1, "ambassador", 0, "d_ambassador_headshot")]
    [TestCase(1, "huntsman", 0, "d_huntsman_headshot")]
    [TestCase(1, "sniperrifle", 1, "d_headshot_player_penetration")]
    [TestCase(1, "sniperrifle", 0, "d_headshot")]
    [TestCase(51, "sniperrifle", 0, "d_headshot")]
    [TestCase(3, "flamethrower", 0, "d_flamethrower")]
    [TestCase(17, "huntsman", 0, "d_huntsman_burning")]
    [TestCase(18, "huntsman", 0, "d_huntsman_flyingburn")]
    [TestCase(19, "world", 0, "d_pumpkindeath")]
    [TestCase(75, "world", 0, "d_bumper_kart")]
    [TestCase(76, "world", 0, "d_necro_smasher")]
    public void CustomKill_AKillByAnotherPlayer_TakesTheCustomIcon(int custom, string weapon, int penetrations, string icon)
    {
        // tf_hud_deathnotice.cpp:975–1133; TF_DMG_CUSTOM_BURNING by another player keeps the weapon's icon.
        DeathNoticeItem notice = Fed(Kill(12, 14, weapon, more: new() { ["customkill"] = custom, ["playerpenetratecount"] = penetrations })).Notices[0];

        (notice.Icon, notice.InfoText).ShouldBe((icon, string.Empty));
    }

    [Test]
    public void CustomKill_BurningByYourself_IsTheFireDeath()
    {
        DeathNoticeItem notice = Fed(Kill(14, 14, "flamethrower", more: new() { ["customkill"] = 3 })).Notices[0];

        (notice.Icon, notice.InfoText).ShouldBe(("d_firedeath", string.Empty));
    }

    [TestCase(14, -1, "suicide")]
    [TestCase(12, -1, "assisted")]
    [TestCase(12, 13, "assisted multi")]
    public void CustomKill_ASuicide_TakesItsText(int attacker, int assister, string text) =>
        // tf_hud_deathnotice.cpp:1022–1032.
        Fed(Kill(attacker, 14, more: new() { ["customkill"] = 6, ["assister"] = assister })).Notices[0].InfoText.ShouldBe(text);

    [TestCase(12, "assisted")]
    [TestCase(14, "")]
    [TestCase(0, "")]
    public void CustomKill_TheCroc_IsAnAssistedSuicideOnlyWithAnotherAttacker(int attacker, string text) =>
        // tf_hud_deathnotice.cpp:1034–1046: `GetInt( "attacker" ) && userid != attacker`.
        Fed(Kill(attacker, 14, "world", more: new() { ["customkill"] = 81 })).Notices[0].InfoText.ShouldBe(text);

    [TestCase(50, "maps/cp_test.bsp", "MONOCULUS")]
    [TestCase(58, "maps/cp_test.bsp", "MERASMUS")]
    [TestCase(59, "maps/cp_test.bsp", "MERASMUS")]
    [TestCase(60, "maps/cp_test.bsp", "MERASMUS")]
    [TestCase(66, "maps/cp_test.bsp", "Skeleton")]
    [TestCase(66, "maps/koth_slime.bsp", "Salmann")]
    public void CustomKill_ATeamlessBoss_IsNamedOnTheHalloweenTeam(int custom, string map, string killer)
    {
        // tf_hud_deathnotice.cpp:1048–1121: only when the killer had no team.
        DeathNoticeItem notice = Fed(Kill(99, 14, "world", more: new() { ["customkill"] = custom }, map: map)).Notices[0];

        (notice.KillerName, notice.KillerTeam).ShouldBe((killer, 5));
    }

    [TestCase(50)]
    [TestCase(58)]
    [TestCase(66)]
    public void CustomKill_ABossKillByAPlayer_KeepsThePlayer(int custom) =>
        Fed(Kill(12, 14, more: new() { ["customkill"] = custom })).Notices[0].KillerName.ShouldBe("Scout");

    [TestCase(84, "d_krampus_melee")]
    [TestCase(85, "d_krampus_ranged")]
    public void CustomKill_Krampus_NamesTheBossWhoeverKilled(int custom, string icon)
    {
        // tf_hud_deathnotice.cpp:1133–1162: no team test.
        DeathNoticeItem notice = Fed(Kill(12, 14, more: new() { ["customkill"] = custom })).Notices[0];

        (notice.KillerName, notice.KillerTeam, notice.Icon, notice.InfoText).ShouldBe(("Krampus", 5, icon, string.Empty));
    }

    [Test]
    public void HandleGameEvent_NerveGasDamage_IsTheSawIcon() =>
        // tf_hud_deathnotice.cpp:1168: DMG_NERVEGAS (1 << 16) after the switch.
        Fed(Kill(12, 14, more: new() { ["damagebits"] = 1 << 16, ["customkill"] = 2 })).Notices[0].Icon.ShouldBe("d_saw_kill");

    [TestCase(14, "leaderboard_streak")]
    [TestCase(11, "leaderboard_streak_dneg")]
    public void HandleGameEvent_AKillStreakWeapon_CountsBeforeTheIcon(int victim, string icon)
    {
        // tf_hud_deathnotice.cpp:1193–1206.
        TfHudDeathNotice feed = Feed(streakIcons: true);

        feed.HandleGameEvent(Kill(12, victim, more: new() { ["kill_streak_wep"] = 3, ["duck_streak_total"] = 4, ["ducks_streaked"] = 1 }));
        DeathNoticeItem notice = feed.Notices[0];

        (notice.PreKillerText, notice.IconPostKillerName!.ShortName).ShouldBe(("ks3", icon));
    }

    [TestCase(14, "eotl_duck")]
    [TestCase(11, "eotl_duck_dneg")]
    public void HandleGameEvent_ADuckStreak_CountsBeforeTheDuck(int victim, string icon)
    {
        // tf_hud_deathnotice.cpp:1207–1214.
        TfHudDeathNotice feed = Feed(streakIcons: true);

        feed.HandleGameEvent(Kill(12, victim, more: new() { ["duck_streak_total"] = 4, ["ducks_streaked"] = 1 }));
        DeathNoticeItem notice = feed.Notices[0];

        (notice.PreKillerText, notice.IconPostKillerName!.ShortName).ShouldBe(("ds4", icon));
    }

    [TestCase(4, 0)]
    [TestCase(0, 1)]
    public void HandleGameEvent_ADuckStreakWithoutDucksThisKill_ShowsNoCount(int total, int thisKill)
    {
        DeathNoticeItem notice = Fed(Kill(12, 14, more: new() { ["duck_streak_total"] = total, ["ducks_streaked"] = thisKill })).Notices[0];

        (notice.PreKillerText, notice.IconPostKillerName).ShouldBe((string.Empty, null));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    public void HandleGameEvent_RuneCarriers_TakeTheRuneIconRedPlainBlueNegative(int rune)
    {
        // tf_hud_deathnotice.cpp:1185–1191 and GetMannPowerIcon :1668: "Red team is normal file and blue is dNeg file".
        PlayerConditions carrying = Carrying(TfConditions.Runes[rune]);
        ScenePlayer[] players = Players().Select(player => player with { Conditions = carrying }).ToArray();

        DeathNoticeItem notice = Fed(Kill(14, 12, players: players)).Notices[0];

        (notice.IconPreKillerName!.ShortName, notice.IconPostVictimName!.ShortName).ShouldBe(("d_" + RuneIconNames[rune], "dneg_" + RuneIconNames[rune]));
    }

    [Test]
    public void HandleGameEvent_NoRunes_DrawsNoRuneIcons()
    {
        DeathNoticeItem notice = Fed(Kill(14, 12)).Notices[0];

        (notice.IconPreKillerName, notice.IconPostVictimName).ShouldBe((null, null));
    }

    [Test]
    public void HandleGameEvent_AnAssisterOnAFifthKillStreak_ShowsTheAssistersBanner()
    {
        // tf_hud_deathnotice.cpp:1225–1229: `pAssister && iKillStreakAssist > 1`.
        TfStreakNotice banner = Fed(Kill(12, 14, more: new() { ["assister"] = 13, ["kill_streak_assist"] = 5 })).Streak!;

        banner.Text.ShouldBe("\u0002Medic\u0001 is on a \u0003spree\u0001 (5)");
    }

    [Test]
    public void HandleGameEvent_AFifthAssistWithNoAssister_ShowsNoBanner() =>
        Fed(Kill(12, 14, more: new() { ["kill_streak_assist"] = 5 })).Streak!.CurrentStreakCount.ShouldBe(0);

    [Test]
    public void RetireExpiredDeathNotices_AtTheMaximum_RemovesNothing()
    {
        TfHudDeathNotice feed = Feed();

        for (int i = 0; i < 4; i++)
        {
            feed.HandleGameEvent(Kill(12, 14));
        }

        feed.RetireExpiredDeathNotices(10f);

        feed.Notices.Count.ShouldBe(4);
    }

    [Test]
    public void RetireExpiredDeathNotices_AllTheRecordersOverTheMaximum_RemovesTheOldestAndKeepsTheNewestOther()
    {
        // hud_basedeathnotice.cpp:357–388: the first pass never removes the newest; the second removes from the front.
        TfHudDeathNotice feed = Feed();

        feed.SetAnimationValue("MaxDeathNotices", 2f);
        feed.HandleGameEvent(Kill(12, 11, more: new() { ["weaponid"] = 1 }));
        feed.HandleGameEvent(Kill(13, 11, more: new() { ["weaponid"] = 2 }));
        feed.HandleGameEvent(Kill(14, 11, more: new() { ["weaponid"] = 3 }));
        feed.HandleGameEvent(Kill(12, 13, more: new() { ["weaponid"] = 4 }));
        feed.RetireExpiredDeathNotices(10f);

        feed.Notices.Select(notice => notice.WeaponId).ShouldBe([3, 4]);
    }

    [Test]
    public void RetireExpiredDeathNotices_OthersOverTheMaximum_RemovesTheOldestOthers()
    {
        TfHudDeathNotice feed = Feed();

        feed.SetAnimationValue("MaxDeathNotices", 2f);
        feed.HandleGameEvent(Kill(12, 14, more: new() { ["weaponid"] = 1 }));
        feed.HandleGameEvent(Kill(12, 11, more: new() { ["weaponid"] = 2 }));
        feed.HandleGameEvent(Kill(13, 14, more: new() { ["weaponid"] = 3 }));
        feed.HandleGameEvent(Kill(12, 4 + 10, more: new() { ["weaponid"] = 4 }));
        feed.RetireExpiredDeathNotices(10f);

        feed.Notices.Select(notice => notice.WeaponId).ShouldBe([2, 4]);
    }

    [Test]
    public void Paint_AKillWithAFont_LaysOutKillerIconAndVictim()
    {
        // hud_basedeathnotice.cpp:129–327. Every glyph 10 wide and the font 12 tall: xSpacing 10, "Scout" 60 with it,
        // "Pyro" 50, the icon (96 + 10) × 14/32 = 46 wide, 42 drawn; total 60 + 46 + 50 + 2 × 10 = 176, from 400 − 176 = 224.
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();

        feed.Wide = 400;
        feed.HandleGameEvent(Kill(12, 14));
        feed.Paint(surface, Context(surface, Files(), Font));

        surface.Calls.ShouldContain("color 46 43 42 220");
        surface.Calls.Single(call => call.StartsWith("polygon ", System.StringComparison.Ordinal)).Split(' ')[1].ShouldBe("224,20");
        surface.Glyphs.ShouldContain("S@234,18");
        surface.SubRects.ShouldContain(rect => rect.StartsWith("294 17 336 31", System.StringComparison.Ordinal));
        surface.Glyphs.ShouldContain("P@340,18");
        surface.Runs.ShouldBe([("Scout", "153 204 255 255"), ("Pyro", "255 64 64 255")]);
    }

    [Test]
    public void Paint_TheRecordersFall_PutsTheTextAfterTheVictim()
    {
        // :296–303 self-inflicted: the info text moves right past the victim, the victim left over it. "fell…" is 31
        // glyphs, 320 with the space; "Recorder" 90; the skull (32 + 10) × 14/32 = 18. Total 448, from 500 − 448 = 52; the
        // icon after the margin at 62, the text at 80 + 90, the victim at 80 + 320 − 320.
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();

        feed.Wide = 500;
        feed.HandleGameEvent(Kill(0, 11, "world", more: new() { ["damagebits"] = 1 << 5 }));
        feed.Paint(surface, Context(surface, Files(), Font));

        surface.Calls.ShouldContain("color 245 229 196 200");
        surface.SubRects.ShouldContain(rect => rect.StartsWith("62 17 76 31", System.StringComparison.Ordinal));
        surface.Glyphs.ShouldContain("f@170,18");
        surface.Glyphs.ShouldContain("R@80,18");
        surface.Runs.ShouldBe([("fell to a clumsy, painful death", "65 65 65 255"), ("Recorder", "255 64 64 255")]);
    }

    [Test]
    public void Paint_EveryOptionalPart_DrawsInEngineOrder()
    {
        // Pre-killer rune, killer, streak text, streak icon, crit glow, weapon, victim, post-victim rune, end text.
        PlayerConditions carrying = Carrying(TfConditions.Runes[0]);
        ScenePlayer[] players = Players().Select(player => player with { Conditions = carrying }).ToArray();
        TfHudDeathNotice feed = Feed(Font, streakIcons: true);
        TextRecorder surface = new();

        feed.Wide = 600;
        feed.HandleGameEvent(Kill(14, 12, more: new() { ["kill_streak_wep"] = 3, ["damagebits"] = 1 << 20 }, players: players));
        feed.Notices[0].InfoTextEnd = "end";
        feed.Paint(surface, Context(surface, Files(), Font));

        // Runes 32 × 14/32 = 14; "Pyro" 50; "ks3" 30 − 10 = 20; streak 14; weapon 46; "Scout" 60; "end" 40; margins 20.
        // Total 14 + 50 + 20 + 14 + 46 + 60 + 14 + 40 + 20 = 278, from 600 − 278 = 322.
        surface.SubRects.Take(1).Single().ShouldStartWith("332 17 346 31");
        surface.Glyphs.ShouldContain("P@356,18");
        surface.Glyphs.ShouldContain("k@416,18");
        surface.SubRects[1].ShouldStartWith("436 17 450 31");
        surface.SubRects[2].ShouldStartWith("460 17 502 31");
        surface.SubRects[3].ShouldStartWith("460 17 502 31");
        surface.Glyphs.ShouldContain("S@506,18");
        surface.SubRects[4].ShouldStartWith("566 17 580 31");
        surface.Glyphs.ShouldContain("e@590,18");
    }

    [TestCase(3, "112 176 74 255")]
    [TestCase(4, "112 176 74 255")]
    [TestCase(5, "134 80 172 255")]
    [TestCase(0, "134 80 172 255")]
    public void Paint_AHalloweenBoss_IsGreenOnLakesideAndHightowerElsePurple(int scenario, string colour)
    {
        // tf_hud_deathnotice.cpp:1597–1606.
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();
        HudViewport viewport = (HudViewport)feed.Parent!;

        viewport.Think(new HudState(true, true, 0, 1, true, Rules: new SceneGameRules(false, scenario, false)));
        feed.HandleGameEvent(Kill(99, 14, "world", more: new() { ["customkill"] = 50 }));
        feed.Paint(surface, viewport.Context!);

        surface.Runs[0].ShouldBe(("MONOCULUS", colour));
    }

    [TestCase(11, "65 65 65 255")]
    [TestCase(14, "255 255 255 255")]
    public void Paint_ATeamlessName_IsTheLocalColourOnlyWhenInvolved(int victim, string colour)
    {
        // tf_hud_deathnotice.cpp:1590–1594: TEAM_UNASSIGNED.
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();

        feed.HandleGameEvent(Fire("teamplay_point_captured", new() { ["cpname"] = "Point", ["cappers"] = victim == 11 ? "\u0001" : "\u0004" }));
        feed.Paint(surface, Context(surface, Files(), Font));

        surface.Runs.Single(run => run.Text == "Point").Colour.ShouldBe(colour);
    }

    [Test]
    public void Paint_ASpectatorTeam_IsWhite()
    {
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();

        feed.HandleGameEvent(Kill(12, 14));
        feed.Notices[0].KillerTeam = 1;
        feed.Paint(surface, Context(surface, Files(), Font));

        surface.Runs[0].Colour.ShouldBe("255 255 255 255");
    }

    [Test]
    public void Paint_LeftJustified_StartsAtTheMargin()
    {
        TfHudDeathNotice feed = Feed(Font);
        TextRecorder surface = new();

        feed.SetAnimationValue("RightJustify", false);
        feed.HandleGameEvent(Kill(12, 14));
        feed.HandleGameEvent(Kill(13, 14));
        feed.Paint(surface, Context(surface, Files(), Font));

        // The second line is LineHeight + LineSpacing = 20 below: y 36, text at 38.
        surface.Glyphs.ShouldContain("S@10,18");
        surface.Glyphs.ShouldContain("M@10,38");
    }

    private static PlayerConditions Carrying(int condition)
    {
        int[] bits = new int[5];

        bits[condition / 32] = 1 << (condition % 32);

        return new PlayerConditions(bits[0], bits[1], bits[2], bits[3], bits[4]);
    }

    private static void Think(TfHudDeathNotice feed, string conVar, string value)
    {
        System.Func<string, string?> lookup = name => name == conVar ? value : null;

        ((HudViewport)feed.Parent!).Think(new HudState(true, true, 0, 1, true, ConVars: new HudConVars(lookup, lookup)));
    }

    private static HudGameEvent Kill(
        int attacker, int victim, string weapon = "scattergun", Dictionary<string, object?>? more = null, string name = "player_death",
        SceneGameRules rules = default, ScenePlayer[]? players = null, string map = "maps/cp_test.bsp", int vision = 0)
    {
        Dictionary<string, object?> values = new() { ["userid"] = victim, ["attacker"] = attacker, ["assister"] = -1, ["weapon"] = weapon };

        foreach ((string key, object? value) in more ?? [])
        {
            values[key] = value;
        }

        return Fire(name, values, rules, players, map, vision);
    }
}
