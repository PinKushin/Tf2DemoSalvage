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

    [Test]
    public void ShouldDraw_SpectatingInEye_Draws()
    {
        (TfHudCrosshair crosshair, _) = Painted(Alive("CTFRocketLauncher"));

        crosshair.ShouldDraw(Alive("CTFRocketLauncher") with { Alive = false, ObserverMode = ObserverModes.InEye }).ShouldBeTrue();
    }

    private static HudState Alive(string weapon) =>
        new(true, true, 0, 200, true, CurTime: 1f, Team: 2, WeaponClass: weapon, PlayerClass: 3);

    private static (TfHudCrosshair Crosshair, TextRecorder Surface) Painted(HudState state, CrosshairSettings? settings = null)
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
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

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
