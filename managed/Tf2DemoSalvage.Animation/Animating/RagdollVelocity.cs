using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The velocity a corpse inherits from the animation it died in (B58).
/// </summary>
/// <remarks>
/// **This is the whole reason a killed Scout keeps running and a killed Heavy drops.** The client
/// poses the player TWICE when it makes a ragdoll — once at `curtime - 0.05` and once at `curtime`
/// (`GetRagdollInitBoneArrays`, `c_baseanimating.cpp:4775`) — and hands the difference to the solver
/// as every element's starting linear and angular velocity:
///
/// <code>
///   void RagdollApplyAnimationAsVelocity( ragdoll_t &amp;ragdoll, const matrix3x4_t *pPrevBones,
///                                         const matrix3x4_t *pCurrentBones, float dt )
///   {
///       for ( int i = 0; i &lt; ragdoll.listCount; i++ )
///       {
///           int boneIndex = ragdoll.boneIndex[i];
///           CalcBoneDerivatives( velocity, angVel, pPrevBones[boneIndex], pCurrentBones[boneIndex], dt );
///           // Angular velocity is always applied in local space in vphysics
///           ragdoll.list[i].pObject-&gt;WorldToLocalVector( &amp;localAngVelocity, angVel );
///           ragdoll.list[i].pObject-&gt;AddVelocity( &amp;velocity, &amp;localAngVelocity );
///       }
///   }
/// </code>
///
/// `ragdoll_shared.cpp:458`, and `const float boneDt = 0.05f` at `c_tf_player.cpp:891`.
///
/// **All of it is published**, which is worth saying because the solver it feeds is not: the
/// integration and the constraint solve live in `vphysics.dll` (D142), but the initial conditions
/// are in the SDK in full and are the part a viewer gets wrong first. A ragdoll built without them
/// appears at the death pose carrying nothing but the kill's force, which is the visible symptom —
/// corpses that fall straight down out of a sprint.
/// </remarks>
public static class RagdollVelocity
{
    /// <summary>The interval the client differences two poses across.</summary>
    /// <remarks>
    /// **`const float boneDt = 0.05f`** (`c_tf_player.cpp:891`), a literal at every call site rather
    /// than a convar — the same 0.05 appears in `C_BaseAnimating::BecomeRagdollOnClient`. It is not
    /// the frame time and not the tick interval: it is a fixed window backwards from now, so the
    /// velocity a corpse inherits does not change with the viewer's frame rate.
    /// </remarks>
    public const float BoneInterval = 0.05f;

    /// <summary>How fast a bone was moving and turning — <c>CalcBoneDerivatives</c>.</summary>
    /// <param name="previous">The bone's matrix at the start of the window, row-major 3x4.</param>
    /// <param name="current">Its matrix at the end, row-major 3x4.</param>
    /// <param name="interval">Seconds between them; <see cref="BoneInterval"/> on the client.</param>
    /// <returns>
    /// Linear velocity in units per second, and angular velocity in DEGREES per second about a
    /// world axis.
    /// </returns>
    /// <exception cref="ArgumentException">A matrix is not twelve floats.</exception>
    /// <remarks>
    /// **`bone_setup.cpp:2521`**, and three things in it are easy to get wrong:
    ///
    /// <code>
    ///   float scale = 1.0;
    ///   if ( dt &gt; 0 ) scale = 1.0 / dt;
    ///   MatrixAngles( prev, startAngles, startPosition );
    ///   MatrixAngles( current, endAngles, endPosition );
    ///   velocity = (endPosition - startPosition) * scale;
    ///   RotationDeltaAxisAngle( startAngles, endAngles, deltaAxis, deltaAngle );
    ///   VectorScale( deltaAxis, (deltaAngle * scale), angVel );
    /// </code>
    ///
    /// - **A non-positive interval is not an error and not a division.** `scale` stays 1, so the
    ///   raw offset is reported as a per-second rate. That is a behaviour, and it is what a caller
    ///   handing over two identical timestamps gets.
    /// - **The angle is in DEGREES**, because `QuaternionAxisAngle` returns degrees and Valve
    ///   multiplies that number straight into the result. Source's `AngularImpulse` is a
    ///   degrees-per-second vector; converting to radians here would be wrong by 57.3 and read as a
    ///   corpse whose limbs barely turn.
    /// - **The rotations go through EULER ANGLES**, not straight from matrix to quaternion.
    ///   `MatrixAngles`' `QAngle` overload is what Valve calls, and `RotationDeltaAxisAngle` takes
    ///   two `QAngle`s and converts them back. The round trip loses roll wherever a bone points at
    ///   the sky — see <see cref="StudioBones.ToAngles"/> — and skipping it would give a DIFFERENT
    ///   answer there rather than a better one.
    /// </remarks>
    public static (Vector3 Velocity, Vector3 Angular) BoneDerivatives(
        ReadOnlySpan<float> previous, ReadOnlySpan<float> current, float interval)
    {
        float scale = interval > 0f ? 1f / interval : 1f;

        (float Pitch, float Yaw, float Roll) start = StudioBones.ToAngles(previous);
        (float Pitch, float Yaw, float Roll) end = StudioBones.ToAngles(current);

        // A matrix3x4_t's translation is its fourth column.
        Vector3 velocity = new(
            (current[3] - previous[3]) * scale,
            (current[7] - previous[7]) * scale,
            (current[11] - previous[11]) * scale);

        ((float X, float Y, float Z) axis, float angle) = RotationDelta(start, end);

        return (velocity, new Vector3(axis.X, axis.Y, axis.Z) * (angle * scale));
    }

    /// <summary>The rotation from one set of angles to another — <c>RotationDeltaAxisAngle</c>.</summary>
    /// <param name="from">The starting angles, in degrees.</param>
    /// <param name="to">The ending angles, in degrees.</param>
    /// <returns>The axis turned about, and how far, in degrees.</returns>
    /// <remarks>
    /// **`mathlib_base.cpp:3542`**:
    ///
    /// <code>
    ///   AngleQuaternion( srcAngles, srcQuat );   AngleQuaternion( destAngles, destQuat );
    ///   QuaternionScale( srcQuat, -1, srcQuatInv );
    ///   QuaternionMult( destQuat, srcQuatInv, out );
    ///   QuaternionNormalize( out );
    ///   QuaternionAxisAngle( out, deltaAxis, deltaAngle );
    /// </code>
    ///
    /// **`QuaternionScale( q, -1 )` is Valve's inverse and it is not a plain conjugate.** The
    /// function scales the ANGLE of a rotation through `sin( asin( |xyz| ) * t )`, which for
    /// `t = -1` negates the vector part and rebuilds `w` from a square root while keeping its sign —
    /// equal to the conjugate for a unit quaternion, and not equal for one that has drifted. The
    /// implementation carries Valve's own note that it is not overly sensitive to accuracy, so this
    /// calls the same scale rather than substituting a conjugate.
    ///
    /// **`QuaternionMult` aligns before multiplying**, flipping the second rotation when the two
    /// point opposite ways. That is what keeps the delta on the short way round before
    /// <see cref="StudioBones.AxisAngle"/>'s own wrap sees it.
    /// </remarks>
    public static ((float X, float Y, float Z) Axis, float Angle) RotationDelta(
        (float Pitch, float Yaw, float Roll) from,
        (float Pitch, float Yaw, float Roll) to)
    {
        (float X, float Y, float Z, float W) source =
            StudioBones.FromAngles(from.Pitch, from.Yaw, from.Roll);

        (float X, float Y, float Z, float W) destination =
            StudioBones.FromAngles(to.Pitch, to.Yaw, to.Roll);

        (float X, float Y, float Z, float W) inverse = StudioBones.Scale(source, -1f);

        return StudioBones.AxisAngle(
            StudioBones.Normalize(StudioBones.Multiply(destination, inverse)));
    }
}
