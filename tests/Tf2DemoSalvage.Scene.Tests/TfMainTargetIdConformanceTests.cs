using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CMainTargetID` over `CTargetID` (game/client/tf/tf_hud_target_id.cpp:1201).</summary>
/// <remarks>
/// The local player is entity 1, RED (team 2), a medic (class 5, so `bMedic` at :799 shows an enemy's health), not
/// observing. Player 2 "Blue" is a BLU soldier. An ordinary RED soldier looking at an ordinary enemy would show
/// nothing at all here — `printFormatString` stays null unless same team, spy, medic, heavy, a disguised-enemy spy,
/// or the local player is dying (`#TF_playerid_diffteam`, :811-816, not modelled: no "dying state" is decoded) — which
/// is genuine TF2 behaviour, not a gap in <see cref="TfMainTargetId"/>.
/// </remarks>
public sealed class TfMainTargetIdConformanceTests
{
    [Test]
    public void ShouldDraw_IdTargetSet_NamesTheTarget()
    {
        TfMainTargetId id = Thought(Playing() with { IdTarget = 2 });

        (id.TargetIndex, id.TargetName).ShouldBe((2, "Blue"));
    }

    [Test]
    public void ShouldDraw_NoIdTarget_IsHidden() =>
        Built().ShouldDraw(Playing() with { IdTarget = null }).ShouldBeFalse();

    [Test]
    public void ShouldDraw_WhileObserving_IsHidden() =>
        // `CMainTargetID::ShouldDraw` (:1201): only while `GetObserverMode() <= OBS_MODE_NONE`.
        Built().ShouldDraw(Playing() with { IdTarget = 2, ObserverMode = ObserverModes.InEye }).ShouldBeFalse();

    private static HudState Playing() =>
        new(true, true, 0, 0, true, CurTime: 1f, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1, PlayerClass: 5,
            Players: [new(1, 0f, 0f, 0f, 2, 0, 5), new(2, 0f, 0f, 0f, 3, 200, 3) { EntityHealth = 200, MaxHealth = 200, MaxHealthForBuffing = 200 }],
            Names: new Dictionary<int, string> { [2] = "Blue" });

    private static TfMainTargetId Thought(HudState state)
    {
        TfMainTargetId id = Built();

        ((HudViewport)id.Parent!).Think(state);
        id.Visible.ShouldBeTrue();
        return id;
    }

    private static TfMainTargetId Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/TargetID.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/TargetID.res"
                {
                    "TargetIDBG" { "ControlName" "CTFImagePanel" "fieldName" "TargetIDBG" "wide" "100" "tall" "39" }
                    "TargetNameLabel" { "ControlName" "Label" "fieldName" "TargetNameLabel" "wide" "720" "tall" "27" "labelText" "%targetname%" }
                    "TargetDataLabel" { "ControlName" "Label" "fieldName" "TargetDataLabel" "wide" "315" "tall" "17" "labelText" "%targetdata%" }
                    "KillStreakIcon" { "ControlName" "ImagePanel" "fieldName" "KillStreakIcon" "visible" "0" }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_playerid_sameteam"] = "%s1%s2",
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
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);
        viewport.Think(Playing() with { IdTarget = null });

        return id;
    }
}
