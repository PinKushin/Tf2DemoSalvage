using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>
/// Combining a bump-lit face's three directional lightmaps against its surface normal.
/// </summary>
/// <remarks>
/// **A bump-lit face carries light from three directions rather than one colour.** vrad samples
/// each luxel from the three vectors in <c>bumpvects.h</c> — evenly spread around the surface
/// normal and all leaning equally towards it — and stores a full lightmap for each. The renderer
/// then asks, per pixel, which way the surface actually faces according to its normal map, and
/// mixes the three accordingly. That is what makes a flat wall look like brick rather than like a
/// photograph of brick.
///
/// **Two combines that share nothing.** An ordinary normal map stores a direction, and the weights
/// are squared saturated dot products normalised by their sum. A self-shadowing bump map stores
/// three weights outright, and they are used as they are. Measured on cp_process_final it is 13
/// materials against 14, so neither is a special case that can be left for later.
///
/// Transcribed from <c>lightmappedgeneric_ps2_3_x.h</c>. Lives in Core, and is pure, because the
/// arithmetic is the part worth testing and a GPU is the worst place to test arithmetic.
/// </remarks>
public static class BumpedLight
{
    private const float OneOverSqrtThree = 0.57735025882720947f;
    private const float OneOverSqrtTwo = 0.70710676908493042f;
    private const float OneOverSqrtSix = 0.40824821591377258f;
    private const float OneOverSqrtTwoOverThree = 0.81649661064147949f;

    /// <summary>The three directions vrad samples each luxel from.</summary>
    /// <remarks>
    /// <c>g_localBumpBasis</c> from <c>src/public/mathlib/bumpvects.h</c>, to the float. Valve
    /// hard-codes these rather than computing them, and recomputing them from <c>sqrt</c> gives
    /// very slightly different values — so they are copied rather than derived.
    /// </remarks>
    public static IReadOnlyList<(float X, float Y, float Z)> Basis { get; } =
    [
        (OneOverSqrtTwoOverThree, 0f, OneOverSqrtThree),
        (-OneOverSqrtSix, OneOverSqrtTwo, OneOverSqrtThree),
        (-OneOverSqrtSix, -OneOverSqrtTwo, OneOverSqrtThree),
    ];

    /// <summary>Mixes three directional lightmaps for one surface normal.</summary>
    /// <param name="normal">The surface normal in tangent space, or three weights for ssbump.</param>
    /// <param name="first">Light from the first basis direction.</param>
    /// <param name="second">Light from the second.</param>
    /// <param name="third">Light from the third.</param>
    /// <param name="selfShadowing">Whether the bump map is self-shadowing rather than a normal.</param>
    /// <returns>The light reaching this pixel.</returns>
    /// <remarks>
    /// **Tangent space on both sides, so no per-vertex basis is needed.** The dot products are
    /// against the constant table, and the normal comes out of the normal map already in that
    /// space. vrad resolved handedness at compile time — <c>GetBumpNormals</c> negates the second
    /// axis on left-handed faces — so the stored sets are always in the canonical frame and the
    /// renderer never has to know how a face was wound.
    /// </remarks>
    public static (float Red, float Green, float Blue) Combine(
        (float X, float Y, float Z) normal,
        (float Red, float Green, float Blue) first,
        (float Red, float Green, float Blue) second,
        (float Red, float Green, float Blue) third,
        bool selfShadowing)
    {
        if (selfShadowing)
        {
            // No dots, no squaring, no division: an ssbump texel is already three weights. The
            // result is deliberately not normalised, so weights summing to two give twice the
            // light.
            return Mix(normal.X, normal.Y, normal.Z, first, second, third);
        }

        IReadOnlyList<(float X, float Y, float Z)> basis = Basis;

        float toFirst = Saturate(Dot(normal, basis[0]));
        float toSecond = Saturate(Dot(normal, basis[1]));
        float toThird = Saturate(Dot(normal, basis[2]));

        toFirst *= toFirst;
        toSecond *= toSecond;
        toThird *= toThird;

        float sum = toFirst + toSecond + toThird;

        if (sum <= 0f)
        {
            // **Valve's shader divides here without checking**, and a GPU is content to hand back
            // infinity or NaN. It takes a normal pointing into the surface, which well-formed
            // content does not contain - but a NaN colour spreads through the rest of the frame as
            // a pixel nothing explains, and refusing costs one comparison.
            return (0f, 0f, 0f);
        }

        (float red, float green, float blue) = Mix(
            toFirst, toSecond, toThird, first, second, third);

        // **The division is what makes this a mix rather than a scale.** The three squared weights
        // do not sum to one - for a normal straight out of the surface they come to a third - so
        // without it the surface brightness swings with the direction the normal faces. That reads
        // as a wall rippling with light rather than with shape.
        return (red / sum, green / sum, blue / sum);
    }

    /// <summary>Turns a stored normal map texel into a signed direction.</summary>
    /// <param name="red">Stored red.</param>
    /// <param name="green">Stored green.</param>
    /// <param name="blue">Stored blue.</param>
    /// <returns>The normal, each component from -1 to 1.</returns>
    /// <remarks>
    /// <c>normalTexel.xyz * 2 - 1</c>, which is <c>NORM_DECODE_NONE</c> in Valve's
    /// <c>DecompressNormal</c>. A normal map stores -1 as 0 and +1 as 255, so 128 is flat.
    /// </remarks>
    public static (float X, float Y, float Z) Decode(byte red, byte green, byte blue) =>
        ((red / 255f * 2f) - 1f, (green / 255f * 2f) - 1f, (blue / 255f * 2f) - 1f);

    /// <summary>Turns a stored self-shadowing texel into three weights.</summary>
    /// <param name="red">Stored red.</param>
    /// <param name="green">Stored green.</param>
    /// <param name="blue">Stored blue.</param>
    /// <returns>The weights, each from 0 to 1.</returns>
    /// <remarks>
    /// **No signed decode, because an ssbump texel is not a direction.** Valve's shader samples it
    /// with a bare <c>tex2D</c> where an ordinary bump map goes through <c>DecompressNormal</c>.
    /// Applying the signed decode anyway sends a flat 128 to zero, and the surface goes black
    /// exactly where it should be evenly lit.
    /// </remarks>
    public static (float X, float Y, float Z) DecodeSelfShadowing(byte red, byte green, byte blue) =>
        (red / 255f, green / 255f, blue / 255f);

    private static (float Red, float Green, float Blue) Mix(
        float toFirst,
        float toSecond,
        float toThird,
        (float Red, float Green, float Blue) first,
        (float Red, float Green, float Blue) second,
        (float Red, float Green, float Blue) third) =>
        (
            (toFirst * first.Red) + (toSecond * second.Red) + (toThird * third.Red),
            (toFirst * first.Green) + (toSecond * second.Green) + (toThird * third.Green),
            (toFirst * first.Blue) + (toSecond * second.Blue) + (toThird * third.Blue));

    private static float Dot((float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
