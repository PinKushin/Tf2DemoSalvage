using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`USER_MESSAGE( PlayerPickupWeapon )` (clientmode_tf.cpp:2469-2475) reaching `CTFHudPlayerClass` as `localplayer_pickup_weapon`.</summary>
public sealed class VguiHudUserMessageTests
{
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Frame_APlayerPickupWeaponMessage_RefreshesTheClassModelPanel(bool sent, bool imageStillShown)
    {
        VguiSurfaceHost host = new(_ => null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
        VguiHud hud = new(host, new EntityModelSet());
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1,
            Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)],
            ConVars: new HudConVars(null, name => name == "cl_hud_playerclass_use_playermodel" ? "1" : null));

        host.BeginFrame(640, 480);
        hud.Frame(state);
        hud.PlayerStatus.PlayerClass.ClassImage.Visible = true;

        host.BeginFrame(640, 480);
        hud.Frame(state, userMessages: sent
            ? [new SceneUserMessage(10, 75, System.Array.Empty<byte>()) { Name = SceneUserMessage.PlayerPickupWeapon }]
            : null);

        // `UpdateModelPanel` with the player model in use hides the 2D image (tf_hud_playerstatus.cpp:436-442).
        hud.PlayerStatus.PlayerClass.ClassImage.Visible.ShouldBe(imageStillShown);
    }
}
