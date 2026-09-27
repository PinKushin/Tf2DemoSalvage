using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CFloatingHealthIcon` (tf_hud_target_id.cpp:1386-1563), made and kept by `CTargetID::IsValidIDTarget` (:459-482).</summary>
/// <remarks>
/// The local player is entity 1, a RED soldier, not observing; 2 is a RED teammate at (0, 0, 0) with 100 of 200 health. The
/// world-to-screen matrix maps world X to clip X and world Z to clip Y, both over 100, with w = 1. A 640 × 480 screen.
/// </remarks>
public sealed class TfFloatingHealthIconConformanceTests
{
    [Test]
    public void Think_ATeammateUnderTheCrosshair_ShowsAFloatingIconFromTheSecondFrame()
    {
        // Made hidden on the first pass (its constructor's `SetVisible( false )`); the next pass, retargeting the same entity,
        // shows it (:464-466).
        (TfMainTargetId _, HudViewport viewport) = Built();

        viewport.Think(Playing(realTime: 0f));
        TfFloatingHealthIcon icon = (TfFloatingHealthIcon)viewport.FindChildByName("HealthIcon")!;
        bool first = icon.Visible;

        viewport.Think(Playing(realTime: 0.1f));

        (first, icon.Visible).ShouldBe((false, true));
    }

    [Test]
    public void Think_AfterATick_ShowsTheEntitysOwnHealthAgainstItsMaxWithNoOverheal()
    {
        (TfMainTargetId _, HudViewport viewport) = Built();

        viewport.Think(Playing(realTime: 0f));
        viewport.Think(Playing(realTime: 0.1f));

        ((TfFloatingHealthIcon)viewport.FindChildByName("HealthIcon")!).TargetHealth.HealthImage.Health.ShouldBe(0.5f);
    }

    [Test]
    public void Paint_ATeammateAtTheOrigin_CentresTheIconAboveTheHullTop()
    {
        // `CalculatePosition` (:1510): origin z + VEC_HULL_MAX z (82) + tf_healthicon_height_offset (10) = 92 → clip y 0.92,
        // HUD y = 0.5 × (1 − 0.92) × 480 = 19; x = 320. The 128-square panel sits centred above: (256, 19 − 128).
        (TfMainTargetId _, HudViewport viewport) = Built();

        viewport.Think(Playing(realTime: 0f));
        viewport.Think(Playing(realTime: 0.1f));

        TfFloatingHealthIcon icon = (TfFloatingHealthIcon)viewport.FindChildByName("HealthIcon")!;

        icon.CalculatePosition(viewport.State).ShouldBeTrue();
        (icon.X, icon.Y).ShouldBe((256, -109));
    }

    [Test]
    public void Think_FloatingHealthDisabled_MakesNoIcon()
    {
        (_, HudViewport viewport) = Built();

        HudConVars disabled = TestConVars.Of(("tf_hud_target_id_disable_floating_health", "1"));
        viewport.Think(Playing(realTime: 0f) with { ConVars = disabled });
        viewport.Think(Playing(realTime: 0.1f) with { ConVars = disabled });

        viewport.FindChildByName("HealthIcon").ShouldBeNull();
    }

    [Test]
    public void Think_ANewTarget_DeletesTheOldIcon()
    {
        (TfMainTargetId _, HudViewport viewport) = Built();

        viewport.Think(Playing(realTime: 0f));
        TfFloatingHealthIcon first = (TfFloatingHealthIcon)viewport.FindChildByName("HealthIcon")!;

        viewport.Think(Playing(realTime: 0.1f) with { IdTarget = 3 });

        first.Parent.ShouldBeNull("`MarkForDeletion` when the scanned index changes (:561-565)");
    }

    private static readonly float[] WorldToScreen =
    [
        0.01f, 0f, 0f, 0f,
        0f, 0f, 0f, 0f,
        0f, 0.01f, 0f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private static HudState Playing(float realTime) =>
        new(true, true, 0, 100, true, CurTime: realTime + 1f, RealTime: realTime, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1,
            PlayerClass: 3, IdTarget: 2, WorldToScreen: WorldToScreen,
            Players:
            [
                new(1, 500f, 0f, 0f, 2, 200, 3),
                new(2, 0f, 0f, 0f, 2, 100, 3) { EntityHealth = 100, MaxHealth = 200, MaxHealthForBuffing = 200 },
                new(3, 0f, 0f, 0f, 2, 100, 3) { EntityHealth = 100, MaxHealth = 200, MaxHealthForBuffing = 200 },
            ],
            Names: new Dictionary<int, string> { [2] = "Mate", [3] = "Other" });

    private static (TfMainTargetId Id, HudViewport Viewport) Built()
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
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase) { ["TF_playerid_sameteam"] = "%s1%s2" };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);

        return (id, viewport);
    }
}
