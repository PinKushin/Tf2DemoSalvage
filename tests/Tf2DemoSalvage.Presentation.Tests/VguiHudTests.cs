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

    /// <summary>A host whose `ClientScheme.res` exists and is empty, and which holds no other file: the HUD runs on defaults.</summary>
    /// <returns>The host.</returns>
    internal static VguiSurfaceHost EmptySchemeHost() =>
        new(path => path == "resource/ClientScheme.res" ? [] : null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
}
