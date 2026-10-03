namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The two animation flags of a class script, `TFPlayerClassData_t` (B437).</summary>
/// <param name="DontDoAirwalk">`m_bDontDoAirwalk`: the class never reaches `HandleJumping`'s air-walk block.</param>
/// <param name="DontDoNewJump">
/// `m_bDontDoNewJump`: the class jumps as the single `ACT_MP_JUMP` and its jump lands with no gesture.
/// </param>
/// <remarks>
/// **The default is the engine's default**, not a guess: `tf_classdata.cpp:187-188` reads both with
/// `GetInt( …, 0 ) &gt; 0`, so a script that omits a key — or a class with no script — describes a class that
/// air-walks and jumps the new way. Measured on the shipped scripts: the soldier and the medic set
/// `DontDoNewJump`, the medic `DontDoAirwalk` (`ClassAirwalkTests`).
/// </remarks>
public readonly record struct ClassAnimationScript(bool DontDoAirwalk, bool DontDoNewJump);

/// <summary>The class scripts the client reads, `scripts/playerclasses/*.txt`, carried in from the installed game.</summary>
/// <remarks>
/// **The demo does not carry them and Core cannot read the game**, so whoever opened the install hands them to
/// <see cref="DemoTimeline.Build"/>. A demo decoded without them takes the engine's default for every class.
/// </remarks>
public interface IClassAnimationScripts
{
    /// <summary>A class's two flags.</summary>
    /// <param name="playerClass">The class number as the demo reports it, or null when unknown.</param>
    /// <returns>The flags, or the default when the class has no script.</returns>
    public ClassAnimationScript ScriptOf(int? playerClass);

    /// <summary>
    /// Whether a class's model has a sequence for the crouch walk its player table translates to —
    /// `SelectWeightedSequence( TranslateActivity( ACT_MP_CROUCHWALK ) ) &gt;= 0` (B437).
    /// </summary>
    /// <param name="playerClass">The class number as the demo reports it, or null when unknown.</param>
    /// <param name="table">The player's own activity table this frame.</param>
    /// <returns>False only when the model is known to lack it.</returns>
    /// <remarks>
    /// **`DoAnimationEvent` and `HandleJumping` drop `bInDuck` when this is false** (`tf_playeranimstate.cpp:971-975`,
    /// `:1429-1433`). Only the player's table is applied: a table that rewrites the crouch walk (the loser's, the
    /// carrier's) leaves a name no weapon table rewrites again, so the model alone answers it. A table that does not
    /// leaves the weapon's role to decide, which is not known at decode, and the answer is true — the flag stands.
    /// Not ported: the item's own `GetActivityOverride`. Without an install every answer is true.
    /// </remarks>
    public bool HasCrouchWalk(int? playerClass, PlayerActivityOverride table) => true;
}
