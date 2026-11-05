using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The HUD with no install behind it: the condition the CI runner is in (docs/memory/ci-is-the-machine-without-tf2.md).</summary>
public sealed class VguiHudTests
{
    [Test]
    public void Frame_WithNoClientScheme_PaintsNothing()
    {
        // An install with no files opens as an empty `GameContent`, not a null one, so the window's `_game is null` guard
        // let the HUD run. With no `ClientScheme.res`, `Panel.BgColor` falls back to opaque white and the viewport panel
        // filled the screen with it — CI's capture was one colour, every pixel lit.
        VguiSurfaceHost host = new(_ => null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
        VguiHud hud = new(host, new EntityModelSet());

        host.BeginFrame(640, 480);
        hud.Frame(new HudState(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: []));

        host.List.Quads.ShouldBeEmpty();
        host.List.Models.ShouldBeEmpty();
    }

    /// <summary>`restart_timer_time` reaches the crosshair, which `ListenForGameEvent`s it (tf_hud_crosshair.cpp:46).</summary>
    [Test]
    public void Frame_ACasualRestartTimerTime_ReachesTheCrosshair()
    {
        VguiHud hud = Hud();

        hud.Frame(Local, events: [RestartTimer(5)]);

        hud.Crosshair.TimeToHideUntil.ShouldBe(6f, "fired at 1.0, five seconds");
    }

    /// <summary>A seek restarts playback, which `LevelShutdown` (tf_hud_crosshair.cpp:103) clears the hide for.</summary>
    [Test]
    public void Frame_AReset_ClearsTheCrosshairsHide()
    {
        VguiHud hud = Hud();

        hud.Frame(Local, events: [RestartTimer(5)]);
        hud.Crosshair.TimeToHideUntil.ShouldBe(6f, "the control");
        hud.Frame(Local, reset: true);

        hud.Crosshair.TimeToHideUntil.ShouldBe(-1f);
    }

    private static VguiHud Hud()
    {
        VguiSurfaceHost host = EmptySchemeHost();

        host.BeginFrame(640, 480);
        return new VguiHud(host, new EntityModelSet());
    }

    private static HudState Local => new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: []);

    private static HudGameEvent RestartTimer(int time) => new(
        new Core.Scene.SceneGameEvent(0, "restart_timer_time", new System.Collections.Generic.Dictionary<string, object?> { ["time"] = time }, new System.Collections.Generic.Dictionary<int, Core.Net.PlayerInfo>()),
        1f,
        [],
        1,
        new Core.Scene.SceneGameRules(false, 0, false) { MatchGroup = 7 },
        "cp_test",
        0);

    /// <summary>
    /// B709. Under `if_match`, TF2's `HudObjectiveTimePanel.res` gives the waiting label `wide "p1"`. `if_match` is asked for only `if ( TFGameRules() )`,
    /// so it arrives with the reload `CTFHudMatchStatus::OnThink` makes on the first match group, when the parent has its size. Read
    /// on the first pass, children first, `p1` took the default of 64 and clipped "WAITING FOR PLAYERS".
    /// </summary>
    [Test]
    public void Frame_TheMatchLabelsParentRelativeWidth_ReadsTheParentsResWidth()
    {
        static byte[] Res(string text) => System.Text.Encoding.UTF8.GetBytes(text);

        VguiSurfaceHost host = new(
            path => path switch
            {
                "resource/ClientScheme.res" => [],
                "resource/UI/HudMatchStatus.res" => Res("""
                    "x" { "ObjectiveStatusTimePanel" { "fieldName" "ObjectiveStatusTimePanel" "wide" "130" "tall" "150" } }
                    """),
                "resource/UI/HudObjectiveTimePanel.res" => Res("""
                    "x" { "WaitingForPlayersLabel" { "ControlName" "CExLabel" "fieldName" "WaitingForPlayersLabel" "wide" "78" "tall" "19" "if_match" { "proportionalToParent" "1" "wide" "p1" } } }
                    """),
                _ => null,
            },
            _ => null,
            new FpsPanelTests.SolidGdi(),
            _ => (0, 0));

        host.BeginFrame(640, 480);

        VguiHud hud = new(host, new EntityModelSet());

        HudState matchHud = Local with { ConVars = new HudConVars(name => name == "tf_use_match_hud" ? "1" : null) };

        hud.Frame(matchHud, reset: false);

        // `.res` hides these until a timer is live, and only a visible panel thinks or takes its scheme; the demo's first timer does this.
        VguiPanel panel = hud.MatchStatus.TimePanel;

        hud.MatchStatus.Visible = true;
        hud.Frame(matchHud, reset: false);
        panel.PerformApplySchemeSettings(hud.Viewport.Context.ShouldNotBeNull());

        panel.Wide.ShouldBe(130, "the control: the parent took its file's width");
        panel.FindChildByName("WaitingForPlayersLabel").ShouldNotBeNull().Wide.ShouldBe(129, "`p1` is the parent less one (Panel.cpp `ComputeWide`), and one pass read 63");
    }

    /// <summary>A host whose `ClientScheme.res` exists and is empty, and which holds no other file: the HUD runs on defaults.</summary>
    /// <returns>The host.</returns>
    internal static VguiSurfaceHost EmptySchemeHost() =>
        new(path => path == "resource/ClientScheme.res" ? [] : null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
}
