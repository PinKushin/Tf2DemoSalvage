using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>What the HUD reads of the local player, carried through the timeline from the wire.</summary>
/// <remarks>
/// `C_BasePlayer::GetHealth` is the entity's own `m_iHealth`, not the resource's (which the scoreboard reads and which
/// lags); `m_Local.m_iHideHUD` hides elements; the resource's `m_iMaxHealth` and `m_iMaxBuffedHealth` — the latter
/// misnamed, it is the buffing base (c_tf_playerresource.h:81) — are the maxima.
/// </remarks>
public sealed class HudHealthFieldsTests
{
    [Test]
    public void PlayersAt_TheRecorder_CarriesItsOwnHealthHideHudAndMaxima()
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithHudHealth(health: 88, hideHud: 2058, maxHealth: 175, maxBuffedHealth: 176, residentHealth: 99));

        ScenePlayer recorder = timeline.PlayersAt(100).Single(player => player.EntityIndex == 1);

        (recorder.EntityHealth, recorder.HideHud, recorder.MaxHealth, recorder.MaxHealthForBuffing).ShouldBe((88, 2058, 175, 176));
        recorder.Health.ShouldBe(99, "the resource's, which is a different number on purpose");
    }
}
