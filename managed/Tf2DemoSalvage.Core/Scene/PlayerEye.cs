namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// How far above a player's origin their eyes sit.
/// </summary>
/// <remarks>
/// **A demo carries where a player IS, not where they LOOK FROM.** Both the entity stream and the
/// recorded view in <c>democmdinfo_t</c> give <c>GetAbsOrigin()</c> — the feet — and the client
/// adds the view offset when it draws:
///
/// <code>
/// // baseentity_shared.cpp
/// Vector CBaseEntity::EyePosition( void )
/// {
///     return GetAbsOrigin() + GetViewOffset();
/// }
/// </code>
///
/// That the recorded view really is the origin rather than the eye was measured rather than
/// assumed — the two agree to the hundredth at every tick of every point-of-view demo in the
/// corpus. See <c>docs/findings/01-container.md</c>.
///
/// **Laid out the way the engine lays it out, which is two different shapes.** Standing height is a
/// per-class table; ducking and death are single values shared by every class. Folding them into
/// one table would read more tidily and would misrepresent the source — and the next person to
/// extend it would have to work out which of the columns were real.
/// </remarks>
public static class PlayerEye
{
    /// <summary>
    /// Standing eye height per class, as <c>g_TFClassViewVectors</c> declares it.
    /// </summary>
    /// <remarks>
    /// <c>tf_gamerules.cpp:1326</c>, transcribed in order. Index 0 is
    /// <c>TF_CLASS_UNDEFINED</c> and is also the fallback for anything outside the table, which is
    /// what indexing the engine's array with an unset class does.
    ///
    /// Ten units separate a scout from a sniper, so this is a table rather than a constant for a
    /// reason: one number for everyone is visibly wrong for most of the roster, and wrong in a way
    /// that reads as "the view feels low" rather than as a defect with a cause.
    /// </remarks>
    private static readonly float[] StandingByClass =
    [
        72f,  // TF_CLASS_UNDEFINED
        65f,  // TF_CLASS_SCOUT
        75f,  // TF_CLASS_SNIPER
        68f,  // TF_CLASS_SOLDIER
        68f,  // TF_CLASS_DEMOMAN
        75f,  // TF_CLASS_MEDIC
        75f,  // TF_CLASS_HEAVYWEAPONS
        68f,  // TF_CLASS_PYRO
        75f,  // TF_CLASS_SPY
        68f,  // TF_CLASS_ENGINEER
        65f,  // TF_CLASS_CIVILIAN
    ];

    /// <summary>Eye height of a standing player of the given class.</summary>
    /// <param name="playerClass">The class, as <c>m_iClass</c> networks it.</param>
    /// <returns>Units above the player's origin.</returns>
    /// <remarks>
    /// A class outside the table falls back to <c>TF_CLASS_UNDEFINED</c> rather than to a guessed
    /// middle value. A demo can name a class this build has never heard of — a later game version,
    /// or a corrupt field — and row zero is the engine's own answer for that.
    /// </remarks>
    public static float Standing(int playerClass) =>
        playerClass >= 0 && playerClass < StandingByClass.Length
            ? StandingByClass[playerClass]
            : StandingByClass[0];

    /// <summary>Eye height of a ducked player, whatever their class.</summary>
    /// <param name="playerClass">
    /// The class, accepted so callers need not know that it makes no difference — and so this
    /// keeps working if a later game version makes it matter.
    /// </param>
    /// <returns>Units above the player's origin.</returns>
    /// <remarks>
    /// <c>VEC_DUCK_VIEW</c> on <c>g_TFViewVectors</c>, which is a single vector rather than a
    /// per-class table: a crouched sniper is not a short sniper.
    /// </remarks>
    public static float Ducking(int playerClass)
    {
        _ = playerClass;

        return DuckViewHeight;
    }

    /// <summary>
    /// Eye height when SPECTATING a player in first person, which is not the per-class height.
    /// </summary>
    /// <param name="ducking">Whether the spectated player is crouched.</param>
    /// <returns>Units above their origin.</returns>
    /// <remarks>
    /// **The spectator camera and the player's own camera use different numbers, and this was got
    /// wrong here first.** <c>C_HLTVCamera::CalcInEyeCamView</c>
    /// (<c>src/game/client/hltvcamera.cpp:314</c>) adds the FLAT vectors:
    ///
    /// <code>
    /// m_vCamOrigin = pPlayer->GetAbsOrigin();
    /// if ( pPlayer->GetFlags() &amp; FL_DUCKING ) { m_vCamOrigin += VEC_DUCK_VIEW; }
    /// else                                      { m_vCamOrigin += VEC_VIEW; }
    /// </code>
    ///
    /// <c>VEC_VIEW</c> is <c>g_TFViewVectors.m_vView</c>, a single 72 for everyone — not
    /// <c>g_TFClassViewVectors</c>. The per-class table reaches a player's OWN view through
    /// <c>SetViewOffset( IsDucked() ? VEC_DUCK_VIEW_SCALED(this) : GetClassEyeHeight() )</c>
    /// (<c>tf_player.cpp:16932</c>), and otherwise drives bots and sentry auto-aim.
    ///
    /// So spectating a sniper in TF2 puts the camera three units BELOW where that sniper's own
    /// client would have put it, and spectating a scout seven units above. That is the engine's
    /// behaviour rather than an approximation of it, and copying it is the point.
    /// </remarks>
    public static float Spectated(bool ducking) => ducking ? DuckViewHeight : GenericViewHeight;

    /// <summary>Height a CHASE camera looks over a dead player's ragdoll from.</summary>
    /// <remarks>
    /// <c>VEC_DEAD_VIEWHEIGHT</c>, 14 units. **This is not an in-eye height**, which is the
    /// mistake this comment exists to prevent: <c>CalcInEyeCamView</c> does not use it, it
    /// abandons first person entirely —
    ///
    /// <code>
    /// if ( !pPlayer->IsAlive() )
    /// {
    ///     // if dead, show from 3rd person
    ///     CalcChaseCamView( eyeOrigin, eyeAngles, fov );
    ///     return;
    /// }
    /// </code>
    ///
    /// — and the chase camera then raises its TARGET by this much, with the comment "look over
    /// ragdoll, not through" (<c>hltvcamera.cpp:129</c>). A first-person camera dropped to 14
    /// units would sit inside the corpse, which is exactly what the engine is avoiding.
    /// </remarks>
    public const float DeadChaseTarget = 14f;

    /// <summary><c>VEC_VIEW</c> from <c>g_TFViewVectors</c>: the flat, class-agnostic height.</summary>
    private const float GenericViewHeight = 72f;

    /// <summary><c>VEC_DUCK_VIEW</c> from <c>g_TFViewVectors</c>.</summary>
    private const float DuckViewHeight = 45f;
}
