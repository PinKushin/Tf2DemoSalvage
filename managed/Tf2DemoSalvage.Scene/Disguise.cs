using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which class and skin a player is DRAWN as, once a spy's disguise is taken into account.
/// </summary>
/// <remarks>
/// **This project had never read a disguise field.** The string "Disguise" appeared zero times in
/// the managed tree while the owner's recording carries `m_nDisguiseClass`, `m_nDisguiseTeam`,
/// `m_iDisguiseBody`, `m_hDisguiseTarget`, `m_hDisguiseWeapon` and `m_iDisguiseHealth` — measured,
/// not assumed. The symptom reached the owner as *"a spy looked like a blue spy and a red demo at
/// the same time"*.
///
/// **Two engine functions, and both branch on the same two things** — the condition and whose side
/// you are on:
///
/// - `C_TFPlayer::ValidateModelIndex`, `c_tf_player.cpp:8990`, picks the MODEL.
/// - `C_TFPlayer::GetSkin`, `c_tf_player.cpp:7790`, picks the SKIN.
///
/// **`IsEnemyPlayer()` is the axis the whole feature turns on** (`c_tf_player.cpp:5384`). It
/// compares against the LOCAL player's team, which in a point-of-view recording is the recorder's.
/// A friendly spy keeps their own model and their own team's skin and gains only a mask offset —
/// which is how their team can see who is disguised. An implementation without it hides every
/// friendly spy behind their disguise, the opposite of what the game does.
///
/// **What is NOT implemented, named with citations rather than omitted**, because a divergence
/// nobody wrote down is the shape of every bug this file exists to fix:
///
/// - **Dispenser disguise** (`TF_COND_DISGUISED_AS_DISPENSER`, the first branch of both functions).
///   It needs `FL_DUCKING` and `GetGroundEntity() != NULL`; the ground entity is a handle this
///   project does not read, and substituting `FL_ONGROUND` would be a different condition wearing
///   the same name.
/// - **Halloween ghost mode**, the third model branch — a separate cosmetic mode.
/// - **`nSkin += 2` for invulnerability** and **`AdjustSkinIndexForZombie`**, `GetSkin` steps 4 and
///   5. Both set `bCheckSpyMask = false`, so they SUPPRESS the mask offset: an übered disguised spy
///   draws with a mask offset here where the engine draws none.
/// - **`m_nDisguiseSkinOverride`** and **`m_iDisguiseBody`**, both carried by the recording and both
///   changing a disguise's appearance beyond class and team.
/// - **`m_hDisguiseWeapon`**, the weapon a disguise appears to hold. Carried, unread; the spy will
///   hold their own weapon.
/// </remarks>
public static class Disguise
{
    /// <summary><c>TF_CLASS_SPY</c>, <c>tf_shareddefs.h:214</c>.</summary>
    public const int SpyClass = 8;

    /// <summary><c>TF_FIRST_NORMAL_CLASS</c>, <c>tf_shareddefs.h:198</c> — undefined plus one.</summary>
    private const int FirstNormalClass = 1;

    /// <summary>What the mask offset starts at, before the per-class stride.</summary>
    /// <remarks>
    /// Skins 0 and 1 are the two teams and 2 and 3 are their invulnerable forms, so the mask
    /// families begin at 4 — `nSkin += 4 + ( ( class - TF_FIRST_NORMAL_CLASS ) * 2 )`. The stride is
    /// two because each masked class has a RED and a BLU family of its own.
    /// </remarks>
    private const int FirstMaskSkin = 4;

    /// <summary>How many skin families each masked class occupies: one per team.</summary>
    private const int MaskStride = 2;

    /// <summary>Which class this player is drawn as.</summary>
    /// <param name="player">The player, with their conditions and disguise already decoded.</param>
    /// <returns>The disguise's class to an enemy, otherwise their own.</returns>
    /// <remarks>
    /// The second branch of `ValidateModelIndex`:
    /// <c>else if ( m_Shared.InCond( TF_COND_DISGUISED ) &amp;&amp; IsEnemyPlayer() )</c>, which takes
    /// the model from `GetPlayerClassData( GetDisguiseClass() )`.
    ///
    /// **The condition is checked, not merely the field.** A spy's disguise class holds whoever they
    /// last impersonated after the disguise drops, so reading it unconditionally would draw them as
    /// that person for the rest of the round.
    /// </remarks>
    public static int? VisibleClass(ScenePlayer player) =>
        IsDisguisedToUs(player) && player.DisguiseClass is { } disguised
            ? disguised
            : player.PlayerClass;

    /// <summary>Which skin family this player is drawn in.</summary>
    /// <param name="player">The player, with their conditions and disguise already decoded.</param>
    /// <returns>The skin index, mask offset included.</returns>
    /// <remarks>
    /// `GetSkin`'s steps 2, 3 and 6. The visible TEAM becomes the disguise team for an enemy, RED
    /// maps to 0 and BLU to 1, and then the mask offset applies — from the DISGUISE class for a
    /// teammate, and from the MASK class for an enemy who is disguised as a spy.
    ///
    /// **The two mask branches are exclusive and asymmetric, which is the part worth stating.** A
    /// teammate always sees the mask; an enemy sees it only when the disguise is itself a spy —
    /// because that is the case where the model being drawn HAS mask families to offset into.
    /// </remarks>
    public static int VisibleSkin(ScenePlayer player)
    {
        int team = IsDisguisedToUs(player) && player.DisguiseTeam is { } disguiseTeam
            ? disguiseTeam
            : player.Team ?? SceneTeams.Red;

        int skin = PlayerSkin.ForTeam(team);

        if (!player.Conditions.Has(PlayerConditions.Disguised))
        {
            return skin;
        }

        // `if ( !IsEnemyPlayer() ) nSkin += 4 + ( ( GetDisguiseClass() - TF_FIRST_NORMAL_CLASS ) * 2 )`
        if (!player.IsEnemy && player.DisguiseClass is { } worn)
        {
            return skin + Mask(worn);
        }

        // `else if ( m_Shared.GetDisguiseClass() == TF_CLASS_SPY )` — and then the MASK class, which
        // `GetDisguiseMask` reads from `m_nMaskClass` (`tf_player_shared.h:375`). A spy impersonating
        // a spy wears somebody else's mask, and this is the only place that field is read.
        if (player.IsEnemy &&
            player.DisguiseClass == SpyClass &&
            player.DisguiseMaskClass is { } mask)
        {
            return skin + Mask(mask);
        }

        return skin;
    }

    /// <summary>The skin offset for one masked class.</summary>
    private static int Mask(int playerClass) =>
        FirstMaskSkin + ((playerClass - FirstNormalClass) * MaskStride);

    /// <summary>Whether this player's disguise is one WE are meant to be fooled by.</summary>
    /// <remarks>
    /// Both engine branches spell it <c>InCond( TF_COND_DISGUISED ) &amp;&amp; IsEnemyPlayer()</c>,
    /// and keeping the pair in one place is what stops the model and the skin ever disagreeing about
    /// whether somebody is disguised — which would draw a demoman in a spy's skin.
    /// </remarks>
    private static bool IsDisguisedToUs(ScenePlayer player) =>
        player.Conditions.Has(PlayerConditions.Disguised) && player.IsEnemy;
}
