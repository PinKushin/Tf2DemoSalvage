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

    /// <remarks>`IsBeingRepurposedForTaunt` (`:398-405`): the weapon draws as the taunt's prop and its wearables do not.</remarks>
    [Test]
    public void Visible_ATiedWearableWhoseWeaponIsInATaunt_IsHidden()
    {
        SceneProp gun = Weapon() with { RepurposedForTaunt = true };

        Kept(Player(), [gun, Wearable()], Wearable()).ShouldBeFalse();
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
    /// **A holstered weapon is not in the moment at all** — the server sets `EF_NODRAW` on it — and its handle still
    /// resolves, since the timeline dereferences against every entity: the wearable goes, by the active-weapon test.
    /// Measured on `demostf-cp_process_f12-2026-08-08-2207` tick 20000: entity 360, the demoman's sticky launcher, absent
    /// from the props while its Botkiller wearable 364 was drawn.
    /// </remarks>
    [Test]
    public void Visible_ATiedWearableWhoseHolsteredWeaponIsNotInTheMoment_IsHidden()
    {
        Kept(Player() with { ActiveWeapon = 40 }, [Wearable()], Wearable()).ShouldBeFalse();
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
