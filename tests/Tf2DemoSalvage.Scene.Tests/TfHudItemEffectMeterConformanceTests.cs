using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudItemEffectMeter`, its manager and its weapon meters (game/client/tf/tf_hud_itemeffectmeter.cpp).</summary>
/// <remarks>
/// A synthetic `HudItemEffectMeter.res` in the shipped file's shape, at 640×480 so a proportional value is itself; the
/// rocket pack's has the second bar. The local player is entity 1.
/// </remarks>
public sealed class TfHudItemEffectMeterConformanceTests
{
    private const int Spy = 8;
    private const int Soldier = 3;
    private const int Pyro = 7;

    private const string MeterRes = """
        "Resource/UI/HudItemEffectMeter.res"
        {
            "HudItemEffectMeter" { "fieldName" "HudItemEffectMeter" "xpos" "100" "ypos" "400" "wide" "137" "tall" "10" "x_offset" "20" }
            "ItemEffectMeterLabel" { "ControlName" "CExLabel" "fieldName" "ItemEffectMeterLabel" "xpos" "17" "wide" "104" "tall" "6" }
            "ItemEffectMeter" { "ControlName" "ContinuousProgressBar" "fieldName" "ItemEffectMeter" "xpos" "17" "wide" "104" "tall" "6" }
            "ItemEffectIcon" { "ControlName" "CTFImagePanel" "fieldName" "ItemEffectIcon" "wide" "10" "tall" "10" }
        }
        """;

    private const string RocketPackRes = """
        "Resource/UI/HudRocketPack.res"
        {
            "HudItemEffectMeter" { "fieldName" "HudItemEffectMeter" "xpos" "100" "ypos" "400" "wide" "137" "tall" "10" }
            "ItemEffectMeter" { "ControlName" "ContinuousProgressBar" "fieldName" "ItemEffectMeter" "wide" "50" "tall" "6" }
            "ItemEffectMeter2" { "ControlName" "ContinuousProgressBar" "fieldName" "ItemEffectMeter2" "xpos" "52" "wide" "50" "tall" "6" }
        }
        """;

    [Test]
    public void Paint_AGainSinceThePrevious_DrawsThePreviousThenTheGainInGreen()
    {
        VguiContinuousProgressBar bar = new(null, "Bar") { Wide = 100, Tall = 10, FgColor = (1, 2, 3, 4) };
        TextRecorder surface = new();

        bar.SetProgress(0.6f);
        bar.SetPrevProgress(0.25f);
        bar.Paint(surface, Context());

        surface.Calls.ShouldBe(["color 1 2 3 4", "fill 0 0 25 10", "color 100 255 100 255", "fill 25 0 60 10"]);
    }

    [Test]
    public void Paint_ALossSinceThePrevious_DrawsTheLossInRedUnderTheBar()
    {
        VguiContinuousProgressBar bar = new(null, "Bar") { Wide = 100, Tall = 10, FgColor = (1, 2, 3, 4) };
        TextRecorder surface = new();

        bar.SetProgress(0.25f);
        bar.SetPrevProgress(0.6f);
        bar.Paint(surface, Context());

        surface.Calls.ShouldBe(["color 1 2 3 4", "color 200 45 45 255", "fill 25 0 60 10", "color 1 2 3 4", "fill 0 0 25 10"]);
    }

    [Test]
    public void Paint_NoPrevious_DrawsTheBarAlone()
    {
        VguiContinuousProgressBar bar = new(null, "Bar") { Wide = 100, Tall = 10, FgColor = (1, 2, 3, 4) };
        TextRecorder surface = new();

        bar.SetProgress(1.5f);
        bar.Paint(surface, Context());

        surface.Calls.ShouldBe(["color 1 2 3 4", "color 1 2 3 4", "fill 0 0 100 10"], "SetProgress clamps to 1 (ProgressBar.cpp:153)");
    }

    [Test]
    public void OnDialogVariablesChanged_ItsVariable_SetsTheProgressAsAPercent()
    {
        VguiProgressBar bar = new(null, "Bar");
        bar.ApplySettings(KeyValuesTree.Load(Encoding.UTF8.GetBytes("Bar { variable \"charge\" }"), "bar", _ => null), Context());

        bar.OnDialogVariablesChanged(new Dictionary<string, string> { ["charge"] = "45" });
        bar.Progress.ShouldBe(0.45f);

        bar.OnDialogVariablesChanged(new Dictionary<string, string> { ["charge"] = "-3" });
        bar.Progress.ShouldBe(0.45f, "a negative value is ignored (ProgressBar.cpp:387)");
    }

    [Test]
    public void Replace_BindingsAndDoublePercent_AreTheUpperCasedKeysAndOnePercent()
    {
        string replaced = TfKeyBindings.Replace(
            "Hit %+attack2% then %reload% at 100%%", command => command == "attack2" ? "mouse2" : null, _ => null);

        replaced.ShouldBe("Hit MOUSE2 then < NOT BOUND > at 100%", "the key is Q_strupr'd, an unbound one too (cdll_util.cpp:917-942)");
    }

    [Test]
    public void Replace_ALocalisedKeyName_UsesIt() =>
        TfKeyBindings.Replace("%+jump%", _ => "space", key => key == "SPACE" ? "Spacebar" : null).ShouldBe("Spacebar");

    [Test]
    public void SetPlayer_ASpy_MakesItsMetersThenEveryClasssHeadFirst()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        ScenePlayer spy = Local(Spy);

        viewport.Think(State(spy));
        manager.SetPlayer(spy);

        manager.Meters.Select(meter => meter.GetType().Name).ShouldBe(
        [
            nameof(TfItemEffectMeterRune), nameof(TfItemEffectMeterPowerupBottle), nameof(TfItemEffectMeterSpellBook),
            nameof(TfItemEffectMeterKillStreak), nameof(TfItemEffectMeterThrowable), nameof(TfItemEffectMeterRevolver),
            nameof(TfItemEffectMeterBuilder), nameof(TfHudItemEffectMeter), nameof(TfItemEffectMeterKnife),
        ]);
        manager.Meters.ShouldAllBe(meter => !meter.Visible && meter.Parent == viewport);
    }

    [Test]
    public void SetPlayer_Again_RemovesTheOldMetersFromTheViewport()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        ScenePlayer spy = Local(Spy);
        viewport.Think(State(spy));
        manager.SetPlayer(spy);
        TfHudItemEffectMeter old = manager.Meters[0];

        manager.SetPlayer(spy);

        old.Parent.ShouldBeNull();
        viewport.Children.Count(child => child is TfHudItemEffectMeter).ShouldBe(9);
    }

    [Test]
    public void HandleGameEvent_BeforeAnySetPlayer_IsNotListenedFor()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        HudState state = State(Local(Spy));
        viewport.Think(state);

        manager.HandleGameEvent(Event("localplayer_pickup_weapon"), state);
        manager.Meters.ShouldBeEmpty("SetPlayer is what listens (:125-129)");

        manager.SetPlayer(null);
        manager.HandleGameEvent(Event("localplayer_pickup_weapon"), state);
        manager.Meters.Count.ShouldBe(9);
    }

    [Test]
    public void Update_TheCloakMeter_FillsTheBarAndLabelsIt()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, VguiContext context) = Built();
        ScenePlayer spy = Local(Spy) with { CloakMeter = 37.5f };
        viewport.Think(State(spy));
        manager.SetPlayer(spy);
        TfHudItemEffectMeter cloak = manager.Meters.Single(meter => meter.GetType() == typeof(TfHudItemEffectMeter));

        cloak.ApplySchemeSettings(context);
        manager.Update(spy);

        cloak.ProgressBars.ShouldHaveSingleItem().Progress.ShouldBe(0.375f);
        cloak.Label.Text.ShouldBe("CLOAK");
        cloak.Label.FgColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Test]
    public void SetLabelText_AFeignDeathWatch_ReplacesTheBindingInItsLabel()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, VguiContext context) = Built();
        viewport.WeaponAttribute = (_, item, name, value) => item.ClassName == "CTFWeaponInvis" && name == "set_weapon_mode" ? 1f : value;
        ScenePlayer spy = Local(Spy) with { Items = [Weapon(40, "CTFWeaponInvis")] };
        viewport.Think(State(spy) with { KeyLookupBinding = command => command == "attack2" ? "mouse2" : null });
        manager.SetPlayer(spy);
        TfHudItemEffectMeter cloak = manager.Meters.Single(meter => meter.GetType() == typeof(TfHudItemEffectMeter));

        cloak.ApplySchemeSettings(context);

        cloak.Label.Text.ShouldBe("FEIGN MOUSE2");
    }

    [Test]
    public void Update_AFeignDeathWatchRefilling_BeepsOnceOnReachingFull()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        List<string> sounds = [];
        manager.SoundEmitter = sounds.Add;
        viewport.WeaponAttribute = (_, item, name, value) => item.ClassName == "CTFWeaponInvis" && name == "set_weapon_mode" ? 1f : value;
        ScenePlayer spy = Local(Spy) with { Items = [Weapon(40, "CTFWeaponInvis")], CloakMeter = 50f };
        viewport.Think(State(spy));
        manager.SetPlayer(spy);

        manager.Update(spy);
        sounds.ShouldBeEmpty("m_flOldProgress starts at 1, so the first frame at half does not beep");

        ScenePlayer full = spy with { CloakMeter = 100f };
        viewport.Think(State(full));
        manager.Update(full);
        manager.Update(full);

        sounds.ShouldBe(["TFPlayer.ReCharged"]);
    }

    [Test]
    public void Update_AFullBanner_FlashesTheBarRedByRealTime()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, VguiContext context) = Built();
        viewport.WeaponAttribute = (_, _, name, value) => name == "set_buff_type" ? 1f : value;
        ScenePlayer soldier = Local(Soldier) with { Items = [Weapon(41, "CTFBuffItem")], RageMeter = 100f };
        viewport.Think(State(soldier) with { RealTime = 3.47f });
        manager.SetPlayer(soldier);
        TfHudItemEffectMeter banner = manager.Meters.OfType<TfItemEffectMeterBuffItem>().Single();

        banner.ApplySchemeSettings(context);
        manager.Update(soldier);

        // `( (int)( realtime * 10 ) ) % 10` is 4, so red is 160 + 40 (:531-533).
        banner.ProgressBars.ShouldHaveSingleItem().FgColor.ShouldBe(((byte)200, (byte)0, (byte)0, (byte)255));
        banner.ProgressBars[0].Progress.ShouldBe(1f);
    }

    [TestCase(3, true)]
    [TestCase(0, false)]
    public void Progress_AMeltedSpycicle_IsTheTimeSinceTheMeltOverTheRegeneration(int knifeMode, bool enabled)
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        viewport.WeaponAttribute = (_, _, name, value) => name == "set_weapon_mode" ? knifeMode : value;
        SceneItem knife = Weapon(42, "CTFKnife") with { KnifeExists = false, KnifeMeltTimestamp = 10f, KnifeRegenerateDuration = 15.5f };
        ScenePlayer spy = Local(Spy) with { Items = [knife] };
        viewport.Think(State(spy) with { ServerTime = 17.75f });
        manager.SetPlayer(spy);
        TfItemEffectMeterKnife meter = manager.Meters.OfType<TfItemEffectMeterKnife>().Single();

        meter.Progress().ShouldBe(0.5f);
        meter.IsEnabled().ShouldBe(enabled, "only KNIFE_ICICLE (3) shows it (:1238)");
    }

    [TestCase(75f, 1f, 0.5f, 255)]
    [TestCase(30f, 0.6f, 0f, 0)]
    public void Update_TheRocketPack_SplitsItsChargeAcrossTwoBars(float charge, float first, float second, int green)
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, VguiContext context) = Built();
        ScenePlayer pyro = Local(Pyro) with
        {
            Items = [Weapon(43, "CTFRocketPack") with { RocketPackEnabled = true }],
            ItemChargeMeter = [0f, charge],
        };
        viewport.Think(State(pyro));
        manager.SetPlayer(pyro);
        TfItemEffectMeterRocketPack pack = manager.Meters.OfType<TfItemEffectMeterRocketPack>().Single();

        pack.ApplySchemeSettings(context);
        manager.Update(pyro);

        pack.ProgressBars.Select(bar => bar.Progress).ShouldBe([first, second]);
        pack.ProgressBars[0].FgColor.ShouldBe(((byte)255, (byte)green, (byte)green, (byte)255), "red until IsRocketPackReady, 50 (tf_player_shared.h:477)");
        pack.IconName().ShouldBe("../hud/pyro_jetpack");
        pack.LabelText().ShouldBe("#TF_RocketPack_Charges");
    }

    [Test]
    public void Update_TheBonusRevolver_CountsHeadsCappedWithAPercent()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        viewport.WeaponAttribute = (_, _, name, value) => name == "extra_damage_on_hit" ? 1f : value;
        ScenePlayer spy = Local(Spy) with { Items = [Weapon(44, "CTFRevolver")], Decapitations = 250 };
        viewport.Think(State(spy));
        manager.SetPlayer(spy);
        TfItemEffectMeterRevolver revolver = manager.Meters.OfType<TfItemEffectMeterRevolver>().Single();

        manager.Update(spy);

        revolver.DialogVariable("progresscount").ShouldBe("200%", "Min( 200, decapitations ) (tf_weapon_revolver.cpp:245)");
        revolver.LabelText().ShouldBe("#TF_BONUS");
    }

    [Test]
    public void Weapon_AStockRocketLauncherFirst_LeavesTheAirStrikeMeterWithout()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        ScenePlayer soldier = Local(Soldier) with
        {
            Items = [Weapon(45, "CTFRocketLauncher"), Weapon(46, "CTFRocketLauncher_AirStrike")],
            Decapitations = 4,
        };
        viewport.Think(State(soldier));
        manager.SetPlayer(soldier);
        TfItemEffectMeterAirStrike meter = manager.Meters.OfType<TfItemEffectMeterAirStrike>().Single();

        meter.Weapon().ShouldBeNull("Weapon_OwnsThisID answers the first TF_WEAPON_ROCKETLAUNCHER, which is no Air Strike");
        meter.IsEnabled().ShouldBeFalse();
    }

    [TestCase(true, 0, true)]
    [TestCase(false, 0, false)]
    [TestCase(true, 1 << 13, false)]
    public void ShouldDraw_TheCloakMeter_FollowsLifeAndTheHiddenBits(bool alive, int hideHud, bool drawn)
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        ScenePlayer spy = Local(Spy) with { LifeState = alive ? 0 : 2 };
        HudState state = State(spy) with { HideHud = hideHud };
        viewport.Think(state);
        manager.SetPlayer(spy);
        TfHudItemEffectMeter cloak = manager.Meters.Single(meter => meter.GetType() == typeof(TfHudItemEffectMeter));

        cloak.ShouldDraw(state).ShouldBe(drawn);
        cloak.Visible.ShouldBe(drawn);
    }

    [Test]
    public void PerformLayout_TwoMetersEnabled_SlidesLeftByTheOffset()
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, VguiContext context) = Built();
        viewport.WeaponAttribute = (_, _, name, value) => name == "set_weapon_mode" ? 3f : value;
        ScenePlayer spy = Local(Spy) with { Items = [Weapon(42, "CTFKnife")] };
        viewport.Think(State(spy));
        manager.SetPlayer(spy);
        TfHudItemEffectMeter cloak = manager.Meters.Single(meter => meter.GetType() == typeof(TfHudItemEffectMeter));

        manager.NumEnabled().ShouldBe(2, "the cloak and the Spy-cicle");
        cloak.ApplySchemeSettings(context);
        cloak.X.ShouldBe(100);
        cloak.Visible = true;
        cloak.Think();

        cloak.X.ShouldBe(80, "SetPos( xPos - m_iXOffset ) (:452)");
    }

    [TestCase(10.099f, null)]
    [TestCase(10.1f, 51)]
    public void OnThink_ALostItem_IsLookedForAgainOnlyOnTheHundredMillisecondTick(float realTime, int? found)
    {
        (HudViewport viewport, TfItemEffectMeterManager manager, _) = Built();
        viewport.Items = ItemSchema.Read(Encoding.UTF8.GetBytes("""
            "items_game" { "items" { "7" { "item_slot" "secondary" "used_by_classes" { "heavy" "1" } } } }
            """));
        SceneItem Worn(int entity) => new(entity, "CTFWearable", 7, new EconAttributeWire([], [], false), IsWeapon: false);
        ScenePlayer heavy = Local(6) with { Items = [Worn(50)] };
        viewport.Think(State(heavy) with { RealTime = 10f });
        TfItemAttributeEffectMeter meter = new(manager, heavy, 1, "#TF_SecondaryMeter", beeps: true);
        meter.Item().ShouldNotBeNull().EntityIndex.ShouldBe(50);

        // Entity 50 is gone and 51 is worn in its slot; `AddTickSignal( GetVPanel(), 100 )` (:1661) asks again at 10.1 s.
        ScenePlayer swapped = heavy with { Items = [Worn(51)] };
        viewport.Think(State(swapped) with { RealTime = realTime });
        meter.Think();

        (meter.Item()?.EntityIndex).ShouldBe(found);
    }

    private static SceneItem Weapon(int entity, string className) => new(entity, className, 1, new EconAttributeWire([], [], false), IsWeapon: true);

    private static ScenePlayer Local(int playerClass) => new(1, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: playerClass, LifeState: 0);

    private static HudState State(ScenePlayer local) =>
        new(true, true, 0, 100, local.IsAlive, 100, 150, 1f, 2, LocalIndex: 1, Players: [local], ServerTime: 1f);

    private static SceneGameEvent Event(string name) => new(0, name, new Dictionary<string, object?>(), new Dictionary<int, Core.Net.PlayerInfo>());

    private static (HudViewport Viewport, TfItemEffectMeterManager Manager, VguiContext Context) Built()
    {
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        return (viewport, new TfItemEffectMeterManager(viewport), context);
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    Colors { }
                    BaseSettings { }
                    Borders { }
                    Fonts { "Default" { "1" { "name" "Arial" "tall" "12" } } }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiSchemeFonts fonts = VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), _ => null, "english", 480);
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudItemEffectMeter.res"] = Encoding.UTF8.GetBytes(MeterRes),
            ["resource/UI/HudRocketPack.res"] = Encoding.UTF8.GetBytes(RocketPackRes),
        };
        Dictionary<string, string> strings = new() { ["TF_CLOAK"] = "CLOAK", ["TF_Feign"] = "FEIGN %+attack2%" };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english", fonts)
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
    }
}
