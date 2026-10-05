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

    /// <summary>Rising fast — the legs run in the air rather than tucking for a jump.</summary>
    /// <remarks>
    /// <c>ACT_MP_AIRWALK</c>, chosen by <c>CTFPlayerAnimState::HandleJumping</c> BEFORE the jump and
    /// superseding it: <c>if ( bValidAirWalkClass &amp;&amp; ( vecVelocity.z > 300.0f ||
    /// m_bInAirWalk ) &amp;&amp; !bInDuck )</c>. It is what a rocket-jumping soldier is drawn with.
    ///
    /// Measured from the shipped class scripts: **only the medic sets <c>DontDoAirwalk</c>**, so
    /// every other class air-walks. That was worth measuring rather than guessing — the plausible
    /// answers were the heavy or the soldier, and both are wrong.
    /// </remarks>
    Airwalk,

    /// <summary>Airborne, in the first half second — the push-off.</summary>
    /// <remarks>
    /// <c>CTFPlayerAnimState::HandleJumping</c> splits a jump in two:
    /// <c>if ( gpGlobals->curtime - m_flJumpStartTime > 0.5 ) idealActivity = ACT_MP_JUMP_FLOAT;
    /// else idealActivity = ACT_MP_JUMP_START;</c>. Both are real animations in every class model,
    /// and playing float throughout skips the launch entirely. Only for a class whose script does not
    /// set <c>DontDoNewJump</c>; see <see cref="LegacyJump"/>.
    /// </remarks>
    JumpStart,

    /// <summary>Airborne, after the push-off.</summary>
    Jump,

    /// <summary>The single old jump, <c>ACT_MP_JUMP</c>, for a class whose script sets <c>DontDoNewJump</c>.</summary>
    /// <remarks>
    /// **This was written off as a finished migration, and it is not** (B437). The branch's comment reads "Remove me
    /// once all classes are doing the new jump", and an earlier note here said every shipped class had the flag
    /// false — but the shipped scripts were measured since (<c>ClassAirwalkTests</c>): the soldier and the medic set
    /// it, so every soldier's jump is this one activity, not the push-off and float.
    /// </remarks>
    LegacyJump,

    /// <summary>In water at least waist deep and still.</summary>
    SwimIdle,

    /// <summary>In water at least waist deep and moving.</summary>
    Swim,

    /// <summary>Dead.</summary>
    Die,

    /// <summary>Crouched and still, deployed — `ACT_MP_CROUCH_DEPLOYED_IDLE` (`tf_playeranimstate.cpp:1356`).</summary>
    CrouchDeployedIdle,

    /// <summary>Crouched and moving, deployed — `ACT_MP_CROUCH_DEPLOYED` (`:1383`).</summary>
    CrouchDeployed,

    /// <summary>Crouched and moving with an air dash spent — `ACT_MP_DOUBLEJUMP_CROUCH` (`:1363`).</summary>
    DoubleJumpCrouch,

    /// <summary>Standing and moving, deployed — `ACT_MP_DEPLOYED` (`:1316`).</summary>
    Deployed,

    /// <summary>Standing still, deployed or holding the deployed pose — `ACT_MP_DEPLOYED_IDLE` (`:1320`, `:1326`).</summary>
    DeployedIdle,
}

/// <summary>What TF's own `HandleDucking` and `HandleMoving` ask of the player beyond the flags (B437).</summary>
/// <param name="IsLoser">`m_Shared.IsLoser()` — <see cref="LoserState.IsLoser"/>.</param>
/// <param name="IsAiming">
/// `m_Shared.IsAiming()`: `TF_COND_AIMING` on anyone but a soldier, or `TF_COND_ZOOMED` for a sniper holding the classic
/// rifle (`tf_player_shared.cpp:11429-11441`).
/// </param>
/// <param name="AimsMinigun">The active weapon is the minigun, which never deployed-crouch-walks (`:1372-1381`).</param>
/// <param name="AirDashing">`m_Shared.GetAirDash() &gt; 0`.</param>
/// <param name="HoldsDeployedPose">`m_flHoldDeployedPoseUntilTime &gt; gpGlobals-&gt;curtime`.</param>
/// <param name="LacksCrouchWalk">
/// `SelectWeightedSequence( TranslateActivity( ACT_MP_CROUCHWALK ) ) &lt; 0` on the model being drawn; false when
/// nobody has asked a model, so the flag stands.
/// </param>
public readonly record struct TfPosture(
    bool IsLoser = false,
    bool IsAiming = false,
    bool AimsMinigun = false,
    bool AirDashing = false,
    bool HoldsDeployedPose = false,
    bool LacksCrouchWalk = false);

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

    /// <summary>How fast a player must rise before the legs air-walk instead of tucking.</summary>
    /// <remarks>
    /// <c>vecVelocity.z &gt; 300.0f</c> in <c>CTFPlayerAnimState::HandleJumping</c>. An ordinary
    /// TF2 jump leaves the ground at 268 units a second, so this deliberately excludes it — the
    /// air-walk is for rocket jumps, blast jumps and launchers.
    /// </remarks>
    public const float AirwalkRiseSpeed = 300f;

    /// <summary>The water level at which a player swims rather than walks or jumps.</summary>
    /// <remarks>
    /// <c>WL_Waist</c>. Valve documents the four values in a comment at <c>player.cpp:1961</c>:
    /// 0 not in water, 1 feet, 2 waist, 3 eyes. Both <c>HandleJumping</c> and
    /// <c>HandleSwimming</c> test <c>GetWaterLevel() >= WL_Waist</c>, so waist deep is where a jump
    /// becomes a swim.
    /// </remarks>
    public const int WaistDeepWaterLevel = 2;

    /// <summary>At rest on the ground — <c>FL_ONGROUND</c>.</summary>
    public const int OnGround = 1 << 0;

    /// <summary>Fully crouched — <c>FL_DUCKING</c>.</summary>
    /// <remarks>
    /// <c>FL_ONGROUND</c> and <c>FL_DUCKING</c> are the only bits both <c>const.h</c> lists agree on; every other bit
    /// moved when <c>FL_ANIMDUCKING</c> took <c>1&lt;&lt;2</c>, so it is read through <see cref="PlayerFlagLayout"/>.
    /// The unused <c>AnimDucking</c> and <c>InWater</c> constants that stood here were removed (B501): <c>InWater</c> held
    /// the orangebox <c>1&lt;&lt;9</c>, which is <c>FL_FAKECLIENT</c> in a current demo.
    /// </remarks>
    public const int Ducking = 1 << 1;

    /// <summary>Chooses the activity for a player's body.</summary>
    /// <param name="flags">The player's <c>m_fFlags</c>.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="waistDeep">Whether the water is at least waist deep.</param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="jumping">
    /// <c>HandleJumping</c>'s answer — the activity it left when it returned true, or null when it returned false —
    /// from <see cref="PlayerGestureFeed.HandleJumping"/>, which holds the state it steps (B437).
    /// </param>
    /// <param name="posture">What TF's `HandleDucking` and `HandleMoving` ask beyond the flags (B437).</param>
    /// <returns>The activity the engine would choose.</returns>
    public static PlayerActivity For(
        int flags, float speed, bool waistDeep, bool alive, PlayerActivity? jumping = null, TfPosture posture = default)
    {
        bool moving = speed > MovingMinimumSpeed;

        // **HandleJumping first, and it outranks everything** — but it answers only for an air-walk or for
        // `m_bJumping`, which PLAYERANIMEVENT_JUMP sets (`multiplayer_animstate.cpp:288`). Being off the ground is not a
        // jump: a rocket jump or a step off a ledge falls through to HandleDucking or HandleMoving below. The dead are
        // cleared before they are asked, so an answer never reaches them.
        if (alive && jumping is { } answered)
        {
            return answered;
        }

        if (alive && Ducked(flags, speed, posture) is { } ducked)
        {
            return ducked;
        }

        if (waistDeep && alive)
        {
            return moving ? PlayerActivity.Swim : PlayerActivity.SwimIdle;
        }

        if (!alive)
        {
            return PlayerActivity.Die;
        }

        return Moved(moving, posture);
    }

    /// <summary>Whether an answer of <see cref="For"/> is `HandleMoving`'s, so that `HandleMoving` ran this frame.</summary>
    /// <param name="activity">The answer.</param>
    /// <returns>True for the run, the stand and the two deployed stances.</returns>
    public static bool IsMoving(PlayerActivity activity) =>
        activity is PlayerActivity.Run or PlayerActivity.StandIdle or PlayerActivity.Deployed or PlayerActivity.DeployedIdle;

    /// <summary>`CTFPlayerAnimState::HandleDucking` (`tf_playeranimstate.cpp:1341-1392`), or null when it returns false.</summary>
    private static PlayerActivity? Ducked(int flags, float speed, TfPosture posture)
    {
        // The duck is dropped when the model has no crouch walk for what is held — unless a loser (:1343-1347).
        if ((flags & Ducking) == 0 || (posture.LacksCrouchWalk && !posture.IsLoser))
        {
            return null;
        }

        // `GetOuterXYSpeed() < MOVING_MINIMUM_SPEED || IsLoser()` (:1351): strictly below, and a loser always idles.
        if (speed < MovingMinimumSpeed || posture.IsLoser)
        {
            return posture.IsAiming || posture.HoldsDeployedPose ? PlayerActivity.CrouchDeployedIdle : PlayerActivity.CrouchIdle;
        }

        if (posture.IsAiming && !posture.AimsMinigun)
        {
            return PlayerActivity.CrouchDeployed;
        }

        return posture.AirDashing ? PlayerActivity.DoubleJumpCrouch : PlayerActivity.CrouchWalk;
    }

    /// <summary>`CTFPlayerAnimState::HandleMoving` (`:1297-1334`) over the base's run (`multiplayer_animstate.cpp:940`).</summary>
    private static PlayerActivity Moved(bool moving, TfPosture posture)
    {
        if (!posture.IsLoser && posture.IsAiming)
        {
            return moving ? PlayerActivity.Deployed : PlayerActivity.DeployedIdle;
        }

        // Moving cancels the hold before anything reads it (:1301-1305).
        if (!posture.IsLoser && posture.HoldsDeployedPose && !moving)
        {
            return PlayerActivity.DeployedIdle;
        }

        // Standing idle is the engine's starting value rather than a case it chooses.
        return moving ? PlayerActivity.Run : PlayerActivity.StandIdle;
    }

    /// <summary><c>CalcMainActivity</c>'s own name for an activity, before any weapon has touched it.</summary>
    /// <param name="activity">The activity.</param>
    /// <returns>The engine's <c>idealActivity</c>, such as <c>ACT_MP_RUN</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The activity is not one of the known values.</exception>
    /// <remarks>
    /// **No model ships these names, and that is the point of them** (B105). A scout carries
    /// <c>ACT_MP_RUN_PRIMARY</c>, <c>ACT_MP_RUN_SECONDARY</c> and the rest and never a bare
    /// <c>ACT_MP_RUN</c>: <c>CTFPlayerAnimState::TranslateActivity</c> (<c>tf_playeranimstate.cpp:124</c>) hands this
    /// name to the held weapon's table, which rewrites it — so it is the table's KEY. Pasting a slot onto the end
    /// instead was a table for ten roles of twelve: the Cow Mangler's PRIMARY2 runs with the primary rows
    /// (<c>tf_weaponbase.cpp:3785</c>) and the all-class melee table is keyed MELEEALLCLASS while its rows say
    /// <c>_MELEE_ALLCLASS</c>.
    ///
    /// Each value is the handler's own (<c>multiplayer_animstate.cpp</c>): <c>ACT_MP_STAND_IDLE</c> is
    /// <c>CalcMainActivity</c>'s starting value (:953), <c>HandleMoving</c> only ever runs (:940),
    /// <c>HandleDucking</c> crouch-idles or crouch-walks (:851, :855), <c>HandleSwimming</c> swims whether or not the
    /// player moves (:880), <c>HandleDying</c> dies (:913), and TF's <c>HandleJumping</c> air-walks, pushes off or
    /// floats. The LAND is deliberately absent: <c>ACT_MP_JUMP_LAND</c> is started with
    /// <c>RestartGesture( GESTURE_SLOT_JUMP, ... )</c>, a layer over whatever the body is doing, not a body activity.
    ///
    /// Thrown rather than defaulted for an unknown value: a wrong activity name resolves to no sequence and freezes the
    /// model in its reference pose, which reads as a model fault rather than a lookup one.
    /// </remarks>
    public static string IdealName(PlayerActivity activity) =>
        activity switch
        {
            PlayerActivity.StandIdle => "ACT_MP_STAND_IDLE",
            PlayerActivity.Run => "ACT_MP_RUN",
            PlayerActivity.CrouchIdle => "ACT_MP_CROUCH_IDLE",
            PlayerActivity.CrouchWalk => "ACT_MP_CROUCHWALK",
            PlayerActivity.Airwalk => "ACT_MP_AIRWALK",
            PlayerActivity.JumpStart => "ACT_MP_JUMP_START",
            PlayerActivity.Jump => "ACT_MP_JUMP_FLOAT",
            PlayerActivity.LegacyJump => "ACT_MP_JUMP",
            PlayerActivity.SwimIdle or PlayerActivity.Swim => "ACT_MP_SWIM",
            PlayerActivity.Die => "ACT_DIESIMPLE",
            PlayerActivity.CrouchDeployedIdle => "ACT_MP_CROUCH_DEPLOYED_IDLE",
            PlayerActivity.CrouchDeployed => "ACT_MP_CROUCH_DEPLOYED",
            PlayerActivity.DoubleJumpCrouch => "ACT_MP_DOUBLEJUMP_CROUCH",
            PlayerActivity.Deployed => "ACT_MP_DEPLOYED",
            PlayerActivity.DeployedIdle => "ACT_MP_DEPLOYED_IDLE",
            _ => throw new ArgumentOutOfRangeException(nameof(activity)),
        };
}
