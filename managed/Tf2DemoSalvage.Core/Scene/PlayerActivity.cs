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

    /// <summary>Airborne, in the first half second — the push-off.</summary>
    /// <remarks>
    /// <c>CTFPlayerAnimState::HandleJumping</c> splits a jump in two:
    /// <c>if ( gpGlobals->curtime - m_flJumpStartTime > 0.5 ) idealActivity = ACT_MP_JUMP_FLOAT;
    /// else idealActivity = ACT_MP_JUMP_START;</c>. Both are real animations in every class model,
    /// and playing float throughout skips the launch entirely.
    ///
    /// **Not gated on <c>m_bDontDoNewJump</c>, which the engine checks first.** That flag comes from
    /// a class script and every shipped class has it false — the branch it guards sets the old
    /// single <c>ACT_MP_JUMP</c>, and the comment beside it reads "Remove me once all classes are
    /// doing the new jump". Reading it would be reproducing a migration that finished.
    /// </remarks>
    JumpStart,

    /// <summary>Airborne, after the push-off.</summary>
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

    /// <summary>How long a jump plays its push-off before becoming a float.</summary>
    /// <remarks>
    /// <c>gpGlobals->curtime - m_flJumpStartTime > 0.5</c> in
    /// <c>CTFPlayerAnimState::HandleJumping</c>. A strict comparison there, so exactly half a second
    /// is still the start.
    /// </remarks>
    public const float JumpStartSeconds = 0.5f;

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
    public static PlayerActivity For(int flags, float speed, bool waistDeep, bool alive) =>
        For(flags, speed, waistDeep, alive, airborneSeconds: null);

    /// <summary>The same, knowing how long the player has been off the ground.</summary>
    /// <param name="flags">The player's <c>m_fFlags</c>.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="waistDeep">Whether the water reaches the waist.</param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="airborneSeconds">
    /// How long since they left the ground, or null when it cannot be told. The engine measures
    /// this from <c>m_flJumpStartTime</c>, set when the jump event arrives; a demo carries no such
    /// event, so a caller derives it from when the ground flag cleared.
    /// </param>
    /// <returns>The activity the engine would choose.</returns>
    public static PlayerActivity For(
        int flags, float speed, bool waistDeep, bool alive, float? airborneSeconds)
    {
        bool moving = speed > MovingMinimumSpeed;

        // **Airborne first, and it outranks everything.** The engine tracks a jump explicitly and
        // clears it once the player has been back on the ground for a fifth of a second; a demo
        // carries no such event, so this reads the ground flag instead. That is an interpolation,
        // flagged as one: it agrees with the engine for an ordinary jump and differs for the
        // moment after landing, where the engine holds the jump a little longer.
        if ((flags & OnGround) == 0 && !waistDeep && alive)
        {
            // **The push-off and the float are different animations**, split at half a second since
            // the jump began. Null means the caller cannot say how long they have been airborne, and
            // the float is the right answer then: it is what a jump spends most of its time in, and
            // it is what this project drew before the phases existed.
            return airborneSeconds is { } airborne && airborne <= JumpStartSeconds
                ? PlayerActivity.JumpStart
                : PlayerActivity.Jump;
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

    /// <summary>
    /// The weapon slot an activity is suffixed with, when the demo does not say which is out.
    /// </summary>
    /// <remarks>
    /// **Every activity a model claims is weapon-suffixed, which is measured and was a surprise.**
    /// A scout ships <c>ACT_MP_RUN_PRIMARY</c>, <c>ACT_MP_RUN_SECONDARY</c>, <c>ACT_MP_RUN_MELEE</c>
    /// and more; the bare <c>ACT_MP_RUN</c> that <c>CalcMainActivity</c> returns appears nowhere in a
    /// model. <c>CTFPlayerAnimState::TranslateActivity</c> is what adds the suffix, which is why that
    /// step exists rather than being an optimisation.
    ///
    /// The primary slot is assumed because <c>m_hActiveWeapon</c> is not decoded yet, and every class
    /// has the primary forms so the name always resolves. It is the same assumption the earlier
    /// guess-the-label code made, now for a stated reason instead of by accident.
    /// </remarks>
    public const string DefaultWeaponSlot = "PRIMARY";

    /// <summary>The engine's name for an activity, which is what a model file stores.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="weaponSlot">Which weapon slot to suffix with.</param>
    /// <returns>Its <c>ACT_MP_</c> name, as a model spells it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The activity is not one of the known values.</exception>
    /// <remarks>
    /// **Measured against a real model rather than composed from the enum.** The naming is not
    /// regular: standing is <c>ACT_MP_STAND_PRIMARY</c> rather than <c>STAND_IDLE</c>, crouching
    /// idle is <c>ACT_MP_CROUCH_PRIMARY</c> with no IDLE at all, and a jump is three activities —
    /// start, float and land — so there is no single name for it.
    ///
    /// Thrown rather than defaulted for an unknown value: a wrong activity name resolves to no
    /// sequence and freezes the model in its reference pose, which reads as a model fault rather
    /// than a lookup one.
    /// </remarks>
    public static string NameOf(PlayerActivity activity, string weaponSlot = DefaultWeaponSlot) =>
        activity switch
        {
            PlayerActivity.StandIdle => $"ACT_MP_STAND_{weaponSlot}",
            PlayerActivity.Run => $"ACT_MP_RUN_{weaponSlot}",
            PlayerActivity.CrouchIdle => $"ACT_MP_CROUCH_{weaponSlot}",
            PlayerActivity.CrouchWalk => $"ACT_MP_CROUCHWALK_{weaponSlot}",

            // **The push-off and the float, split at half a second.** A demo carries no jump event,
            // so the moment of leaving the ground is derived from when FL_ONGROUND cleared — see
            // ScenePlayer.AirborneSeconds.
            //
            // The LAND is deliberately absent, and that is a fact about the engine rather than a
            // gap here: ACT_MP_JUMP_LAND is started with RestartGesture( GESTURE_SLOT_JUMP, ... ),
            // so it is a layered gesture played over whatever the body is doing, not a body
            // activity. Returning it here would replace the run a player lands into.
            PlayerActivity.JumpStart => $"ACT_MP_JUMP_START_{weaponSlot}",
            PlayerActivity.Jump => $"ACT_MP_JUMP_FLOAT_{weaponSlot}",

            PlayerActivity.SwimIdle => $"ACT_MP_SWIM_{weaponSlot}",
            PlayerActivity.Swim => $"ACT_MP_SWIM_{weaponSlot}",
            PlayerActivity.Die => "ACT_DIESIMPLE",
            _ => throw new ArgumentOutOfRangeException(nameof(activity)),
        };
}
