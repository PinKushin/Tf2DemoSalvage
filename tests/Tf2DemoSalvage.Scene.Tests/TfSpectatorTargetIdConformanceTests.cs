using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CSpectatorTargetID` over `CTargetID` (game/client/tf/tf_hud_target_id.cpp).</summary>
/// <remarks>
/// The local player is a spectator (team 1, entity 1). Player 2 "Blue" is a BLU soldier; 3 "Doc" a RED medic at half charge;
/// 4 "Sneak" a RED spy disguised as a BLU scout. A 640 × 480 screen.
/// </remarks>
public sealed class TfSpectatorTargetIdConformanceTests
{
    [Test]
    public void ShouldDraw_InEye_NamesTheTarget()
    {
        TfSpectatorTargetId id = Thought(InEye(2));

        (id.TargetIndex, id.TargetName, id.TargetData).ShouldBe((2, "Blue", string.Empty));
    }

    [Test]
    public void ShouldDraw_Chasing_NamesTheTargetToo() =>
        Thought(InEye(2) with { ObserverMode = ObserverModes.Chase }).TargetName.ShouldBe("Blue");

    [Test]
    public void ShouldDraw_NotObserving_IsHidden()
    {
        TfSpectatorTargetId id = Built();

        id.ShouldDraw(InEye(2) with { ObserverMode = ObserverModes.None }).ShouldBeFalse();
        id.ShouldDraw(InEye(2) with { ObserverMode = ObserverModes.FreezeCam }).ShouldBeFalse();
        id.ShouldDraw(InEye(2)).ShouldBeTrue("the control");
    }

    [Test]
    public void ShouldDraw_AMedic_ShowsTheCharge() =>
        Thought(InEye(3)).TargetData.ShouldBe("Uber: 50%");

    [Test]
    public void ShouldDraw_ADisguisedSpy_ShowsTheDisguiseToASpectator() =>
        Thought(InEye(4)).TargetData.ShouldBe("Disguised as enemy Scout");

    [Test]
    public void ShouldDraw_AKillStreak_ShowsTheCount() =>
        Thought(InEye(2) with { Players = [.. Players(), Soldier() with { EntityIndex = 5, KillStreak = 7 }] } with { ObserverTarget = 5 })
            .TargetData.ShouldBe("Streak 7");

    [Test]
    public void PerformLayout_ARedTarget_ShowsTheRedBackground()
    {
        TfSpectatorTargetId id = Thought(InEye(3));

        VguiLayout.SolveTraverse(id.Parent!, ((HudViewport)id.Parent!).Context!);

        id.FindChildByName("TargetIDBG_Spec_Red")!.Visible.ShouldBeTrue();
        id.FindChildByName("TargetIDBG_Spec_Blue")!.Visible.ShouldBeFalse();
    }

    private static HudState InEye(int target) =>
        new(true, true, 0, 0, false, CurTime: 1f, Team: 1, ObserverMode: ObserverModes.InEye, LocalIndex: 1, ObserverTarget: target,
            Players: Players(), Names: new Dictionary<int, string> { [2] = "Blue", [3] = "Doc", [4] = "Sneak", [5] = "Streaker" });

    private static ScenePlayer Soldier() => new(2, 0f, 0f, 0f, 3, 200, 3) { EntityHealth = 200, MaxHealth = 200, MaxHealthForBuffing = 200 };

    private static List<ScenePlayer> Players() =>
    [
        new(1, 0f, 0f, 0f, 1, 0, 0),
        Soldier(),
        new(3, 0f, 0f, 0f, 2, 150, 5) { EntityHealth = 150, MaxHealth = 150, Medigun = (0.5f, 0, 29) },
        new(4, 0f, 0f, 0f, 2, 125, 8, Conditions: new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0), DisguiseClass: 1, DisguiseTeam: 3)
        {
            EntityHealth = 125,
            MaxHealth = 125,
        },
    ];

    private static TfSpectatorTargetId Thought(HudState state)
    {
        TfSpectatorTargetId id = Built();

        // `CHud::Think`: the element's `ShouldDraw`, and its visibility set from it.
        ((HudViewport)id.Parent!).Think(state);
        id.Visible.ShouldBeTrue();
        return id;
    }

    private static TfSpectatorTargetId Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/TargetID.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/TargetID.res"
                {
                    "TargetIDBG" { "ControlName" "CTFImagePanel" "fieldName" "TargetIDBG" "wide" "100" "tall" "39" }
                    "TargetIDBG_Spec_Blue" { "ControlName" "ScalableImagePanel" "fieldName" "TargetIDBG_Spec_Blue" "wide" "100" "tall" "39" "visible" "0" }
                    "TargetIDBG_Spec_Red" { "ControlName" "ScalableImagePanel" "fieldName" "TargetIDBG_Spec_Red" "wide" "100" "tall" "39" "visible" "0" }
                    "TargetNameLabel" { "ControlName" "Label" "fieldName" "TargetNameLabel" "wide" "720" "tall" "27" "labelText" "%targetname%" }
                    "TargetDataLabel" { "ControlName" "Label" "fieldName" "TargetDataLabel" "wide" "315" "tall" "17" "labelText" "%targetdata%" }
                    "KillStreakIcon" { "ControlName" "ImagePanel" "fieldName" "KillStreakIcon" "visible" "0" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_sameteam"] = "%s1%s2",
            ["TF_playerid_mediccharge"] = "Uber: %s1%",
            ["TF_playerid_friendlyspy_disguise"] = "Disguised as %s1 %s2",
            ["TF_playerid_ammo"] = "Streak %s1",
            ["TF_enemy"] = "enemy",
            ["TF_friendly"] = "friendly",
            ["TF_Class_Name_Scout"] = "Scout",
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
        TfSpectatorTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);

        // Not observing, so the element has no earlier target to fall back on.
        viewport.Think(InEye(2) with { ObserverMode = ObserverModes.None, ObserverTarget = 0 });

        return id;
    }
}
