using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The minigun's weapon state, as <c>m_iWeaponState</c> carries it.
/// </summary>
/// <remarks>
/// Four bits unsigned on the wire (<c>tf_weapon_minigun.cpp:51</c>), and the names are the engine's
/// (<c>tf_weapon_minigun.h:30</c>).
/// </remarks>
public enum MinigunState
{
    /// <summary><c>AC_STATE_IDLE</c> — not wound up.</summary>
    Idle = 0,

    /// <summary><c>AC_STATE_STARTFIRING</c> — winding up.</summary>
    StartFiring = 1,

    /// <summary><c>AC_STATE_FIRING</c>.</summary>
    Firing = 2,

    /// <summary><c>AC_STATE_SPINNING</c> — wound up, not firing.</summary>
    Spinning = 3,

    /// <summary><c>AC_STATE_DRYFIRE</c> — spun up with no ammo, and still spinning.</summary>
    DryFire = 4,
}

/// <summary>
/// The minigun's barrel spin, which the CLIENT computes rather than the animation (B347).
/// </summary>
/// <remarks>
/// **`CTFMinigun` overrides `StandardBlendingRules` to overwrite one bone**
/// (<c>tf_weapon_minigun.cpp:1068</c>):
///
/// <code>
///   BaseClass::StandardBlendingRules( hdr, pos, q, currentTime, boneMask );
///
///   if (m_iBarrelBone != -1)
///   {
///       UpdateBarrelMovement();
///       AngleQuaternion( RadianEuler( 0, 0, m_flBarrelAngle ), q[m_iBarrelBone] );
///   }
/// </code>
///
/// **It ASSIGNS rather than composes**, so whatever `fire_loop` put on that bone is discarded — the
/// barrel is procedural at every moment, including the ones where no sequence is playing.
///
/// **Nothing about the angle is on the wire.** `m_flBarrelAngle` is integrated client-side and the
/// only networked input is `m_iWeaponState`, so the client has no more information than a demo does.
/// That is what makes it reproducible, and it is the same shape as the burn clock (B336): watch the
/// state, run the engine's own arithmetic from it.
///
/// **The pieces are separated so each can be checked against its citation.** They are trivial
/// individually and that is the point — the mistakes available here are picking the wrong axis,
/// lerping where the engine snaps, and reading the state as a set of cases instead of a comparison.
/// </remarks>
public static class MinigunBarrel
{
    /// <summary><c>MAX_BARREL_SPIN_VELOCITY</c> (<c>tf_weapon_minigun.cpp:34</c>).</summary>
    public const float FullSpinVelocity = 20f;

    /// <summary>How fast the barrel comes up to speed, per FRAME.</summary>
    /// <param name="canHolsterWhileSpinning">
    /// Whether the weapon can be holstered while spinning — the Tomislav's attribute.
    /// </param>
    /// <returns>The step <see cref="Approach"/> takes.</returns>
    /// <remarks>
    /// **Per frame, not per second, and that is TF2's arithmetic rather than a misreading.**
    /// `UpdateBarrelMovement` calls `Approach` once with a literal 0.1
    /// (<c>tf_weapon_minigun.cpp:1105</c>) and is reached from `StandardBlendingRules`, which runs
    /// per rendered frame — so a minigun genuinely spins up five times faster at 300fps than at 60.
    /// Reproduced rather than corrected: a per-second rate would be our number, not the game's.
    /// </remarks>
    public static float Acceleration(bool canHolsterWhileSpinning) =>
        canHolsterWhileSpinning ? 0.5f : 0.1f;

    /// <summary>The velocity the barrel is heading for, from the networked state.</summary>
    /// <param name="state">The weapon state.</param>
    /// <returns>Zero when idle, otherwise the full spin velocity.</returns>
    /// <remarks>
    /// **A comparison, not a table of cases, because that is what the engine writes.**
    /// `m_iWeaponState > AC_STATE_IDLE` is the test `CanHolster`, `Holster` and `Lower` all use to
    /// mean "wound up" (<c>tf_weapon_minigun.cpp:806, 824, 837</c>), and `WindUp`/`WindDown` set the
    /// target to `MAX_BARREL_SPIN_VELOCITY` and zero respectively (<c>:951, :880</c>).
    ///
    /// **`AC_STATE_DRYFIRE` counts as spinning**, which a state-by-state mapping is the natural way
    /// to get wrong: a heavy holding fire with no ammo is still wound up, and the comparison
    /// includes it without anybody having to remember.
    /// </remarks>
    public static float TargetVelocity(MinigunState state) =>
        state > MinigunState.Idle ? FullSpinVelocity : 0f;

    /// <summary>Steps a value toward a target, snapping once inside one step.</summary>
    /// <param name="target">Where it is heading.</param>
    /// <param name="value">Where it is.</param>
    /// <param name="speed">How far it may move.</param>
    /// <returns>The new value.</returns>
    /// <remarks>
    /// `Approach` (<c>mathlib_base.cpp:3433</c>), verbatim:
    ///
    /// <code>
    ///   float delta = target - value;
    ///   if ( delta &gt; speed )       value += speed;
    ///   else if ( delta &lt; -speed ) value -= speed;
    ///   else                        value = target;
    /// </code>
    ///
    /// **The snap is the half a lerp gets wrong.** Without it the velocity approaches the target
    /// asymptotically and never arrives, so the engine's `if (0 == m_flBarrelCurrentVelocity)`
    /// wind-down check (<c>tf_weapon_minigun.cpp:1110</c>) would never fire.
    /// </remarks>
    public static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
        {
            return value + speed;
        }

        return delta < -speed ? value - speed : target;
    }

    /// <summary>Advances the barrel's angle by one frame.</summary>
    /// <param name="angle">The angle so far, in radians.</param>
    /// <param name="velocity">The current spin velocity.</param>
    /// <param name="seconds">The frame's duration.</param>
    /// <returns>The new angle.</returns>
    /// <remarks>
    /// `m_flBarrelAngle += m_flBarrelCurrentVelocity * gpGlobals->frametime`
    /// (<c>tf_weapon_minigun.cpp:1118</c>).
    ///
    /// **It does not wrap.** A quaternion built from a large angle is still a valid rotation and the
    /// engine lets the number grow, so a modulo here would be ours rather than the game's — and
    /// would disagree with TF2 on whichever frame it happened.
    /// </remarks>
    public static float Advance(float angle, float velocity, float seconds) =>
        angle + (velocity * seconds);

    /// <summary>The barrel bone's rotation for an angle.</summary>
    /// <param name="angle">The angle, in radians.</param>
    /// <returns>The quaternion, as x, y, z, w.</returns>
    /// <remarks>
    /// **A pure Z rotation, and reading that off the source takes two hops.** The call passes
    /// `RadianEuler( 0, 0, m_flBarrelAngle )` — the third component — and `RadianEuler`'s members
    /// are `x, y, z` in declaration order (<c>vector.h:1692</c>). `AngleQuaternion` reads `angles.z`
    /// into the YAW terms (<c>mathlib_base.cpp:2039</c>):
    ///
    /// <code>
    ///   SinCos( angles.z * 0.5f, &amp;sy, &amp;cy );   // yaw
    ///   SinCos( angles.y * 0.5f, &amp;sp, &amp;cp );   // pitch
    ///   SinCos( angles.x * 0.5f, &amp;sr, &amp;cr );   // roll
    /// </code>
    ///
    /// With roll and pitch zero the four output terms collapse to <c>(0, 0, sin(a/2), cos(a/2))</c>.
    ///
    /// **The file's own commented-out alternative sets `a.x`, which is ROLL** — a different axis
    /// from the live code. It is guarded by "Weapon happens to be aligned to (0,0,0)", so it is a
    /// sketch of the general case rather than an equivalent, and following it would spin the barrel
    /// about the wrong axis.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Rotation(float angle) =>
        (0f, 0f, MathF.Sin(angle * 0.5f), MathF.Cos(angle * 0.5f));
}
