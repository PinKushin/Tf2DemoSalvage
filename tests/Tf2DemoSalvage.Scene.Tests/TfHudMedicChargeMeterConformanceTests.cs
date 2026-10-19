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

    /// <remarks>
    /// `if ( flCharge >= 1.0 )` starts `HudMedicCharged` (:263-267) — at exactly 1, which a strict comparison would miss.
    /// Each sequence in the test script sets the charge label's alpha to its own value, so which one ran is readable.
    /// </remarks>
    [Test]
    public void Think_AChargeOfExactlyOne_StartsHudMedicCharged()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context) = Animated();

        meter.Think(State(active: MedigunSlot, charge: 1f), viewport);

        LabelAlpha(meter, viewport, context).ShouldBe(Charged);
    }

    [Test]
    public void Think_BelowAFullCharge_StartsNothing()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context) = Animated();

        meter.Think(State(active: MedigunSlot, charge: 0.99f), viewport);

        LabelAlpha(meter, viewport, context).ShouldBe(Untouched, "the control: no sequence below 1");
    }

    /// <remarks>
    /// `else if ( !pMedigun->IsReleasingCharge() )` (:269-276): once charged, ANY changed charge while not releasing stops
    /// it, with no comparison to 1, so a charge that is still at or above 1 stops it too.
    /// </remarks>
    [TestCase(0.5f)]
    [TestCase(1.5f)]
    public void Think_AChangedChargeNotReleasingAfterCharged_StartsHudMedicChargedStop(float secondCharge)
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context) = Animated();

        meter.Think(State(active: MedigunSlot, charge: 1f), viewport);
        meter.Think(State(active: MedigunSlot, charge: secondCharge), viewport);

        LabelAlpha(meter, viewport, context).ShouldBe(ChargedStop);
    }

    /// <remarks>While the charge drains through a release, the charged sequence keeps running: no `HudMedicChargedStop`.</remarks>
    [Test]
    public void Think_AChargeFallingWhileReleased_KeepsHudMedicCharged()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context) = Animated();

        meter.Think(State(active: MedigunSlot, charge: 1f), viewport);
        meter.Think(State(active: MedigunSlot, charge: 0.5f, releasing: true), viewport);

        LabelAlpha(meter, viewport, context).ShouldBe(Charged);
    }

    /// <remarks>
    /// `if( flProgress &lt; 1.f &amp;&amp; !pMedigun-&gt;IsReleasingCharge() )` dims a part-full chunk (:239-242); during a
    /// release none is dimmed.
    /// </remarks>
    [Test]
    public void Think_TheVaccinatorReleasing_DimsNoChunk()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport) = Built(mode: 3f);

        meter.Think(State(active: MedigunSlot, charge: 0.625f, releasing: true), viewport);

        meter.FindChildByName("ChargeMeter3")!.FgColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    private const float Untouched = 255f;
    private const float Charged = 10f;
    private const float ChargedStop = 20f;

    private static float LabelAlpha(TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context)
    {
        viewport.Animations.UpdateAnimations(10f, context);

        return meter.FindChildByName("ChargeLabel")!.GetFloat("alpha");
    }

    private static (TfHudMedicChargeMeter Meter, HudViewport Viewport, VguiContext Context) Animated()
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, VguiContext context) = BuiltWithContext(mode: 0f);

        viewport.Animations.SetScriptFile(viewport, "scripts/medic.txt", wipeAll: true, context).ShouldBeTrue();

        return (meter, viewport, context);
    }

    private static HudState State(int active, float charge = 0f, bool releasing = false)
    {
        EconAttributeWire none = new([], [], HasValidItemId: false);
        ScenePlayer player = new(1, 0f, 0f, 0f, Team: 3, Health: 150, PlayerClass: Medic, ActiveWeapon: active)
        {
            Items =
            [
                new SceneItem(MedigunSlot, "CWeaponMedigun", 29, none, IsWeapon: true) { ChargeLevel = charge, ChargeRelease = releasing },
                new SceneItem(BonesawSlot, "CTFBonesaw", 8, none, IsWeapon: true),
                new SceneItem(SyringeSlot, "CTFSyringeGun", 17, none, IsWeapon: true),
            ],
        };

        return new HudState(true, true, 0, 150, true, 150, 225, CurTime: 1f, PlayerClass: Medic, LocalIndex: 1, Players: [player], ActiveWeapon: active);
    }

    private static (TfHudMedicChargeMeter Meter, HudViewport Viewport) Built(float mode = 0f)
    {
        (TfHudMedicChargeMeter meter, HudViewport viewport, _) = BuiltWithContext(mode);

        return (meter, viewport);
    }

    private static (TfHudMedicChargeMeter Meter, HudViewport Viewport, VguiContext Context) BuiltWithContext(float mode)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/HudMedicCharge.res"] = Encoding.UTF8.GetBytes(Res),
            ["scripts/medic.txt"] = Encoding.UTF8.GetBytes(
                $"event HudMedicCharged\n{{\n\tAnimate ChargeLabel Alpha {Charged} Linear 0.0 0.0\n}}\n" +
                $"event HudMedicChargedStop\n{{\n\tAnimate ChargeLabel Alpha {ChargedStop} Linear 0.0 0.0\n}}\n"),
        };
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

        return (meter, viewport, context);
    }
}
