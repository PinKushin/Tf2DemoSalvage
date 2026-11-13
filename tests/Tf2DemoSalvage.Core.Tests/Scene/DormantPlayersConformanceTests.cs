using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A player out of the PVS is still the client's player, at his last origin and life state (B725).</summary>
/// <remarks>
/// **`UTIL_PlayerByIndex` returns a dormant player** — `LeavePVS` makes the client entity dormant and keeps it — so
/// `C_TFPlayer::UpdateLookAt` (`c_tf_player.cpp:6223-6247`) can look at him, at the origin last received, alive by the
/// `m_lifeState` last received. No one draws him (`ShouldDraw() &amp;&amp; !IsDormant()`), so he is not in the frame's players:
/// he is in its dormant players.
/// </remarks>
public sealed class DormantPlayersConformanceTests
{
    private const float Interval = 0.015f;

    private const int Soldier = 3;

    /// <summary>`cl_interp 0`, `cl_interp_ratio 0`: no window.</summary>
    private static readonly ClientInterp NoWindow = new(0f, 0f, 66f);

    [TestCase(0, true)]
    [TestCase(2, false)]
    public void DormantPlayersAt_APlayerWhoLeftThePvs_IsHeldAtHisLastOriginAndLife(int lifeState, bool alive)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfGestures(
                Interval,
                SceneTeams.Red,
                Soldier,
                rules: null,
                alwaysLoser: false,
                new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround) { X = 80f, LifeState = lifeState },
                new SyntheticPlayer.GestureSnapshot(110, 0f, PlayerActivityState.OnGround) { Dormant = true }),
            client: NoWindow);

        timeline.DormantPlayersAt(100).ShouldBeEmpty("the control: in the PVS, he is a player, not a dormant one");

        SceneDormantPlayer dormant = timeline.DormantPlayersAt(110).ShouldHaveSingleItem();

        (dormant.EntityIndex, dormant.X, dormant.Z, dormant.IsAlive).ShouldBe((1, 80f, 0f, alive));
        timeline.PlayersAt(110).ShouldBeEmpty("no one draws a dormant player");
    }
}
