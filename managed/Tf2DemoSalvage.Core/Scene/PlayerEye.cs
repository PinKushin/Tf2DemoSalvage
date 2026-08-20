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

    /// <summary>Eye height while dead, before the ragdoll takes over.</summary>
    /// <remarks>
    /// <c>VEC_DEAD_VIEWHEIGHT</c>, 14 units — a camera near the floor. TF2 never animates a dying
    /// player and the corpse is a separate <c>CTFRagdoll</c> entity, so this is the height of the
    /// brief view between the two.
    /// </remarks>
    public const float Dead = 14f;

    /// <summary><c>VEC_DUCK_VIEW</c> from <c>g_TFViewVectors</c>.</summary>
    private const float DuckViewHeight = 45f;
}
