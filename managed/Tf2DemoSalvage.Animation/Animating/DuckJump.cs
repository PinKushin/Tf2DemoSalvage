using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The duck-jump interpolation: how far a crouching airborne player's skeleton is offset.
/// </summary>
/// <remarks>
/// **Ducking in mid-air moves the player's ORIGIN.** The hull shrinks from 82 units tall to 62
/// (`tf_gamerules.cpp:1313`) and the model would teleport upward with it, so
/// `C_TFPlayer::BuildTransformations` draws the whole skeleton twenty units low at that instant and
/// eases the correction to zero over 0.15 seconds (`c_tf_player.cpp:8764`):
///
/// <code>
///   if ( GetGroundEntity() == NULL )
///   {
///       Vector duckOffset = ( hullSizeNormal - hullSizeCrouch );
///       if ( GetFlags() &amp; FL_DUCKING )
///       {
///           if ( !m_bDuckJumpInterp ) m_flFirstDuckJumpInterp = gpGlobals->curtime;
///           m_bDuckJumpInterp = true;
///           m_flLastDuckJumpInterp = gpGlobals->curtime;
///           float flRatio = MIN( 0.15f, gpGlobals->curtime - m_flFirstDuckJumpInterp ) / 0.15f;
///           m_flDuckJumpInterp = 1.f - flRatio;
///       }
///       else if ( m_bDuckJumpInterp )
///       {
///           float flRatio = MIN( 0.15f, gpGlobals->curtime - m_flLastDuckJumpInterp ) / 0.15f;
///           m_flDuckJumpInterp = -(1.f - flRatio);
///           if ( m_flDuckJumpInterp == 0.f ) m_bDuckJumpInterp = false;
///       }
///       ...
///   }
///   else if ( m_bDuckJumpInterp ) { m_bDuckJumpInterp = false; }
/// </code>
///
/// **Its absence is a twenty-unit pop on every crouch jump** — a quarter of a player's height — and
/// roughly a fifth of the player states sampled in `z1800` are airborne and ducking (B314). A game
/// of rocket jumps and crouch-jumps should look exactly like that.
///
/// **None of the three members is networked.** The client derives all of them from the flags over
/// time, so there is no decoded field whose absence anyone could have noticed and no measurement of
/// our data could have found this — only reading the function.
///
/// **State per entity, like the transition queue**, because the answer depends on when the duck
/// began and when it last held rather than on this frame alone.
/// </remarks>
public sealed class DuckJump
{
    /// <summary>How long the correction takes to fade, in seconds.</summary>
    /// <remarks>
    /// **0.15 exactly, and it appears twice in the engine** — once for the ramp in and once for the
    /// ramp out, as both the clamp and the divisor. Written once here because two spellings of one
    /// constant is how the two halves come to disagree.
    /// </remarks>
    private const float RampSeconds = 0.15f;

    private bool _interpolating;
    private double _began;
    private double _lastHeld;

    /// <summary>Advances the state and returns this frame's interpolation.</summary>
    /// <param name="ducking">Whether the player carries <c>FL_DUCKING</c>.</param>
    /// <param name="airborne">Whether they are off the ground.</param>
    /// <param name="seconds">Demo time.</param>
    /// <returns>
    /// The fraction of the hull difference to subtract from every bone: 1 at the instant of ducking
    /// in air, decaying to 0; NEGATIVE while coming out of a duck; 0 when nothing applies.
    /// </returns>
    /// <remarks>
    /// **The whole block is inside `GetGroundEntity() == NULL`**, so a crouch on the ground does
    /// nothing at all — that is an ordinary animation rather than a correction, and offsetting it
    /// would sink every crouching player into the floor.
    ///
    /// **Landing clears the state outright rather than ramping out.** `else if ( m_bDuckJumpInterp )
    /// m_bDuckJumpInterp = false;` — the origin being corrected against has stopped moving, so
    /// there is nothing left to correct.
    ///
    /// **The release ramp measures from when the duck LAST held, not from when it began.** The
    /// engine stamps `m_flLastDuckJumpInterp` on every ducking frame, so a player who held a crouch
    /// for a second still gets a full 0.15 to come out of it; measuring from the start would make a
    /// long crouch release instantly.
    /// </remarks>
    public float Update(bool ducking, bool airborne, double seconds)
    {
        if (!airborne)
        {
            _interpolating = false;

            return 0f;
        }

        if (ducking)
        {
            if (!_interpolating)
            {
                _began = seconds;
            }

            _interpolating = true;
            _lastHeld = seconds;

            return 1f - Ramp(seconds - _began);
        }

        if (!_interpolating)
        {
            return 0f;
        }

        float coming = -(1f - Ramp(seconds - _lastHeld));

        // `if ( m_flDuckJumpInterp == 0.f ) m_bDuckJumpInterp = false;` — the exact comparison is
        // Valve's, and it is reachable rather than a formality: the ramp is clamped at 0.15, so the
        // subtraction lands on exactly zero once that much time has passed.
#pragma warning disable S1244
        if (coming == 0f)
#pragma warning restore S1244
        {
            _interpolating = false;
        }

        return coming;
    }

    /// <summary>`MIN( 0.15f, elapsed ) / 0.15f`, which is why it never exceeds one.</summary>
    private static float Ramp(double elapsed) =>
        MathF.Min(RampSeconds, (float)Math.Max(0d, elapsed)) / RampSeconds;
}
