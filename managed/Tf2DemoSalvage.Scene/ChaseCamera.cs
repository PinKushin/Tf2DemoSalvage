using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Where a third-person camera watching a player sits, and which way it looks.
/// </summary>
/// <remarks>
/// **Valve's <c>C_HLTVCamera::CalcChaseCamView</c>** (<c>game/client/hltvcamera.cpp</c>), reduced to
/// the single-target auto-director case this viewer has. The engine's version also follows a second
/// target when the director names one, lets a spectator drive the angles by hand, and traces the
/// camera against the world.
///
/// **This is the mode the engine falls back to whenever first person is not available**, which is
/// what makes it worth having rather than a nicety. `CalcInEyeCamView` (<c>hltvcamera.cpp:307</c>)
/// hands straight to it for a dead target, and `CViewRender::ShouldDrawViewModel`
/// (<c>viewrender.cpp:974</c>) draws no viewmodel whenever the view is third person. Both of the
/// viewmodel rules this project has been arguing about reduce to "the camera is not in an eye".
///
/// The parts that ARE here, from the auto-director single-target path:
///
/// <code>
///   targetOrigin1 = target1->GetRenderOrigin();
///   if      ( !target1->IsAlive() )              targetOrigin1 += VEC_DEAD_VIEWHEIGHT;
///   else if ( target1->GetFlags() &amp; FL_DUCKING ) targetOrigin1 += VEC_DUCK_VIEW;
///   else                                         targetOrigin1 += VEC_VIEW;
///
///   cameraAngles = target1->EyeAngles();
///   cameraAngles.x = 0; // no PITCH
///   cameraAngles.z = 0; // no ROLL
///   if ( !target1->IsAlive() ) angleOffset.x = 15;
///   cameraAngles += angleOffset;
///
///   AngleVectors( cameraAngles, &amp;forward );
///   VectorNormalize( forward );
///   VectorMA( targetOrigin1, -m_flDistance, forward, cameraOrigin );
/// </code>
///
/// **The wall trace is NOT implemented and that is a stated gap.** The engine pulls the camera in
/// when the ray from target to camera hits world geometry, and grows the distance back out at 32
/// units a second. Without it this camera will sit inside walls when the player has his back to
/// one. Implementing it needs a collision trace against the BSP, which this project does not have
/// yet; the alternative — quietly shortening the distance — would be inventing behaviour, which is
/// worse than a visible limitation.
/// </remarks>
public static class ChaseCamera
{
    /// <summary>How far behind the target the camera sits: <c>m_flDistance</c>.</summary>
    /// <remarks>
    /// 96 units, set in <c>C_HLTVCamera::Reset</c> (<c>hltvcamera.cpp:93</c>) as
    /// <c>m_flDistance = m_flLastDistance = 96.0f</c>. A demo can override it through the
    /// <c>hltv_changed</c> event's <c>distance</c> field, which this does not read.
    /// </remarks>
    public const float Distance = 96f;

    /// <summary>Pitch the camera takes when the target is dead: <c>angleOffset.x = 15</c>.</summary>
    /// <remarks>
    /// Applied only when the target is not alive, and only under the auto-director
    /// (<c>hltvcamera.cpp:194</c>). Looking down fifteen degrees is what puts a corpse in frame
    /// rather than the sky above it.
    /// </remarks>
    public const float DeadPitch = 15f;

    /// <summary>Half the width of the box swept against the world: <c>WALL_OFFSET</c>.</summary>
    /// <remarks>
    /// <c>#define WALL_OFFSET 6.0f</c> (<c>hltvcamera.cpp:31</c>), which
    /// <c>WALL_MIN</c>/<c>WALL_MAX</c> turn into a twelve-unit cube for
    /// <c>UTIL_TraceHull</c>. A bare ray would let the camera's near plane poke through a surface
    /// the trace called clear.
    /// </remarks>
    public const float WallHalfExtent = 6f;

    /// <summary>How fast the camera eases back out once a wall stops blocking it.</summary>
    /// <remarks>
    /// <c>m_flLastDistance += gpGlobals->frametime * 32.0f</c> (<c>hltvcamera.cpp:227</c>), under
    /// the comment "grow distance by 32 unit a second". Without it the camera SNAPS back to full
    /// distance the instant a wall clears, which is the visible half of this mechanism.
    /// </remarks>
    public const float RecoveryPerSecond = 32f;

    /// <summary>How far behind the target the camera may sit this frame.</summary>
    /// <param name="blockedAt">How far the world allows, from the target; <see cref="Distance"/> when clear.</param>
    /// <param name="lastDistance">What it was allowed last frame — <c>m_flLastDistance</c>.</param>
    /// <param name="seconds">Time since the previous frame.</param>
    /// <returns>The distance to use, which is also the next <paramref name="lastDistance"/>.</returns>
    /// <remarks>
    /// **The engine moves IN instantly and OUT slowly, and that asymmetry is the whole point.**
    ///
    /// <code>
    ///   m_flLastDistance += gpGlobals->frametime * 32.0f;
    ///   if ( dist > m_flLastDistance )
    ///       VectorMA( targetOrigin1, -m_flLastDistance, forward, cameraOrigin );
    ///   else
    ///   {
    ///       cameraOrigin = trace.endpos;
    ///       m_flLastDistance = dist;
    ///   }
    /// </code>
    ///
    /// A camera that eased inwards as slowly as it eases out would spend that time inside the wall,
    /// so blocking has to bite at once. Both branches reduce to taking whichever is smaller, and the
    /// result becomes the new state either way.
    ///
    /// **It stops the camera; it does not hide the wall.** Worth stating because the alternative is
    /// a real technique in other engines — culling geometry between camera and subject — and it is
    /// not what Source does here.
    /// </remarks>
    public static float Approach(float blockedAt, float lastDistance, float seconds) =>
        MathF.Min(blockedAt, lastDistance + (RecoveryPerSecond * seconds));

    /// <summary>Where the chase camera sits and which way it looks.</summary>
    /// <param name="x">The target's position, which is its feet.</param>
    /// <param name="y">The target's position.</param>
    /// <param name="z">The target's position.</param>
    /// <param name="eyeYaw">The target's own eye yaw; the camera looks the way they are looking.</param>
    /// <param name="alive">Whether the target is alive, which changes both height and pitch.</param>
    /// <param name="ducking">Whether the target is ducking, which lowers the point looked at.</param>
    /// <returns>The camera's origin and angles.</returns>
    public static (float X, float Y, float Z, float Pitch, float Yaw, float Roll) View(
        float x, float y, float z, float eyeYaw, bool alive, bool ducking) =>
        View(x, y, z, eyeYaw, alive, ducking, Distance);

    /// <summary>Where the chase camera sits, with the director's parameters and second target.</summary>
    /// <param name="x">The target's position, which is its feet.</param>
    /// <param name="y">The target's position.</param>
    /// <param name="z">The target's position.</param>
    /// <param name="eyeYaw">The target's own eye yaw, used when there is no second target.</param>
    /// <param name="alive">Whether the target is alive.</param>
    /// <param name="ducking">Whether the target is ducking.</param>
    /// <param name="settings">The director's distance and angle offsets.</param>
    /// <param name="lookAt">
    /// The second target's eye point, or null when the director names none. When present the camera
    /// looks from the first target TOWARDS this, which is how a chase shot frames two players.
    /// </param>
    /// <returns>The camera's origin and angles.</returns>
    public static (float X, float Y, float Z, float Pitch, float Yaw, float Roll) View(
        float x, float y, float z, float eyeYaw, bool alive, bool ducking,
        ChaseSettings settings, (float X, float Y, float Z)? lookAt)
    {
        float height = alive ? PlayerEye.Spectated(ducking) : PlayerEye.DeadChaseTarget;

        // The point looked at: the target's eyes, or the height a ragdoll is looked over.
        (float X, float Y, float Z) at = (x, y, z + height);

        // **A record STRUCT's default does not run its primary constructor's defaults.**
        // `new ChaseSettings()` and `default` both zero every field, so the declared
        // `Back = ChaseCamera.Distance` applies only when someone passes arguments by name. A zero
        // here would put the camera inside the player, and it caught two of these tests.
        //
        // Reading zero as "unset" is Valve's own meaning rather than an invented sentinel:
        // `m_flDistance` is 96 from `Reset` and the `hltv_chase` event only ever overrides it, so a
        // distance of nothing is a value the engine cannot hold.
        float back = settings.Back > 0f ? settings.Back : Distance;

        // **`QAngle angleOffset( m_flPhi, m_flTheta, 0 )` — pitch, yaw, roll, in that order.**
        (float Pitch, float Yaw) offset = (settings.Phi, settings.Theta);

        float pitch;
        float yaw;

        if (lookAt is { } second)
        {
            // `forward = targetOrigin2 - targetOrigin1; VectorAngles( forward, cameraAngles );`
            // The camera faces from one target towards the other, whichever way either is looking.
            (pitch, yaw) = Facing(second.X - at.X, second.Y - at.Y, second.Z - at.Z);
        }
        else
        {
            // `cameraAngles = target1->EyeAngles(); cameraAngles.x = 0; cameraAngles.z = 0;`
            // The target's yaw only — never their pitch, which is what stops a player looking at
            // the floor from burying the camera in it.
            pitch = 0f;
            yaw = eyeYaw;
        }

        // `if ( !target1->IsAlive() ) angleOffset.x = 15;` — it REPLACES phi rather than adding to
        // it, and it runs after the angles are chosen, so it applies to the second-target case too.
        if (!alive)
        {
            offset.Pitch = DeadPitch;
        }

        pitch += offset.Pitch;
        yaw += offset.Yaw;

        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch, yaw);

        // `VectorMA( targetOrigin1, -m_flDistance, forward, cameraOrigin )`.
        //
        // **`targetOrigin1.z += m_flOffset` happens on the NEXT line in the engine, after this.**
        // So the offset moves what is looked at — and where the wall trace begins — while leaving
        // the camera exactly here. Applying it before would look natural and be wrong.
        return (
            at.X - (back * forward.X),
            at.Y - (back * forward.Y),
            at.Z - (back * forward.Z),
            pitch,
            yaw,
            0f);
    }


    /// <summary>Where the chase camera sits at a shortened distance.</summary>
    /// <param name="x">The target's position, which is its feet.</param>
    /// <param name="y">The target's position.</param>
    /// <param name="z">The target's position.</param>
    /// <param name="eyeYaw">The target's own eye yaw.</param>
    /// <param name="alive">Whether the target is alive.</param>
    /// <param name="ducking">Whether the target is ducking.</param>
    /// <param name="distance">
    /// How far back to sit. <see cref="Distance"/> unless a wall has shortened it, in which case it
    /// is what <see cref="Approach"/> allows — the angles are unchanged either way, because a wall
    /// moves the camera without turning it.
    /// </param>
    public static (float X, float Y, float Z, float Pitch, float Yaw, float Roll) View(
        float x, float y, float z, float eyeYaw, bool alive, bool ducking, float distance)
    {
        // **Three heights, and which one applies is decided before any angle is.** A dead target is
        // looked at over its ragdoll rather than through it, so this is not an eye height at all —
        // see the warning on `PlayerEye.DeadChaseTarget`.
        float height = alive
            ? PlayerEye.Spectated(ducking)
            : PlayerEye.DeadChaseTarget;

        // `cameraAngles = target1->EyeAngles()` with `.x = 0` and `.z = 0`, then the offset. The
        // camera takes the target's YAW only: it looks the way they are looking, never up or down
        // with them, which is what stops a player staring at the floor from burying the camera.
        float pitch = alive ? 0f : DeadPitch;

        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch, eyeYaw);

        // `VectorMA( targetOrigin1, -m_flDistance, forward, cameraOrigin )` — back along the way it
        // faces. Negative, so the camera is BEHIND the target; the positive form puts it in front,
        // which frames the world from the wrong side while still tracking the player.
        return (
            x - (distance * forward.X),
            y - (distance * forward.Y),
            z + height - (distance * forward.Z),
            pitch,
            eyeYaw,
            0f);
    }

    /// <summary>Valve's <c>VectorAngles</c>: which way a direction points, as pitch and yaw.</summary>
    /// <remarks>
    /// **Pitch is NEGATIVE for up**, which is Source's convention throughout and the opposite of
    /// the intuition that catches everyone once. <c>VectorAngles</c> computes
    /// <c>-atan2(forward.z, hypot(forward.x, forward.y))</c> for exactly that reason.
    ///
    /// A zero-length direction has no angles; this answers level rather than inventing one, which
    /// is the case a second target standing exactly on the first would otherwise produce.
    /// </remarks>
    private static (float Pitch, float Yaw) Facing(float x, float y, float z)
    {
        float flat = MathF.Sqrt((x * x) + (y * y));

        const float Degrees = 180f / MathF.PI;

        if (flat < 1e-6f)
        {
            return (z > 0f ? -90f : 90f, 0f);
        }

        return (-MathF.Atan2(z, flat) * Degrees, MathF.Atan2(y, x) * Degrees);
    }
}
