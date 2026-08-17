using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The activities TF2's animation state can choose for a player's body.
/// </summary>
/// <remarks>
/// **Named as the engine names them**, because the name is the lookup: `mstudioseqdesc_t.activity`
/// is documented in <c>studio.h</c> as "initialized at loadtime to game DLL values", so a model file
/// does not store the number — it stores <c>szactivitynameindex</c>, the activity's NAME, and the
/// game resolves it. Matching on the name is therefore how a sequence is found, and guessing at
/// sequence names like <c>run_PRIMARY</c> is not.
///
/// Only the movement activities are here. Attacking, reloading, taunting and the rest exist and are
/// chosen by state this project does not decode yet (B100).
/// </remarks>
public enum PlayerActivity
{
    /// <summary>Standing still. The engine's starting value before any handler runs.</summary>
    StandIdle,

    /// <summary>Moving on foot. TF2 never walks — see <see cref="PlayerActivityState"/>.</summary>
    Run,

    /// <summary>Crouched and still.</summary>
    CrouchIdle,

    /// <summary>Crouched and moving.</summary>
    CrouchWalk,

    /// <summary>Airborne.</summary>
    Jump,

    /// <summary>In water at least waist deep and still.</summary>
    SwimIdle,

    /// <summary>In water at least waist deep and moving.</summary>
    Swim,

    /// <summary>Dead.</summary>
    Die,
}

/// <summary>
/// Chooses a player's body activity from the state a demo carries.
/// </summary>
/// <remarks>
/// **A demo never networks a player's sequence, so this has to be recomputed rather than read.** The
/// server sends position, flags and health; the client's <c>CTFPlayerAnimState</c> turns those into
/// an activity and then into a sequence. A viewer that wants the right animation has to do the same.
///
/// **This is `CMultiPlayerAnimState::CalcMainActivity`**, whose whole shape is the order it asks in:
///
/// <code>
/// Activity idealActivity = ACT_MP_STAND_IDLE;
///
/// if ( HandleJumping( idealActivity ) ||
///      HandleDucking( idealActivity ) ||
///      HandleSwimming( idealActivity ) ||
///      HandleDying( idealActivity ) )
/// { }
/// else
/// {
///     HandleMoving( idealActivity );
/// }
/// </code>
///
/// The order is the specification: a crouching player who is also moving crouch-walks rather than
/// runs, and an airborne one jumps whatever else is true. Standing idle is the value it starts from,
/// so it is what remains when nothing else applies.
///
/// **TF2 has no walk.** `HandleMoving` carries the comment "In TF we run all the time now" and sets
/// <c>ACT_MP_RUN</c> for any speed above the threshold — there is no walk activity to choose, which
/// is why the previous two-state guess was not as wrong as it looked for a player on flat ground.
/// What it missed was crouching, jumping, swimming and dying.
/// </remarks>
public static class PlayerActivityState
{
    /// <summary>
    /// Below this, a player counts as standing still.
    /// </summary>
    /// <remarks>
    /// <c>MOVING_MINIMUM_SPEED</c> from <c>multiplayer_animstate.cpp</c>. Half a unit a second is
    /// slow enough that only genuine stillness falls under it, and non-zero so that floating point
    /// noise in an interpolated position does not read as walking.
    /// </remarks>
    public const float MovingMinimumSpeed = 0.5f;

    /// <summary>At rest on the ground — <c>FL_ONGROUND</c>.</summary>
    public const int OnGround = 1 << 0;

    /// <summary>Fully crouched — <c>FL_DUCKING</c>.</summary>
    public const int Ducking = 1 << 1;

    /// <summary>
    /// Crouching or standing up, possibly mid-transition — <c>FL_ANIMDUCKING</c>.
    /// </summary>
    /// <remarks>
    /// Not used for the activity, and recorded so nobody reaches for it thinking it is the crouch
    /// flag. <c>const.h</c> spells the combination out: fully ducked is both flags, and
    /// <c>FL_DUCKING</c> without this one means previously ducked and now standing up.
    /// </remarks>
    public const int AnimDucking = 1 << 2;

    /// <summary>Standing in water — <c>FL_INWATER</c>.</summary>
    public const int InWater = 1 << 9;

    /// <summary>Chooses the activity for a player's body.</summary>
    /// <param name="flags">The player's <c>m_fFlags</c>.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="waistDeep">Whether the water is at least waist deep.</param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <returns>The activity the engine would choose.</returns>
    public static PlayerActivity For(int flags, float speed, bool waistDeep, bool alive)
    {
        bool moving = speed > MovingMinimumSpeed;

        // **Airborne first, and it outranks everything.** The engine tracks a jump explicitly and
        // clears it once the player has been back on the ground for a fifth of a second; a demo
        // carries no such event, so this reads the ground flag instead. That is an interpolation,
        // flagged as one: it agrees with the engine for an ordinary jump and differs for the
        // moment after landing, where the engine holds the jump a little longer.
        if ((flags & OnGround) == 0 && !waistDeep && alive)
        {
            return PlayerActivity.Jump;
        }

        // Then crouching, so a crouching player who is also moving crouch-walks rather than runs.
        if ((flags & Ducking) != 0 && alive)
        {
            return moving ? PlayerActivity.CrouchWalk : PlayerActivity.CrouchIdle;
        }

        if (waistDeep && alive)
        {
            return moving ? PlayerActivity.Swim : PlayerActivity.SwimIdle;
        }

        if (!alive)
        {
            return PlayerActivity.Die;
        }

        // And what is left. Standing idle is the engine's starting value rather than a case it
        // chooses, which is why HandleMoving only ever sets the running one.
        return moving ? PlayerActivity.Run : PlayerActivity.StandIdle;
    }

    /// <summary>The engine's name for an activity, which is what a model file stores.</summary>
    /// <param name="activity">The activity.</param>
    /// <returns>Its <c>ACT_MP_</c> name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The activity is not one of the known values.</exception>
    /// <remarks>
    /// Thrown rather than defaulted for an unknown value: a silently wrong activity name resolves
    /// to no sequence and freezes the model in its reference pose, which reads as a model bug.
    /// </remarks>
    public static string NameOf(PlayerActivity activity) => activity switch
    {
        PlayerActivity.StandIdle => "ACT_MP_STAND_IDLE",
        PlayerActivity.Run => "ACT_MP_RUN",
        PlayerActivity.CrouchIdle => "ACT_MP_CROUCH_IDLE",
        PlayerActivity.CrouchWalk => "ACT_MP_CROUCHWALK",
        PlayerActivity.Jump => "ACT_MP_JUMP",
        PlayerActivity.SwimIdle => "ACT_MP_SWIM_IDLE",
        PlayerActivity.Swim => "ACT_MP_SWIM",
        PlayerActivity.Die => "ACT_MP_DIE",
        _ => throw new ArgumentOutOfRangeException(nameof(activity)),
    };
}
