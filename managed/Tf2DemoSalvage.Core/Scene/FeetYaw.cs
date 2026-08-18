using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Where a player's feet point, which is not where their eyes point.
/// </summary>
/// <remarks>
/// **The body is drawn at the FEET yaw and the torso twists to make up the difference.**
/// <c>ComputePoseParam_AimYaw</c> (<c>multiplayer_animstate.cpp:1702</c>) ends with
/// <c>m_angRender[YAW] = m_flCurrentFeetYaw</c> and then
/// <c>SetPoseParameter( m_iAimYaw, -( m_flEyeYaw - m_flCurrentFeetYaw ) )</c>. So a player standing
/// still and turning their view keeps their feet planted, twisting at the waist, until the
/// difference grows too large — and only then do the feet step round.
///
/// While moving the two are the same: "The feet match the eye direction when moving - the move yaw
/// takes care of the rest." That is why using the eye yaw as the body yaw has looked right for
/// everything except a player turning on the spot.
///
/// **This is a simulation, not a reading**, and it runs per tick because the engine runs it per
/// frame. It carries state between ticks, which is why it is a struct the caller keeps rather than
/// a function the caller calls.
/// </remarks>
public record struct FeetYaw
{
    /// <summary>How far the torso may twist before the feet step round, in degrees.</summary>
    /// <remarks>
    /// <c>45.0f</c>, written inline in <c>ComputePoseParam_AimYaw</c> beside a commented-out
    /// <c>m_AnimConfig.m_flMaxBodyYawDegrees</c> — the configurable version is disabled and the
    /// literal is what runs.
    /// </remarks>
    public const float MaxBodyYaw = 45f;

    /// <summary>How fast the feet turn, in degrees a second.</summary>
    /// <remarks>
    /// <c>720.0f</c>, likewise inline beside a commented-out <c>DOD_BODYYAW_RATE</c>.
    /// </remarks>
    public const float YawRate = 720f;

    /// <summary>The turn size above which the feet turn at full rate.</summary>
    /// <remarks>
    /// <c>FADE_TURN_DEGREES</c>, defined and undefined inside <c>ConvergeYawAngles</c> itself. Below
    /// it the rate scales down, so a small correction eases rather than snapping.
    /// </remarks>
    public const float FadeTurnDegrees = 60f;

    /// <summary>The speed above which a player counts as moving.</summary>
    /// <remarks>
    /// <c>vecVelocity.Length() > 1.0f</c> — the THREE-dimensional length, so a player rising in a
    /// lift with no horizontal motion is still "moving" and their feet follow their eyes.
    /// </remarks>
    public const float MovingSpeed = 1f;

    private bool _started;

    /// <summary>Where the feet are trying to point.</summary>
    public float Goal { get; private set; }

    /// <summary>Where the feet actually point, which is the yaw the body is drawn at.</summary>
    public float Current { get; private set; }

    /// <summary>Advances the feet one step.</summary>
    /// <param name="eyeYaw">Where the player is looking, in degrees.</param>
    /// <param name="speed">How fast they are moving, in units a second, in three dimensions.</param>
    /// <param name="deltaSeconds">How long since the last step.</param>
    /// <remarks>
    /// The order is the engine's: choose a goal, normalise it, converge toward it, and only then
    /// read the aim yaw off the difference.
    /// </remarks>
    public void Advance(float eyeYaw, float speed, float deltaSeconds)
    {
        if (!_started)
        {
            // `if ( m_PoseParameterData.m_flLastAimTurnTime <= 0.0f )` — the feet start under the
            // eyes rather than at zero, so a player who has never moved is not drawn facing east.
            Goal = eyeYaw;
            Current = eyeYaw;
            _started = true;

            return;
        }

        if (speed > MovingSpeed)
        {
            Goal = eyeYaw;
        }
        else
        {
            // **The feet step round in whole 45-degree jumps rather than tracking the eyes.** Valve
            // marks this one as unfinished in place — the comment beside it reads "Do something
            // better here!" — and this reproduces what it does rather than the better thing.
            float twist = Normalize(Goal - eyeYaw);

            if (MathF.Abs(twist) > MaxBodyYaw)
            {
                Goal += MaxBodyYaw * (twist > 0f ? -1f : 1f);
            }
        }

        Goal = Normalize(Goal);

        // **An exact comparison, which is the engine's** — `if ( m_flGoalFeetYaw !=
        // m_flCurrentFeetYaw )`. The analyser wants a tolerance and a tolerance would be wrong
        // here: the guard exists only to skip work when the feet are already home, and any epsilon
        // would abandon the last fraction of a degree of every turn, leaving the body permanently
        // a little off the goal it is converging to. Converge itself snaps when the remaining
        // distance is smaller than one step, so nothing depends on this comparison being loose.
#pragma warning disable S1244
        if (Goal != Current)
#pragma warning restore S1244
        {
            Converge(Goal, YawRate, deltaSeconds);
        }
    }

    /// <summary>How far the torso is twisted from the feet, in degrees.</summary>
    /// <param name="eyeYaw">Where the player is looking.</param>
    /// <returns>The value <c>body_yaw</c> takes, already negated as the engine negates it.</returns>
    /// <remarks>
    /// <c>float flAimYaw = m_flEyeYaw - m_flCurrentFeetYaw; ... SetPoseParameter( m_iAimYaw,
    /// -flAimYaw )</c>. The negation is part of setting the parameter, so it belongs here with the
    /// value rather than at the far end of the pipeline.
    /// </remarks>
    public readonly float AimYaw(float eyeYaw) => -Normalize(eyeYaw - Current);

    /// <summary>Moves the current yaw toward the goal, as <c>ConvergeYawAngles</c> does.</summary>
    /// <remarks>
    /// **The magnitude is taken BEFORE the angle is normalised, and the sign after it.** That is
    /// Valve's order:
    ///
    /// <code>
    /// float flDeltaYaw = flGoalYaw - flCurrentYaw;
    /// float flDeltaYawAbs = fabs( flDeltaYaw );
    /// flDeltaYaw = AngleNormalize( flDeltaYaw );
    /// </code>
    ///
    /// It matters at the wrap. Turning from 170 to −170 is twenty degrees the short way, but the
    /// raw difference is 340 — so the scale saturates at full rate and the "close enough to snap"
    /// test compares against 340 rather than 20, while the direction still comes from the
    /// normalised −20 and turns the short way. Normalising first, which is what a tidy-up would do,
    /// eases through the wrap instead of turning at full speed.
    /// </remarks>
    private void Converge(float goal, float rate, float deltaSeconds)
    {
        float delta = goal - Current;
        float magnitude = MathF.Abs(delta);

        delta = Normalize(delta);

        // "Always do at least a bit of the turn (1%)."
        float scale = Math.Clamp(magnitude / FadeTurnDegrees, 0.01f, 1f);

        float step = rate * deltaSeconds * scale;

        float side = delta < 0f ? -1f : 1f;

        Current = magnitude < step ? goal : Current + (step * side);

        Current = Normalize(Current);
    }

    /// <summary>Brings an angle into −180 to 180, as <c>AngleNormalize</c> does.</summary>
    private static float Normalize(float degrees)
    {
        float wrapped = degrees % 360f;

        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }
}
