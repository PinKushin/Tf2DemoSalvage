using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The minigun's barrel is spun by the CLIENT, not by its animation (B347).
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
/// **It ASSIGNS rather than composes**, so whatever `fire_loop` animated onto that bone is
/// discarded. The barrel's motion is procedural at every moment, including the ones where the
/// sequence is not playing.
///
/// **The bone is found by NAME** — `m_iBarrelBone = LookupBone( "barrel" )`
/// (<c>tf_weapon_minigun.cpp:1048</c>), and `c_minigun.mdl` carries exactly three bones:
/// `weapon_bone`, `barrel`, `c_weapon_stattrack`. Measured, not assumed.
///
/// **Nothing about the angle is networked.** `m_flBarrelAngle` is integrated client-side
/// (<c>:1118</c>) and the only thing on the wire is `m_iWeaponState`, four bits unsigned
/// (<c>:51</c>) — so the client has no more information than a demo does, which is what makes this
/// reproducible at all. Same shape as the burn clock (B336) and the airborne clock.
/// </remarks>
public sealed class MinigunBarrelConformanceTests
{
    /// <remarks>
    /// **`Approach` steps by a fixed amount and SNAPS inside it** (<c>mathlib_base.cpp:3433</c>):
    ///
    /// <code>
    ///   float delta = target - value;
    ///   if ( delta &gt; speed )       value += speed;
    ///   else if ( delta &lt; -speed ) value -= speed;
    ///   else                        value = target;
    /// </code>
    ///
    /// The snap is the half a lerp would get wrong — without it the velocity approaches the target
    /// asymptotically and never reaches it, so the wind-down sound would never fire.
    /// </remarks>
    [Test]
    public void Approach_WhenFurtherThanOneStep_MovesByExactlyTheStep()
    {
        MinigunBarrel.Approach(target: 20f, value: 0f, speed: 0.1f).ShouldBe(0.1f);
        MinigunBarrel.Approach(target: 0f, value: 20f, speed: 0.1f).ShouldBe(19.9f);
    }

    [Test]
    public void Approach_WhenWithinOneStep_SnapsToTheTarget()
    {
        MinigunBarrel.Approach(target: 20f, value: 19.95f, speed: 0.1f).ShouldBe(20f);
        MinigunBarrel.Approach(target: 0f, value: 0.05f, speed: 0.1f).ShouldBe(0f);
        MinigunBarrel.Approach(target: 20f, value: 20f, speed: 0.1f).ShouldBe(20f);
    }

    /// <remarks>
    /// **Anything but idle is spinning, and that is the engine's own idiom.**
    /// `m_iWeaponState > AC_STATE_IDLE` is what `CanHolster`, `Holster` and `Lower` all test
    /// (<c>tf_weapon_minigun.cpp:806, 824, 837</c>) to mean "wound up". `WindUp` sets the target to
    /// `MAX_BARREL_SPIN_VELOCITY` (20) and `WindDown` sets it to zero (<c>:951, :880</c>).
    ///
    /// **`AC_STATE_DRYFIRE` counts.** A heavy holding fire with no ammo is still spun up, and the
    /// comparison above includes it — a mapping written state-by-state would be the place to get
    /// that wrong.
    /// </remarks>
    [Test]
    public void TargetVelocity_ForEveryStateAboveIdle_IsTheFullSpinVelocity()
    {
        MinigunBarrel.TargetVelocity(MinigunState.StartFiring).ShouldBe(20f);
        MinigunBarrel.TargetVelocity(MinigunState.Firing).ShouldBe(20f);
        MinigunBarrel.TargetVelocity(MinigunState.Spinning).ShouldBe(20f);
        MinigunBarrel.TargetVelocity(MinigunState.DryFire).ShouldBe(20f);
    }

    [Test]
    public void TargetVelocity_WhenIdle_IsZero()
    {
        MinigunBarrel.TargetVelocity(MinigunState.Idle).ShouldBe(0f);
    }

    /// <remarks>
    /// **The rotation is about Z, and getting that from the source needs two hops.** The call
    /// passes `RadianEuler( 0, 0, m_flBarrelAngle )` — the THIRD component — and `RadianEuler`'s
    /// members are `x, y, z` in declaration order (<c>vector.h:1692</c>). `AngleQuaternion` then
    /// reads `angles.z` into `sy`/`cy` (<c>mathlib_base.cpp:2039</c>), the YAW terms:
    ///
    /// <code>
    ///   SinCos( angles.z * 0.5f, &amp;sy, &amp;cy );   // yaw
    ///   SinCos( angles.y * 0.5f, &amp;sp, &amp;cp );   // pitch
    ///   SinCos( angles.x * 0.5f, &amp;sr, &amp;cr );   // roll
    /// </code>
    ///
    /// **The file's own commented-out alternative sets `a.x` instead**, which is ROLL — a different
    /// axis from the live code. It is guarded by "Weapon happens to be aligned to (0,0,0)", so it
    /// is a sketch rather than an equivalent, and following it would spin the barrel the wrong way
    /// about the wrong axis.
    ///
    /// With roll and pitch zero the four terms collapse to a pure Z rotation:
    /// <c>(0, 0, sin(a/2), cos(a/2))</c>.
    /// </remarks>
    [Test]
    public void Rotation_ForAnAngle_IsAPureZRotation()
    {
        (float X, float Y, float Z, float W) quarter = MinigunBarrel.Rotation(MathF.PI / 2f);

        quarter.X.ShouldBe(0f, 1e-6f);
        quarter.Y.ShouldBe(0f, 1e-6f);
        quarter.Z.ShouldBe(MathF.Sin(MathF.PI / 4f), 1e-6f);
        quarter.W.ShouldBe(MathF.Cos(MathF.PI / 4f), 1e-6f);
    }

    /// <remarks>
    /// **The control: zero is the identity**, so a barrel that never spun is left exactly where the
    /// bind pose put it rather than being nudged by an accumulating error.
    /// </remarks>
    [Test]
    public void Rotation_ForZero_IsTheIdentity()
    {
        (float X, float Y, float Z, float W) still = MinigunBarrel.Rotation(0f);

        still.X.ShouldBe(0f);
        still.Y.ShouldBe(0f);
        still.Z.ShouldBe(0f);
        still.W.ShouldBe(1f);
    }

    /// <remarks>
    /// **The acceleration is per FRAME, not per second, and that is a real engine quirk rather than
    /// a reading error.** `UpdateBarrelMovement` calls `Approach` once per call with a literal
    /// 0.1, and is called from `StandardBlendingRules` — which runs per rendered frame. So a
    /// minigun spins up five times faster at 300fps than at 60. Reproduced rather than corrected,
    /// because parity is the point and a per-second rate would be our arithmetic rather than TF2's.
    ///
    /// **0.5 for a weapon that can holster while spinning** (`:1105`) — the Tomislav.
    /// </remarks>
    [Test]
    public void Acceleration_ForAnOrdinaryMinigun_IsATenthPerFrame()
    {
        MinigunBarrel.Acceleration(canHolsterWhileSpinning: false).ShouldBe(0.1f);
        MinigunBarrel.Acceleration(canHolsterWhileSpinning: true).ShouldBe(0.5f);
    }

    /// <remarks>
    /// **The angle advances by velocity × frametime** (`:1118`), and it does NOT wrap. A quaternion
    /// built from a large angle is still a valid rotation, and the engine lets it grow — so a
    /// modulo here would be our idea, and would disagree with TF2 on the frame it happened.
    /// </remarks>
    [Test]
    public void Advance_ForOneSecondAtFullSpin_TurnsTwentyRadians()
    {
        MinigunBarrel.Advance(angle: 0f, velocity: 20f, seconds: 1f).ShouldBe(20f);
        MinigunBarrel.Advance(angle: 20f, velocity: 20f, seconds: 0.5f).ShouldBe(30f);
    }
}
