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

    /// <summary>The matrix a <c>$basetexturetransform</c> string names (B332).</summary>
    /// <param name="text">
    /// The packed form, e.g. <c>center .5 .5 scale 1 1 rotate 0 translate 0 0</c>. Null, empty or
    /// malformed gives the identity.
    /// </param>
    /// <returns>The transform's first two rows, which is all a shader is given.</returns>
    /// <remarks>
    /// **The string's form is stated by the parameter's own declared default**, which is the SDK
    /// answering a question about a parser it does not ship:
    ///
    /// <code>
    /// SHADER_PARAM( BASETEXTURETRANSFORM, SHADER_PARAM_TYPE_MATRIX,
    ///               "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$baseTexture texcoord transform" )
    /// </code>
    ///
    /// 53 shaders declare it with exactly that string.
    ///
    /// **And the COMPOSITION is in the SDK, in a proxy that builds the same matrix from separate
    /// variables** — so it is the authority on what the packed form means:
    ///
    /// <code>
    /// MatrixBuildTranslation( mat,  -center.x, -center.y, 0.0f );
    /// MatrixBuildScale      ( temp,  scale.x,   scale.y,  1.0f );  MatrixMultiply( temp, mat, mat );
    /// MatrixBuildRotateZ    ( temp,  angle );                      MatrixMultiply( temp, mat, mat );
    /// MatrixBuildTranslation( temp,  center.x,  center.y, 0.0f );  MatrixMultiply( temp, mat, mat );
    /// MatrixBuildTranslation( temp,  translation… );               MatrixMultiply( temp, mat, mat );
    /// </code>
    ///
    /// <c>CTextureTransformProxy::OnBind</c>, <c>matrixproxy.cpp:75-113</c>. Read-from-source.
    /// <c>MatrixMultiply( A, B, out )</c> is <c>out = A * B</c> and Source applies matrices to
    /// column vectors, so each step composes on the LEFT and runs AFTER the ones before it: move the
    /// centre to the origin, scale, rotate, move it back, then translate.
    ///
    /// **Every wrong order still produces a matrix**, and for the declared default they all produce
    /// the identity — which is why the conformance suite uses a centre away from the origin, a scale
    /// that is not one and a rotation that is not zero.
    ///
    /// **The defaults for an absent keyword are Valve's, and a centre of zero is the trap.** The
    /// proxy initialises <c>center( 0.5, 0.5 )</c> and <c>translation( 0, 0 )</c> before reading
    /// anything, so a string naming only a scale still scales about the middle of the texture. A
    /// centre defaulting to the origin would scale and rotate about a corner.
    /// </remarks>
    public static TextureTransform TextureTransformFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TextureTransform.Identity;
        }

        string[] words = text.Split(
            [' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Valve's own initialisation, before any keyword is read.
        (float X, float Y) centre = (0.5f, 0.5f);
        (float X, float Y) scale = (1f, 1f);
        (float X, float Y) translate = (0f, 0f);

        float rotate = 0f;
        bool understood = false;

        // **Scanned rather than iterated**, because a keyword consumes the words after it and a
        // `for` that advanced its own counter would be the analyzer's S127. The scan is the same
        // shape either way: read a keyword, take its arguments, continue past them.
        int at = 0;

        while (at < words.Length)
        {
            if (Keyword(words[at], "center") && Pair(words, at, out (float X, float Y) read))
            {
                centre = read;
                understood = true;
                at += 3;
            }
            else if (Keyword(words[at], "scale") && Pair(words, at, out read))
            {
                scale = read;
                understood = true;
                at += 3;
            }
            else if (Keyword(words[at], "translate") && Pair(words, at, out read))
            {
                translate = read;
                understood = true;
                at += 3;
            }
            else if (Keyword(words[at], "rotate") && Single(words, at, out float angle))
            {
                rotate = angle;
                understood = true;
                at += 2;
            }
            else
            {
                // A word that is not a keyword, or one whose arguments are missing: skipped rather
                // than abandoning the rest, so a malformed clause cannot silently drop a valid one
                // after it.
                at++;
            }
        }

        // **Nothing understood is the identity, which is Valve's fallback for a variable that is not
        // a matrix at all** — `transformation[0].Init( 1, 0, 0, 0 )`, `BaseVSShader.cpp:317-321`. A
        // material naming a malformed transform draws as though it named none.
        if (!understood)
        {
            return TextureTransform.Identity;
        }

        float radians = rotate * ToRadians;

        float cos = (float)Math.Cos(radians);
        float sin = (float)Math.Sin(radians);

        // The rotation and scale, composed: R · S, with the scale applied first.
        float m00 = cos * scale.X;
        float m01 = -sin * scale.Y;
        float m10 = sin * scale.X;
        float m11 = cos * scale.Y;

        // T(+c) · R · S · T(-c), then the translation on the outside. The centre's contribution is
        // `c - M·c`, which is what makes a scale about the middle move the coordinate at all.
        float tx = centre.X - ((m00 * centre.X) + (m01 * centre.Y)) + translate.X;
        float ty = centre.Y - ((m10 * centre.X) + (m11 * centre.Y)) + translate.Y;

        return new TextureTransform((m00, m01, 0f, tx), (m10, m11, 0f, ty));
    }

    /// <summary>Whether a word is this keyword, case-insensitively as KeyValues is.</summary>
    private static bool Keyword(string word, string keyword) =>
        word.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>The two numbers after a keyword, or false when they are not both there.</summary>
    private static bool Pair(string[] words, int at, out (float X, float Y) value)
    {
        value = default;

        if (at + 2 >= words.Length ||
            !Number(words[at + 1], out float x) ||
            !Number(words[at + 2], out float y))
        {
            return false;
        }

        value = (x, y);

        return true;
    }

    /// <summary>The one number after a keyword.</summary>
    private static bool Single(string[] words, int at, out float value)
    {
        value = 0f;

        return at + 1 < words.Length && Number(words[at + 1], out value);
    }

    /// <remarks>
    /// **Invariant culture**, for the reason recorded on `PhysicsModel`: these strings write `.5`
    /// with a full stop, and under a comma locale every number reads as zero — a transform that
    /// collapses the texture rather than an error.
    /// </remarks>
    private static bool Number(string word, out float value) =>
        float.TryParse(
            word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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

    /// <summary>Splits a proxy's variable reference into a name and a component (B339).</summary>
    /// <param name="reference">What the VMT wrote, such as <c>$envmaptint[1]</c>.</param>
    /// <returns>The name without brackets, and the component or -1 for none.</returns>
    /// <remarks>
    /// **The brackets are STRIPPED, because the lookup uses the bare name.** `CResultProxy::Init`
    /// copies the string, replaces the `[` with a terminator and looks the variable up by what is
    /// left (<c>functionproxy.cpp:117-133</c>) — so a table keyed on the bracketed form would match
    /// nothing and the proxy would be refused.
    ///
    /// **`strtol` is what reads the index, and it answers 0 for anything that is not a number.**
    /// `$foo[]` and `$foo[x]` are therefore component zero rather than errors. Reproduced rather
    /// than tightened: a material relying on that is relying on component zero, and refusing it
    /// here would change what draws.
    ///
    /// The same parse runs on the SOURCES — `CFloatInput::Init` (<c>:38-58</c>) — so it is used for
    /// both ends.
    /// </remarks>
    public static (string Name, int Component) Reference(string? reference)
    {
        if (reference is null)
        {
            return (string.Empty, -1);
        }

        int bracket = reference.IndexOf('[', StringComparison.Ordinal);

        if (bracket < 0)
        {
            return (reference, -1);
        }

        // `strtol` skips leading space, takes an optional sign and as many digits as it finds, and
        // answers 0 having consumed nothing when there are none.
        int at = bracket + 1;
        int component = 0;

        while (at < reference.Length && char.IsAsciiDigit(reference[at]))
        {
            component = (component * 10) + (reference[at] - '0');
            at++;
        }

        return (reference[..bracket], component);
    }

    /// <summary>How many components a variable holds in this layer.</summary>
    private const int Components = 3;

    /// <summary>Writes a proxy's result into one component, or across all of them (B339).</summary>
    /// <param name="into">The variable's current value.</param>
    /// <param name="component">Which component, or -1 for none.</param>
    /// <param name="value">What the proxy computed.</param>
    /// <returns>The variable's new value.</returns>
    /// <remarks>
    /// **Valve's two paths, and the first is the one this project was missing**
    /// (<c>functionproxy.cpp:141-160</c>): a named component is written ALONE and the rest of the
    /// vector keeps what it had; an unnamed one BROADCASTS the float across every component.
    ///
    /// Writing all three where the material named one turns a reflection tint or a
    /// self-illumination ramp into a grey of itself — measured on 150 shipped materials.
    /// </remarks>
    public static (float Red, float Green, float Blue) WriteComponent(
        (float Red, float Green, float Blue) into, int component, float value)
    {
        if (component < 0)
        {
            return (value, value, value);
        }

        return component switch
        {
            0 => (value, into.Green, into.Blue),
            1 => (into.Red, value, into.Blue),
            2 => (into.Red, into.Green, value),

            // The engine indexes a `float v[4]`, so a fourth component is legal there and is not
            // here. Left alone rather than wrapped: a silent write to the wrong component is worse
            // than no write at all.
            _ => into,
        };
    }

    /// <summary>Reads one component of a variable as a scalar (B339).</summary>
    /// <param name="from">The variable's value.</param>
    /// <param name="component">Which component, or -1 to take the whole vector.</param>
    /// <returns>The component in every place, or the value unchanged.</returns>
    /// <remarks>
    /// **A named component makes the operation FLOAT-typed** — `ComputeResultType` answers
    /// `MATERIAL_VAR_TYPE_FLOAT` when a component is named (<c>functionproxy.cpp:238</c>) — so the
    /// arithmetic runs once on a scalar rather than three times on a vector. Broadcasting the
    /// component is how that is expressed in a layer that holds every variable as a triple.
    /// </remarks>
    public static (float Red, float Green, float Blue) ReadComponent(
        (float Red, float Green, float Blue) from, int component)
    {
        if (component < 0)
        {
            return from;
        }

        float value = component switch
        {
            0 => from.Red,
            1 => from.Green,
            2 => from.Blue,
            _ => 0f,
        };

        return component < Components ? (value, value, value) : from;
    }

    /// <summary>The engine's default animation rate when a material states none.</summary>
    /// <remarks>
    /// <c>m_FrameRate = pKeyValues-&gt;GetFloat( "animatedTextureFrameRate", 15 )</c>
    /// (<c>baseanimatedtextureproxy.cpp:59</c>). TF2's own materials almost all state 30, so this
    /// is reached only by the handful that do not — and at one second in, 15 and 30 are different
    /// pictures.
    /// </remarks>
    public const float DefaultAnimationRate = 15f;

    /// <summary>Which frame an animated texture is showing, <c>CBaseAnimatedTextureProxy</c> (B338).</summary>
    /// <param name="seconds">Playback time since the animation's start.</param>
    /// <param name="rate"><c>animatedTextureFrameRate</c>.</param>
    /// <param name="frames">The texture's own <c>numFrames</c>.</param>
    /// <returns>The frame index, always inside the texture.</returns>
    /// <remarks>
    /// **The largest unimplemented proxy in the game — 7,027 shipped materials**, and it is TIME
    /// driven rather than entity driven: `CAnimatedTextureProxy::GetAnimationStartTime` returns 0
    /// (<c>animatedtextureproxy.cpp:25-28</c>), so every material sharing a texture shows the same
    /// frame at the same moment.
    ///
    /// <code>
    /// float deltaTime = gpGlobals->curtime - startTime;
    /// if (deltaTime &lt; 0.0f) deltaTime = 0.0f;
    /// float frame    = m_FrameRate * deltaTime;
    /// int   intFrame = ((int)frame) % numFrames;
    /// </code>
    ///
    /// **Three things a plausible implementation drops.** The truncation is `(int)`, not a round.
    /// The clamp on negative time is load-bearing HERE in a way it is not in the engine — a client
    /// clock never goes backwards and this project seeks, and C#'s modulo of a negative is negative,
    /// so without it the index reads off the FRONT of the file. And a texture declaring no frames
    /// is refused rather than divided by, which the engine does with an assert.
    ///
    /// **What is NOT reproduced: the wrap callback.** `AnimationWrapped` fires when the frame goes
    /// round, and under `animationNoWrap` the frame is pinned to the last one instead. Nothing here
    /// subscribes to that callback — it drives `MaterialModify` entities and particle systems — and
    /// `animationNoWrap` is stated by no shipped material this census has seen.
    /// </remarks>
    public static int AnimationFrame(double seconds, float rate, int frames)
    {
        if (frames <= 0)
        {
            return 0;
        }

        if (seconds < 0d)
        {
            seconds = 0d;
        }

        return (int)(rate * seconds) % frames;
    }

    /// <summary>One of Valve's arithmetic proxies, <c>mathproxy.cpp</c> (B337).</summary>
    /// <remarks>
    /// **Named rather than dispatched on a string at the call site**, so an unrecognised proxy is
    /// refused where it is read rather than silently computing the wrong operation.
    /// </remarks>
    public enum MathProxy
    {
        /// <summary><c>CEqualsProxy</c> — a copy, which is how a value reaches two variables.</summary>
        Equals,

        /// <summary><c>CAddProxy</c>.</summary>
        Add,

        /// <summary><c>CSubtractProxy</c> — src1 minus src2, in that order.</summary>
        Subtract,

        /// <summary><c>CMultiplyProxy</c>.</summary>
        Multiply,

        /// <summary><c>CDivideProxy</c> — and a zero divisor yields the NUMERATOR.</summary>
        Divide,
    }

    /// <summary>Runs one arithmetic proxy over two variables (B337).</summary>
    /// <param name="operation">Which proxy.</param>
    /// <param name="first"><c>srcVar1</c>.</param>
    /// <param name="second"><c>srcVar2</c>, ignored by <see cref="MathProxy.Equals"/>.</param>
    /// <returns>What the proxy writes to <c>resultVar</c>.</returns>
    /// <remarks>
    /// **Componentwise, because every variable in this layer is a triple.** The engine chooses a
    /// result type per bind — `CFunctionProxy::ComputeResultType` (`functionproxy.cpp:231`) takes
    /// the RESULT variable's own type, then src1's, then src2's — and computes on a vector, a float
    /// or an int accordingly. Componentwise arithmetic on floats agrees with the vector and float
    /// paths exactly; it differs from the INT path only for a variable holding a fraction the
    /// engine would have truncated first, and from a two-component variable by writing a third
    /// component. Both are stated in `MathProxyConformanceTests` rather than left to be discovered.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The operation is not one of the five.</exception>
    public static (float Red, float Green, float Blue) Apply(
        MathProxy operation,
        (float Red, float Green, float Blue) first,
        (float Red, float Green, float Blue) second) =>
        operation switch
        {
            MathProxy.Equals => first,
            MathProxy.Add => (
                first.Red + second.Red, first.Green + second.Green, first.Blue + second.Blue),
            MathProxy.Subtract => (
                first.Red - second.Red, first.Green - second.Green, first.Blue - second.Blue),
            MathProxy.Multiply => (
                first.Red * second.Red, first.Green * second.Green, first.Blue * second.Blue),
            MathProxy.Divide => (
                Divide(first.Red, second.Red),
                Divide(first.Green, second.Green),
                Divide(first.Blue, second.Blue)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    /// <summary>One component of <c>CDivideProxy</c>.</summary>
    /// <remarks>
    /// **A zero divisor yields the NUMERATOR** — Valve's own guard, `mathproxy.cpp:229-233`. Not
    /// zero and not infinity: letting it divide would put an infinity in a material variable and a
    /// NaN in a colour, which draws as black or as nothing depending on the blend and reads as a
    /// missing texture rather than as arithmetic.
    /// </remarks>
    private static float Divide(float numerator, float divisor) =>
        divisor != 0f ? numerator / divisor : numerator;

    /// <summary><c>CClampProxy</c>, bounds and all (B337).</summary>
    /// <param name="value"><c>srcVar1</c>.</param>
    /// <param name="minimum">The proxy's <c>min</c>, default 0.</param>
    /// <param name="maximum">Its <c>max</c>, default 1.</param>
    /// <returns>The value brought inside the range.</returns>
    /// <remarks>
    /// **The bounds are SWAPPED first when they arrive the wrong way round**
    /// (<c>mathproxy.cpp:283-288</c>), which is not tidiness: a material stating `min 1 max 0`
    /// otherwise clamps to a range that contains nothing, and the answer becomes whichever
    /// comparison runs first.
    /// </remarks>
    public static (float Red, float Green, float Blue) Clamp(
        (float Red, float Green, float Blue) value, float minimum = 0f, float maximum = 1f)
    {
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        return (
            Math.Clamp(value.Red, minimum, maximum),
            Math.Clamp(value.Green, minimum, maximum),
            Math.Clamp(value.Blue, minimum, maximum));
    }

    /// <summary>How long a flame lives, <c>TF_BURNING_FLAME_LIFE</c>.</summary>
    /// <remarks>
    /// <c>#define TF_BURNING_FLAME_LIFE 10.0</c> (<c>tf_shareddefs.h:665</c>). There is a
    /// <c>TF_BURNING_FLAME_LIFE_PYRO</c> of 0.25 beside it, and it is NOT this: it shortens how
    /// long a pyro BURNS, and the proxy reads the plain one whoever is alight.
    /// </remarks>
    private const float FlameLife = 10f;

    /// <summary>How long the burn takes to reach full strength.</summary>
    /// <remarks><c>float flBurnPeakTime = flBurnStartTime + 0.3;</c> (<c>c_tf_player.cpp:1871</c>).</remarks>
    private const float BurnPeak = 0.3f;

    /// <summary>How alight a player looks, <c>CProxyBurnLevel</c> (B336).</summary>
    /// <param name="since">Seconds since the burning condition was added; may be negative.</param>
    /// <returns>0 to 1, for <c>$detailblendfactor</c>.</returns>
    /// <remarks>
    /// **Fast in, slow out, and the asymmetry is the whole shape** — up over 0.3 seconds and down
    /// over the remaining 9.7 (<c>c_tf_player.cpp:1868-1885</c>):
    ///
    /// <code>
    /// if ( gpGlobals->curtime &lt; flBurnPeakTime )
    ///     flTempResult = RemapValClamped( curtime, flBurnStartTime, flBurnPeakTime, 0.0, 1.0 );
    /// else
    ///     flTempResult = RemapValClamped( curtime, flBurnPeakTime, flBurnStartTime + TF_BURNING_FLAME_LIFE, 1.0, 0.0 );
    /// flResult = 1.0 - abs( flTempResult - 1.0 );
    /// </code>
    ///
    /// **That last line is an identity and is not reproduced.** `RemapValClamped` already clamps to
    /// [0, 1], and on that interval `1 - |t - 1|` is `t`. Valve's comment above it says *"We have to
    /// do some more calc here instead of in materialvars"*, which reads as a leftover from when the
    /// remap was unclamped. Writing it out would suggest it does something.
    ///
    /// **The clamps are load-bearing here in a way they are not in the engine.** A negative
    /// `since` cannot arise in a client that only moves forward; this project seeks, so it can, and
    /// an unclamped ramp would answer a negative blend factor.
    /// </remarks>
    public static float BurnLevel(float since)
    {
        if (since <= 0f)
        {
            return 0f;
        }

        return since < BurnPeak
            ? since / BurnPeak
            : Math.Clamp((FlameLife - since) / (FlameLife - BurnPeak), 0f, 1f);
    }

    /// <summary>How yellow a jarate'd player is, <c>CProxyUrineLevel</c> (B336).</summary>
    /// <param name="urine">Whether the player is in <c>TF_COND_URINE</c>.</param>
    /// <param name="isBlue">Whether the team the viewer SEES is BLU.</param>
    /// <returns>A multiplier, white when the condition is absent.</returns>
    /// <remarks>
    /// **The numbers are multipliers, not colours** (<c>c_tf_player.cpp:1948-1960</c>): `(6,9,2)`
    /// for RED and `(7,5,1)` for BLU, well above one, which is how the effect BRIGHTENS into yellow
    /// rather than tinting toward it. Read as 0-255 and divided they would draw an almost-black
    /// player.
    ///
    /// **The team is the one the viewer sees, not the one the player is on**, and the engine is
    /// explicit: a disguised spy is tinted for the DISGUISE team unless the viewer is on the spy's
    /// own team or is the spy. That distinction is not made here yet — see B336 — because it needs
    /// the viewing player, and this layer is handed one entity.
    /// </remarks>
    public static (float Red, float Green, float Blue) YellowLevel(bool urine, bool isBlue)
    {
        if (!urine)
        {
            return (1f, 1f, 1f);
        }

        return isBlue ? (7f, 5f, 1f) : (6f, 9f, 2f);
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
