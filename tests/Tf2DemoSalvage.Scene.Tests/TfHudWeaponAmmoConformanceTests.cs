using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudWeaponAmmo` (game/client/tf/tf_hud_ammostatus.cpp).</summary>
/// <remarks>
/// The synthetic `HudAmmoWeapons.res` places the low-ammo image at 10,10 20×20. A clip weapon shows `%Ammo%` (clip) and
/// `%AmmoInReserve%`; a clipless one shows `%Ammo%` (the reserve) on the no-clip label. Low ammo is under round(40% of
/// max ammo plus max clip), grown by (threshold − total) / threshold × 5.
/// </remarks>
public sealed class TfHudWeaponAmmoConformanceTests
{
    private const string Res = """
        "Resource/UI/HudAmmoWeapons.res"
        {
            "HudWeaponLowAmmoImage" { "ControlName" "ImagePanel" "xpos" "10" "ypos" "10" "wide" "20" "tall" "20" "visible" "0" }
            "AmmoInClip" { "ControlName" "CExLabel" "visible" "0" "labelText" "%Ammo%" }
            "AmmoInClipShadow" { "ControlName" "CExLabel" "visible" "0" "labelText" "%Ammo%" }
            "AmmoInReserve" { "ControlName" "CExLabel" "visible" "0" "labelText" "%AmmoInReserve%" }
            "AmmoInReserveShadow" { "ControlName" "CExLabel" "visible" "0" "labelText" "%AmmoInReserve%" }
            "AmmoNoClip" { "ControlName" "CExLabel" "visible" "0" "labelText" "%Ammo%" }
            "AmmoNoClipShadow" { "ControlName" "CExLabel" "visible" "0" "labelText" "%Ammo%" }
        }
        """;

    private static readonly TfAmmoState Scattergun = new(true, true, true, true, Clip1: 6, Reserve: 32, MaxAmmo: 32, MaxClip1: 6);

    [Test]
    public void Think_AClipWeapon_ShowsClipAndReserve()
    {
        TfHudWeaponAmmo ammo = Built();

        ammo.Think(State(Scattergun), weapon: 5);

        Label(ammo, "AmmoInClip").Text.ShouldBe("6");
        Label(ammo, "AmmoInReserve").Text.ShouldBe("32");
        (Label(ammo, "AmmoInClip").Visible, Label(ammo, "AmmoInClipShadow").Visible, Label(ammo, "AmmoNoClip").Visible).ShouldBe((true, true, false));
    }

    [Test]
    public void Think_AClipless_ShowsTheReserveAlone()
    {
        TfHudWeaponAmmo ammo = Built();

        ammo.Think(State(Scattergun with { UsesClips = false, Clip1 = -1, Reserve = 150 }), weapon: 5);

        Label(ammo, "AmmoNoClip").Text.ShouldBe("150");
        (Label(ammo, "AmmoInClip").Visible, Label(ammo, "AmmoNoClip").Visible).ShouldBe((false, true));
    }

    [Test]
    public void Think_UnderFortyPercent_ShowsAndGrowsTheRedWarning()
    {
        TfHudWeaponAmmo ammo = Built();

        // (6 + 32) × 0.4 = 15.2 → 15; total 3 + 5 = 8; (15.2 − 8) / 15.2 × 5 = 2.37 → 2.
        ammo.Think(State(Scattergun with { Clip1 = 3, Reserve = 5 }), weapon: 5);

        VguiImagePanel image = (VguiImagePanel)ammo.FindChildByName("HudWeaponLowAmmoImage")!;
        (image.Visible, image.X, image.Wide, image.FgColor).ShouldBe((true, 8, 24, ((byte)255, (byte)0, (byte)0, (byte)255)));

        ammo.Think(State(Scattergun) with { CurTime = 2f }, weapon: 5);
        (image.Visible, image.X, image.Wide).ShouldBe((false, 10, 20), "back to its own bounds when hidden");
    }

    [Test]
    public void Think_WithinATenthOfASecond_DoesNotReadAgain()
    {
        TfHudWeaponAmmo ammo = Built();

        ammo.Think(State(Scattergun), weapon: 5);
        ammo.Think(State(Scattergun with { Clip1 = 5 }) with { CurTime = 1.05f }, weapon: 5);

        Label(ammo, "AmmoInClip").Text.ShouldBe("6");
    }

    [Test]
    public void ShouldDraw_NotShown_IsHidden() =>
        ((IHudElement)Built()).ShouldDraw(State(Scattergun with { Shown = false })).ShouldBeFalse();

    private static HudState State(TfAmmoState ammo) => new(true, true, 0, 125, true, 125, 185, CurTime: 1f, Ammo: ammo, ActiveWeapon: 5);

    private static VguiLabel Label(VguiPanel panel, string name) => (VguiLabel)panel.FindChildByName(name)!;

    private static TfHudWeaponAmmo Built()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new() { ["resource/UI/HudAmmoWeapons.res"] = Encoding.UTF8.GetBytes(Res) };
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
        };
        TfHudWeaponAmmo ammo = new(new HudViewport());

        ammo.PerformApplySchemeSettings(context);

        return ammo;
    }
}
