using System;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A corpse's initial conditions: the velocity its animation was carrying (B58).
/// </summary>
/// <remarks>
/// **Predicted from the SDK before any of it was written.** This whole chain is published —
/// `RagdollApplyAnimationAsVelocity` (`ragdoll_shared.cpp:458`), `CalcBoneDerivatives`
/// (`bone_setup.cpp:2521`), `RotationDeltaAxisAngle` and `QuaternionAxisAngle`
/// (`mathlib_base.cpp:3542` and below it) — so nothing here is a design choice.
///
/// **What it decides is whether a corpse keeps running.** The client poses the player twice, 0.05 s
/// apart, and hands the difference to the solver as every element's starting velocity
/// (`c_tf_player.cpp:891`, `boneDt = 0.05f`). Without it a ragdoll appears at the death pose with
/// nothing but the kill's force on it, and a sprinting Scout drops straight down.
/// </remarks>
public sealed class RagdollVelocityConformanceTests
{
    /// <summary>Tolerance for a float predicted through trigonometry.</summary>
    private const double Close = 1e-3;

    /// <remarks>
    /// **`MatrixAngles`, general branch** (`mathlib_base.cpp:208`). A pure 90-degree yaw: forward
    /// becomes +Y, so `atan2( forward.y, forward.x )` is a quarter turn and both other angles are
    /// zero.
    /// </remarks>
    [Test]
    public void ToAngles_ForAQuarterTurnOfYaw_ReadsNinetyDegrees()
    {
        float[] matrix =
        [
            0f, -1f, 0f, 0f,
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
        ];

        (float pitch, float yaw, float roll) = StudioBones.ToAngles(matrix);

        pitch.ShouldBe(0f, Close);
        yaw.ShouldBe(90f, Close);
        roll.ShouldBe(0f, Close);
    }

    /// <remarks>
    /// **The gimbal-lock branch, and the input is chosen so the two branches DISAGREE.** With
    /// forward pointing straight up, `xyDist` is zero and Valve switches to
    /// `yaw = atan2( -left.x, left.y ); roll = 0` — *"one degree of freedom has been lost"*.
    ///
    /// The matrix below is pitch −90, yaw 0, roll 45. The general formula would return
    /// `yaw = atan2(0, 0) = 0` and `roll = atan2(0, 0) = 0`; the branch Valve actually takes turns
    /// that roll into a YAW of 45. So an implementation that skipped the branch as an edge case
    /// returns a different answer for a bone pointing at the sky, which on a ragdoll is a spine.
    /// </remarks>
    [Test]
    public void ToAngles_WhenForwardPointsUp_TakesTheGimbalBranchAndReportsRollAsYaw()
    {
        const float Half = 0.70710678f;

        float[] matrix =
        [
            0f, -Half, -Half, 0f,
            0f, Half, -Half, 0f,
            1f, 0f, 0f, 0f,
        ];

        (float pitch, float yaw, float roll) = StudioBones.ToAngles(matrix);

        pitch.ShouldBe(-90f, Close);
        yaw.ShouldBe(45f, Close, "the lost roll reappears as yaw");
        roll.ShouldBe(0f, "Valve writes a literal zero rather than a degenerate atan2");
    }

    /// <remarks>
    /// **`AngleQuaternion` taking a `QAngle`, `mathlib_base.cpp`**, which is NOT the `RadianEuler`
    /// overload already in this codebase. Valve's own comment on the difference: *"the ordering here
    /// is different from the AngleQuaternion above because p, y, r are not in the same locations in
    /// QAngle + RadianEuler. Yay!"*
    ///
    /// The triple below is asymmetric on purpose. Every component differs, so a transcription that
    /// took the euler order from the wrong overload produces a different quaternion rather than the
    /// same one.
    /// </remarks>
    [Test]
    public void FromAngles_ForAnAsymmetricTriple_UsesTheQAngleOrdering()
    {
        (float x, float y, float z, float w) = StudioBones.FromAngles(30f, 90f, 0f);

        // pitch 30 about Y, then yaw 90 about Z, roll 0:
        //   sy = sin 45 = cy = 0.7071, sp = sin 15 = 0.258819, cp = 0.965926, sr = 0, cr = 1.
        //   x = sr*cp*cy - cr*sp*sy = -0.258819 * 0.7071 = -0.183013
        //   y = cr*sp*cy + sr*cp*sy =  0.258819 * 0.7071 =  0.183013
        //   z = cr*cp*sy - sr*sp*cy =  0.965926 * 0.7071 =  0.683013
        //   w = cr*cp*cy + sr*sp*sy =  0.965926 * 0.7071 =  0.683013
        x.ShouldBe(-0.183013f, Close);
        y.ShouldBe(0.183013f, Close);
        z.ShouldBe(0.683013f, Close);
        w.ShouldBe(0.683013f, Close);
    }

    /// <remarks>
    /// **`QuaternionAxisAngle`** — `angle = RAD2DEG( 2 * acos( q.w ) )`, then the axis is the vector
    /// part normalised. A quarter turn about Z reads as 90 degrees about (0, 0, 1).
    /// </remarks>
    [Test]
    public void AxisAngle_ForAQuarterTurnAboutZ_IsNinetyDegreesAboutZ()
    {
        const float Half = 0.70710678f;

        ((float x, float y, float z), float angle) = StudioBones.AxisAngle((0f, 0f, Half, Half));

        angle.ShouldBe(90f, Close);
        x.ShouldBe(0f, Close);
        y.ShouldBe(0f, Close);
        z.ShouldBe(1f, Close);
    }

    /// <remarks>
    /// **`if ( angle &gt; 180 ) angle -= 360;`**, and this is the input where keeping it and dropping
    /// it differ. A rotation of 300 degrees one way is 60 degrees the other; reporting 300 makes the
    /// derived angular velocity five times too large AND points it the wrong way round, which on a
    /// corpse is a limb that spins instead of swinging.
    /// </remarks>
    [Test]
    public void AxisAngle_ForMoreThanHalfATurn_WrapsToTheShortWayRound()
    {
        // w = cos(150 deg) puts 2*acos(w) at 300.
        float w = MathF.Cos(150f * MathF.PI / 180f);
        float part = MathF.Sin(150f * MathF.PI / 180f);

        (_, float angle) = StudioBones.AxisAngle((0f, 0f, part, w));

        angle.ShouldBe(-60f, Close);
    }

    /// <remarks>
    /// **`CalcBoneDerivatives`, `bone_setup.cpp:2521`.** The translation half: the difference of the
    /// two matrices' positions, divided by the interval. The client's interval is
    /// `const float boneDt = 0.05f` (`c_tf_player.cpp:891`), so a bone that moved one unit in that
    /// window is travelling at twenty units a second.
    /// </remarks>
    [Test]
    public void BoneDerivatives_ForABoneThatMoved_DividesTheOffsetByTheInterval()
    {
        float[] previous = Identity(0f, 0f, 0f);
        float[] current = Identity(1f, 2f, 3f);

        (Vector3 velocity, _) = RagdollVelocity.BoneDerivatives(previous, current, 0.05f);

        velocity.X.ShouldBe(20f, Close);
        velocity.Y.ShouldBe(40f, Close);
        velocity.Z.ShouldBe(60f, Close);
    }

    /// <remarks>
    /// **The rotation half, and it is in DEGREES per second** because `QuaternionAxisAngle` returns
    /// degrees and Valve scales that number directly:
    /// `VectorScale( deltaAxis, (deltaAngle * scale), angVel )`. An `AngularImpulse` in Source is a
    /// degrees-per-second vector, not radians — a transcription that converted would be off by
    /// 57.3, which reads as a corpse that barely rotates.
    ///
    /// A quarter turn of yaw across 0.05 s is 1800 degrees a second about Z.
    /// </remarks>
    [Test]
    public void BoneDerivatives_ForABoneThatTurned_ReportsDegreesPerSecond()
    {
        float[] previous = Identity(0f, 0f, 0f);

        float[] current =
        [
            0f, -1f, 0f, 0f,
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
        ];

        (_, Vector3 angular) = RagdollVelocity.BoneDerivatives(previous, current, 0.05f);

        angular.X.ShouldBe(0f, Close);
        angular.Y.ShouldBe(0f, Close);
        angular.Z.ShouldBe(1800f, Close);
    }

    /// <remarks>
    /// **`float scale = 1.0; if ( dt &gt; 0 ) scale = 1.0 / dt;`** — a non-positive interval is not
    /// an error and not a division; the difference is taken as if one second had passed. That is a
    /// behaviour rather than a guard, so it is transcribed as one.
    /// </remarks>
    [Test]
    public void BoneDerivatives_WithAZeroInterval_TreatsTheOffsetAsPerSecond()
    {
        float[] previous = Identity(0f, 0f, 0f);
        float[] current = Identity(1f, 2f, 3f);

        (Vector3 velocity, _) = RagdollVelocity.BoneDerivatives(previous, current, 0f);

        velocity.X.ShouldBe(1f, Close);
        velocity.Y.ShouldBe(2f, Close);
        velocity.Z.ShouldBe(3f, Close);
    }

    /// <summary>A bone matrix that is the identity rotation at a position.</summary>
    private static float[] Identity(float x, float y, float z) =>
    [
        1f, 0f, 0f, x,
        0f, 1f, 0f, y,
        0f, 0f, 1f, z,
    ];
}
