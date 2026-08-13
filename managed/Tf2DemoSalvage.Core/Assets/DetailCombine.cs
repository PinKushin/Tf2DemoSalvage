using System;
using System.IO;

namespace Tf2DemoSalvage.Core.Assets;

/// <summary>A colour as the shader carries it: linear, unclamped, with alpha.</summary>
/// <param name="Red">Red.</param>
/// <param name="Green">Green.</param>
/// <param name="Blue">Blue.</param>
/// <param name="Alpha">Alpha, which several combine modes read and four of them write.</param>
/// <remarks>
/// **Unclamped on purpose.** Mode 0 doubles, mode 1 adds, and both routinely exceed one before the
/// lightmap multiply brings them back down. Clamping here would flatten highlights that Valve's own
/// shader keeps, and the saturation belongs at the end of the pipeline rather than in the middle of
/// it.
/// </remarks>
public readonly record struct MaterialColour(float Red, float Green, float Blue, float Alpha);

/// <summary>
/// Combining a detail texture with a base texture, exactly as Source's shaders do it.
/// </summary>
/// <remarks>
/// **Transcribed from <c>TextureCombine</c> and <c>TextureCombinePostLighting</c> in
/// <c>src/materialsystem/stdshaders/common_ps_fxc.h</c>.** The two functions exist separately
/// because two of the twelve modes are self-illumination: they add the detail to the *lit* colour,
/// after the lightmap has been applied, so applying them here as well would add them twice.
///
/// A detail texture is a small tiling pattern - noise, concrete grain, brick speckle - scaled up by
/// <c>$detailscale</c> (four by default) and multiplied into the base texture to break up the
/// repetition a 512-pixel wall texture would otherwise show. It is a large share of what makes a
/// Source surface look like a surface rather than a flat colour, and on cp_process_f12 it covers 36
/// materials and 36 million drawn units.
///
/// **Two things here differ from what the modes look like at a glance**, both established by
/// reading the source rather than by inference:
///
/// - Mode 11 has no blend factor in it at all, so <c>$detailblendfactor "0"</c> does not turn it
///   off. Every other mode fades to nothing.
/// - Modes 3, 4, 8 and 9 write alpha. Since alpha is what the alpha test reads, those four change
///   which pixels survive rather than only what colour they are - the clip has to happen after
///   this, not before it.
/// </remarks>
public static class DetailCombine
{
    /// <summary>Base times the detail doubled: the original mode, and the default.</summary>
    public const int BaseTimesDetailDoubled = 0;

    /// <summary>The detail is added to the base.</summary>
    public const int Additive = 1;

    /// <summary>The detail is blended over the base through its own alpha.</summary>
    public const int DetailOverBase = 2;

    /// <summary>A straight fade between base and detail, alpha included.</summary>
    public const int Fade = 3;

    /// <summary>The base is blended over the detail through the base's alpha.</summary>
    public const int BaseOverDetail = 4;

    /// <summary>The detail is added after lighting, as self-illumination.</summary>
    public const int AdditiveSelfIllum = 5;

    /// <summary>As <see cref="AdditiveSelfIllum"/>, remapping a widening band of the detail.</summary>
    public const int AdditiveSelfIllumThresholdFade = 6;

    /// <summary>The base's alpha selects between two patterns in the detail.</summary>
    public const int Mod2xSelectTwoPatterns = 7;

    /// <summary>The base is multiplied by the detail.</summary>
    public const int Multiply = 8;

    /// <summary>The detail's alpha masks the base's alpha.</summary>
    public const int MaskBaseByDetailAlpha = 9;

    /// <summary>The detail is a self-shadowing bump map, modulating bumped lighting.</summary>
    public const int SelfShadowBump = 10;

    /// <summary>The detail is a self-shadowing bump map used as an albedo.</summary>
    public const int SelfShadowBumpNoBump = 11;

    /// <summary>The highest mode the shader's combo declares.</summary>
    public const int HighestMode = SelfShadowBumpNoBump;

    /// <summary>Combines a detail texture into an albedo, before lighting.</summary>
    /// <param name="albedo">The base texture's colour.</param>
    /// <param name="detail">The detail texture's colour, already tinted.</param>
    /// <param name="mode">The mode from <c>$detailblendmode</c>.</param>
    /// <param name="blendFactor">The strength from <c>$detailblendfactor</c>.</param>
    /// <returns>The combined colour.</returns>
    /// <exception cref="InvalidDataException">The mode is outside the declared range.</exception>
    /// <remarks>
    /// Modes handled after lighting return the albedo untouched, as Valve's chain of <c>if</c>s
    /// does - they are not unimplemented, they belong to <see cref="ApplyAfterLighting"/>.
    /// </remarks>
    public static MaterialColour Apply(
        MaterialColour albedo, MaterialColour detail, int mode, float blendFactor)
    {
        RefuseUnknownMode(mode);

        return mode switch
        {
            Mod2xSelectTwoPatterns => Scale(
                albedo, Lerp(1f, 2f * Lerp(detail.Red, detail.Alpha, albedo.Alpha), blendFactor)),
            BaseTimesDetailDoubled => new MaterialColour(
                albedo.Red * Lerp(1f, 2f * detail.Red, blendFactor),
                albedo.Green * Lerp(1f, 2f * detail.Green, blendFactor),
                albedo.Blue * Lerp(1f, 2f * detail.Blue, blendFactor),
                albedo.Alpha),
            Additive => new MaterialColour(
                albedo.Red + (blendFactor * detail.Red),
                albedo.Green + (blendFactor * detail.Green),
                albedo.Blue + (blendFactor * detail.Blue),
                albedo.Alpha),
            DetailOverBase => BlendColour(albedo, detail, blendFactor * detail.Alpha),
            Fade => new MaterialColour(
                Lerp(albedo.Red, detail.Red, blendFactor),
                Lerp(albedo.Green, detail.Green, blendFactor),
                Lerp(albedo.Blue, detail.Blue, blendFactor),
                Lerp(albedo.Alpha, detail.Alpha, blendFactor)),
            BaseOverDetail => BlendColour(
                albedo, detail, blendFactor * (1f - albedo.Alpha)) with { Alpha = detail.Alpha },
            Multiply => new MaterialColour(
                Lerp(albedo.Red, albedo.Red * detail.Red, blendFactor),
                Lerp(albedo.Green, albedo.Green * detail.Green, blendFactor),
                Lerp(albedo.Blue, albedo.Blue * detail.Blue, blendFactor),
                Lerp(albedo.Alpha, albedo.Alpha * detail.Alpha, blendFactor)),
            MaskBaseByDetailAlpha => albedo with
            {
                Alpha = Lerp(albedo.Alpha, albedo.Alpha * detail.Alpha, blendFactor),
            },

            // **No blend factor, deliberately.** Valve's line is a bare multiply, so this mode
            // cannot be faded out the way every other one can.
            SelfShadowBumpNoBump => Scale(
                albedo, (detail.Red + detail.Green + detail.Blue) * (2f / 3f)),

            // Modes 5, 6 and 10 belong to a later stage: the first two to ApplyAfterLighting, and
            // the third to the bumped lighting path, which modulates light rather than albedo.
            _ => albedo,
        };
    }

    /// <summary>Adds a self-illuminating detail texture to a colour that has been lit.</summary>
    /// <param name="lit">The colour after the lightmap has been applied.</param>
    /// <param name="detail">The detail texture's colour, already tinted.</param>
    /// <param name="mode">The mode from <c>$detailblendmode</c>.</param>
    /// <param name="blendFactor">The strength from <c>$detailblendfactor</c>.</param>
    /// <returns>The lit colour with any self-illumination added.</returns>
    /// <exception cref="InvalidDataException">The mode is outside the declared range.</exception>
    /// <remarks>
    /// Every mode other than 5 and 6 passes through unchanged, which is the control: a mode already
    /// applied to the albedo must not be applied again here.
    /// </remarks>
    public static (float Red, float Green, float Blue) ApplyAfterLighting(
        (float Red, float Green, float Blue) lit,
        MaterialColour detail,
        int mode,
        float blendFactor)
    {
        RefuseUnknownMode(mode);

        if (mode == AdditiveSelfIllum)
        {
            return (
                lit.Red + (blendFactor * detail.Red),
                lit.Green + (blendFactor * detail.Green),
                lit.Blue + (blendFactor * detail.Blue));
        }

        if (mode != AdditiveSelfIllumThresholdFade)
        {
            return lit;
        }

        // Valve's own comment calls this "an unusual way" to fade: rather than fading the colour
        // out, it remaps an increasing band of it onto nought-to-one. The two branches meet at a
        // blend factor of one half.
        float multiplier = blendFactor >= 0.5f ? 1f / blendFactor : 4f * blendFactor;
        float offset = blendFactor >= 0.5f ? 1f - multiplier : -0.5f * multiplier;

        return (
            lit.Red + Saturate((multiplier * detail.Red) + offset),
            lit.Green + Saturate((multiplier * detail.Green) + offset),
            lit.Blue + Saturate((multiplier * detail.Blue) + offset));
    }

    private static void RefuseUnknownMode(int mode)
    {
        // **Valve's chain of ifs would draw an unknown mode as though there were no detail at
        // all.** That is a silent fallback, and this project does not have those: a material
        // naming a mode outside the shader's declared "0..11" is malformed, and saying so is the
        // difference between finding it and shipping a surface that is quietly missing its grain.
        if (mode is < 0 or > HighestMode)
        {
            throw new InvalidDataException(
                $"A material names detail blend mode {mode}, which is outside the range 0 to " +
                $"{HighestMode} that the shader declares.");
        }
    }

    // Named "start" rather than "from", which is a query keyword and turns `from with { ... }`
    // into the opening of a LINQ expression.
    private static MaterialColour BlendColour(
        MaterialColour start, MaterialColour to, float amount) =>
        start with
        {
            Red = Lerp(start.Red, to.Red, amount),
            Green = Lerp(start.Green, to.Green, amount),
            Blue = Lerp(start.Blue, to.Blue, amount),
        };

    private static MaterialColour Scale(MaterialColour colour, float by) =>
        colour with
        {
            Red = colour.Red * by,
            Green = colour.Green * by,
            Blue = colour.Blue * by,
        };

    private static float Lerp(float from, float to, float amount) =>
        from + ((to - from) * amount);

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
