using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A wearable's weapon handle, dereferenced as `GetWeaponAssociatedWith` does (B770).</summary>
/// <remarks>
/// **Against every entity, not against what is drawn.** A holstered weapon carries `EF_NODRAW` and is in no prop list,
/// yet the client's entity list holds it and the handle resolves: measured on `demostf-cp_process_f12-2026-08-08-2207`
/// tick 20000, sticky launcher 360 absent from the props while its Botkiller wearable named it. And slot AND serial
/// (`CBaseHandle::Get`), so a handle naming a slot's previous occupant names nothing.
/// </remarks>
public sealed class WeaponAssociationTests
{
    private const int Weapon = 30;
    private const int Serial = 5;

    [TestCase(Serial, false, true, TestName = "PropsAt_AWearableNamingALiveWeapon_KeepsTheHandle")]
    [TestCase(Serial, true, true, TestName = "PropsAt_AWearableNamingAHiddenWeapon_KeepsTheHandle")]
    [TestCase(Serial + 1, false, false, TestName = "PropsAt_AWearableNamingAPreviousOccupant_HasNoWeapon")]
    public void PropsAt_AWearablesWeaponHandle_ResolvesAgainstEveryEntity(int serial, bool weaponHidden, bool resolves)
    {
        ScenePropTrack weapon = new(Weapon, "models/weapons/c_models/c_sticky.mdl", Serial);
        weapon.Add(0, new ScenePose { Hidden = weaponHidden });

        ScenePropTrack wearable = new(31, "models/player/items/mvm_loot/demo/fob_h_stickybomb_diamond.mdl")
        {
            WeaponAssociatedWith = (Weapon, serial),
        };
        wearable.Add(0, new ScenePose());
        wearable.Stamp(0);

        List<SceneProp> props = [];
        DemoTimeline.ForTracks([weapon, wearable]).PropsAt(10, props);

        (props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null).ShouldBe(resolves);
    }

    /// <remarks>`m_bBeingRepurposedForTaunt` reaches the drawn weapon as it stood at the tick: false before, true after.</remarks>
    [TestCase(5, false, TestName = "PropsAt_AWeaponBeforeItsTaunt_IsNotRepurposed")]
    [TestCase(15, true, TestName = "PropsAt_AWeaponDuringItsTaunt_IsRepurposed")]
    public void PropsAt_AWeaponsTauntFlag_IsCarriedAtTheTick(int tick, bool repurposed)
    {
        ScenePropTrack weapon = new(Weapon, "models/weapons/c_models/c_sticky.mdl", Serial);
        ScenePose pose = new();
        weapon.Add(0, pose);
        weapon.Stamp(0);
        weapon.Add(10, pose);
        weapon.RepurposedForTaunt = true;
        weapon.Stamp(10);

        List<SceneProp> props = [];
        DemoTimeline.ForTracks([weapon]).PropsAt(tick, props);

        props.ShouldHaveSingleItem().RepurposedForTaunt.ShouldBe(repurposed);
    }
}
