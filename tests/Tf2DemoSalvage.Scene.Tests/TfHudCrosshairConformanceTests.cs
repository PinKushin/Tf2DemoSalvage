using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudWeapon` choosing the icon and `CHudTFCrosshair` drawing it (hud_weapon.cpp, hud_crosshair.cpp, tf_hud_crosshair.cpp).</summary>
/// <remarks>
/// A 640 × 480 screen. The rocket launcher's script names a 32 × 32 `crosshair` at (64, 0) of `sprites/crosshairs`; the PDA's
/// names none; `crosshair_default` is (0, 0) of `sprites/default`. Textures are 64 × 64.
/// </remarks>
public sealed class TfHudCrosshairConformanceTests
{
    [Test]
    public void Paint_AWeaponsCrosshair_IsCentredAtItsSizeInTheCvarColour()
    {
        (TfHudCrosshair crosshair, TextRecorder surface) = Painted(Alive("CTFRocketLauncher"));

        crosshair.Crosshair!.TextureFile.ShouldBe("sprites/crosshairs");
        surface.SubRects.ShouldHaveSingleItem().ShouldStartWith("304 224 336 256 ");
        surface.Calls.ShouldContain("color 200 200 200 255");
    }

    [Test]
    public void Paint_AScaleOf64_DoublesIt() =>
        Painted(Alive("CTFRocketLauncher"), new CrosshairSettings(Scale: 64f)).Surface.SubRects.ShouldHaveSingleItem().ShouldStartWith("288 208 352 272 ");

    [Test]
    public void Paint_AWeaponWithoutACrosshairIcon_UsesTheDefault() =>
        Painted(Alive("CTFSomethingUnscripted")).Crosshair.Crosshair!.TextureFile.ShouldBe("sprites/default");

    [Test]
    public void Paint_ACustomFile_DrawsItTwiceItsSizeAboutTheCentre()
    {
        (_, TextRecorder surface) = Painted(Alive("CTFRocketLauncher"), new CrosshairSettings(File: "crosshair3"));

        surface.Calls.ShouldContain("texture vgui/crosshairs/crosshair3");
        surface.Calls.ShouldContain("textured 288 208 352 272");
    }

    [TestCase(false, 0, 0, TestName = "ShouldDraw_Dead_IsHidden")]
    [TestCase(true, 7, 0, TestName = "ShouldDraw_Taunting_IsHidden")]
    public void ShouldDraw_WhenTheGameHidesIt_IsFalse(bool alive, int condition, int unused)
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));
        HudState state = Alive("CTFRocketLauncher") with
        {
            Alive = alive,
            Conditions = condition == 0 ? default : new PlayerConditions(1 << condition, 0, 0, 0, 0),
        };

        crosshair.ShouldDraw(Alive("CTFRocketLauncher")).ShouldBeTrue("the control");
        crosshair.ShouldDraw(state).ShouldBeFalse();
    }

    [Test]
    public void ShouldDraw_APda_IsHidden()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFWeaponPDA_Spy")).ShouldBeFalse();
    }

    /// <remarks>`if ( CTFMinigameLogic::GetMinigameLogic() &amp;&amp; …->GetActiveMinigame() ) return false;` (tf_hud_crosshair.cpp:67).</remarks>
    [Test]
    public void ShouldDraw_InAnActiveMinigame_IsHidden()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Rules = new SceneGameRules(false, 0, false) }).ShouldBeTrue("the control");
        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Rules = new SceneGameRules(false, 0, false) { ActiveMinigame = true } }).ShouldBeFalse();
    }

    /// <remarks>`if ( TFGameRules() &amp;&amp; TFGameRules()->ShowMatchSummary() ) return false;` (tf_hud_crosshair.cpp:70).</remarks>
    [Test]
    public void ShouldDraw_UnderTheMatchSummary_IsHidden()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Rules = new SceneGameRules(false, 0, false) }).ShouldBeTrue("the control");
        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Rules = new SceneGameRules(false, 0, false) { ShowMatchSummary = true } }).ShouldBeFalse();
    }

    /// <remarks>`!( pPlayer->GetFlags() &amp; FL_FROZEN )` (hud_crosshair.cpp:127); `FL_FROZEN` is `1&lt;&lt;6` (const.h:158).</remarks>
    [Test]
    public void ShouldDraw_Frozen_IsHidden()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Flags = 1 << 0 }).ShouldBeTrue("FL_ONGROUND, the control");
        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Flags = (1 << 6) | (1 << 0) }).ShouldBeFalse();
    }

    /// <remarks>
    /// `FireGameEvent` (tf_hud_crosshair.cpp:122-137): in a competitive-mode match a `time` of 1..10 hides it until
    /// `curtime + time`, and `ShouldDraw` tests `m_flTimeToHideUntil &gt; gpGlobals-&gt;curtime` (:84) — strict, so it draws
    /// again at exactly that time. The event fires at 1.0; casual is match group 7, which `IsCompetitiveMode` counts (:2219).
    /// </remarks>
    [TestCase(5.99f, true, TestName = "HandleGameEvent_ACasualCountdownOf5_HidesBeforeItEnds")]
    [TestCase(6f, false, TestName = "HandleGameEvent_ACasualCountdownOf5_DrawsWhenItEnds")]
    public void HandleGameEvent_ACasualCountdownOf5(float curTime, bool hidden) =>
        Hidden(RestartTimer(5, matchGroup: 7), curTime).ShouldBe(hidden);

    /// <remarks>The ladder (2) is `MATCH_TYPE_COMPETITIVE` (tf_match_description_comp.cpp:49).</remarks>
    [Test]
    public void HandleGameEvent_ALadderCountdownOf10_Hides() =>
        Hidden(RestartTimer(10, matchGroup: 2), curTime: 10.5f).ShouldBeTrue();

    /// <remarks>
    /// Outside a match (-1, no description), in MvM (`MATCH_TYPE_MVM`), at 11 or at 0 the event clears the hide to -1 (:137).
    /// </remarks>
    [TestCase(5, -1, TestName = "HandleGameEvent_OutsideAMatch_DoesNotHide")]
    [TestCase(5, 0, TestName = "HandleGameEvent_InMannVsMachine_DoesNotHide")]
    [TestCase(11, 7, TestName = "HandleGameEvent_ElevenSeconds_DoesNotHide")]
    [TestCase(0, 7, TestName = "HandleGameEvent_ZeroSeconds_DoesNotHide")]
    public void HandleGameEvent_NotACompetitiveCountdown_DoesNotHide(int time, int matchGroup) =>
        Hidden(RestartTimer(time, matchGroup), curTime: 2f).ShouldBeFalse();

    [Test]
    public void HandleGameEvent_ALaterCountdownOutOfRange_ClearsTheHide()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.HandleGameEvent(RestartTimer(5, matchGroup: 7));
        crosshair.HandleGameEvent(RestartTimer(11, matchGroup: 7));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { CurTime = 2f }).ShouldBeTrue();
    }

    /// <remarks>
    /// `CTFRevolver::GetWeaponCrosshairScale` (tf_weapon_revolver.cpp:207-223): with `set_weapon_mode` 1 (`CanHeadshot`, .h:49)
    /// `RemapValClamped( t, 1.0, 0.5, 0.75, 2.5 )` of the time since the last shot; `CHudCrosshair::Paint` multiplies the
    /// default 32 × 32 icon by it (hud_crosshair.cpp:252-280). 0.75 → 24 px, 2.5 → 80, t = 0.75 → 1.625 → (int)(52.5) = 52.
    /// </remarks>
    [TestCase(61, 1.0f, "308 228 332 252 ", TestName = "Paint_TheAmbassadorASecondAfterAShot_IsThreeQuarterSize")]
    [TestCase(61, 0.5f, "280 200 360 280 ", TestName = "Paint_TheAmbassadorHalfASecondAfterAShot_IsTwoAndAHalfTimes")]
    [TestCase(61, 0.75f, "294 214 346 266 ", TestName = "Paint_TheAmbassadorBetween_IsRemapped")]
    [TestCase(24, 0.5f, "304 224 336 256 ", TestName = "Paint_TheStockRevolver_IsUnscaled")]
    public void Paint_ARevolver(int definition, float sinceShot, string rect)
    {
        (_, TextRecorder surface) = Painted(Revolver(definition, sinceShot), weaponAttribute: AmbassadorMode);

        surface.SubRects.ShouldHaveSingleItem().ShouldStartWith(rect);
    }

    /// <remarks>`CHudTFCrosshair::Paint` (tf_hud_crosshair.cpp:189-215): the custom file is scaled the same way.</remarks>
    [Test]
    public void Paint_TheAmbassadorWithACustomFile_ScalesIt()
    {
        (_, TextRecorder surface) = Painted(Revolver(61, 1.0f), new CrosshairSettings(File: "crosshair3"), AmbassadorMode);

        surface.Calls.ShouldContain("textured 296 216 344 264");
    }

    [Test]
    public void ShouldDraw_SpectatingInEye_Draws()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Alive = false, ObserverMode = ObserverModes.InEye }).ShouldBeTrue();
    }

    private static HudState Alive(string weapon) =>
        new(true, true, 0, 200, true, CurTime: 1f, Team: 2, WeaponClass: weapon, PlayerClass: 3);

    /// <summary>`restart_timer_time` fired at curtime 1.0 under a match group.</summary>
    private static HudGameEvent RestartTimer(int time, int matchGroup)
    {
        SceneGameEvent restart = new(0, "restart_timer_time", new Dictionary<string, object?> { ["time"] = time }, new Dictionary<int, Core.Net.PlayerInfo>());

        return new HudGameEvent(restart, 1f, [], 1, new SceneGameRules(false, 0, false) { MatchGroup = matchGroup }, "cp_test", 0);
    }

    /// <summary>Whether the crosshair is hidden at a curtime after the event, against the control that it draws before any.</summary>
    private static bool Hidden(HudGameEvent fired, float curTime)
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));
        HudState state = Alive("CTFRocketLauncher") with { CurTime = curTime };

        crosshair.ShouldDraw(state).ShouldBeTrue("the control, before the event");
        crosshair.HandleGameEvent(fired);
        return !crosshair.ShouldDraw(state);
    }

    /// <summary>`set_weapon_mode` 1 on the Ambassador (61), nothing on anything else.</summary>
    private static float AmbassadorMode(ScenePlayer player, SceneItem weapon, string name, float value) =>
        name == "set_weapon_mode" && weapon.DefinitionIndex == 61 ? 1f : value;

    /// <summary>The local player holding a revolver that last fired at 10.0, on a server clock <paramref name="sinceShot"/> later.</summary>
    private static HudState Revolver(int definition, float sinceShot)
    {
        SceneItem revolver = new(30, "CTFRevolver", definition, new EconAttributeWire([], [], false), IsWeapon: true) { LastFireTime = 10f };
        ScenePlayer local = new(1, 0f, 0f, 0f, 2, 125, 8, ActiveWeapon: 30) { Items = [revolver] };

        return Alive("CTFRevolver") with { LocalIndex = 1, Players = [local], ActiveWeapon = 30, ServerTime = 10f + sinceShot, PlayerClass = 8 };
    }

    private static (TfHudCrosshair Crosshair, TextRecorder Surface) Painted(
        HudState state,
        CrosshairSettings? settings = null,
        System.Func<ScenePlayer, SceneItem, string, float, float>? weaponAttribute = null)
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/hud_textures.txt"] = Encoding.UTF8.GetBytes("""
                "sprites/640_hud" { TextureData { "crosshair_default" { "file" "sprites/default" "x" "0" "y" "0" "width" "32" "height" "32" } } }
                """),
            ["scripts/tf_weapon_rocketlauncher.txt"] = Encoding.UTF8.GetBytes("""
                WeaponData
                {
                    "DrawCrosshair" "1"
                    TextureData { "crosshair" { "file" "sprites/crosshairs" "x" "64" "y" "0" "width" "32" "height" "32" } }
                }
                """),
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        TextRecorder surface = new();
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = surface,
            Read = files.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context, WeaponAttribute = weaponAttribute };

        viewport.Icons = HudTextures.Load(context);
        viewport.Scripts = new TfWeaponData(files.GetValueOrDefault);

        TfHudWeapon weapon = new(viewport);
        TfHudCrosshair crosshair = new(viewport) { Settings = settings ?? new CrosshairSettings() };

        weapon.PerformApplySchemeSettings(context);
        crosshair.PerformApplySchemeSettings(context);
        viewport.Think(state);
        weapon.Paint(surface, context);
        surface.SubRects.Clear();
        surface.Calls.Clear();
        crosshair.Paint(surface, context);

        return (crosshair, surface);
    }
}
