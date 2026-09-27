using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`MedicGetHealTarget` (tf_player_shared.cpp:13021) reads the ACTIVE medigun's `m_hHealingTarget`; `CWeaponMedigun::ClientThink` (tf_weapon_medigun.cpp:2273) its charge.</summary>
public sealed class SceneActiveMedigunTests
{
    [Test]
    public void PlayersAt_AMedigunInHand_CarriesItsHealTargetAndCharge()
    {
        ScenePlayer[] players = [.. DemoTimeline.Build(SyntheticPlayer.DemoWithMedigun(0.75f)).PlayersAt(100).OrderBy(player => player.EntityIndex)];

        (players[0].ActiveMedigun, players[1].ActiveMedigun).ShouldBe((((int?)2, 0.75f), ((int? HealTarget, float Charge)?)null));
    }
}
