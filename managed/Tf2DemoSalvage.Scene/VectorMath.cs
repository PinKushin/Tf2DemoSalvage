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
    /// <remarks>
    /// `vector.h:2242`'s <c>1.0e-10f</c>, and the same constant `C_OP_RenderSpriteTrail` adds before each of its two
    /// reciprocal roots (<c>DAT_1092704c</c> in `client.dll`).
    /// </remarks>
    public const float ZeroGuard = 1.0e-10f;

    /// <summary>The unit vector in a direction, as <c>VectorNormalize</c> computes it.</summary>
    /// <param name="x">The direction, east-west.</param>
    /// <param name="y">The direction, north-south.</param>
    /// <param name="z">The direction, vertically.</param>
    /// <returns>The unit vector, or the zero vector for a zero input.</returns>
    public static (float X, float Y, float Z) Normalized(float x, float y, float z)
    {
        float reciprocal = ReciprocalSqrt((x * x) + (y * y) + (z * z) + ZeroGuard);

        return (x * reciprocal, y * reciprocal, z * reciprocal);
    }

    /// <summary>A squared length's guarded reciprocal square root, as the engine computes it everywhere.</summary>
    /// <param name="x">The squared length with any guard already added.</param>
    /// <returns>Approximately <c>1 / sqrt(x)</c>.</returns>
    /// <remarks>
    /// **`rsqrtss` and one Newton step, <c>e·(3 − e²·x)·0.5</c>** — `_SSE_RSqrtInline` (`vector.h:2224`), and the same
    /// sequence `C_OP_RenderSpriteTrail`'s builder inlines twice in `client.dll` (the <c>3.0</c> at <c>DAT_1092706c</c>
    /// and <c>0.5</c> at <c>DAT_10926e38</c>). Shared so the trail and <see cref="Normalized"/> cannot drift apart.
    /// </remarks>
    public static float ReciprocalSqrt(float x)
    {
        float estimate = Sse.IsSupported
            ? Sse.ReciprocalSqrtScalar(Vector128.CreateScalar(x)).ToScalar()
            : 1f / MathF.Sqrt(x);

        return (3f - (estimate * estimate * x)) * estimate * 0.5f;
    }
}
