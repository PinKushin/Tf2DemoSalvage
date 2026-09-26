using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudDeathNotice` over `CHudBaseDeathNotice` (game/client/tf/tf_hud_deathnotice.cpp, hud_basedeathnotice.cpp).</summary>
/// <remarks>
/// A 640 × 480 viewport. Players: 1 "Recorder" (user 11, RED), 2 "Scout" (user 12, BLU), 3 "Medic" (user 13, BLU),
/// 4 "Pyro" (user 14, RED). `d_scattergun`, `dneg_scattergun`, `d_skull_tf`, `d_crit` and `leaderboard_dominated` exist.
/// </remarks>
public sealed class TfHudDeathNoticeConformanceTests
{
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    [Test]
    public void HandleGameEvent_AKill_NamesBothSidesWithTheirTeamsAndTheWeaponIcon()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Death(attacker: 12, victim: 14, weapon: "scattergun"));

        DeathNoticeItem notice = feed.Notices.ShouldHaveSingleItem();
        (notice.KillerName, notice.KillerTeam, notice.VictimName, notice.VictimTeam).ShouldBe(("Scout", 3, "Pyro", 2));
        notice.IconDeath!.ShortName.ShouldBe("d_scattergun");
        notice.LocalPlayerInvolved.ShouldBeFalse();
        notice.CreationTime.ShouldBe(10f);
    }

    [Test]
    public void HandleGameEvent_TheRecorderInvolved_TakesTheInvertedIcon() =>
        Fed(Death(attacker: 12, victim: 11, weapon: "scattergun")).Notices[0].IconDeath!.ShortName.ShouldBe("dneg_scattergun");

    [Test]
    public void HandleGameEvent_AnUnknownWeapon_FallsBackToTheSkull() =>
        Fed(Death(attacker: 12, victim: 14, weapon: "nosuchgun")).Notices[0].IconDeath!.ShortName.ShouldBe("d_skull_tf");

    [Test]
    public void HandleGameEvent_ACritical_CarriesTheCritGlow()
    {
        DeathNoticeItem notice = Fed(Death(attacker: 12, victim: 14, weapon: "scattergun", damageBits: 1 << 20)).Notices[0];

        notice.Crit.ShouldBeTrue();
        notice.IconCritDeath!.ShortName.ShouldBe("d_crit");
    }

    [Test]
    public void HandleGameEvent_AnAssist_JoinsTheNamesWithAPlus() =>
        Fed(Death(attacker: 12, victim: 14, weapon: "scattergun", assister: 13)).Notices[0].KillerName.ShouldBe("Scout + Medic");

    [Test]
    public void HandleGameEvent_ADomination_AddsALineAfterTheKill()
    {
        TfHudDeathNotice feed = Fed(Death(attacker: 12, victim: 14, weapon: "scattergun", deathFlags: 0x0001));

        feed.Notices.Count.ShouldBe(2);
        feed.Notices[1].InfoText.ShouldBe("is DOMINATING");
        feed.Notices[1].IconDeath!.ShortName.ShouldBe("leaderboard_dominated");
        (feed.Notices[1].KillerName, feed.Notices[1].VictimName).ShouldBe(("Scout", "Pyro"));
    }

    [Test]
    public void HandleGameEvent_AFall_IsSelfInflictedWithTheFallText()
    {
        DeathNoticeItem notice = Fed(Death(attacker: 0, victim: 14, weapon: "world", damageBits: 1 << 5)).Notices[0];

        notice.SelfInflicted.ShouldBeTrue();
        notice.KillerName.ShouldBe(string.Empty);
        notice.InfoText.ShouldBe("fell to a clumsy, painful death");
    }

    [Test]
    public void HandleGameEvent_AFeignedDeathOfATeammate_IsNotShown() =>
        Fed(Death(attacker: 12, victim: 4 + 10, weapon: "scattergun", deathFlags: 0x0020)).Notices.ShouldBeEmpty("the Pyro is on the recorder's team");

    [Test]
    public void HandleGameEvent_AFeignedDeathOfAnEnemy_IsShown() =>
        Fed(Death(attacker: 14, victim: 12, weapon: "flamethrower", deathFlags: 0x0020)).Notices.Count.ShouldBe(1);

    [Test]
    public void HandleGameEvent_AFlagPickupInMannVsMachine_IsDropped()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Fire("teamplay_flag_event", new() { ["eventtype"] = 1, ["player"] = 2 }, rules: new SceneGameRules(true, 0, false)));

        feed.Notices.ShouldBeEmpty();
    }

    [Test]
    public void HandleGameEvent_ACapture_NamesTheCappersAndTakesTheTeamIcon()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Fire("teamplay_point_captured", new() { ["cpname"] = "Point A", ["cappers"] = "\u0002\u0003" }));

        DeathNoticeItem notice = feed.Notices.ShouldHaveSingleItem();
        (notice.KillerName, notice.KillerTeam, notice.VictimName).ShouldBe(("Scout, Medic", 3, "Point A"));
        notice.Icon.ShouldBe("d_bluecapture");
        notice.InfoText.ShouldBe("captured");
    }

    [Test]
    public void RetireExpiredDeathNotices_PastSixSeconds_RemovesTheOthersButKeepsTheRecordersForTwelve()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Death(attacker: 12, victim: 14, weapon: "scattergun"));
        feed.HandleGameEvent(Death(attacker: 12, victim: 11, weapon: "scattergun"));
        // Created at 10: the other line lasts to 16, the recorder's to 22 — 20 is past both 16 and 10 + 1.5 × 6.
        feed.RetireExpiredDeathNotices(20f);

        feed.Notices.ShouldHaveSingleItem().VictimName.ShouldBe("Recorder");
    }

    [Test]
    public void RetireExpiredDeathNotices_OverTheMaximum_DropsTheOldestNotInvolvingTheRecorderFirst()
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(Death(attacker: 12, victim: 11, weapon: "scattergun"));

        for (int i = 0; i < 4; i++)
        {
            feed.HandleGameEvent(Death(attacker: 12, victim: 14, weapon: "scattergun"));
        }

        feed.RetireExpiredDeathNotices(10f);

        feed.Notices.Count.ShouldBe(4);
        feed.Notices[0].VictimName.ShouldBe("Recorder", "the recorder's line outranks an older-than-it non-local one");
    }

    [Test]
    public void Paint_OneKill_DrawsItsRoundedBackgroundAgainstTheRightEdge()
    {
        TfHudDeathNotice feed = Feed();
        TextRecorder surface = new();

        feed.HandleGameEvent(Death(attacker: 12, victim: 14, weapon: "scattergun"));
        feed.Paint(surface, Context(surface, Files()));

        // No font: text is zero wide, so the line is the margins (2 × XRES(10) = 20) and the icon (96 wide, 32 tall, scaled
        // to 16 − YRES(2) = 14 → 42 wide). Right-justified in 200: x 138 to 200, y YRES(16) + 1 = 17 to 16 + 16 − 1 = 31.
        string polygon = surface.Calls.Single(call => call.StartsWith("polygon ", System.StringComparison.Ordinal));
        string[] corners = polygon["polygon ".Length..].Split(' ');

        // The corner radius is 3: the top-left arc runs from (x0, y0 + 3) to (x0 + 3, y0), the top-right from (x1 − 3, y0).
        corners.Length.ShouldBe(40);
        corners[0].ShouldBe("138,20");
        corners[9].ShouldBe("141,17");
        corners[10].ShouldBe("197,17");
        corners[19].ShouldBe("200,20");
        // The icon after the margin, 42 × 14, centred in the 16-tall line: (16 − 14) / 2 below its top.
        surface.SubRects.ShouldContain(rect => rect.StartsWith("148 17 190 31", System.StringComparison.Ordinal));
    }

    [Test]
    public void TfCustomKills_EveryValue_IsValves()
    {
        RequireTheSdk();
        IReadOnlyDictionary<string, int> custom = SourceSdk.Enumerators(SharedDefs, "ETFDmgCustom");

        new Dictionary<string, int>
        {
            ["TF_DMG_CUSTOM_HEADSHOT"] = TfCustomKills.Headshot,
            ["TF_DMG_CUSTOM_BACKSTAB"] = TfCustomKills.Backstab,
            ["TF_DMG_CUSTOM_BURNING"] = TfCustomKills.Burning,
            ["TF_DMG_CUSTOM_SUICIDE"] = TfCustomKills.Suicide,
            ["TF_DMG_CUSTOM_BURNING_ARROW"] = TfCustomKills.BurningArrow,
            ["TF_DMG_CUSTOM_FLYINGBURN"] = TfCustomKills.FlyingBurn,
            ["TF_DMG_CUSTOM_PUMPKIN_BOMB"] = TfCustomKills.PumpkinBomb,
            ["TF_DMG_CUSTOM_FISH_KILL"] = TfCustomKills.FishKill,
            ["TF_DMG_CUSTOM_EYEBALL_ROCKET"] = TfCustomKills.EyeballRocket,
            ["TF_DMG_CUSTOM_HEADSHOT_DECAPITATION"] = TfCustomKills.HeadshotDecapitation,
            ["TF_DMG_CUSTOM_MERASMUS_GRENADE"] = TfCustomKills.MerasmusGrenade,
            ["TF_DMG_CUSTOM_MERASMUS_ZAP"] = TfCustomKills.MerasmusZap,
            ["TF_DMG_CUSTOM_MERASMUS_DECAPITATION"] = TfCustomKills.MerasmusDecapitation,
            ["TF_DMG_CUSTOM_SPELL_SKELETON"] = TfCustomKills.SpellSkeleton,
            ["TF_DMG_CUSTOM_KART"] = TfCustomKills.Kart,
            ["TF_DMG_CUSTOM_GIANT_HAMMER"] = TfCustomKills.GiantHammer,
            ["TF_DMG_CUSTOM_SLAP_KILL"] = TfCustomKills.SlapKill,
            ["TF_DMG_CUSTOM_CROC"] = TfCustomKills.Croc,
            ["TF_DMG_CUSTOM_KRAMPUS_MELEE"] = TfCustomKills.KrampusMelee,
            ["TF_DMG_CUSTOM_KRAMPUS_RANGED"] = TfCustomKills.KrampusRanged,
        }.ShouldAllBe(pair => custom[pair.Key] == pair.Value);
    }

    [Test]
    public void TfWeaponIds_TheStackingWeapons_AreValves()
    {
        RequireTheSdk();
        IReadOnlyDictionary<string, int> weapons = SourceSdk.Enumerators(SharedDefs, "ETFWeaponType");

        (weapons["TF_WEAPON_BAT_FISH"], weapons["TF_WEAPON_THROWABLE"], weapons["TF_WEAPON_GRENADE_THROWABLE"], weapons["TF_WEAPON_SLAP"])
            .ShouldBe((TfWeaponIds.BatFish, TfWeaponIds.Throwable, TfWeaponIds.GrenadeThrowable, TfWeaponIds.Slap));
    }

    [Test]
    public void TfConditions_TheRunes_AreValvesInRuneOrder()
    {
        RequireTheSdk();
        IReadOnlyDictionary<string, int> conditions = SourceSdk.Enumerators(SharedDefs, "ETFCond");
        string[] runes = ["STRENGTH", "HASTE", "REGEN", "RESIST", "VAMPIRE", "REFLECT", "PRECISION", "AGILITY", "KNOCKOUT", "KING", "PLAGUE", "SUPERNOVA"];

        runes.Select(rune => conditions["TF_COND_RUNE_" + rune]).ShouldBe(TfConditions.Runes);
    }

    private static void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    private static TfHudDeathNotice Fed(HudGameEvent fired)
    {
        TfHudDeathNotice feed = Feed();

        feed.HandleGameEvent(fired);

        return feed;
    }

    private static HudGameEvent Death(int attacker, int victim, string weapon, int assister = -1, int deathFlags = 0, int damageBits = 0) =>
        Fire("player_death", new()
        {
            ["userid"] = victim,
            ["attacker"] = attacker,
            ["assister"] = assister,
            ["weapon"] = weapon,
            ["death_flags"] = deathFlags,
            ["damagebits"] = damageBits,
        });

    private static HudGameEvent Fire(string name, Dictionary<string, object?> values, SceneGameRules rules = default)
    {
        Dictionary<int, PlayerInfo> roster = new()
        {
            [1] = new PlayerInfo("Recorder", 11, string.Empty, 1, false, false),
            [2] = new PlayerInfo("Scout", 12, string.Empty, 2, false, false),
            [3] = new PlayerInfo("Medic", 13, string.Empty, 3, false, false),
            [4] = new PlayerInfo("Pyro", 14, string.Empty, 4, false, false),
        };
        ScenePlayer[] players =
        [
            new(1, 0f, 0f, 0f, 2, 125, 1),
            new(2, 0f, 0f, 0f, 3, 125, 1),
            new(3, 0f, 0f, 0f, 3, 150, 5),
            new(4, 0f, 0f, 0f, 2, 175, 7),
        ];

        return new HudGameEvent(new SceneGameEvent(1000, name, values, roster), 10f, players, 1, rules, "maps/cp_test.bsp", 0);
    }

    private static TfHudDeathNotice Feed()
    {
        HudViewport viewport = new() { Wide = 640, Tall = 480 };
        TfHudDeathNotice feed = new(viewport) { Wide = 200, Tall = 100 };
        TextRecorder surface = new();
        VguiContext context = Context(surface, Files());

        viewport.Context = context;
        viewport.Icons = HudTextures.Load(context);
        feed.PerformApplySchemeSettings(context);

        return feed;
    }

    private static Dictionary<string, byte[]> Files() => new()
    {
        ["scripts/mod_textures.txt"] = Encoding.UTF8.GetBytes("""
            "sprites/640_hud"
            {
                TextureFileRefs { "dfile" { "prefix" "d_" } "dnegfile" { "prefix" "dneg_" } }
                TextureData
                {
                    "scattergun" { "dfile" "HUD/d_images" "dnegfile" "HUD/dneg_images" "x" "96" "y" "192" "width" "96" "height" "32" }
                    "skull_tf" { "dfile" "HUD/d_images" "x" "0" "y" "0" "width" "32" "height" "32" }
                    "crit" { "dfile" "HUD/d_images" "x" "0" "y" "32" "width" "96" "height" "32" }
                    "leaderboard_dominated" { "file" "HUD/leaderboard_dominated" "x" "0" "y" "0" "width" "32" "height" "32" }
                }
            }
            """),
    };

    private static VguiContext Context(TextRecorder surface, Dictionary<string, byte[]> files)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["Msg_Dominating"] = "is DOMINATING",
            ["DeathMsg_Fall"] = "fell to a clumsy, painful death",
            ["Msg_Captured"] = "captured",
            ["Msg_Captured_Multiple"] = "captured",
        };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = surface,
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
    }
}
