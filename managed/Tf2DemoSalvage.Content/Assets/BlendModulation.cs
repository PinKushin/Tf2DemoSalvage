namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// `$blendmodulatetexture`: how a two-texture world material turns a vertex's blend into the mix it draws with.
/// </summary>
/// <remarks>
/// **The vertex alpha is not the mix; it is the input to a per-texel edge.** A displacement's alpha is a soft gradient
/// across a few vertices, and drawn as it is the dirt fades into the grass like fog. The mask gives every texel its own
/// threshold (green) and half-width (red), so the line follows the mask's pattern — blades of grass, cracks — and is as
/// sharp as the artist drew it.
///
/// Transcribed from `lightmappedgeneric_ps2_3_x.h:319-333`; the pixel shader in `WorldRenderer` carries the same lines
/// and is not unit-testable, which is why this exists (as <c>BumpedLight</c> does for <c>CombineBumped</c>).
/// </remarks>
public static class BlendModulation
{
    /// <summary>The mix between the first and second base texture at one pixel.</summary>
    /// <param name="vertexBlend">The interpolated vertex alpha, `vertexBlendX_fogFactorW.r`.</param>
    /// <param name="red">The mask texel's red: half the width of the edge.</param>
    /// <param name="green">The mask texel's green: where the edge sits.</param>
    /// <returns>`smoothstep( saturate( g - r ), saturate( g + r ), vertexBlend )`.</returns>
    /// <remarks>
    /// **A red of zero is a division by zero, on purpose**: both bounds are green and the result is a hard step.
    /// HLSL's `smoothstep` is `t = saturate( (x - min) / (max - min) )`, `t * t * (3 - 2 * t)`; below the step that is
    /// `saturate(-inf)`, above it `saturate(+inf)`, and exactly on it `saturate(NaN)`, which Direct3D 11 defines as 0.
    /// </remarks>
    public static float Factor(float vertexBlend, float red, float green)
    {
        float minimum = Saturate(green - red);
        float maximum = Saturate(green + red);
        float t = Saturate((vertexBlend - minimum) / (maximum - minimum));

        return t * t * (3f - (2f * t));
    }

    /// <summary>Whether the shader reads the mask at all.</summary>
    /// <param name="hasMask">The material names a `$blendmodulatetexture` that loaded — `FANCY_BLENDING` (`lightmappedgeneric_dx9_helper.cpp:315-317, 568`).</param>
    /// <param name="hasSecondBaseTexture">`bBaseTexture2`: the sample sits inside that branch (`lightmappedgeneric_ps2_3_x.h:317`).</param>
    /// <param name="selfIllum">`SELFILLUM`, which the branch excludes (`:319`).</param>
    /// <returns>True when the mix goes through <see cref="Factor"/>.</returns>
    /// <remarks>
    /// The third guard, `PIXELFOGTYPE != PIXEL_FOG_TYPE_HEIGHT`, is the camera being under water and is not reproduced:
    /// a submerged view keeps the modulated edge here where the engine falls back to the raw vertex blend.
    /// </remarks>
    public static bool Applies(bool hasMask, bool hasSecondBaseTexture, bool selfIllum) =>
        hasMask && hasSecondBaseTexture && !selfIllum;

    /// <summary>HLSL's `saturate`, NaN to zero included.</summary>
    private static float Saturate(float value) => value > 0f ? System.MathF.Min(value, 1f) : 0f;
}
