using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudMedicChargeMeter` (game/client/tf/tf_hud_mediccharge.cpp).</summary>
/// <remarks>
/// The viewer had no such element: a medic's ÜberCharge meter was missing from the HUD (2026-10-17, against TF2 on
/// cp_process_f12, where it reads "UBERCHARGE: 0%" bottom right).
/// </remarks>
public sealed class TfHudMedicChargeMeterConformanceTests
{
    private const string Res = """
        "Resource/UI/HudMedicCharge.res"
        {
            "ChargeLabel" { "ControlName" "CExLabel" "labelText" "%charge%" }
            "IndividualChargesLabel" { "ControlName" "CExLabel" "labelText" "%charge_count%" }
            "ChargeMeter" { "ControlName" "ContinuousProgressBar" "wide" "86" "tall" "8" }
            "ChargeMeter1" { "ControlName" "ContinuousProgressBar" "wide" "19" "tall" "8" }
            "ChargeMeter2" { "ControlName" "ContinuousProgressBar" "wide" "19" "tall" "8" }
            "ChargeMeter3" { "ControlName" "ContinuousProgressBar" "wide" "19" "tall" "8" }
            "ChargeMeter4" { "ControlName" "ContinuousProgressBar" "wide" "19" "tall" "8" }
        }
        """;

    private const int Medic = 5;
    private const int MedigunSlot = 40;
    private const int BonesawSlot = 41;
    private const int SyringeSlot = 42;

    [Test]
    public void ShouldDraw_AMedicHoldingTheMedigun_IsDrawn() =>
        ((IHudElement)Built().Meter).ShouldDraw(State(active: MedigunSlot)).ShouldBeTrue("the control for the refusals below");

    [Test]
    public void ShouldDraw_AMedicHoldingTheBonesaw_IsDrawn() =>
        ((IHudElement)Built().Meter).ShouldDraw(State(active: BonesawSlot)).ShouldBeTrue("TF_WEAPON_BONESAW (:163)");

    [Test]
    public void ShouldDraw_AMedicHoldingAPlainSyringeGun_IsHidden() =>
        ((IHudElement)Built().Meter).ShouldDraw(State(active: SyringeSlot)).ShouldBeFalse("only one with UberChargeAmmoPerShot (:164)");

    [Test]
    public void ShouldDraw_NotAMedic_IsHidden() =>
        ((IHudElement)Built().Meter).ShouldDraw(State(active: MedigunSlot) with { PlayerClass = 3 }).ShouldBeFalse();

    [Test]
    public void ShouldDraw_ADeadMedic_IsHidden() =>
        ((IHudElement)Built().Meter).ShouldDraw(State(active: MedigunSlot) with { Alive = false }).ShouldBeFalse();

    /// <remarks>`SetProgress( flCharge )` and `SetDialogVariable( "charge", (int)( flCharge * 100 ) )` (:257-258).</remarks>
    [Test]
    public void Think_AStandardMedigunAt62Point5Percent_FillsTheBarAndSays62()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport) = Built();

        meter.Think(State(active: MedigunSlot, charge: 0.625f), viewport);

        ((VguiContinuousProgressBar)meter.FindChildByName("ChargeMeter")!).Progress.ShouldBe(0.625f);
        ((VguiLabel)meter.FindChildByName("ChargeLabel")!).Text.ShouldBe("62");
        (meter.FindChildByName("ChargeMeter")!.Visible, meter.FindChildByName("ChargeMeter1")!.Visible).ShouldBe((true, false));
    }

    /// <remarks>
    /// The Vaccinator, `set_weapon_mode` 3 (`MEDIGUN_RESIST`): four chunks of a quarter each, a part-full one dimmed to 190
    /// grey, the count of full ones in `charge_count` (:231-253), and the chunks shown in place of the bar (:331-344).
    /// </remarks>
    [Test]
    public void Think_TheVaccinatorAt62Point5Percent_FillsTwoAndAHalfChunks()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport) = Built(mode: 3f);

        meter.Think(State(active: MedigunSlot, charge: 0.625f), viewport);

        float[] progress = new float[4];

        for (int chunk = 0; chunk < progress.Length; chunk++)
        {
            progress[chunk] = ((VguiContinuousProgressBar)meter.FindChildByName($"ChargeMeter{chunk + 1}")!).Progress;
        }

        progress.ShouldBe([1f, 1f, 0.5f, 0f], 1e-6f);
        meter.FindChildByName("ChargeMeter3")!.FgColor.ShouldBe(((byte)190, (byte)190, (byte)190, (byte)255));
        meter.FindChildByName("ChargeMeter1")!.FgColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
        ((VguiLabel)meter.FindChildByName("IndividualChargesLabel")!).Text.ShouldBe("2");
        (meter.FindChildByName("ChargeMeter")!.Visible, meter.FindChildByName("ChargeMeter1")!.Visible).ShouldBe((false, true));
    }

    private static HudState State(int active, float charge = 0f)
    {
        EconAttributeWire none = new([], [], HasValidItemId: false);
        ScenePlayer player = new(1, 0f, 0f, 0f, Team: 3, Health: 150, PlayerClass: Medic, ActiveWeapon: active)
        {
            Items =
            [
                new SceneItem(MedigunSlot, "CWeaponMedigun", 29, none, IsWeapon: true) { ChargeLevel = charge },
                new SceneItem(BonesawSlot, "CTFBonesaw", 8, none, IsWeapon: true),
                new SceneItem(SyringeSlot, "CTFSyringeGun", 17, none, IsWeapon: true),
            ],
        };

        return new HudState(true, true, 0, 150, true, 150, 225, CurTime: 1f, PlayerClass: Medic, LocalIndex: 1, Players: [player], ActiveWeapon: active);
    }

    private static (TfHudMedicChargeMeter Meter, HudViewport Viewport) Built(float mode = 0f)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new() { ["resource/UI/HudMedicCharge.res"] = Encoding.UTF8.GetBytes(Res) };
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
        };
        HudViewport viewport = new()
        {
            WeaponAttribute = (_, item, name, value) => name == "set_weapon_mode" && item.ClassName == "CWeaponMedigun" ? mode : value,
        };
        TfHudMedicChargeMeter meter = new(viewport);

        meter.PerformApplySchemeSettings(context);

        return (meter, viewport);
    }
}
