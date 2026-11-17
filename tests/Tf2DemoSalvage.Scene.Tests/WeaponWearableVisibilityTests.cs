using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A wearable a weapon controls draws with that weapon (B770): `CTFWearable::ShouldDraw`'s `GetWeaponAssociatedWith`
/// branch (`tf_item_wearable.cpp:381-408`) and `CEconWearable::ShouldDraw`'s dead-owner test (`econ_wearable.cpp:395`,
/// `ShouldDrawWhenPlayerIsDead`, `tf_item_wearable.h:65`).
/// </summary>
public sealed class WeaponWearableVisibilityTests
{
    private const int Owner = 2;
    private const int Gun = 30;
    private const int GunSerial = 5;
    private const int Lights = 31;

    /// <remarks>The control: his active weapon, drawn, not in a taunt, him alive. Every other case changes one input.</remarks>
    [Test]
    public void Visible_ATiedWearableWhoseWeaponIsOutAndDrawn_IsKept()
    {
        Kept(Player(), [Weapon(), Wearable()], Wearable()).ShouldBeTrue();
    }

    /// <remarks>`pOwner->GetActiveWeapon() != pWeapon`: a holstered weapon's wearable goes with it (`:384-387`).</remarks>
    [Test]
    public void Visible_ATiedWearableWhoseWeaponIsHolstered_IsHidden()
    {
        Kept(Player() with { ActiveWeapon = 40 }, [Weapon(), Wearable()], Wearable()).ShouldBeFalse();
    }

    /// <remarks>
    /// `!pWeapon->ShouldDraw()`, for a third-person wearable only (`:389-396`): the weapon is in the moment but its own rule
    /// left it out of the list. A viewmodel wearable (`CTFWearableVM`, `IsViewModelWearable`) skips the test.
    /// </remarks>
    [TestCase("CTFWearable", false, TestName = "Visible_ATiedWearableWhoseWeaponIsNotDrawn_IsHidden")]
    [TestCase("CTFWearableVM", true, TestName = "Visible_ATiedViewmodelWearableWhoseWeaponIsNotDrawn_IsKept")]
    public void Visible_ATiedWearableWhoseWeaponIsNotDrawn_FollowsItsKind(string className, bool kept)
    {
        SceneProp wearable = Wearable() with { ClassName = className };

        Kept(Player(), [wearable], wearable, moment: [Weapon(), wearable]).ShouldBe(kept);
    }

    /// <remarks>
    /// `IsBeingRepurposedForTaunt` (`:398-405`): the weapon draws as the taunt's prop and its wearables do not — either kind,
    /// since the test follows the viewmodel one. For a viewmodel wearable the weapon is read from the moment.
    /// </remarks>
    [TestCase("CTFWearable", TestName = "Visible_ATiedWearableWhoseWeaponIsInATaunt_IsHidden")]
    [TestCase("CTFWearableVM", TestName = "Visible_ATiedViewmodelWearableWhoseWeaponIsInATaunt_IsHidden")]
    public void Visible_ATiedWearableWhoseWeaponIsInATaunt_IsHiddenEitherKind(string className)
    {
        SceneProp gun = Weapon() with { RepurposedForTaunt = true };
        SceneProp wearable = Wearable() with { ClassName = className };

        Kept(Player(), [wearable], wearable, moment: [gun, wearable]).ShouldBeFalse();
    }

    /// <remarks>
    /// **A viewmodel wearable skips `!pWeapon->ShouldDraw()`** (`:389-396`): with its weapon out but in no list, it stays,
    /// where a third-person one goes (`Visible_ATiedWearableWhoseActiveWeaponIsInNoList_IsHidden`). Ghost mode returns
    /// first for it too (`:322-335`).
    /// </remarks>
    [Test]
    public void Visible_ATiedViewmodelWearableWhoseWeaponIsInNoList_IsKept()
    {
        SceneProp wearable = Wearable() with { ClassName = "CTFWearableVM" };

        Kept(Player(), [wearable], wearable).ShouldBeTrue();
    }

    [Test]
    public void Visible_ATiedViewmodelWearableOfAGhostWhoseWeaponIsHolstered_IsLeftToTheGhostRule()
    {
        SceneProp wearable = Wearable() with { ClassName = "CTFWearableVM" };
        ScenePlayer ghost = Player() with
        {
            Conditions = new PlayerConditions(0, 0, 1 << (PlayerConditions.HalloweenGhostMode - 64), 0, 0),
            ActiveWeapon = 40,
        };

        Kept(ghost, [Weapon(), wearable], wearable).ShouldBeTrue();
    }

    /// <remarks>
    /// `!ShouldDrawWhenPlayerIsDead() &amp;&amp; !pPlayerOwner-&gt;IsAlive()`: a tied wearable only (`GetWeaponAssociatedWith() ==
    /// NULL` is the override's answer), so an untied one on the same dead owner is the control.
    /// </remarks>
    [TestCase(true, false, TestName = "Visible_ATiedWearableOfADeadOwner_IsHidden")]
    [TestCase(false, true, TestName = "Visible_AnUntiedWearableOfADeadOwner_IsKept")]
    public void Visible_AWearableOfADeadOwner_IsHiddenOnlyWhenTied(bool tied, bool kept)
    {
        SceneProp wearable = tied ? Wearable() : Wearable() with { WeaponAssociatedWith = null };

        Kept(Player() with { LifeState = 2 }, [Weapon(), wearable], wearable).ShouldBe(kept);
    }

    /// <remarks>
    /// **The weapon branch is the `else` of the disguise branch, and ghost mode returns before both** (`:322-341`,
    /// `:344-377`): a disguised or ghost owner's wearables answer `DisguiseVisibility`, not this, so a holstered weapon
    /// does not hide them here.
    /// </remarks>
    [TestCase(PlayerConditions.Disguised, TestName = "Visible_ATiedWearableOfADisguisedOwner_IsLeftToTheDisguise")]
    [TestCase(PlayerConditions.HalloweenGhostMode, TestName = "Visible_ATiedWearableOfAGhost_IsLeftToTheGhostRule")]
    public void Visible_ATiedWearableOfAnOwnerInAnEarlierBranch_IsNotJudgedHere(int condition)
    {
        PlayerConditions conditions = condition < 32
            ? new PlayerConditions(1 << condition, 0, 0, 0, 0)
            : new PlayerConditions(0, 0, 1 << (condition - 64), 0, 0);

        Kept(Player() with { Conditions = conditions, ActiveWeapon = 40 }, [Weapon(), Wearable()], Wearable()).ShouldBeTrue();
    }

    /// <remarks>
    /// **A weapon in no list at all still resolved**: the timeline dereferences against every entity, so a weapon kept out
    /// of the props (`EF_NODRAW`) is a weapon that does not draw, and a third-person wearable goes even with it out.
    /// Measured on `demostf-cp_process_f12-2026-08-08-2207` tick 20000 for the holstered case: sticky launcher 360 absent
    /// from the props while its Botkiller 364 named it.
    /// </remarks>
    [Test]
    public void Visible_ATiedWearableWhoseActiveWeaponIsInNoList_IsHidden()
    {
        Kept(Player(), [Wearable()], Wearable()).ShouldBeFalse();
    }

    /// <remarks>
    /// **The weapon in the list must be the one the handle names, serial and all**: a prop in the same slot with another
    /// serial is a different entity. The handle's own serial is the control, kept.
    /// </remarks>
    [TestCase(GunSerial + 1, false, TestName = "Visible_ATiedWearableWhoseSlotHoldsAnotherSerial_IsHidden")]
    [TestCase(GunSerial, true, TestName = "Visible_ATiedWearableWhoseSlotHoldsItsSerial_IsKept")]
    public void Visible_ATiedWearable_MatchesItsWeaponBySerial(int listedSerial, bool kept)
    {
        Kept(Player(), [Weapon() with { SerialNumber = listedSerial }, Wearable()], Wearable()).ShouldBe(kept);
    }

    /// <remarks>
    /// **A disguise returns first for a third-person wearable only** (`!IsViewModelWearable()`, `tf_item_wearable.cpp:344`):
    /// a disguised spy's `CTFWearableVM` still answers to its weapon, so a holstered one hides it.
    /// </remarks>
    [Test]
    public void Visible_ATiedViewmodelWearableOfADisguisedOwnerWhoseWeaponIsHolstered_IsHidden()
    {
        SceneProp wearable = Wearable() with { ClassName = "CTFWearableVM" };
        ScenePlayer spy = Player() with
        {
            Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
            ActiveWeapon = 40,
        };

        Kept(spy, [Weapon(), wearable], wearable).ShouldBeFalse();
    }

    /// <remarks>
    /// `CEconWearable::ShouldDraw` refuses a wearable whose owner is no player (`econ_wearable.cpp:369-373`); one owned by
    /// nobody in the moment is the case here.
    /// </remarks>
    [Test]
    public void Visible_ATiedWearableWhoseOwnerIsNoPlayer_IsHidden()
    {
        Kept(Player() with { EntityIndex = 9 }, [Weapon(), Wearable()], Wearable()).ShouldBeFalse();
    }

    /// <remarks>Nobody tied and nobody dead: the list is handed back as it came, nothing allocated for it.</remarks>
    [Test]
    public void Visible_NoTiedWearable_ReturnsTheListItself()
    {
        List<SceneProp> drawn = [Weapon(), Wearable() with { WeaponAssociatedWith = null }];

        WeaponWearableVisibility.Visible(drawn, drawn, [Player()]).ShouldBeSameAs(drawn);
    }

    private static bool Kept(ScenePlayer owner, List<SceneProp> drawn, SceneProp wearable, List<SceneProp>? moment = null)
    {
        IReadOnlyList<SceneProp> kept = WeaponWearableVisibility.Visible(drawn, moment ?? drawn, [owner]);

        // By value: the cases build the wearable twice, once for the list and once to ask after.
        return kept.Any(prop => prop == wearable);
    }

    private static ScenePlayer Player() =>
        new(Owner, 0f, 0f, 0f, Team: SceneTeams.Blu, Health: 125, PlayerClass: 3, LifeState: 0) { ActiveWeapon = Gun };

    private static SceneProp Weapon() =>
        new(
            EntityIndex: Gun,
            ModelPath: "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl",
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: Owner,
            OwnedBy: Owner,
            WeaponState: EntityState.WeaponActive,
            BoneMerged: true,
            ClassName: "CTFRocketLauncher",
            SerialNumber: GunSerial);

    private static SceneProp Wearable() =>
        new(
            EntityIndex: Lights,
            ModelPath: "models/weapons/c_models/c_rocketlauncher/festive_lights.mdl",
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: Owner,
            OwnedBy: Owner,
            BoneMerged: true,
            ClassName: "CTFWearable",
            WeaponAssociatedWith: (Gun, GunSerial));
}
