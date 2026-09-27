using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CSecondaryTargetID` (tf_hud_target_id.cpp:1126-1196), and `CTargetID::CalculateTargetIndex`'s subtraction of it (:702).</summary>
/// <remarks>The local player is entity 1 "Me", RED, not observing. 2 "Mate" is a RED soldier; 3 and 4 are RED medics.</remarks>
public sealed class TfSecondaryTargetIdConformanceTests
{
    [Test]
    public void ShouldDraw_ALocalMedicHealing_NamesTheHealTargetWithTheHealingPrepend()
    {
        (TfSecondaryTargetId secondary, _) = Thought(Local(medic: true) with { ActiveMedigun = (2, 0.5f) });

        (secondary.TargetIndex, secondary.TargetName).ShouldBe((2, "Healing: Mate"));
    }

    [Test]
    public void ShouldDraw_TwoMedicsHealingUs_NamesTheOneWithMoreCharge()
    {
        // `SetHealer` (c_tf_player.cpp:8852): "Show the healer who's got the highest charge level."
        (TfSecondaryTargetId secondary, _) = Thought(
            Local(medic: false),
            Medic(3) with { ActiveMedigun = (1, 0.6f) },
            Medic(4) with { ActiveMedigun = (1, 0.4f) });

        (secondary.TargetIndex, secondary.TargetName).ShouldBe((3, "Healer: Doc3"));
    }

    [Test]
    public void ShouldDraw_ADeadMedicHealingUs_IsNotAHealer() =>
        // `CWeaponMedigun::ClientThink` (tf_weapon_medigun.cpp:2284): a dead firing player sets no healer.
        Built().Secondary.ShouldDraw(State(Local(medic: false), Medic(3) with { ActiveMedigun = (1, 0.6f), LifeState = 2 })).ShouldBeFalse();

    [Test]
    public void ShouldDraw_TheMainTargetIsTheHealTarget_LeavesItToTheSecondary()
    {
        // `CTargetID::CalculateTargetIndex` (:707): "If our target entity is already in our secondary ID, don't show it in primary."
        (_, TfMainTargetId main) = Thought(Local(medic: true) with { ActiveMedigun = (2, 0.5f) }, idTarget: 2);

        main.Visible.ShouldBeFalse();
    }

    [Test]
    public void ShouldDraw_NotHealingOrHealed_IsHidden() =>
        Built().Secondary.ShouldDraw(State(Local(medic: false))).ShouldBeFalse();

    private static ScenePlayer Local(bool medic) => new(1, 0f, 0f, 0f, 2, 150, medic ? 5 : 3);

    private static ScenePlayer Medic(int index) => new(index, 0f, 0f, 0f, 2, 150, 5) { EntityHealth = 150, MaxHealth = 150, MaxHealthForBuffing = 150 };

    private static HudState State(ScenePlayer local, params ScenePlayer[] others) =>
        new(true, true, 0, 150, true, CurTime: 1f, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1, PlayerClass: local.PlayerClass ?? 0,
            Players: [local, new(2, 0f, 0f, 0f, 2, 200, 3) { EntityHealth = 200, MaxHealth = 200, MaxHealthForBuffing = 200 }, .. others],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Mate", [3] = "Doc3", [4] = "Doc4" });

    private static (TfSecondaryTargetId Secondary, TfMainTargetId Main) Thought(ScenePlayer local, params ScenePlayer[] others) =>
        Thought(local, idTarget: null, others);

    private static (TfSecondaryTargetId Secondary, TfMainTargetId Main) Thought(ScenePlayer local, int? idTarget, params ScenePlayer[] others)
    {
        (TfSecondaryTargetId secondary, TfMainTargetId main, HudViewport viewport) = Built();

        viewport.Think(State(local, others) with { IdTarget = idTarget });
        return (secondary, main);
    }

    private static (TfSecondaryTargetId Secondary, TfMainTargetId Main, HudViewport Viewport) Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/TargetID.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/TargetID.res"
                {
                    "TargetNameLabel" { "ControlName" "Label" "fieldName" "TargetNameLabel" "wide" "720" "tall" "27" "labelText" "%targetname%" }
                    "TargetDataLabel" { "ControlName" "Label" "fieldName" "TargetDataLabel" "wide" "315" "tall" "17" "labelText" "%targetdata%" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_sameteam"] = "%s1%s2",
            ["TF_playerid_healtarget"] = "Healing: ",
            ["TF_playerid_healer"] = "Healer: ",
            ["TF_playerid_mediccharge"] = "Uber: %s1%",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };

        // The secondary first, so the main ID reads the index it settled on this frame.
        TfSecondaryTargetId secondary = new(viewport);
        TfMainTargetId main = new(viewport);

        secondary.PerformApplySchemeSettings(context);
        main.PerformApplySchemeSettings(context);

        return (secondary, main, viewport);
    }
}
