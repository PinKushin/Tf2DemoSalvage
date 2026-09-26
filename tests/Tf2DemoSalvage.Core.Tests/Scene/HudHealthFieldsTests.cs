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

    [Test]
    public void PlayersAt_TheRecordersLoadout_IsWeaponsThenWearablesUpToTheVectorsLength()
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithLoadout([(5, 200), (6, 199)], [(8, 30000)], staleWearable: 9));

        ScenePlayer recorder = timeline.PlayersAt(100).Single(player => player.EntityIndex == 1);

        recorder.Items.ShouldNotBeNull();
        recorder.Items.Select(item => (item.EntityIndex, item.DefinitionIndex, item.IsWeapon))
            .ShouldBe([(5, (int?)200, true), (6, (int?)199, true), (8, (int?)30000, false)]);
    }

    [Test]
    public void PlayersAt_TheActiveWeaponsClip_IsTheWireValueLessOne()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithLoadout([(5, 200)], []));

        ScenePlayer recorder = timeline.PlayersAt(100).Single(player => player.EntityIndex == 1);

        (recorder.ActiveWeapon, recorder.WeaponClip1).ShouldBe(((int?)5, (int?)4), "RecvProxy_IntSubOne");
    }
}
