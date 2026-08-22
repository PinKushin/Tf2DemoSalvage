using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A material's texture coordinate transform, as the engine hands it to a vertex shader.
/// </summary>
/// <param name="Row0">The first row: <c>u' = dot(texcoord, Row0)</c>.</param>
/// <param name="Row1">The second row: <c>v' = dot(texcoord, Row1)</c>.</param>
/// <remarks>
/// **Two rows of a matrix, not a scale and an offset.** Valve uploads exactly this, from
/// <c>CBaseVSShader::SetVertexShaderTextureTransform</c> (<c>BaseVSShader.cpp:307</c>):
///
/// <code>
/// transformation[0].Init( mat[0][0], mat[0][1], mat[0][2], mat[0][3] );
/// transformation[1].Init( mat[1][0], mat[1][1], mat[1][2], mat[1][3] );
/// </code>
///
/// and the vertex shader dots each row against the incoming coordinate
/// (<c>unlittwotexture_vs20.fxc:63</c>):
///
/// <code>
/// o.baseTexCoord.x = dot( v.vTexCoord0, cBaseTexCoordTransform[0] );
/// o.baseTexCoord.y = dot( v.vTexCoord0, cBaseTexCoordTransform[1] );
/// </code>
///
/// The fourth column is therefore a translation, which only works because the coordinate arrives
/// as a <c>float4</c> with w = 1 — that is the whole reason the transform is a matrix rather than
/// a pair of floats, and the reason a scroll can be expressed at all.
///
/// **A material carries two independent ones**, for its base texture and its second texture, both
/// applied to the SAME incoming coordinate. TF2's capture point beams rely on that: one texture
/// holds still while the other scrolls over it.
/// </remarks>
public readonly record struct TextureTransform(
    (float X, float Y, float Z, float W) Row0,
    (float X, float Y, float Z, float W) Row1)
{
    /// <summary>The transform that changes nothing, which is what a material without one gets.</summary>
    /// <remarks>
    /// Valve's own fallback when the variable is missing or is not a matrix, from the same routine:
    /// <c>(1,0,0,0)</c> and <c>(0,1,0,0)</c>. Stated rather than left to a zeroed struct, because a
    /// zeroed one collapses every coordinate onto the texture's first texel.
    /// </remarks>
    public static TextureTransform Identity { get; } = new((1f, 0f, 0f, 0f), (0f, 1f, 0f, 0f));

    /// <summary>Whether this transform leaves coordinates alone.</summary>
    public bool IsIdentity => this == Identity;
}

/// <summary>
/// The material proxies this project reproduces, which are functions of time rather than of state.
/// </summary>
/// <remarks>
/// **A proxy rewrites a material's variables every frame, and without them a material is frozen.**
/// TF2's capture point is entirely proxy-driven: the beam scrolls, the lit sign pulses its colour
/// and the dark one pulses its alpha. With none of them the point renders as a still image that is
/// correct in every particular and obviously not alive — reported as "the brightness didn't seem to
/// change at all".
///
/// Only the time-driven ones belong here. A proxy that reads entity state — team, health, a player's
/// item — needs the entity, and belongs wherever the scene is assembled rather than in a static
/// helper.
/// </remarks>
public static class MaterialProxies
{
    /// <summary>Degrees to radians, as the engine writes it.</summary>
    private const float ToRadians = MathF.PI / 180f;

    /// <summary>Scrolls a texture across a surface over time.</summary>
    /// <param name="seconds">Playback time, which stands in for the engine's <c>curtime</c>.</param>
    /// <param name="rate">Scroll rate, from <c>textureScrollRate</c>.</param>
    /// <param name="angle">Direction in degrees, from <c>textureScrollAngle</c>.</param>
    /// <param name="scale">Coordinate scale, from <c>textureScale</c>; 1 by default.</param>
    /// <returns>The transform to hand the vertex shader.</returns>
    /// <remarks>
    /// **Ported from <c>CTextureScrollMaterialProxy::OnBind</c>**
    /// (<c>game/client/texturescrollmaterialproxy.cpp</c>):
    ///
    /// <code>
    /// sOffset = gpGlobals->curtime * cos( angle * ( M_PI / 180.0f ) ) * rate;
    /// tOffset = gpGlobals->curtime * sin( angle * ( M_PI / 180.0f ) ) * rate;
    /// if( sOffset &lt; 0.0f ) sOffset += 1.0f + -( int )sOffset;
    /// if( tOffset &lt; 0.0f ) tOffset += 1.0f + -( int )tOffset;
    /// sOffset = sOffset - ( int )sOffset;
    /// tOffset = tOffset - ( int )tOffset;
    /// VMatrix mat( scale, 0.0f, 0.0f, sOffset,
    ///              0.0f, scale, 0.0f, tOffset, … );
    /// </code>
    ///
    /// **The wrapping is kept exactly as the engine writes it**, rather than simplified to a
    /// modulo. The two are not the same function: Valve lifts a negative offset by
    /// <c>1 + -(int)offset</c> and then takes the fractional part, which lands in 0..1 for every
    /// input — and a naive <c>offset % 1</c> returns a NEGATIVE fraction for negative input, which
    /// scrolls the texture the wrong way for any material with a negative rate. The defaults for
    /// rate and scale are Valve's too, from the <c>Init</c> calls above <c>OnBind</c>.
    ///
    /// The offsets are bounded to one texture repeat deliberately: without the wrap they grow with
    /// playback time and lose precision, which on a long demo shows as a texture that jitters and
    /// then stops moving.
    /// </remarks>
    public static TextureTransform TextureScroll(
        double seconds, float rate = 1f, float angle = 0f, float scale = 1f)
    {
        float sOffset = (float)(seconds * Math.Cos(angle * ToRadians) * rate);
        float tOffset = (float)(seconds * Math.Sin(angle * ToRadians) * rate);

        return new TextureTransform(
            (scale, 0f, 0f, Wrap(sOffset)),
            (0f, scale, 0f, Wrap(tOffset)));
    }

    /// <summary>Brings an offset into 0..1 the way the engine does.</summary>
    private static float Wrap(float offset)
    {
        if (offset < 0f)
        {
            offset += 1f + -(int)offset;
        }

        return offset - (int)offset;
    }

    /// <summary>Oscillates a value between two bounds.</summary>
    /// <param name="seconds">Playback time, standing in for <c>curtime</c>.</param>
    /// <param name="period">Seconds for one full cycle, from <c>sineperiod</c>.</param>
    /// <param name="minimum">The low end, from <c>sinemin</c>.</param>
    /// <param name="maximum">The high end, from <c>sinemax</c>.</param>
    /// <returns>The value for this moment.</returns>
    /// <remarks>
    /// **This is what makes a capture point breathe.** The lit sign runs a Sine on <c>$color</c>
    /// between .8 and 1 over a second; the dark one runs a faster one on <c>$alpha</c>. Both were
    /// static here, which is why the owner saw no brightness change at all.
    ///
    /// Valve's <c>CSineProxy</c> is the same shape as the scroll: a function of <c>curtime</c>
    /// mapped onto a range.
    ///
    /// **A period of zero becomes a period of ONE, and this used to hold at the maximum instead.**
    /// <c>mathproxy.cpp:408</c> is one line and unambiguous:
    ///
    /// <code>
    /// if (flSinePeriod == 0)
    ///     flSinePeriod = 1;
    /// </code>
    ///
    /// The old reasoning — "a material naming no period is not asking to oscillate, and must not
    /// divide by zero" — is sound engineering and is not what the engine does. It had a passing
    /// test, written alongside the implementation, so the two agreed with each other rather than
    /// with Valve. Caught by <c>MaterialProxyConformanceTests</c>, which reads the source instead.
    ///
    /// A NEGATIVE period is left alone, as the engine leaves it: the guard is <c>== 0</c>, and a
    /// negative period simply runs the phase backwards.
    /// </remarks>
    public static float Sine(double seconds, float period, float minimum, float maximum)
    {
        if (period == 0f)
        {
            period = 1f;
        }

        // Half the span either side of the midpoint, which is what a sine between two bounds is.
        float middle = (maximum + minimum) / 2f;
        float half = (maximum - minimum) / 2f;

        return middle + (half * MathF.Sin((float)(seconds * 2d * Math.PI / period)));
    }

    /// <summary>Reads a proxy's numeric argument, or its default when the key is absent.</summary>
    /// <param name="value">The raw text from the VMT, or null.</param>
    /// <param name="fallback">What the engine's own <c>Init</c> call passes as the default.</param>
    /// <returns>The number.</returns>
    /// <remarks>
    /// Invariant culture, because a VMT is machine text: a decimal comma would read
    /// <c>.1</c> as 1 on a machine whose locale uses one, which is a tenfold scroll rate and looks
    /// like a renderer fault.
    /// </remarks>
    public static float Number(string? value, float fallback) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
}
