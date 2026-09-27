using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The local `C_TFPlayer`'s part in `g_ItemEffectMeterManager`: `OnPlayerClassChange` sets the player (c_tf_player.cpp:5257),
/// a new `m_iSpawnCounter` fires `localplayer_respawn` (:4543, :7951), and a new entity after a seek starts again.
/// </summary>
public sealed class VguiHudItemEffectMeterTests
{
    [Test]
    public void Frame_TheLocalPlayersClass_MakesItsMeters()
    {
        (VguiSurfaceHost host, VguiHud hud) = Hud();

        Frame(host, hud, Spy(spawnCounter: 0));

        hud.ItemEffectMeters.Meters.Count.ShouldBe(9, "a spy's four, then every class's five");
        hud.ItemEffectMeters.Listening.ShouldBeTrue();
    }

    [Test]
    public void Frame_AnUnchangedLocalPlayer_KeepsItsMeters()
    {
        (VguiSurfaceHost host, VguiHud hud) = Hud();
        Frame(host, hud, Spy(spawnCounter: 0));
        TfHudItemEffectMeter first = hud.ItemEffectMeters.Meters[0];

        Frame(host, hud, Spy(spawnCounter: 0));

        hud.ItemEffectMeters.Meters[0].ShouldBeSameAs(first);
    }

    [Test]
    public void Frame_ANewSpawnCounter_RebuildsTheMeters()
    {
        (VguiSurfaceHost host, VguiHud hud) = Hud();
        Frame(host, hud, Spy(spawnCounter: 0));
        TfHudItemEffectMeter first = hud.ItemEffectMeters.Meters[0];

        Frame(host, hud, Spy(spawnCounter: 1));

        hud.ItemEffectMeters.Meters[0].ShouldNotBeSameAs(first, "localplayer_respawn runs SetPlayer (:163-181)");
    }

    [Test]
    public void Frame_ASeek_ClearsAndRemakesTheMeters()
    {
        (VguiSurfaceHost host, VguiHud hud) = Hud();
        Frame(host, hud, Spy(spawnCounter: 0));
        TfHudItemEffectMeter first = hud.ItemEffectMeters.Meters[0];

        Frame(host, hud, Spy(spawnCounter: 0), reset: true);

        first.Parent.ShouldBeNull("the old local player's destructor cleared it (:4072)");
        hud.ItemEffectMeters.Meters.Count.ShouldBe(9);
    }

    private static (VguiSurfaceHost Host, VguiHud Hud) Hud()
    {
        VguiSurfaceHost host = new(_ => null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));

        return (host, new VguiHud(host, new EntityModelSet()));
    }

    private static void Frame(VguiSurfaceHost host, VguiHud hud, ScenePlayer spy, bool reset = false)
    {
        host.BeginFrame(640, 480);
        hud.Frame(new HudState(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [spy]), reset: reset);
    }

    private static ScenePlayer Spy(int spawnCounter) => new(1, 0f, 0f, 0f, 2, 100, 8) { SpawnCounter = spawnCounter };
}
