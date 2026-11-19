using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`CTFHudFlagObjectives::FireGameEvent` (tf_hud_flagstatus.cpp:927) reached through the HUD's event dispatch.</summary>
public sealed class VguiHudFlagObjectivesTests
{
    /// <remarks>
    /// The flag panel looks at what the local player carries only in `UpdateStatus`, which `flagstatus_update` runs: the
    /// event dispatched with a frame is what shows the carry. The control is the same frame without it.
    /// </remarks>
    [Test]
    public void Frame_FlagstatusUpdateNamingTheLocalPlayer_ShowsTheCarry()
    {
        (VguiSurfaceHost host, VguiHud hud) = Hud();
        HudState carrying = new(true, true, 0, 125, true, Team: 3, LocalIndex: 1)
        {
            CaptureFlags = new SceneFlags([new SceneFlag(51, 2, 1, false, false, 0, string.Empty, default, 1, 0f)], []),
        };

        Frame(host, hud, carrying, events: null);
        bool withoutEvent = hud.ObjectiveStatus.FlagPanel.CarryingFlag;
        Frame(host, hud, carrying, [FlagStatusUpdate(userId: 101)]);

        (withoutEvent, hud.ObjectiveStatus.FlagPanel.CarryingFlag).ShouldBe((false, true));
    }

    private static HudGameEvent FlagStatusUpdate(int userId)
    {
        Dictionary<int, PlayerInfo> roster = new() { [1] = new PlayerInfo("Me", 101, string.Empty, 1, false, false) };
        Dictionary<string, object?> fields = new() { ["userid"] = userId, ["entindex"] = 51 };

        return new HudGameEvent(new SceneGameEvent(0, "flagstatus_update", fields, roster), 1f, [], 1, default, string.Empty, 0);
    }

    private static (VguiSurfaceHost Host, VguiHud Hud) Hud()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();

        return (host, new VguiHud(host, new EntityModelSet()));
    }

    private static void Frame(VguiSurfaceHost host, VguiHud hud, HudState state, IReadOnlyList<HudGameEvent>? events)
    {
        host.BeginFrame(640, 480);
        hud.Frame(state, events);
    }
}
