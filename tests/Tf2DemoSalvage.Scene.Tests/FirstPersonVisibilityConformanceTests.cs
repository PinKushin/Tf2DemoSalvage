using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What the engine hides when the camera is inside somebody's head (B414).</summary>
/// <remarks>
/// **The owner rule belongs to WEAPONS and to nothing else.** <c>C_BaseCombatWeapon::ShouldDraw</c>
/// (<c>c_basecombatweapon.cpp</c>) is the only place it lives:
///
/// <code>
/// C_BaseCombatCharacter *pOwner = GetOwner();
/// if ( !pOwner ) return true;                 // weapon has no owner, always draw it
/// if ( pOwner == pLocalPlayer ) {
///     if ( !bIsActive ) return false;
///     if ( !pOwner->ShouldDraw() ) return false;
///     if ( !ShouldDrawLocalPlayerViewModel() ) return true;   // 3rd person
///     return false;                            // the viewmodel draws it instead
/// }
/// </code>
///
/// **A projectile is not a weapon and has no such override.** <c>CTFBaseRocket : CBaseProjectile</c>
/// (<c>tf_weaponbase_rocket.h:36</c>), and <c>C_TFProjectile_Rocket</c> declares
/// <c>OnDataChanged</c>, <c>CreateTrails</c> and <c>GetTrailParticleName</c> and no <c>ShouldDraw</c>
/// — grepped across every <c>c_tf_projectile_*</c> in the SDK, which returns nothing. So a rocket
/// owned by the player whose eyes you are using is drawn like any other entity.
///
/// **The owner reported it as a thing he could see**: *"for some reason the first person rockets
/// still dont draw, btw, that was never fixed i guess"*. Every rocket he fired while watching his own
/// first-person view was being skipped, because this filter applied the weapon rule to every prop.
///
/// Synthetic (D38): each prop is built here, so the test knows the right answer.
/// </remarks>
public sealed class FirstPersonVisibilityConformanceTests
{
    private const int Viewed = 3;

    /// <remarks>
    /// The defect, and it is the whole projectile family rather than the rocket the owner happened to
    /// notice. **Not one TF projectile overrides <c>ShouldDraw</c>** — grepped across `shared/tf` and
    /// `client/tf`, where the only overrides are `CCaptureFlag`, `CHalloweenGiftPickup`, `CTFItem` and
    /// the spellbook HUD. Every class below therefore inherits a base with no owner rule:
    ///
    /// <code>
    /// C_TFProjectile_Rocket, _Arrow, _EnergyBall, _Flare : C_TFBaseRocket : CBaseProjectile
    /// CTFProjectile_Syringe, _EnergyRing                 : CTFBaseProjectile
    /// CTFGrenadePipebombProjectile                       : CTFWeaponBaseGrenadeProj : CBaseGrenade : CBaseProjectile
    /// </code>
    ///
    /// Each carries <c>m_hOwnerEntity</c> naming whoever fired it, so an owner test that does not ask
    /// whether the prop is a WEAPON hides all of them from the player best placed to see them.
    /// </remarks>
    [TestCase("models/weapons/w_models/w_rocket.mdl", "CTFProjectile_Rocket")]
    [TestCase("models/weapons/w_models/w_stickybomb.mdl", "CTFGrenadePipebombProjectile")]
    [TestCase("models/weapons/w_models/w_grenade_grenadelauncher.mdl", "CTFGrenadePipebombProjectile")]
    [TestCase("models/weapons/w_models/w_arrow.mdl", "CTFProjectile_Arrow")]
    [TestCase("models/weapons/w_models/w_flaregun_shell.mdl", "CTFProjectile_Flare")]
    [TestCase("models/weapons/w_models/w_syringe_proj.mdl", "CTFProjectile_Syringe")]
    public void Visible_AProjectileOwnedByTheViewedPlayer_IsStillDrawn(string model, string className)
    {
        SceneProp projectile = Owned(entity: 40, model, weaponState: null);

        FirstPersonVisibility.Visible([projectile], Viewed)
            .ShouldContain(
                projectile,
                $"{className} has no ShouldDraw override, so the owner rule never reaches it");
    }

    /// <remarks>
    /// The control, and the reason the owner test exists at all: a carried weapon owned by the viewed
    /// player IS hidden, because the viewmodel draws it instead. Without this, "draw everything owned"
    /// would pass the test above and put a rocket launcher across the lens (B190's two sticky launchers).
    /// </remarks>
    [Test]
    public void Visible_AWeaponOwnedByTheViewedPlayer_IsHidden()
    {
        SceneProp launcher = Owned(entity: 41, "models/weapons/w_models/w_rocketlauncher.mdl", weaponState: 1);

        FirstPersonVisibility.Visible([launcher], Viewed).ShouldBeEmpty();
    }

    [Test]
    public void Visible_TheViewedPlayerThemselves_IsHidden()
    {
        SceneProp player = new(Viewed, "models/player/soldier.mdl", SceneModelKind.Studio, new ScenePose());

        FirstPersonVisibility.Visible([player], Viewed).ShouldBeEmpty();
    }

    /// <remarks>
    /// A hat has no origin of its own — <c>EF_BONEMERGE</c> takes the wearer's bones — so keeping it
    /// while hiding the wearer leaves it hanging in mid-picture, which is what the first capture showed.
    /// </remarks>
    [Test]
    public void Visible_ACosmeticAttachedToTheViewedPlayer_IsHidden()
    {
        SceneProp hat = new(
            42,
            "models/player/items/soldier/hat.mdl",
            SceneModelKind.Studio,
            new ScenePose(),
            AttachedTo: Viewed);

        FirstPersonVisibility.Visible([hat], Viewed).ShouldBeEmpty();
    }

    /// <remarks>A weapon somebody ELSE is carrying is drawn, or the world empties around you.</remarks>
    [Test]
    public void Visible_AWeaponOwnedByAnotherPlayer_IsDrawn()
    {
        SceneProp theirs = new(
            43,
            "models/weapons/w_models/w_scattergun.mdl",
            SceneModelKind.Studio,
            new ScenePose(),
            OwnedBy: 9,
            WeaponState: 1);

        FirstPersonVisibility.Visible([theirs], Viewed).ShouldContain(theirs);
    }

    private static SceneProp Owned(int entity, string model, int? weaponState) =>
        new(
            entity,
            model,
            SceneModelKind.Studio,
            new ScenePose(),
            OwnedBy: Viewed,
            WeaponState: weaponState);
}
