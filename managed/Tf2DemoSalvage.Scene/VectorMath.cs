using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Tf2DemoSalvage.Scene;

/// <summary>Valve's <c>VectorNormalize</c>, reproduced including its approximation.</summary>
/// <remarks>
/// **This is not "divide by the length", and the difference is deliberate.** `vector.h:2239` takes the SSE
/// reciprocal-square-root estimate and refines it with one Newton step, rather than computing a true reciprocal:
///
/// <code>
/// FORCEINLINE float VectorNormalize( Vector&amp; vec )
/// {
/// #if defined( PLATFORM_INTEL )
///     float sqrlen = vec.LengthSqr() + 1.0e-10f, invlen;
///     _SSE_RSqrtInline(sqrlen, &amp;invlen);
///     vec.x *= invlen;  vec.y *= invlen;  vec.z *= invlen;
///     return sqrlen * invlen;
/// </code>
///
/// with `_SSE_RSqrtInline` (`vector.h:2224`) being `rsqrtss` followed by `x·(3 − x²·a)·0.5`.
///
/// **Two details that a tidier formulation loses.** The `1.0e-10f` added to the SQUARED length is the
/// divide-by-zero guard — it is what makes a zero vector answer zero instead of `NaN`, so it is behaviour rather
/// than numerical hygiene. And the estimate carries roughly a part in ten million of error that a true
/// reciprocal does not, which is invisible in a direction but is a real difference in the bits.
///
/// **It lives here rather than inside its one caller because it already had three.** `PlayerGibs.Unit`,
/// `ViewFrustum` and `StudioBones` each carry their own reading of this function — the same shape of duplication
/// that <see cref="AngleVectors"/> exists to have ended (B204). Those three are not converted here; converting
/// them changes what they answer in the last bits and wants its own change and its own sabotage.
/// </remarks>
public static class VectorMath
{
    /// <summary>The guard added to the SQUARED length, so a zero vector normalises to zero rather than NaN.</summary>
    private const float ZeroGuard = 1.0e-10f;

    /// <summary>The unit vector in a direction, as <c>VectorNormalize</c> computes it.</summary>
    /// <param name="x">The direction, east-west.</param>
    /// <param name="y">The direction, north-south.</param>
    /// <param name="z">The direction, vertically.</param>
    /// <returns>The unit vector, or the zero vector for a zero input.</returns>
    public static (float X, float Y, float Z) Normalized(float x, float y, float z)
    {
        float squared = (x * x) + (y * y) + (z * z) + ZeroGuard;

        float estimate = Sse.IsSupported
            ? Sse.ReciprocalSqrtScalar(Vector128.CreateScalar(squared)).ToScalar()
            : 1f / MathF.Sqrt(squared);

        float reciprocal = (3f - (estimate * estimate * squared)) * estimate * 0.5f;

        return (x * reciprocal, y * reciprocal, z * reciprocal);
    }
}
