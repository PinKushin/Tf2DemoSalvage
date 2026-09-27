using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudPlayerStatus::ShouldDraw` (tf_hud_playerstatus.cpp:1087).</summary>
public sealed class TfHudPlayerStatusShouldDrawConformanceTests
{
    private static readonly HudState Alive = new(true, true, 0, 100, true);

    [Test]
    public void ShouldDraw_AnOrdinaryLivingPlayer_Draws() =>
        ((IHudElement)new TfHudPlayerStatus(new HudViewport())).ShouldDraw(Alive).ShouldBeTrue();

    [Test]
    public void ShouldDraw_AHalloweenGhost_IsHidden() =>
        // TF_COND_HALLOWEEN_GHOST_MODE = 77 (tf_shareddefs.h:767): Ex2 bit 77 - 64.
        ((IHudElement)new TfHudPlayerStatus(new HudViewport()))
            .ShouldDraw(Alive with { Conditions = new PlayerConditions(0, 0, 1 << (77 - 64), 0, 0) })
            .ShouldBeFalse();

    [Test]
    public void ShouldDraw_UnderTheMatchSummary_IsHidden() =>
        ((IHudElement)new TfHudPlayerStatus(new HudViewport()))
            .ShouldDraw(Alive with { Rules = new SceneGameRules(false, 0, false) { ShowMatchSummary = true } })
            .ShouldBeFalse();
}
