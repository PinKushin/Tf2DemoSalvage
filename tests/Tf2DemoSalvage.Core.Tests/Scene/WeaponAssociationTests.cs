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

    /// <remarks>
    /// **The weapon's life, not its slot**: a weapon born after the asked tick, or ended before it, is no entity then, and
    /// the handle names nothing (`Alive`). The control is the same serial inside its life.
    /// </remarks>
    [TestCase(5, false, TestName = "PropsAt_AWearableBeforeItsWeaponIsBorn_HasNoWeapon")]
    [TestCase(25, true, TestName = "PropsAt_AWearableDuringItsWeaponsLife_KeepsTheHandle")]
    [TestCase(45, false, TestName = "PropsAt_AWearableAfterItsWeaponEnded_HasNoWeapon")]
    public void PropsAt_AWearablesWeaponHandle_ResolvesOnlyInsideTheWeaponsLife(int tick, bool resolves)
    {
        List<SceneProp> props = [];
        Born(20, 40).PropsAt(tick, props);

        (props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null).ShouldBe(resolves);
    }

    /// <remarks>
    /// **Stepped across the weapon's birth and end, the answer is a cold one's** (B438's invariant): the wearable has one
    /// keyframe, so nothing of its own wakes it; only the weapon's life can.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossItsWeaponsBirthAndEnd_MatchesACold()
    {
        DemoTimeline stepped = Born(20, 40);
        List<SceneProp> props = [];
        List<bool> steppedAnswers = [];
        List<bool> coldAnswers = [];

        foreach (int tick in new[] { 5, 25, 45 })
        {
            stepped.PropsAt(tick, props);
            steppedAnswers.Add(props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null);

            Born(20, 40).PropsAt(tick, props);
            coldAnswers.Add(props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null);
        }

        steppedAnswers.ShouldBe(coldAnswers);
        coldAnswers.ShouldBe([false, true, false], "the control: the cold answers do change");
    }

    /// <remarks>
    /// **A reused slot wakes the wearable for its own weapon only**: occupant serial 4 lives 0-20 and serial 5 from 30, and
    /// the wearable names serial 5. Stepped through both, it matches a cold walk — none until 30, then the weapon.
    /// </remarks>
    [Test]
    public void PropsAt_SteppedAcrossASlotReusedByItsWeapon_MatchesACold()
    {
        DemoTimeline Reused()
        {
            ScenePropTrack former = new(Weapon, "models/weapons/c_models/c_sticky.mdl", Serial - 1);
            former.Add(0, new ScenePose());
            former.End(20);

            ScenePropTrack weapon = new(Weapon, "models/weapons/c_models/c_sticky.mdl", Serial);
            weapon.Add(30, new ScenePose());

            ScenePropTrack wearable = new(31, "models/player/items/mvm_loot/demo/fob_h_stickybomb_diamond.mdl")
            {
                WeaponAssociatedWith = (Weapon, Serial),
            };
            wearable.Add(0, new ScenePose());
            wearable.Stamp(0);

            return DemoTimeline.ForTracks([former, weapon, wearable]);
        }

        DemoTimeline stepped = Reused();
        List<SceneProp> props = [];
        List<bool> steppedAnswers = [];
        List<bool> coldAnswers = [];

        foreach (int tick in new[] { 10, 25, 35 })
        {
            stepped.PropsAt(tick, props);
            steppedAnswers.Add(props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null);

            Reused().PropsAt(tick, props);
            coldAnswers.Add(props.Single(prop => prop.EntityIndex == 31).WeaponAssociatedWith is not null);
        }

        steppedAnswers.ShouldBe(coldAnswers);
        coldAnswers.ShouldBe([false, false, true], "the control: the former occupant never answers for this serial");
    }

    /// <summary>A weapon's extra-wearable handles reach its prop as the facts held them (B69), undereferenced.</summary>
    [Test]
    public void PropsAt_AWeaponWithExtraWearables_CarriesBothHandles()
    {
        ScenePropTrack weapon = new(Weapon, "models/weapons/c_models/c_buffpack.mdl", Serial)
        {
            ExtraWearable = (40, 3),
            ExtraWearableViewModel = (41, 7),
        };
        weapon.Add(0, new ScenePose());
        weapon.Stamp(0);

        List<SceneProp> props = [];
        DemoTimeline.ForTracks([weapon]).PropsAt(10, props);

        SceneProp drawn = props.ShouldHaveSingleItem();
        (drawn.ExtraWearable, drawn.ExtraWearableViewModel).ShouldBe(((40, 3), (41, 7)));
    }

    /// <summary>A wearable from tick 0 tied to a weapon that lives from <paramref name="birth"/> to <paramref name="end"/>.</summary>
    private static DemoTimeline Born(int birth, int end)
    {
        ScenePropTrack weapon = new(Weapon, "models/weapons/c_models/c_sticky.mdl", Serial);
        weapon.Add(birth, new ScenePose());
        weapon.End(end);

        ScenePropTrack wearable = new(31, "models/player/items/mvm_loot/demo/fob_h_stickybomb_diamond.mdl")
        {
            WeaponAssociatedWith = (Weapon, Serial),
        };
        wearable.Add(0, new ScenePose());
        wearable.Stamp(0);

        return DemoTimeline.ForTracks([weapon, wearable]);
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
