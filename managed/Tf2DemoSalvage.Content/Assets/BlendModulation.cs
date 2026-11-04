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
    /// <param name="heightFog">
    /// `PIXELFOGTYPE == PIXEL_FOG_TYPE_HEIGHT`: the draw is under a water volume's height fog. A property of the DRAW,
    /// so the renderer's material table asks without it and the pixel shader applies it.
    /// </param>
    public static bool Applies(bool hasMask, bool hasSecondBaseTexture, bool selfIllum, bool heightFog = false) =>
        hasMask && hasSecondBaseTexture && !selfIllum && !heightFog;

    /// <summary>A cubemap's contrast and saturation as a lightmapped material draws them — `#if FANCY_BLENDING == 0` (`lightmappedgeneric_ps2_3_x.h:538-543`).</summary>
    /// <param name="hasMask">`FANCY_BLENDING`: the mask is a texture (`lightmappedgeneric_dx9_helper.cpp:315-317`, `:568`).</param>
    /// <param name="contrast">`$envmapcontrast`.</param>
    /// <param name="saturation">`$envmapsaturation`.</param>
    /// <param name="fresnelReflection">`$fresnelreflection`.</param>
    /// <param name="selfIllumTinted">`$selfillum` with a `$selfillumtint` off white.</param>
    /// <returns>
    /// The pair as given where the shader reads its registers, the fast path's constants where it does not
    /// (`lightmappedgeneric_dx9_helper.cpp:714-720, 925`; `ps2_3_x.h:54-59`), and the two values that change nothing where the
    /// block is compiled out. So `$envmapcontrast .5` alone is no contrast, and a lone `$envmapsaturation` is ignored.
    /// </returns>
    /// <remarks>
    /// *A ceiling, not parity:* `SEAMLESS` and `$outline` force the shader's fast path too (`ps2_3_x.h:44-48`, helper `:924`), and
    /// this does not. Neither is read here; a material with either and a reflection keeps its registers' values.
    /// </remarks>
    public static (float Contrast, float Saturation) EnvMapShaping(
        bool hasMask, float contrast, float saturation, float fresnelReflection = 1f, bool selfIllumTinted = false)
    {
        if (hasMask)
        {
            return (0f, 1f);
        }

        // Exact comparisons, as the helper makes them: a VMT's 1 is the float 1, and .999 really is off the fast path.
        bool usingContrast = contrast is not (0f or 1f) && saturation is not 1f;

        if (usingContrast || fresnelReflection is not 1f || selfIllumTinted)
        {
            return (contrast, saturation);
        }

        return (contrast is 1f ? 1f : 0f, 1f);
    }

    /// <summary>HLSL's `saturate`, NaN to zero included.</summary>
    private static float Saturate(float value) => value > 0f ? System.MathF.Min(value, 1f) : 0f;
}
