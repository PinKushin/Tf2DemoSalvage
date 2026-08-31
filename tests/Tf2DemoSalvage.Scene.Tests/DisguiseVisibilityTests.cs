using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A disguise's cosmetics and weapon are shown to the ENEMY, and to nobody else.
/// </summary>
/// <remarks>
/// **Found by the owner watching the demo and naming the ticks:** *"tick 870 is where i can see
/// him, till 903 ... its a soldier not a demo drawing inside the spy"*, then later *"he disguised as
/// a demo later actually and hes holding a pipe launcher so yea"*. Measured at those exact ticks:
///
/// <code>
///   SPY 2 class 8 team 3 enemy False as 3/2
///   PROP-AT-SPY 1031 '.../items/soldier/hwn2023_warlocks_warcloak.mdl' attached 2
///   PROP-AT-SPY 1032 '.../items/soldier/dec23_wanderers_wool.mdl'      attached 2
///   PROP-AT-SPY 1034 '.../items/soldier/sum26_inglorious_patriot.mdl'  attached 2
///   PROP-AT-SPY 1015 '.../c_rocketlauncher.mdl'                        attached 2
/// </code>
///
/// The server sends a disguise's cosmetics and weapon as their own entities, bone-merged to the spy,
/// so an ENEMY sees a convincing soldier. This project drew all of them regardless of who is
/// looking, which put soldier hats and a rocket launcher on a spy's skeleton.
///
/// **`CTFWearable::ShouldDraw`, `tf_item_wearable.cpp:344`:**
///
/// <code>
///   if ( pOwner-&gt;m_Shared.InCond( TF_COND_DISGUISED ) &amp;&amp; !IsViewModelWearable() )
///   {
///       if ( m_bDisguiseWearable &amp;&amp; pLocalPlayer )
///       {
///           if ( GetEnemyTeam( pOwner-&gt;GetTeamNumber() ) != iLocalPlayerTeam )
///               return false;                    // on the spy's team: we do not see the disguise
///           else if ( disguiseClass == TF_CLASS_SPY &amp;&amp; disguiseTeam == iLocalPlayerTeam )
///               return false;                    // an enemy spy disguised as one of ours
///           else
///               return BaseClass::ShouldDraw();  // an enemy: show it
///       }
///       return false;                            // his OWN cosmetics, hidden while disguised
///   }
/// </code>
///
/// **The last line is the part worth stating**: while a spy is disguised his own cosmetics are
/// hidden from EVERYONE, not merely swapped for the disguise's. A teammate sees a bare spy.
///
/// **`CTFWeaponBase::ShouldDraw`, `tf_weaponbase.cpp:3226`, is the mirror image:**
///
/// <code>
///   if ( iLocalPlayerTeam != pOwner-&gt;GetTeamNumber() &amp;&amp; iLocalPlayerTeam != TEAM_SPECTATOR )
///       if ( GetDisguiseWeapon() != this ) return false;   // enemy: ONLY the disguise weapon
///   else
///       if ( m_bDisguiseWeapon ) return false;             // friendly: never the disguise weapon
/// </code>
///
/// **Not implemented, named rather than omitted:** the coaching substitution in both functions
/// (`m_bIsCoaching` / `m_hStudent`), Halloween ghost mode's `ghost_wearable` tag, the sniper-zoom
/// hide, and `TEAM_SPECTATOR`'s branch in the weapon rule — a SourceTV recording has no local
/// player, and this treats that as "not an enemy", which draws the spy's own loadout.
/// </remarks>
public sealed class DisguiseVisibilityTests
{
    /// <summary><c>TF_CLASS_SOLDIER</c>.</summary>
    private const int Soldier = 3;

    /// <summary><c>TF_CLASS_SPY</c>.</summary>
    private const int Spy = 8;

    [Test]
    public void Visible_ADisguisesCosmeticOnATeammatesSpy_IsHidden()
    {
        // The owner's exact case: soldier hats on a friendly spy.
        List<SceneProp> drawn = [Wearable(ofDisguise: true), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [FriendlySpy()]));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WearableEntity);
    }

    [Test]
    public void Visible_ADisguisesCosmeticOnAnEnemySpy_IsShown()
    {
        // **The control that stops this becoming "hide all disguise gear".** The whole point of the
        // disguise is that the enemy sees it, so a filter that hid it from everyone would swap one
        // divergence for a worse one.
        List<SceneProp> drawn = [Wearable(ofDisguise: true), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [EnemySpy()]));

        drawn.ShouldContain(prop => prop.EntityIndex == WearableEntity);
    }

    [Test]
    public void Visible_ASpysOWNCosmeticWhileDisguised_IsHiddenFromEveryone()
    {
        // `return false` at the end of the outer `if` — his own hats go too, not just get swapped.
        // A teammate sees a bare spy, which is the line easiest to miss when reading the branch.
        List<SceneProp> drawn = [Wearable(ofDisguise: false), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [FriendlySpy()]));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WearableEntity);
    }

    [Test]
    public void Visible_ACosmeticOnAnUNDISGUISEDPlayer_IsUntouched()
    {
        // **The control on the condition itself.** The whole rule sits inside
        // `if ( InCond( TF_COND_DISGUISED ) )`, so an ordinary player's hats must be immune — and
        // without this, "hides disguise gear" and "hides cosmetics" are the same observation.
        List<SceneProp> drawn = [Wearable(ofDisguise: false), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [Undisguised()]));

        drawn.ShouldContain(prop => prop.EntityIndex == WearableEntity);
    }

    [Test]
    public void Visible_ADisguisesWEAPONOnATeammatesSpy_IsHidden()
    {
        // `if ( m_bDisguiseWeapon ) return false;` for a friendly — the rocket launcher and the
        // pipe launcher the owner saw.
        List<SceneProp> drawn = [Weapon(ofDisguise: true), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [FriendlySpy()]));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WeaponEntity);
    }

    [Test]
    public void Visible_ASpysREALWeaponSeenByAnEnemy_IsHidden()
    {
        // **The mirror image, and it is not the same test.** `if ( GetDisguiseWeapon() != this )
        // return false` — an enemy sees ONLY the disguise weapon, so the spy's actual revolver is
        // hidden from them. Getting one direction right and not the other leaves a spy holding two
        // weapons to somebody.
        List<SceneProp> drawn = [Weapon(ofDisguise: false), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [EnemySpy()]));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WeaponEntity);
    }

    [Test]
    public void Visible_AnEnemySpyDisguisedAsOurOwnTeam_ShowsNoDisguiseGear()
    {
        // `if ( disguiseClass == TF_CLASS_SPY && disguiseTeam == iLocalPlayerTeam ) return false`.
        // An enemy spy impersonating one of OUR spies shows nothing, so his cosmetics cannot give
        // him away.
        //
        // **The team here is the LOCAL player's, and a first draft got that backwards.** The spy is
        // BLU and is our enemy, so we are RED — the rule fires when he is disguised as a RED spy,
        // not a BLU one. Setting the disguise team to his OWN team describes a spy pretending to be
        // on his own side, which we are meant to see straight through.
        List<SceneProp> drawn = [Wearable(ofDisguise: true), Player()];

        ScenePlayer spy = EnemySpy() with { DisguiseClass = Spy, DisguiseTeam = SceneTeams.Red };

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [spy]));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WearableEntity);
    }

    [Test]
    public void Visible_ThePlayerProp_IsNeverRemoved()
    {
        // The control that keeps this a filter on GEAR. The spy's own body is decided by
        // `Disguise.VisibleClass`, and a visibility rule that removed players would blank the spy
        // rather than undress him.
        List<SceneProp> drawn = [Wearable(ofDisguise: true), Player()];

        DrawList.KeepOnly(drawn, DisguiseVisibility.Visible(drawn, [FriendlySpy()]));

        drawn.ShouldContain(prop => prop.EntityIndex == SpyEntity);
    }

    private const int SpyEntity = 2;
    private const int WearableEntity = 1031;
    private const int WeaponEntity = 1015;

    /// <summary>A BLU spy disguised as a RED soldier, seen by a BLU recorder.</summary>
    private static ScenePlayer FriendlySpy() => Undisguised() with
    {
        Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
        DisguiseClass = Soldier,
        DisguiseTeam = SceneTeams.Red,
        IsEnemy = false,
    };

    /// <summary>The same disguise, seen by the other side.</summary>
    private static ScenePlayer EnemySpy() => FriendlySpy() with { IsEnemy = true };

    private static ScenePlayer Undisguised() =>
        new(SpyEntity, 0f, 0f, 0f, Team: SceneTeams.Blu, Health: 125, PlayerClass: Spy);

    private static SceneProp Wearable(bool ofDisguise) =>
        Gear(WearableEntity, "models/workshop/player/items/soldier/hat.mdl", ofDisguise, weapon: false);

    private static SceneProp Weapon(bool ofDisguise) =>
        Gear(WeaponEntity, "models/weapons/c_models/c_rocketlauncher.mdl", ofDisguise, weapon: true);

    private static SceneProp Gear(int entity, string model, bool ofDisguise, bool weapon) =>
        new(
            EntityIndex: entity,
            ModelPath: model,
            Kind: SceneModelKind.Studio,
            Pose: default,
            AttachedTo: SpyEntity,
            AttachmentPoint: null,
            OwnedBy: SpyEntity,
            WeaponState: weapon ? 2 : null,
            BoneMerged: true,
            ItemDefinitionIndex: 1,
            ClassName: weapon ? "CTFRocketLauncher" : "CTFWearable",
            OfDisguise: ofDisguise);

    private static SceneProp Player() =>
        new(
            EntityIndex: SpyEntity,
            ModelPath: "models/player/spy.mdl",
            Kind: SceneModelKind.Studio,
            Pose: default);
}
