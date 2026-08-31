using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A disguised spy is drawn as their disguise — to the enemy, and only to the enemy.
/// </summary>
/// <remarks>
/// **Owner's report:** *"a spy looked like a blue spy and a red demo at the same time"*, and the
/// project had never read a disguise field at all — the string "Disguise" appeared zero times in the
/// managed tree while the recording carries `m_nDisguiseClass`, `m_nDisguiseTeam`, `m_iDisguiseBody`,
/// `m_hDisguiseTarget`, `m_hDisguiseWeapon` and `m_iDisguiseHealth`.
///
/// **The model, `C_TFPlayer::ValidateModelIndex` (`c_tf_player.cpp:8990`)**, in order:
///
/// <code>
///   if ( DISGUISED_AS_DISPENSER &amp;&amp; IsEnemyPlayer() &amp;&amp; ducking &amp;&amp; on ground )
///       dispenser_light.mdl
///   else if ( InCond( TF_COND_DISGUISED ) &amp;&amp; IsEnemyPlayer() )
///       GetPlayerClassData( GetDisguiseClass() )-&gt;GetModelName()
///   else if ( InCond( TF_COND_HALLOWEEN_GHOST_MODE ) )
///       ghost_no_hat.mdl, by team
///   else
///       GetPlayerClass()-&gt;GetModelName()
/// </code>
///
/// **The skin, `C_TFPlayer::GetSkin` (`c_tf_player.cpp:7790`)**: the visible TEAM becomes the
/// disguise team under the same condition, RED maps to 0 and BLUE to 1, and then the spy mask adds
/// an offset:
///
/// <code>
///   if ( bCheckSpyMask &amp;&amp; InCond( TF_COND_DISGUISED ) )
///   {
///       if ( !IsEnemyPlayer() )
///           nSkin += 4 + ( ( GetDisguiseClass() - TF_FIRST_NORMAL_CLASS ) * 2 );
///       else if ( GetDisguiseClass() == TF_CLASS_SPY )
///           nSkin += 4 + ( ( GetDisguiseMask() - TF_FIRST_NORMAL_CLASS ) * 2 );
///   }
/// </code>
///
/// **`IsEnemyPlayer()` is the axis the whole feature turns on** (`c_tf_player.cpp:5384`): it
/// compares against the LOCAL player's team, which in a point-of-view recording is the recorder's.
/// A friendly spy keeps their own model and their own team's skin and gains only the mask offset —
/// which is how a teammate can see who is disguised. Implementing the disguise without this would
/// hide every friendly spy behind their disguise, which is the opposite of what the game does.
///
/// **Branches deliberately NOT implemented, named with their citations rather than omitted:**
///
/// - **Dispenser disguise** (`TF_COND_DISGUISED_AS_DISPENSER`, first branch of both functions). It
///   needs `FL_DUCKING` and `GetGroundEntity() != NULL`; the ground entity is a handle this project
///   does not read, and guessing it from `FL_ONGROUND` would be a different condition wearing the
///   same name.
/// - **Halloween ghost mode** (third model branch) — a separate cosmetic mode, not part of the
///   disguise.
/// - **Invulnerability's `nSkin += 2`** and **`AdjustSkinIndexForZombie`** (`GetSkin` steps 4 and
///   5). Both set `bCheckSpyMask = false`, so they SUPPRESS the mask offset — meaning an übered
///   disguised spy will draw with a mask offset here where the engine draws none. That is a real
///   divergence and it is written down rather than discovered later.
/// - **`m_nDisguiseSkinOverride`** and **`m_iDisguiseBody`**, which are carried by the recording and
///   change a disguise's appearance beyond class and team.
/// </remarks>
public sealed class DisguiseConformanceTests
{
    /// <summary><c>TF_CLASS_DEMOMAN</c>, <c>tf_shareddefs.h:210</c>.</summary>
    private const int Demoman = 4;

    /// <summary><c>TF_CLASS_SPY</c>, <c>tf_shareddefs.h:214</c>.</summary>
    private const int Spy = 8;

    /// <summary><c>TF_CLASS_MEDIC</c>, <c>tf_shareddefs.h:211</c>.</summary>
    private const int Medic = 5;

    [Test]
    public void Model_ADisguisedEnemySpy_DrawsTheDisguiseClass()
    {
        // The owner's case: a BLU spy disguised as a RED demoman, seen by a RED recorder.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Demoman, disguiseTeam: SceneTeams.Red, enemy: true);

        Disguise.VisibleClass(spy).ShouldBe(Demoman, "the enemy sees the disguise, not the spy");
    }

    [Test]
    public void Model_ADisguisedFRIENDLYSpy_DrawsTheirOwnClass()
    {
        // **The axis the feature turns on.** `IsEnemyPlayer()` gates the disguise branch, so a
        // teammate sees the spy as a spy — which is how their own team can tell who is disguised.
        // Without this the feature hides friendly spies, the exact opposite of the game.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Demoman, disguiseTeam: SceneTeams.Red, enemy: false);

        Disguise.VisibleClass(spy).ShouldBe(Spy, "a teammate sees through it");
    }

    [Test]
    public void Model_AnUndisguisedSpy_DrawsTheirOwnClass()
    {
        // The control on the condition itself: without `TF_COND_DISGUISED` the disguise fields may
        // still hold stale values from a previous disguise, and reading them unconditionally would
        // draw a spy as whoever they last impersonated.
        ScenePlayer spy = Player(team: SceneTeams.Blu, playerClass: Spy) with
        {
            DisguiseClass = Demoman,
            DisguiseTeam = SceneTeams.Red,
            IsEnemy = true,
        };

        Disguise.VisibleClass(spy).ShouldBe(Spy, "not disguised, so the fields mean nothing");
    }

    [Test]
    public void Skin_ADisguisedEnemySpy_TakesTheDisguiseTeam()
    {
        // `iVisibleTeam = m_Shared.GetDisguiseTeam()`, then RED -> 0 and BLUE -> 1. A BLU spy
        // disguised as RED must draw in RED's skin family or the disguise is transparent.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Demoman, disguiseTeam: SceneTeams.Red, enemy: true);

        Disguise.VisibleSkin(spy).ShouldBe(0, "RED is skin 0, and the disguise team is what shows");
    }

    [Test]
    public void Skin_AnUndisguisedPlayer_TakesTheirOwnTeam()
    {
        // The control: BLU is skin 1, and nothing about a disguise applies.
        Disguise.VisibleSkin(Player(team: SceneTeams.Blu, playerClass: Medic)).ShouldBe(1);
    }

    [Test]
    public void Skin_ADisguisedFRIENDLYSpy_GainsTheMaskOffset()
    {
        // `nSkin += 4 + ( ( GetDisguiseClass() - TF_FIRST_NORMAL_CLASS ) * 2 )` for a teammate.
        // BLU is 1; disguised as a demoman (class 4) gives 1 + 4 + (4 - 1) * 2 = 11.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Demoman, disguiseTeam: SceneTeams.Red, enemy: false);

        Disguise.VisibleSkin(spy).ShouldBe(
            11, "a teammate sees the spy in their own team's colours wearing the disguise's mask");
    }

    [Test]
    public void Skin_AnEnemySpyDisguisedAsASpy_TakesTheMaskClass()
    {
        // **The second mask branch, and the only place `m_nMaskClass` is read.**
        // `else if ( GetDisguiseClass() == TF_CLASS_SPY )` — a spy impersonating a spy wears the
        // mask of whoever the MASK class says, not of the disguise class. RED is 0; mask class
        // medic (5) gives 0 + 4 + (5 - 1) * 2 = 12.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Spy, disguiseTeam: SceneTeams.Red, enemy: true)
            with
            { DisguiseMaskClass = Medic };

        Disguise.VisibleSkin(spy).ShouldBe(12);
    }

    [Test]
    public void Skin_AnEnemySpyDisguisedAsAnyoneElse_GainsNoMaskOffset()
    {
        // **The control on that branch.** The enemy mask offset applies ONLY when the disguise
        // class is spy; an enemy disguised as a demoman shows a plain demoman. Without this,
        // "reads the mask branch" and "always adds an offset" are the same observation, and every
        // disguised enemy would draw in a skin family that may not exist on their model.
        ScenePlayer spy = Disguised(
            team: SceneTeams.Blu, disguiseClass: Demoman, disguiseTeam: SceneTeams.Red, enemy: true)
            with
            { DisguiseMaskClass = Medic };

        Disguise.VisibleSkin(spy).ShouldBe(0, "a demoman disguise is just a demoman");
    }

    private static ScenePlayer Disguised(
        int team, int disguiseClass, int disguiseTeam, bool enemy) =>
        Player(team, Spy) with
        {
            Conditions = new PlayerConditions(1 << PlayerConditions.Disguised, 0, 0, 0, 0),
            DisguiseClass = disguiseClass,
            DisguiseTeam = disguiseTeam,
            IsEnemy = enemy,
        };

    private static ScenePlayer Player(int team, int playerClass) =>
        new(
            EntityIndex: 3,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Team: team,
            Health: 125,
            PlayerClass: playerClass);
}
