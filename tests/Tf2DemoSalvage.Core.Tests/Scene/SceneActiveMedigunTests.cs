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

    [Test]
    public void MedigunItem_TwoMediguns_IsTheFirstWeaponOfThatClass()
    {
        // `Weapon_OwnsThisID( TF_WEAPON_MEDIGUN )`: the first in `m_hMyWeapons` order, which `Items` keeps for the weapons
        // (`CarriedItems`, the same walk `MedigunOf` makes); a wearable of the class is no weapon.
        EconAttributeWire none = new([], [], HasValidItemId: false);
        ScenePlayer medic = new(1, 0f, 0f, 0f, 2, 150, 5)
        {
            Items =
            [
                new SceneItem(40, HealBeamFeed.MedigunClassName, 1, none, IsWeapon: false),
                new SceneItem(30, "CTFSyringeGun", 17, none, IsWeapon: true),
                new SceneItem(31, HealBeamFeed.MedigunClassName, 29, none, IsWeapon: true),
                new SceneItem(32, HealBeamFeed.MedigunClassName, 35, none, IsWeapon: true),
            ],
        };

        medic.MedigunItem().ShouldNotBeNull().EntityIndex.ShouldBe(31);
    }
}
