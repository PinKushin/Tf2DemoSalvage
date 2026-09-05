using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One proxy a material runs, and the arguments it was written with.</summary>
/// <param name="Name">The proxy's name, as the engine registers it — <c>Sine</c>, <c>TextureScroll</c>.</param>
/// <remarks>
/// **Arguments are matched case-insensitively because KeyValues is, and TF2's own files rely on
/// it.** <c>cappoint_logo_blue</c> writes <c>Sineperiod</c> and <c>SineMax</c> where the engine's
/// <c>CSineProxy::Init</c> reads <c>"sinePeriod"</c> and <c>"sineMax"</c>. A case-sensitive reader
/// finds neither, silently takes the defaults, and oscillates at the wrong rate — a failure that
/// looks like a wrong number rather than a missing lookup.
/// </remarks>
public sealed record MaterialProxy(string Name)
{
    private readonly Dictionary<string, string> _arguments =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads one argument, or null when the material did not state it.</summary>
    /// <param name="key">The argument name, matched case-insensitively.</param>
    /// <returns>The raw text, or null.</returns>
    /// <remarks>
    /// Null rather than empty so a caller can tell "not stated" from "stated as nothing" and apply
    /// the engine's own default rather than parsing an empty string as zero.
    /// </remarks>
    public string? Argument(string key) =>
        _arguments.TryGetValue(key, out string? value) ? value : null;

    /// <summary>Records an argument while parsing.</summary>
    internal void Add(string key, string value) => _arguments[key] = value;
}

/// <summary>
/// A Valve Material file: which shader a surface uses, and which textures it names.
/// </summary>
/// <remarks>
/// A VMT is KeyValues text — the same brace-and-quoted-pairs format as the BSP entity lump:
///
/// <code>
/// "LightMappedGeneric"
/// {
///     "$basetexture" "concrete/concretefloor007b"
///     "$bumpmap"     "concrete/concretefloor007b_height-ssbump"
///     "$detail"      "overlays/detail001"
/// }
/// </code>
///
/// **Only what the renderer needs is interpreted.** The shader name and `$basetexture` decide what
/// is drawn; `$translucent` and `$alphatest` decide whether it needs blending. Everything else is
/// kept as raw key/values so a later pass can use it without this having to know about it first.
///
/// **`Patch` is the one indirection that has to be followed.** A patch material is a stub that
/// includes another VMT and overrides a few keys, and it is common in TF2 — a reader that does not
/// resolve it sees a material with no `$basetexture` and draws nothing.
/// </remarks>
public sealed class VmtMaterial
{
    private readonly Dictionary<string, string> _values;

    private VmtMaterial(
        string shader, Dictionary<string, string> values, List<MaterialProxy>? proxies = null)
    {
        Shader = shader;
        _values = values;
        Proxies = proxies ?? [];
    }

    /// <summary>The proxies this material runs, in the order it declares them.</summary>
    /// <remarks>
    /// **Kept, but kept separate.** A proxy's arguments are not the material's keys — a
    /// <c>TextureScroll</c> names a <c>$basetexture</c> that is the texture it animates rather than
    /// the surface's — so folding them in would draw the wrong picture. That is the same rule the
    /// parser already applied by dropping the block; this keeps the contents without merging them.
    ///
    /// Order matters: two proxies writing the same variable resolve last-wins, which is only well
    /// defined if the order survives.
    /// </remarks>
    public IReadOnlyList<MaterialProxy> Proxies { get; }

    /// <summary>The shader the material uses, such as <c>LightMappedGeneric</c>.</summary>
    public string Shader { get; }

    /// <summary>Whether this is a patch that includes another material.</summary>
    public bool IsPatch => Shader.Equals("Patch", StringComparison.OrdinalIgnoreCase);

    /// <summary>The material a patch is based on, or null.</summary>
    public string? Include => Value("include");

    /// <summary>The texture drawn on the surface, without extension, or null.</summary>
    public string? BaseTexture => Value("$basetexture");

    /// <summary>The texture that carries this material's colour, whatever its shader calls it.</summary>
    /// <remarks>
    /// **Not every material has a <c>$basetexture</c>.** TF2 paints eyes with <c>EyeRefract</c>,
    /// which composes one from an iris, a cornea normal map, an occlusion map and a light warp:
    ///
    /// <code>
    /// "EyeRefract"
    /// {
    ///     "$Iris"          "models/player/shared/eye-iris-blue"
    ///     "$CorneaTexture" "models/player/shared/eye-cornea"
    /// }
    /// </code>
    ///
    /// Asking only for <c>$basetexture</c> finds nothing there and draws the missing-texture
    /// chequer, which is what put purple eyes on every player in the viewer.
    ///
    /// **This is not an implementation of those shaders and does not pretend to be.** It answers
    /// "if you can only draw one texture for this material, which one is the colour" — the iris for
    /// an eye. A renderer that later implements <c>EyeRefract</c> properly should stop using this
    /// for eyes rather than build on it.
    ///
    /// Ordered so <c>$basetexture</c> always wins when present: a material naming both should not
    /// have its wall repainted by whichever fallback happened to match.
    /// </remarks>
    public string? PrimaryTexture => BaseTexture ?? Fallback();

    /// <summary>Parameters that carry a material's colour when it names no base texture.</summary>
    /// <remarks>
    /// Deliberately short. Each entry is a shader whose output a viewer would otherwise lose
    /// entirely, and adding one is a claim that this parameter is the closest single texture to
    /// what the player sees.
    /// </remarks>
    private static readonly string[] ColourBearingParameters =
    [
        // EyeRefract: the iris is the eye's colour; the cornea is a normal map and the
        // ambient-occlusion texture is a mask.
        "$iris",
    ];

    private string? Fallback()
    {
        foreach (string parameter in ColourBearingParameters)
        {
            if (Value(parameter) is { Length: > 0 } named)
            {
                return named;
            }
        }

        return null;
    }

    /// <summary>Whether the surface is not simply opaque, by either route.</summary>
    /// <remarks>
    /// Kept for callers that only need to know a surface is not a solid. Anything that has to
    /// DRAW it wants <see cref="IsAlphaTested"/> or <see cref="IsTranslucent"/>, which are
    /// different operations and mutually exclusive.
    /// </remarks>
    public bool IsTransparent => IsAlphaTested || IsTranslucent;

    /// <summary>Whether the surface is cut out by a threshold rather than blended.</summary>
    /// <remarks>
    /// The cheap form, and what foliage and grates use: each pixel is drawn or discarded, nothing
    /// in between, so it needs no sorting and can be drawn in the opaque pass.
    /// </remarks>
    public bool IsAlphaTested => Flag("$alphatest");

    /// <summary>The alpha value at and above which an alpha-tested texel is kept.</summary>
    /// <remarks>
    /// **Zero means "use the hardware default", not "keep everything".** Valve applies the override
    /// only when the material's value is above zero — <c>BaseVSShader.cpp:927</c>:
    ///
    /// <code>
    /// if( alphaTestReferenceVar != -1 &amp;&amp; params[alphaTestReferenceVar]->GetFloatValue() > 0.0f )
    ///     s_pShaderShadow->AlphaFunc( SHADER_ALPHAFUNC_GEQUAL, params[...]->GetFloatValue() );
    /// </code>
    ///
    /// and the parameter is declared with an EMPTY default (<c>depthwrite.cpp:23</c>), so an absent
    /// key leaves the API's own reference alone. Treating a missing value as a cutoff of zero would
    /// keep every texel and turn a grate into a solid sheet.
    ///
    /// The comparison is <c>GEQUAL</c>, so a texel exactly at the reference is KEPT.
    /// </remarks>
    public float AlphaTestReference => Number("$alphatestreference", 0f);

    /// <summary>Whether the surface is blended with what is behind it.</summary>
    /// <remarks>
    /// **Alpha test wins when a material declares both**, which is Valve's own clause rather than
    /// a tie-break invented here:
    ///
    /// <code>
    /// isTranslucent = ... || ( TextureIsTranslucent( textureVar, isBaseTexture ) &amp;&amp;
    ///                          !(CurrentMaterialVarFlags() &amp; MATERIAL_VAR_ALPHATEST ) );
    /// </code>
    ///
    /// **And <c>$translucent</c> is not the only route in.** Constant modulation through
    /// <c>$alpha</c>, and per-vertex alpha, both reach the same conclusion — so a material can be
    /// translucent without ever naming the key. Source also consults the texture's own alpha
    /// channel, which this cannot do without the texture; a caller holding one should add that.
    /// </remarks>
    public bool IsTranslucent
    {
        get
        {
            if (IsAlphaTested)
            {
                return false;
            }

            if (Flag("$translucent") || Flag("$vertexalpha"))
            {
                return true;
            }

            // $alpha is a constant multiplier, so anything short of fully opaque blends. A missing
            // or unparseable value is not translucency - it is a material that said nothing.
            return Value("$alpha") is { } alpha &&
                float.TryParse(alpha, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                value < 1f;
        }
    }

    /// <summary>Whether the material is drawn by ADDING its colour to what is behind it.</summary>
    /// <remarks>
    /// **Black contributes nothing under additive blending, which is the whole point.** Source
    /// returns BT_ADD for <c>$additive</c>, so a light cone under a lamp brightens what it covers
    /// and its dark parts disappear. Drawn opaque instead, the same cone is a solid black shape -
    /// measured on cp_process_f12, where <c>props_lights/light_cone_farm_32</c> carries baked
    /// lighting of exactly 0.000 and every lamp in the map wears one.
    /// </remarks>
    public bool IsAdditive => Flag("$additive");

    /// <summary>Whether the material MULTIPLIES what is already drawn, rather than covering it.</summary>
    /// <remarks>
    /// **The shader name is the whole declaration here.** <c>Modulate</c> has no
    /// <c>$translucent</c>, no <c>$additive</c> and often no <c>$alpha</c> below one, so every
    /// predicate this project had said "opaque" — and a material whose entire purpose is to darken
    /// what is behind it was then painted as solid geometry.
    ///
    /// Measured on the capture points: each sign is a coincident pair, a lit logo drawn additively
    /// and a <c>cappoint_logo_*_dark</c> drawn with this shader. Read as opaque, the dark one wins
    /// and the point renders as a dark slab — worst on BLU, whose <c>$modblend</c> is .63 against
    /// RED's .43, which is why one team looked broken and the other did not.
    ///
    /// <c>$mod2x</c> doubles the result, so a texel of mid grey leaves the destination unchanged
    /// and the material can brighten as well as darken. Reported separately because the two want
    /// different blend factors.
    /// </remarks>
    public bool IsModulate => Shader.Equals("Modulate", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this material is drawn from both sides.</summary>
    /// <remarks>
    /// **A material flag in the engine, not a global setting.** $nocull sets MATERIAL_VAR_NOCULL
    /// (<c>imaterial.h:369</c>, bit 13) and shaders test it per material — <c>depthwrite.cpp:93</c>
    /// calls <c>EnableCulling</c> with it inverted. Everything else culls, with front faces wound
    /// clockwise per MATERIAL_CULLMODE_CCW in <c>imaterialsystem.h:180</c>.
    /// </remarks>
    public bool IsNoCull => Flag("$nocull");

    /// <summary>Whether direct light wraps around the surface instead of stopping at the terminator.</summary>
    /// <remarks>
    /// **Valve's half-Lambert, from <c>common_vs_fxc.h:826</c>:**
    ///
    /// <code>
    /// NDotL = NDotL * 0.5 + 0.5;
    /// NDotL = NDotL * NDotL;
    /// </code>
    ///
    /// It maps −1..1 onto 0..1 and squares it, so a surface facing directly away from a light still
    /// receives a quarter of it rather than none. That is why TF2's characters read as solid shapes
    /// in shade instead of going black on their unlit side — 190 of cp_process's 1,034 prop and
    /// model materials ask for it.
    ///
    /// **It applies to DIRECT light only.** The routine is inside <c>DoLightInternal</c>, so the
    /// ambient cube is unaffected; a model in shade is lit by the cube either way.
    /// </remarks>
    public bool IsHalfLambert => Flag("$halflambert");

    /// <summary>Whether the material draws TWO textures multiplied together.</summary>
    /// <remarks>
    /// **Valve's UnLitTwoTexture, whose pixel shader is one line**
    /// (<c>stdshaders/unlittwotexture_ps2x.fxc</c>):
    ///
    /// <code>
    /// HALF4 result = baseColor * baseColor2 * g_DiffuseModulation;
    /// float alpha = 1.0f;
    /// </code>
    ///
    /// Two textures, each with its own coordinates, multiplied — and alpha forced to one. A
    /// renderer that samples only the base draws half the material, and because multiplication is
    /// commutative the AUTHOR is free to put either one first. TF2's capture point beams do exactly
    /// that: red and neutral name the colour first, blue names the stripes, so dropping the second
    /// texture is invisible on two of them and turns the third into a grey column.
    /// </remarks>
    public bool IsTwoTexture =>
        Shader.Equals("UnLitTwoTexture", StringComparison.OrdinalIgnoreCase) &&
        SecondTexture is { Length: > 0 };

    /// <summary>The material's second texture, without extension, or null.</summary>
    public string? SecondTexture => Value("$texture2");

    /// <summary>Whether a modulating material doubles its result.</summary>
    public bool IsModulateTwice => IsModulate && Flag("$mod2x");

    /// <summary>The detail texture tiled over the base, without extension, or null.</summary>
    public string? Detail => Value("$detail");

    /// <summary>How many times the detail texture tiles per tile of the base texture.</summary>
    /// <remarks>
    /// **Four by default, not one.** That is Valve's own default from the SHADER_PARAM declaration
    /// in <c>lightmappedgeneric_dx9.cpp</c>, and the helper's comment says the transform is set
    /// unconditionally because "you'll always have a detailscale". Reading the default as one puts
    /// the pattern at a quarter of its frequency on every material that omits the key, which is
    /// invisible without a side-by-side.
    /// </remarks>
    public (float U, float V) DetailScale => ReadDetailScale();

    // Split out of the property because a getter may not throw (CA1065), and a malformed
    // $detailscale must be reported rather than silently becoming the default.
    private (float U, float V) ReadDetailScale()
    {
        if (Value("$detailscale") is not { } text)
        {
            return (4f, 4f);
        }

        // **Two dimensional, and a scalar broadcasts.** Valve branches on the var's type: a vector
        // supplies U and V independently, and anything else defined is read as one float and
        // copied to both. Two components, not three - a colour is three numbers and this is not a
        // colour, so reading it through the colour parser refuses "[1.1 2.3]" for having too few,
        // which is how the whole material loses its detail texture.
        if (!text.TrimStart().StartsWith('[') && !text.TrimStart().StartsWith('{'))
        {
            float scale = Number("$detailscale", 4f);

            return (scale, scale);
        }

        string[] parts = text.Trim().Trim('[', ']', '{', '}').Split(
            [' ', '\t', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2 ||
            !float.TryParse(
                parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float across) ||
            !float.TryParse(
                parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float down))
        {
            throw new InvalidDataException(
                $"A material's $detailscale is \"{text}\", which is not two numbers.");
        }

        return (across, down);
    }

    /// <summary>How strongly the detail texture is applied, from zero to one.</summary>
    /// <remarks>
    /// One by default. Zero is the identity for eleven of the twelve combine modes, so reading the
    /// default as zero would disable detail everywhere while still loading the texture and
    /// reporting success.
    /// </remarks>
    public float DetailBlendFactor => Number("$detailblendfactor", 1f);

    /// <summary>Which of the twelve combine modes the detail texture uses.</summary>
    /// <remarks>
    /// **This is not the last word.** If the detail texture's own VTF carries the SSBUMP flag the
    /// engine overrides this with mode 10 or 11 regardless of what the material says, so a caller
    /// has to check the texture before trusting the number.
    /// </remarks>
    public int DetailBlendMode => Integer("$detailblendmode", 0);

    /// <summary>The colour the detail texture is multiplied by before it is combined.</summary>
    /// <remarks>
    /// White by default, which is the multiplicative identity. Both spellings appear in Valve's own
    /// defaults for the same white: <c>[1 1 1]</c> is floats and <c>{255 255 255}</c> is bytes.
    /// </remarks>
    public (float Red, float Green, float Blue) DetailTint => Colour("$detailtint");

    /// <summary>The normal or self-shadowing bump map, without extension, or null.</summary>
    public string? BumpMap => Value("$bumpmap");

    /// <summary>Whether the bump map stores three light weights rather than a direction.</summary>
    /// <remarks>
    /// **Two textures that look alike and decode completely differently.** An ordinary normal map
    /// stores a direction, decoded as <c>xyz * 2 - 1</c> and used in squared dot products against
    /// the basis. A self-shadowing one already holds three weights and is sampled raw. Applying the
    /// signed decode to an ssbump sends a flat 128 to zero and the surface goes black exactly where
    /// it should be evenly lit.
    ///
    /// **Not the last word**, the same way <c>$detailblendmode</c> is not: the texture's own
    /// <c>TEXTUREFLAGS_SSBUMP</c> says so as well, and on cp_process_final the two agree on all 13
    /// of the materials that use one. The flag is data and this is a declaration, so a caller that
    /// has the texture should prefer the flag.
    /// </remarks>
    public bool IsSelfShadowingBump => Flag("$ssbump");

    /// <summary>Whether parts of the surface light themselves.</summary>
    /// <remarks>
    /// **Masked by the base texture's alpha**, so a self-illuminated material must keep its alpha
    /// channel through upload even though it is otherwise opaque:
    ///
    /// <code>
    /// float3 selfIllumComponent = g_SelfIllumTint * albedo.xyz;
    /// diffuseComponent = lerp( diffuseComponent, selfIllumComponent, baseColor.a );
    /// </code>
    ///
    /// Alpha one is fully unlit, alpha zero is normally lit — so flattening the channel to opaque
    /// makes the whole surface glow rather than just the lamp in the middle of it.
    /// </remarks>
    public bool IsSelfIlluminated => Flag("$selfillum");

    /// <summary>The colour the self-illuminated part is tinted by.</summary>
    public (float Red, float Green, float Blue) SelfIllumTint => Colour("$selfillumtint");

    /// <summary>The colour and alpha the whole material's output is scaled by.</summary>
    /// <remarks>
    /// **The per-material modulation, from <c>CBaseVSShader::ColorVarsToVector</c>**
    /// (<c>BaseVSShader.cpp:677-698</c>). Every shader that draws anything folds this in; ignoring
    /// it renders a coloured glow as a white one and a half-faded haze at full strength.
    ///
    /// <code>
    /// color.Init( 1.0, 1.0, 1.0, 1.0 );
    /// if ( colorVar != -1 ) { ...vector, else broadcast the float... }
    /// if ( alphaVar != -1 ) color[3] = clamp( s_ppParams[alphaVar]->GetFloatValue(), 0.0f, 1.0f );
    /// </code>
    ///
    /// Three details that are each wrong under the obvious reading:
    ///
    /// - **Alpha is clamped and colour is not.** Deliberate rather than an oversight — the linear
    ///   space variant at line 652 reads <c>color[i] &gt; 1.0f ? color[i] : GammaToLinear(color[i])</c>,
    ///   which only has a meaning for a channel allowed above one. Over-bright modulation is how a
    ///   material is made to glow.
    /// - **<c>$color2</c> multiplies rather than replaces.** <c>BaseShader.h:271</c> states the
    ///   operation on the declaration itself: <c>ApplyColor2Factor( float* ) // (*pColorOut) *= COLOR2</c>.
    /// - **A scalar <c>$color</c> broadcasts.** See <see cref="Colour"/>.
    ///
    /// <c>ComputeModulationColor</c> itself is in the closed shaderlib, so what is reproduced here
    /// is the published conversion it is built on rather than the whole engine path — the render
    /// state it feeds (per-instance colour, alpha from a fading entity) is not a material property
    /// and does not belong here.
    /// </remarks>
    public (float Red, float Green, float Blue, float Alpha) Modulation
    {
        get
        {
            (float red, float green, float blue) = Colour("$color");
            (float red2, float green2, float blue2) = Colour("$color2");

            return (
                red * red2,
                green * green2,
                blue * blue2,
                Math.Clamp(Number("$alpha", 1f), 0f, 1f));
        }
    }

    /// <summary>Whether the tint lands only where the base texture's alpha says (B331).</summary>
    /// <remarks>
    /// <c>$blendtintbybasealpha</c>, which is what makes TF2's paint colour a hat's band and not the
    /// whole hat. The shader branches on it:
    ///
    /// <code>
    /// if (bBlendTintByBaseAlpha)
    /// {
    ///     float3 tintedColor = albedo * g_DiffuseModulation.rgb;
    ///     tintedColor = lerp(tintedColor, g_DiffuseModulation.rgb, g_fTintReplacementControl);
    ///     albedo = lerp(albedo, tintedColor, baseColor.a);
    /// }
    /// else
    ///     albedo = albedo * g_DiffuseModulation.rgb;
    /// </code>
    ///
    /// <c>skin_ps20b.fxc:317-326</c>. The mask is the base texture's OWN alpha rather than the
    /// modulated one, and the two differ as soon as a material names <c>$alpha</c>.
    ///
    /// **Self-illumination wins where both are asked for**, which is a shader limit rather than an
    /// art decision: <c>bBlendTintByBaseAlpha = IsBoolSet( … ) &amp;&amp; !bHasSelfIllum; // Pixel
    /// shader can't do both BLENDTINTBYBASEALPHA and SELFILLUM, so let selfillum win</c>
    /// (<c>skin_dx9_helper.cpp:269</c>), and the shader declares
    /// <c>SKIP: ( $BLENDTINTBYBASEALPHA ) &amp;&amp; ( $SELFILLUM )</c> so the combination cannot
    /// even be compiled. Reproduced here rather than left to the renderer, because a material
    /// asking for both is a question about the MATERIAL.
    /// </remarks>
    public bool TintsByBaseAlpha => Flag("$blendtintbybasealpha") && !IsSelfIlluminated;

    /// <summary>How much the tint REPLACES the albedo rather than multiplying into it.</summary>
    /// <remarks>
    /// <c>$blendtintcoloroverbase</c>, Valve's <c>g_fTintReplacementControl</c>, lerping between the
    /// two in the branch quoted on <see cref="TintsByBaseAlpha"/>. **Zero by default and zero on
    /// TF2's cosmetics**, which keeps the texture's detail visible under the colour; one paints the
    /// masked region flat.
    /// </remarks>
    public float TintOverBase => Number("$blendtintcoloroverbase", 0f);

    /// <summary><c>$color</c> alone, the factor <c>$color2</c> is multiplied against (B330).</summary>
    /// <remarks>
    /// **Carried separately because a proxy REPLACES `$color2` and not the product.** TF2's paint
    /// chain ends `SelectFirstIfNonZero( $colortint_tmp, $colortint_base ) -> $color2`, so the
    /// modulation a painted item draws with is `$color * paint` — and this project's
    /// <see cref="Modulation"/> is the resting product of the two, which cannot be taken apart
    /// afterwards without dividing by a value that is legally zero.
    ///
    /// White where the material names none, which is Valve's initialisation:
    /// <c>color.Init( 1.0, 1.0, 1.0, 1.0 )</c>.
    /// </remarks>
    public (float Red, float Green, float Blue) ColourFactor => Colour("$color");

    /// <summary>The colour a tintable item wears when it is NOT painted (B330).</summary>
    /// <remarks>
    /// <c>$colortint_base</c>, the second input to the `SelectFirstIfNonZero` every tintable TF2
    /// item pairs with `ItemTintColor`. Shipped materials initialise `$color2` to the same value, so
    /// the resting modulation already carries it — this is here so the proxy chain can be run as the
    /// chain rather than as an assumption about that initialisation holding.
    ///
    /// Null when the material names none, which is every material that is not tintable.
    /// </remarks>
    /// <summary>The transform a material applies to its base texture's coordinates (B332).</summary>
    /// <remarks>
    /// <c>$basetexturetransform</c>, whose packed string form the parameter's own declared default
    /// states: <c>"center .5 .5 scale 1 1 rotate 0 translate 0 0"</c>, in 53 shaders. Null when the
    /// material names none, which is nearly all of them — and null rather than the identity string
    /// so a census can tell "not asked for" from "asked for and neutral".
    /// </remarks>
    public string? BaseTextureTransform => Value("$basetexturetransform");

    /// <summary>The same for the second texture, where a shader has one.</summary>
    /// <remarks>
    /// <c>$texture2transform</c>. Both are applied to the SAME incoming coordinate, which is what
    /// lets one texture hold still while the other moves.
    /// </remarks>
    public string? SecondTextureTransform => Value("$texture2transform");

    public (float Red, float Green, float Blue)? TintBase =>
        Value("$colortint_base") is null ? null : Colour("$colortint_base");

    /// <summary>Whether the modulation is anything other than the identity.</summary>
    /// <remarks>
    /// Asked so a renderer can skip the work for the overwhelming majority of materials that name
    /// no colour, and so the census can report the parameter as consumed only where it is.
    /// </remarks>
    public bool IsModulated => Modulation is not (1f, 1f, 1f, 1f);

    /// <summary>The cubemap or texture this material reflects, or null.</summary>
    /// <remarks>
    /// **After a map is compiled this is a concrete texture name, never the literal
    /// <c>env_cubemap</c>.** vbsp's <c>PatchEnvmapForMaterialAndDependents</c>
    /// (<c>vbsp/cubemap.cpp:531</c>) rewrites it, and only for a material whose original value IS
    /// <c>env_cubemap</c>:
    ///
    /// <code>
    /// bool bShouldPatchEnvCubemap = DoesMaterialHaveKeyValuePair( pMaterialName, "$envmap", "env_cubemap" );
    /// ...
    /// pPatchInfo[nPatchCount].m_pKey = "$envmap";
    /// pPatchInfo[nPatchCount].m_pRequiredOriginalValue = "env_cubemap";
    /// pPatchInfo[nPatchCount].m_pValue = pCubemapTexture;
    /// </code>
    ///
    /// So the map has already done the assignment: a face's material names the exact baked cubemap
    /// it reflects, and no nearest-by-position search is needed at load. A material still reading
    /// <c>env_cubemap</c> after resolution was NOT patched — it sits where the compiler found no
    /// cubemap to bind, which is a real state rather than a decode failure.
    ///
    /// A material naming a specific texture is deliberately left alone by vbsp ("Do *NOT* patch the
    /// material if there is an $envmap specified and it's not 'env_cubemap'"), so a stock skybox
    /// reflection survives compilation unchanged.
    /// </remarks>
    public string? EnvMap => Value("$envmap");

    /// <summary>Whether the material asks for the map's own baked reflection.</summary>
    /// <remarks>
    /// True only for an UNPATCHED material, which on a compiled map means one the compiler bound to
    /// no cubemap. Distinguished from <see cref="EnvMap"/> being null, which means the material
    /// reflects nothing at all.
    /// </remarks>
    public bool WantsMapCubemap =>
        string.Equals(EnvMap, "env_cubemap", StringComparison.OrdinalIgnoreCase);

    /// <summary>The colour the reflection is multiplied by.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( ENVMAPTINT, SHADER_PARAM_TYPE_COLOR, "[1 1 1]", "envmap tint" )</c>. The
    /// default falls out of <see cref="Colour"/> returning white for an absent key, so a material
    /// naming no tint reflects the cubemap unchanged.
    /// </remarks>
    public (float Red, float Green, float Blue) EnvMapTint => Colour("$envmaptint");

    /// <summary>How much the reflection is pushed toward its own square.</summary>
    /// <remarks>
    /// **Zero is normal**, which is the opposite way round from <see cref="EnvMapSaturation"/> and
    /// is why the two are documented together. Valve's help text says it outright:
    /// <c>"contrast 0 == normal 1 == color*color"</c> (<c>lightmappedgeneric_dx9.cpp:42</c>), and
    /// the shader is a lerp toward the end that is not the default:
    ///
    /// <code>
    /// HALF3 specularLightingSquared = specularLighting * specularLighting;
    /// specularLighting = lerp( specularLighting, specularLightingSquared, g_EnvmapContrast );
    /// </code>
    /// </remarks>
    public float EnvMapContrast => Number("$envmapcontrast", 0f);

    /// <summary>How much colour the reflection keeps.</summary>
    /// <remarks>
    /// **One is normal**, where zero is greyscale — <c>"saturation 0 == greyscale 1 == normal"</c>
    /// (<c>lightmappedgeneric_dx9.cpp:43</c>). So this defaults high and
    /// <see cref="EnvMapContrast"/> defaults low, and an implementation defaulting both to the same
    /// number is wrong in one direction or the other for every material on the map.
    ///
    /// <code>
    /// HALF3 greyScale = dot( specularLighting, HALF3( 0.299f, 0.587f, 0.114f ) );
    /// specularLighting = lerp( greyScale, specularLighting, g_EnvmapSaturation );
    /// </code>
    ///
    /// The weights are Rec.601 luma rather than a third each. They sum to one, so a grey reflection
    /// is unchanged either way — which is exactly why an average passes a casual check and greens
    /// what should stay red.
    /// </remarks>
    public float EnvMapSaturation => Number("$envmapsaturation", 1f);

    /// <summary>How much of the reflection survives at a head-on viewing angle.</summary>
    /// <remarks>
    /// **One is a mirror and means NO Fresnel falloff, which is the default.** Valve's own
    /// description is the clearest statement of it —
    /// <c>SHADER_PARAM( FRESNELREFLECTION, SHADER_PARAM_TYPE_FLOAT, "1.0", "1.0 == mirror,
    /// 0.0 == water" )</c> (<c>lightmappedgeneric_dx9.cpp:44</c>).
    ///
    /// The shader computes Schlick and then remaps it by this value
    /// (<c>lightmappedgeneric_ps2_3_x.h:530</c>):
    ///
    /// <code>
    /// HALF fresnel = 1.0 - dot( worldSpaceNormal, eyeVect );
    /// fresnel = pow( fresnel, 5.0 );
    /// fresnel = fresnel * g_OneMinusFresnelReflection + g_FresnelReflection;
    /// </code>
    ///
    /// with the pair packed as <c>[ 0, 0, 1-R(0), R(0) ]</c>
    /// (<c>lightmappedgeneric_dx9_helper.cpp:728</c>). So the whole term is
    /// <c>schlick * (1 - R) + R</c>, and at the default R = 1 it is the constant 1 — the Schlick
    /// factor is computed and discarded.
    ///
    /// **This is the parameter whose absence made every reflection here far too dark.** Applying
    /// Schlick unconditionally attenuates a surface viewed head-on to a few percent, which on a
    /// flat capture-point disc seen from standing height is indistinguishable from no reflection.
    ///
    /// **It does not apply to models at all.** <c>VertexLitGeneric</c>'s envmap block has no Fresnel
    /// term of any kind, so a model reflects at full strength whatever this says — see
    /// <c>EnvmapConformanceTests.Envmap_AModelsReflection_HasNoFresnelTermAtAll</c>.
    /// </remarks>
    public float FresnelReflection => Number("$fresnelreflection", 1f);

    /// <summary>Whether the material draws a specular highlight from the light.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( PHONG, SHADER_PARAM_TYPE_BOOL, "0", "enables phong lighting" )</c>. Setting
    /// it on a <c>VertexLitGeneric</c> material routes it to the <b>Skin</b> shader, which is where
    /// every phong parameter is actually read — which is why <c>$envmapfresnel</c> is declared on
    /// <c>VertexLitGeneric</c> and consumed by <c>skin_dx9_helper</c>.
    /// </remarks>
    public bool HasPhong => Flag("$phong");

    /// <summary>Whether this material marks a surface rather than being one.</summary>
    /// <remarks>
    /// <c>$decal</c> sets <c>MATERIAL_VAR_DECAL</c> (<c>imaterial.h:372</c>), and cp_process's wall
    /// stripes carry it — <c>concrete/stripe_blue</c>, <c>overlays/stripe_red</c> and
    /// <c>signs/team_blue</c> all declare <c>"$decal" "1"</c> on a <c>LightmappedGeneric</c> shader.
    ///
    /// **What the flag causes is not settled from published source.** Both places the SDK reads it
    /// — <c>lightmappedgeneric_dx9_helper.cpp:155</c> and <c>BaseVSShader.cpp:2134</c> — only set
    /// <c>MATERIAL_VAR_NO_DEBUG_OVERRIDE</c> with it, and whatever else the engine does with it is
    /// in the closed surface renderer. So this reports the flag; the renderer decides what a
    /// marking needs, and records its reasoning where it does (B135).
    /// </remarks>
    public bool IsDecal => Flag("$decal");

    /// <summary>Whether the material carries a rim light along its silhouette.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( RIMLIGHT, SHADER_PARAM_TYPE_BOOL, "0", "enables rim lighting" )</c>.
    ///
    /// **It only means anything alongside <see cref="HasPhong"/>**, and that is a property of the
    /// engine's dispatch rather than a simplification: rim lighting is implemented in the Skin
    /// shader, which <c>VertexLitGeneric</c> reaches only when <c>$phong</c> is set. A material
    /// asking for a rim and no phong gets neither. Measured on cp_process: 330 materials declare
    /// <c>$phong</c> and 301 declare <c>$rimlight</c>.
    /// </remarks>
    public bool HasRimLight => Flag("$rimlight");

    /// <summary>The authored lighting ramp this material reads instead of a linear falloff.</summary>
    /// <remarks>
    /// <c>$lightwarptexture</c>, a one-dimensional texture the engine looks up with the diffuse
    /// term. **308 of cp_process's materials name one**, and it is a large part of why TF2 reads as
    /// illustrated rather than photographed: the artist draws the falloff curve rather than
    /// accepting Lambert's.
    ///
    /// Two things about its use are easy to miss, both in <c>DiffuseTerm</c>
    /// (<c>common_vertexlitgeneric_dx9.h:86</c>): the half-Lambert SQUARE is skipped when a warp is
    /// present, because the ramp carries that curve already; and the lookup is DOUBLED, so a
    /// mid-grey ramp is neutral rather than a white one.
    /// </remarks>
    public string? LightWarpTexture => Value("$lightwarptexture");

    /// <summary>A texture replacing the base map's alpha as the self-illumination mask.</summary>
    /// <remarks>
    /// <c>$selfillummask</c>, and Valve's own description of it is the whole definition:
    /// <c>"If we bind a texture here, it overrides base alpha (if any) for self illum"</c>
    /// (<c>vertexlitgeneric_dx9.cpp:62</c>). The shader states it as one line —
    ///
    /// <code>
    /// float3 vSelfIllumMask = tex2D( SelfIllumMaskSampler, i.baseTexCoord.xy );
    /// vSelfIllumMask = lerp( baseColor.aaa, vSelfIllumMask, g_SelfIllumMaskControl );
    /// diffuseComponent = lerp( diffuseComponent, g_SelfIllumTint * albedo, vSelfIllumMask );
    /// </code>
    ///
    /// (<c>vertexlit_and_unlit_generic_ps2x.fxc:441-443</c>) — so the mask is not a new mechanism,
    /// it is the base alpha's replacement in one that already exists. Sampled on the BASE texture's
    /// coordinates, not its own set.
    ///
    /// **It has no effect without <c>$selfillum</c>**, which the helper gates on explicitly
    /// (<c>vertexlitgeneric_dx9_helper.cpp:289</c>). 53 of the 30,684 materials TF2 ships declare
    /// one, and every one of those declares it inside a <c>&gt;=DX90</c> block — which is why it
    /// became visible only once those blocks were read (B326, B327).
    /// </remarks>
    public string? SelfIllumMask => Value("$selfillummask");

    /// <summary>How tightly the rim hugs the silhouette; higher is a thinner edge.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( RIMLIGHTEXPONENT, SHADER_PARAM_TYPE_FLOAT, "4.0", … )</c> — a different
    /// default from <see cref="PhongExponent"/>'s 5, and applied to the same <c>L·R</c>.
    ///
    /// **Clamped to at least one, which we were not doing** (B334). The helper is explicit about
    /// it and says why in as many words:
    ///
    /// <code>
    /// vSpecularTint[3] = params[info.m_nRimLightPower]-&gt;GetFloatValue();
    /// vSpecularTint[3] = max(vSpecularTint[3], 1.0f);	// Make sure this is at least 1
    /// </code>
    ///
    /// (<c>skin_dx9_helper.cpp:843-844</c>.) Below one the exponent opens the rim out into a wash
    /// across the whole lit side rather than a line along the silhouette, and at zero
    /// <c>pow(x, 0)</c> is 1 — a uniform flood of rim colour over the entire model.
    /// </remarks>
    public float RimLightExponent => Math.Max(Number("$rimlightexponent", 4f), 1f);

    /// <summary>How much of the surroundings the rim picks up.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( RIMLIGHTBOOST, SHADER_PARAM_TYPE_FLOAT, "1.0", … )</c>. It scales the half
    /// of the rim that comes from the ambient cube rather than from the light —
    /// <c>(vRimAmbientCubeColor * g_fRimBoost) * saturate(fRimMultiply * worldSpaceNormal.z)</c> —
    /// so it is what makes a model pick up its surroundings on the edge with no direct light on it.
    /// </remarks>
    public float RimLightBoost => Number("$rimlightboost", 1f);

    /// <summary>How tight the highlight is; higher is smaller and sharper.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( PHONGEXPONENT, SHADER_PARAM_TYPE_FLOAT, "5.0", … )</c>, and **that declared
    /// default is not the one the shader uses** (B334). This answered 5 for a material stating no
    /// exponent, which is what the parameter declares and what nothing reads.
    ///
    /// The helper writes <c>-1</c> and replaces it only when the VMT states a value ABOVE zero
    /// (<c>skin_dx9_helper.cpp:815-828</c>), and with no <c>$phongexponenttexture</c> it binds
    /// <c>TEXTURE_WHITE</c> to that sampler (<c>:560-567</c>). The shader then takes its other
    /// branch:
    ///
    /// <code>
    /// fSpecExp = (g_EyePos_SpecExponent.w &gt;= 0.0) ? g_EyePos_SpecExponent.w
    ///                                              : (1.0f + 149.0f * vSpecExpMap.r);
    /// </code>
    ///
    /// With white in the sampler that is <c>1 + 149 × 1</c>, so the effective default is **150** —
    /// a tight point where 5 is a broad wash, a factor of thirty apart. Seven of cp_process's 330
    /// phong materials state no exponent and were all being drawn with the wrong one.
    ///
    /// **A declared default is what the material system would answer, not what the shader uses**,
    /// and the neighbouring <c>$phongtint</c> proves the general point: its declared default is the
    /// nonsensical <c>"5.0"</c> for a VEC3, so if declared defaults reached the shader every phong
    /// material would carry a fivefold white tint.
    ///
    /// See <see cref="PhongExponentFromTexture"/> for the case where a texture supplies it instead.
    /// </remarks>
    public float PhongExponent =>
        StatesAPositivePhongExponent ? Number("$phongexponent", 0f) : ExponentFromWhite;

    /// <summary>What the shader computes when the exponent sampler holds white.</summary>
    /// <remarks>
    /// <c>1.0f + 149.0f * 1.0f</c>. Named rather than written as 150 so the arithmetic that
    /// produces it stays visible beside the branch that produces it.
    /// </remarks>
    private const float ExponentFromWhite = 1f + (149f * 1f);

    /// <summary>Whether the VMT states an exponent the helper would honour.</summary>
    /// <remarks>
    /// Both halves matter. The helper tests <c>IsDefined()</c>, so an absent parameter is not the
    /// declared default here; and it tests <c>fValue &gt; 0.f</c>, so a stated zero is NOT an
    /// exponent of zero but a request for the map.
    /// </remarks>
    private bool StatesAPositivePhongExponent =>
        Value("$phongexponent") is not null && Number("$phongexponent", 0f) > 0f;

    /// <summary>The per-texel exponent map, or null for a material without one.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( PHONGEXPONENTTEXTURE, SHADER_PARAM_TYPE_TEXTURE, … )</c>. Three channels,
    /// three unrelated jobs: red is the exponent, green selects how much of the albedo tints the
    /// highlight, and alpha masks the rim. Sampled on the BASE texture's coordinates
    /// (<c>skin_ps20b.fxc:253</c>), not a set of its own.
    /// </remarks>
    public string? PhongExponentTexture => Value("$phongexponenttexture");

    /// <summary>The multiplier <c>$phongexponentfactor</c> applies to the map, or null.</summary>
    /// <remarks>
    /// <c>fSpecExp = ( 1.0f + g_EyePos_SpecExponent.w * vSpecExpMap.r )</c> under the
    /// <c>PHONG_USE_EXPONENT_FACTOR</c> combo (<c>skin_ps20b.fxc:266</c>) — the material's own
    /// multiplier in place of the fixed 149.
    ///
    /// **It is gated on being non-zero, not on being stated**:
    /// <c>const bool bHasPhongExponentFactor = flPhongExponentFactor != 0.0f;</c>
    /// (<c>skin_dx9_helper.cpp:274</c>), which matters because its declared default IS <c>"0.0"</c>.
    ///
    /// **And it overrides `$phongexponent` outright** rather than losing to it: the helper's
    /// `if ( bHasPhongExponentFactor )` takes the whole branch, so a material stating both a factor
    /// and an exponent uses the factor and the map (<c>:815-817</c>). 60 of the 30,684 materials
    /// TF2 ships state one; the values run to 255 and 75.
    /// </remarks>
    public float? PhongExponentFactor =>
        Number("$phongexponentfactor", 0f) is var factor && factor != 0f ? factor : null;

    /// <summary>Whether the exponent comes from the map's red channel rather than a constant.</summary>
    /// <remarks>
    /// **The constant wins when it is positive**, which is the way round a naive implementation
    /// gets backwards — *"Nonzero value in material overrides map channel"*
    /// (<c>skin_dx9_helper.cpp:825</c>).
    ///
    /// **A stated <see cref="PhongExponentFactor"/> takes the map whatever the exponent says**,
    /// because the helper branches on it first.
    /// </remarks>
    public bool PhongExponentFromTexture =>
        PhongExponentTexture is not null &&
        (PhongExponentFactor is not null || !StatesAPositivePhongExponent);

    /// <summary>Whether the highlight is tinted by the albedo through the map's green channel.</summary>
    /// <remarks>
    /// <c>bHasPhongTintMap = bHasSpecularExponentTexture &amp;&amp; $phongalbedotint != 0</c>
    /// (<c>skin_dx9_helper.cpp:252</c>), and then only when the stated tint is all zeros:
    ///
    /// <code>
    /// if ( (vSpecularTint[0] == 0.0f) &amp;&amp; (vSpecularTint[1] == 0.0f) &amp;&amp; (vSpecularTint[2] == 0.0f) )
    /// {
    ///     if ( bHasPhongTintMap ) vSpecularTint[0] = -1;   // tell the shader to read the map
    ///     else                    vSpecularTint[0..2] = 1; // otherwise just tint with white
    /// }
    /// </code>
    ///
    /// **An all-zero <c>$phongtint</c> is a REQUEST, not a colour.** Read literally it would
    /// multiply every highlight by black.
    /// </remarks>
    public bool PhongTintFromAlbedo =>
        PhongExponentTexture is not null &&
        Flag("$phongalbedotint") &&
        PhongTint is (0f, 0f, 0f);

    /// <summary>Whether the rim term is masked by the exponent map's alpha.</summary>
    /// <remarks>
    /// <c>bHasRimMaskMap = bHasSpecularExponentTexture &amp;&amp; bHasRimLight &amp;&amp;
    /// $rimmask != 0</c> (<c>skin_dx9_helper.cpp:263</c>). Any one missing leaves
    /// <c>g_RimMaskControl</c> at zero, and <c>lerp(1, a, 0)</c> is 1 — which is why
    /// <c>$rimmask</c> has counted as inert rather than unimplemented for as long as there was no
    /// exponent texture to read.
    /// </remarks>
    public bool MasksRimByExponentAlpha =>
        PhongExponentTexture is not null && HasRimLight && Flag("$rimmask");

    /// <summary>How much of the rim the exponent map's alpha masks — <c>g_RimMaskControl</c>.</summary>
    /// <remarks>
    /// **A float, not a flag**, and the difference is Valve's: the gate is an INTEGER test and the
    /// value written is a FLOAT.
    ///
    /// <code>
    /// bHasRimMaskMap = … &amp;&amp; ( params[info.m_nRimMask]-&gt;GetIntValue() != 0 );
    /// …
    /// vRimMaskControl[0] = bHasRimMaskMap ? params[info.m_nRimMask]-&gt;GetFloatValue() : 0.0f;
    /// </code>
    ///
    /// (<c>skin_dx9_helper.cpp:263, 856</c>), and the shader lerps with it —
    /// <c>lerp( 1.0f, vSpecExpMap.a, g_RimMaskControl )</c>. So <c>$rimmask 2</c> is a legal
    /// over-mask past the alpha, while <c>$rimmask 0.5</c> fails the integer gate outright and
    /// masks nothing. Reading it as a boolean would collapse the first case and get the second
    /// right by accident.
    ///
    /// 1,942 of the shipped materials state one; 1,643 of those say 1.
    /// </remarks>
    public float RimMaskControl =>
        MasksRimByExponentAlpha ? Number("$rimmask", 0f) : 0f;

    /// <summary>How far the highlight is pushed past the light's own brightness.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( PHONGBOOST, SHADER_PARAM_TYPE_FLOAT, "1.0", "Phong overbrightening factor
    /// (specular mask channel should be authored to account for this)" )</c>. The parenthesis is
    /// the important half: boost and mask are ONE calibration, so applying a mask without its boost
    /// is not "half of the effect", it is a different material.
    /// </remarks>
    public float PhongBoost => Number("$phongboost", 1f);

    /// <summary>Whether the phong mask is the base texture's alpha rather than the bump map's.</summary>
    /// <remarks>
    /// <c>"indicates that there is no normal map and that the phong mask is in base alpha"</c>
    /// (<c>vertexlitgeneric_dx9.cpp:59</c>) — so the flag means more than which channel to read.
    /// The shader selects both at once with a lerp (<c>skin_ps20b.fxc:199</c>):
    ///
    /// <code>
    /// tangentSpaceNormal = lerp( 2.0f * normalTexel.xyz - 1.0f, float3(0, 0, 1), g_fBaseMapAlphaPhongMask );
    /// fSpecMask = lerp( normalTexel.a, baseColor.a, g_fBaseMapAlphaPhongMask );
    /// </code>
    ///
    /// **Reading the wrong channel gives a plausible sheen in the wrong places**, which is worse
    /// than no highlight: a base texture's alpha usually holds opacity or a self-illum mask, so the
    /// model shines in whatever pattern that channel happens to carry.
    /// </remarks>
    public bool UsesBaseMapAlphaAsPhongMask => Flag("$basemapalphaphongmask");

    /// <summary>The colour the highlight is multiplied by, or null for the material's default.</summary>
    /// <remarks>
    /// <c>$phongtint</c>. Null rather than white, because "not stated" and "stated as white" reach
    /// the shader differently in the engine: with no tint and no exponent texture the term stays
    /// white, and with <c>$phongalbedotint</c> AND an exponent texture it would come from that
    /// texture's green channel instead (<c>skin_ps20b.fxc:275</c>). This project does not read an
    /// exponent texture, so the albedo-tint path cannot arise — see
    /// <c>PhongConformanceTests.Phong_TheAlbedoTint_NeedsAnExponentTexture</c>.
    /// </remarks>
    public (float Red, float Green, float Blue)? PhongTint =>
        Value("$phongtint") is null ? null : Colour("$phongtint");

    /// <summary>
    /// <c>$phongfresnelranges</c>, already encoded the way the shader wants it.
    /// </summary>
    /// <remarks>
    /// **The three numbers in the VMT are not the three the shader reads**, and Valve says so beside
    /// the code (<c>common_vertexlitgeneric_dx9.h:229</c>):
    ///
    /// <code>
    /// // note: vRanges is now encoded as ((mid-min)*2, mid, (max-mid)*2) to optimize math
    /// float f = saturate( 1 - dot( vNormal, vEyeDir ) );
    /// f = f*f - 0.5;
    /// return vRanges.y + (f >= 0.0 ? vRanges.z : vRanges.x) * f;
    /// </code>
    ///
    /// Encoded here rather than in the renderer because it is a property of the parameter, and
    /// because passing the raw triple is silently wrong rather than obviously wrong: at the default
    /// <c>[0 0.5 1]</c> a head-on surface returns 0.5 instead of 0, so the highlight never fades out
    /// and every model wears a uniform sheen.
    /// </remarks>
    public (float Low, float Mid, float High) PhongFresnelRanges
    {
        get
        {
            (float Red, float Green, float Blue) ranges = Value("$phongfresnelranges") is null
                ? (0f, 0.5f, 1f)
                : Colour("$phongfresnelranges");

            return ((ranges.Green - ranges.Red) * 2f, ranges.Green, (ranges.Blue - ranges.Green) * 2f);
        }
    }

    /// <summary>Whether the base texture's alpha masks the reflection instead of blending.</summary>
    /// <remarks>
    /// **Inverted, and Valve annotated it:** <c>specularFactor *= 1.0 - blendedAlpha; // Reversing
    /// alpha blows!</c> An opaque texel reflects LEAST.
    ///
    /// It also costs the material its transparency, which is the half that is easy to miss — three
    /// lines below, <c>alpha *= baseColor.a</c> is guarded by <c>!bBaseAlphaEnvmapMask</c>, because
    /// the alpha channel has been spent on the mask and cannot also mean opacity.
    /// </remarks>
    public bool UsesBaseAlphaAsEnvMapMask =>
        Flag("$basealphaenvmapmask") && !UsesNormalMapAlphaAsEnvMapMask;

    /// <summary>Whether the bump map's alpha masks the reflection.</summary>
    /// <remarks>
    /// **The mask TF2's models use, and its sense is the OPPOSITE of
    /// <see cref="UsesBaseAlphaAsEnvMapMask"/>.** Not inverted:
    ///
    /// <code>
    /// if ( bNormalMapAlphaEnvmapMask )
    ///     specularFactor = normalTexel.a;
    /// </code>
    ///
    /// (<c>vertexlit_and_unlit_generic_bump_ps2x.fxc:169</c>, and
    /// <c>specularFactor *= vNormal.a</c> in <c>lightmappedgeneric_ps2_3_x.h</c>).
    /// An alpha of 1 reflects MOST, where an opaque texel
    /// under the base-alpha mask reflects least. Implementing this with the other's sense puts the
    /// shine exactly where the artist masked it out.
    ///
    /// **A bumped material cannot use the base-alpha mask at all**, which is why models use this
    /// one. <c>lightmappedgeneric_dx9_helper.cpp:197</c> warns and drops the envmap outright when a
    /// material has a normal map and <c>$basealphaenvmapmask</c>, and clears that flag when this one
    /// is set — which is what the guard on the property above reproduces. The three masks are
    /// mutually exclusive by construction: the shader declares
    /// <c>SKIP: $NORMALMAPALPHAENVMAPMASK &amp;&amp; $BASEALPHAENVMAPMASK</c>.
    /// </remarks>
    public bool UsesNormalMapAlphaAsEnvMapMask => Flag("$normalmapalphaenvmapmask");

    /// <summary>Whether this is a tool material the player never sees.</summary>
    /// <remarks>
    /// A second line of defence behind the surface flags. A map can paint a nodraw-ish material
    /// without the flag, and drawing one puts a solid slab across the map.
    /// </remarks>
    public bool IsTool => Shader.StartsWith("UnlitGeneric", StringComparison.OrdinalIgnoreCase) &&
        Value("%compilenodraw") is "1";

    /// <summary>Reads any key.</summary>
    /// <param name="key">Key name, matched case-insensitively, including the leading <c>$</c>.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return _values.TryGetValue(key, out string? value) ? value : null;
    }

    /// <summary>Every parameter this material declares.</summary>
    /// <remarks>
    /// **For reporting what a material asked for**, which is a different question from what it
    /// got. A viewer that logs only failures reads clean while every material quietly falls back
    /// on an effect nobody implemented — the case that hid <c>$envmap</c> on a quarter of a map
    /// (B55) behind an hour of searching.
    ///
    /// The shader name is deliberately not in here: it is <see cref="Shader"/>, and folding it in
    /// would make a census of parameters count something that is not one.
    /// </remarks>
    public IReadOnlyCollection<string> Keys => _values.Keys;

    /// <summary>Strips a platform condition from a key, leaving the parameter it qualifies.</summary>
    /// <remarks>
    /// **A VMT key may be prefixed with the platform it applies to** — <c>360?$color2</c> sets
    /// <c>$color2</c> on Xbox 360 and nothing anywhere else. Reading the whole string as a name
    /// invents a parameter that no shader has ever declared, and loses the real one: five materials
    /// on cp_process_final carry <c>360?$color2</c>, and their <c>$color2</c> was simply not there.
    ///
    /// Found by <c>AssetCoverageConformanceTests</c> on its first run, which reported
    /// <c>360?$color2</c> as an unimplemented parameter. It is not unimplemented; it was misparsed.
    /// A census is only as good as the names going into it, and this is the second time a
    /// substring problem has produced a plausible wrong number here — the first was counting
    /// <c>$envmaptint</c> as <c>$envmap</c> (B55).
    ///
    /// **The PC value is the one to keep**, so the prefix is dropped rather than the key. This
    /// project draws the PC build; a 360-only override would be wrong to apply, but the parameter
    /// name it qualifies is the right thing to count and usually the material declares the plain
    /// form as well, which then wins by ordinary overwrite.
    /// </remarks>
    private static string PlatformIndependent(string key)
    {
        int condition = key.IndexOf('?', StringComparison.Ordinal);

        return condition >= 0 && condition + 1 < key.Length ? key[(condition + 1)..] : key;
    }

    /// <summary>Parses a VMT.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The material.</returns>
    /// <remarks>
    /// **A hand-written scanner, not a regular expression.** A material file is untrusted content
    /// once maps carry their own, and nothing here needs backtracking.
    ///
    /// Comments (<c>//</c>) are skipped, unquoted tokens are accepted — real VMTs contain both —
    /// and nested blocks such as <c>Proxies</c> are read but their keys are not merged, since a
    /// proxy's <c>$basetexture</c> is not the surface's.
    /// </remarks>
    public static VmtMaterial Parse(ReadOnlyMemory<byte> content)
    {
        // UTF-8 rather than ASCII: community materials carry non-English paths and comments.
        string text = Encoding.UTF8.GetString(content.Span);

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string shader = string.Empty;
        string? pendingKey = null;
        int at = 0;

        // **The name of every block currently open, not just how many.** Depth alone cannot tell a
        // patch's `replace` block from a `Proxies` block, and the two need opposite treatment: a
        // patch's overrides ARE the material's keys, and a proxy's are emphatically not.
        List<string> blocks = [];

        List<MaterialProxy> proxies = [];

        while (at < text.Length)
        {
            char character = text[at];

            if (char.IsWhiteSpace(character))
            {
                at++;
            }
            else if (character == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                while (at < text.Length && text[at] is not ('\n' or '\r'))
                {
                    at++;
                }
            }
            else if (character == '{')
            {
                blocks.Add(pendingKey ?? string.Empty);

                // A block opening directly inside Proxies IS a proxy, and its name is the key that
                // introduced it. Created here rather than when its first argument appears, so a
                // proxy written with no arguments still exists — the engine would run it on its
                // defaults.
                if (IsAProxyName(blocks))
                {
                    proxies.Add(new MaterialProxy(blocks[^1]));
                }

                at++;
                pendingKey = null;
            }
            else if (character == '}')
            {
                if (blocks.Count > 0)
                {
                    blocks.RemoveAt(blocks.Count - 1);
                }

                at++;
                pendingKey = null;
            }
            else
            {
                string token = ReadToken(text, ref at);

                if (token.Length == 0)
                {
                    break;
                }

                if (blocks.Count == 0)
                {
                    // Outside any block: this is the shader name.
                    if (shader.Length == 0)
                    {
                        shader = token;
                    }
                }
                else if (pendingKey is null)
                {
                    pendingKey = token;
                }
                else
                {
                    if (DescribesTheSurface(blocks) || TakesThisFallback(blocks, shader))
                    {
                        values[PlatformIndependent(pendingKey)] = token;
                    }
                    else if (IsAProxyArgument(blocks))
                    {
                        // The block one level up is the proxy's name, and it was created when its
                        // brace opened — so there is always one to add to here.
                        proxies[^1].Add(pendingKey, token);
                    }

                    pendingKey = null;
                }
            }
        }

        return new VmtMaterial(shader, values, proxies);
    }

    /// <summary>Whether the key currently being read is one of the material's own.</summary>
    /// <param name="blocks">The names of every block open right now, outermost first.</param>
    /// <remarks>
    /// **Two rules, and each one exists because of a bug the other would cause.**
    ///
    /// The material's top-level block is the obvious case. Anything deeper is somebody else's by
    /// default — a <c>Proxies</c> block carries its own <c>$basetexture</c> naming the texture a
    /// proxy animates, and taking that as the surface's draws the wrong picture.
    ///
    /// **The exception is a patch's <c>replace</c> and <c>insert</c> blocks, whose keys ARE the
    /// material's**, and missing it made every patch on every map a silent no-op. <c>Parse</c>
    /// returned a patch carrying <c>include</c> and nothing else; <c>ApplyPatch</c> drops
    /// <c>include</c> and overlays the rest, so it overlaid nothing and the merged material was the
    /// stock one exactly. On cp_process_final that is 51 materials, every cubemap reflection among
    /// them.
    ///
    /// <code>
    /// "patch"
    /// {
    ///     "include"  "materials/ICARUS/GLASSCHROME001.vmt"
    ///     "replace"
    ///     {
    ///         "$envmap"  "maps/cp_process_final/c1568_1728_976"
    ///     }
    /// }
    /// </code>
    ///
    /// **Keyed on depth AND name rather than on the name alone**, because a <c>replace</c> block
    /// nested inside <c>Proxies</c> is a proxy's, not a patch's. Matching the name anywhere would
    /// swap one bug for a rarer one.
    /// </remarks>
    /// <summary>Whether the block just opened is a proxy: one level inside <c>Proxies</c>.</summary>
    private static bool IsAProxyName(List<string> blocks) =>
        blocks.Count == 3 && blocks[1].Equals("Proxies", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the key being read is an argument of the proxy currently open.</summary>
    private static bool IsAProxyArgument(List<string> blocks) => IsAProxyName(blocks);

    private static bool DescribesTheSurface(List<string> blocks) =>
        blocks.Count == 1 ||
        (blocks.Count == 2 &&
            (blocks[1].Equals("replace", StringComparison.OrdinalIgnoreCase) ||
                blocks[1].Equals("insert", StringComparison.OrdinalIgnoreCase) ||
                SatisfiesTheDirectXCondition(blocks[1])));

    /// <summary>Whether a block name is a DirectX-level condition this renderer meets.</summary>
    /// <param name="block">The block's name, as the file spells it.</param>
    /// <returns>True to merge its keys; false for anything else, condition or not.</returns>
    /// <remarks>
    /// **A block named for a DirectX support level gates its keys on that level** (B326):
    ///
    /// <code>
    /// "VertexlitGeneric"
    /// {
    ///     "$envmaptint" "[1.5 1.2 .2]"
    ///
    ///     "&gt;=DX90" { "$envmap" "cubemaps/cubemap_gold001" }
    /// }
    /// </code>
    ///
    /// This project draws through Direct3D 11, so every `&gt;=` condition TF2 ships is met and every
    /// `&lt;` one is not. Dropping the lot was not neutral: **5,415 of the 30,684 materials TF2
    /// ships declare `$selfillum` inside a `&gt;=DX90` block**, a parameter this reader implements
    /// and was reading in none of them, and 59 gate their `$envmap` the same way — which is how the
    /// gap was found, on a corpse carrying the tint of a reflection it had no cubemap for.
    ///
    /// <code>
    /// dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- vmt-blocks "&gt;=DX90"
    /// </code>
    ///
    /// **Evidence class: shipped data plus convention, NOT read-from-source, and that matters.**
    /// `source-sdk-2013` publishes `shaderapidx9` and `stdshaders` but not the material system's
    /// VMT loader, so the merge cannot be quoted from Valve. What IS measured is which spellings
    /// exist and what they contain; that `&gt;=` means "at least this level" is the convention the
    /// files are written against. Flagged every time, because an interpolation that reads like a
    /// measurement is how a wrong conclusion gets repeated.
    ///
    /// **The suffixed forms are shader models at the same level.** `dx90_20b` is shader model 2.0b
    /// on dxlevel 90, and TF2 ships ten materials gating on it; the number is what orders them and
    /// the suffix rides along. Parsed as digits-then-anything rather than matched literally, so a
    /// spelling this corpus does not happen to contain still lands on the right side.
    ///
    /// **A name that is not a condition at all answers false**, which is what keeps `Proxies`, a
    /// proxy's own name, and every `&lt;Shader&gt;_DX&lt;n&gt;` fallback block out — the last of
    /// those is a different mechanism (a whole substitute shader for a level) and stays unhandled
    /// deliberately, since all 800-odd that TF2 ships are low-end.
    /// </remarks>
    private static bool SatisfiesTheDirectXCondition(string block)
    {
        bool atLeast = block.StartsWith(">=", StringComparison.Ordinal);

        if (!atLeast && !block.StartsWith('<'))
        {
            return false;
        }

        ReadOnlySpan<char> rest = block.AsSpan(atLeast ? 2 : 1);

        if (!rest.StartsWith("dx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = rest[2..];

        int digits = 0;

        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        // No digits is not a condition — it is some other block whose name happens to begin "<dx".
        if (digits == 0 ||
            !int.TryParse(rest[..digits], NumberStyles.None, CultureInfo.InvariantCulture,
                out int level))
        {
            return false;
        }

        return atLeast ? DirectXLevel >= level : DirectXLevel < level;
    }

    /// <summary>Whether a block names the shader FALLBACK this renderer would actually be using.</summary>
    /// <param name="blocks">The open block names; only a depth-two block can be one.</param>
    /// <param name="shader">The material's own shader, which the block's name must extend.</param>
    /// <returns>True to merge its keys.</returns>
    /// <remarks>
    /// **A block named for a shader supplies the material's parameters for when THAT shader draws
    /// it** (B328). The mechanism is the fallback chain, and the SDK states it plainly:
    ///
    /// <code>
    /// SHADER_FALLBACK
    /// {
    ///     if( g_pHardwareConfig->GetDXSupportLevel() &lt; 90 )
    ///         return "LightmappedGeneric_DX8";
    ///     return 0;
    /// }
    /// </code>
    ///
    /// `lightmappedgeneric_dx9.cpp:139-145`, with `DEFINE_FALLBACK_SHADER( LightmappedGeneric,
    /// LightmappedGeneric_DX8 )` at `lightmappedgeneric_dx8.cpp:21`.
    ///
    /// **This is NOT the same test as the `&gt;=DX90` conditions** — see
    /// <see cref="SatisfiesTheDirectXCondition"/>. Those gate parameters inside one shader; these
    /// name another shader entirely, and the difference decides the comparison. **A `_DX8` block is
    /// refused even though 80 is below this renderer's 95**, because it belongs to the substitute
    /// the engine uses below 90 and we are not using it. "At or below our level" is the wrong rule
    /// and would paint the cheap path on expensive hardware.
    ///
    /// **Measured, and it corrected a claim made without looking inside one.** These blocks were
    /// first written off as low-end fallbacks in their entirety. Over TF2's 30,684 materials,
    /// `LightmappedGeneric_DX9` appears in 403 — and 89 of those declare `$bumpmap` ONLY there, 49
    /// `$envmap`, 8 `$parallaxmap`. `tile/tilefloor018a_c17.vmt` keeps its whole bump-and-reflection
    /// setup in one, so it drew flat and matte.
    ///
    /// **The HDR variants are refused because this renderer is LDR**, by the decision recorded on
    /// `BspLightmaps.Read`: it reads lump 8 in preference to lump 53 because the two are scaled
    /// differently and preferring HDR washed the map out.
    ///
    /// **Both spellings of a level are the same level.** TF2 ships `Water_DX90` beside
    /// `Water_DX80`, and `LightmappedGeneric_DX9` beside `LightmappedGeneric_DX8`, so a single
    /// digit is a major version and reads as ninety.
    ///
    /// **What is NOT established**, and it is recorded rather than smoothed over: no shader is
    /// registered as `LightmappedGeneric_DX9` anywhere in `source-sdk-2013` — only helper types and
    /// functions carry the spelling — yet 403 materials name a block for it. Either TF2's engine
    /// registers it and this SDK snapshot omits it, or the material system matches these by some
    /// other rule. The BEHAVIOUR is settled by the shipped data either way: Valve did not author a
    /// bump map that draws on no hardware.
    /// </remarks>
    private static bool TakesThisFallback(List<string> blocks, string shader)
    {
        if (blocks.Count != 2 || shader.Length == 0)
        {
            return false;
        }

        string block = blocks[1];

        // The block must extend the material's own shader — `VertexLitGeneric_DX9` inside a
        // `LightmappedGeneric` material names somebody else's.
        if (block.Length <= shader.Length ||
            !block.StartsWith(shader, StringComparison.OrdinalIgnoreCase) ||
            block[shader.Length] != '_')
        {
            return false;
        }

        ReadOnlySpan<char> suffix = block.AsSpan(shader.Length + 1);

        // LDR, deliberately: `BspLightmaps.Read` prefers the LDR lump, so the HDR fallback is not
        // the shader this renderer would be running.
        if (suffix.Contains("HDR", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int marker = suffix.LastIndexOf("DX", StringComparison.OrdinalIgnoreCase);

        // `LightmappedGeneric_NoBump_DX8` reaches here and is refused by its LEVEL below, not by
        // its infix — the rule is about which shader is drawing, and DX8's is not.
        if (marker < 0)
        {
            return false;
        }

        ReadOnlySpan<char> digits = suffix[(marker + 2)..];

        if (digits.Length == 0 ||
            !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int level))
        {
            return false;
        }

        // `_DX9` and `_DX90` are one level in two spellings; a single digit is a major version.
        if (level < 10)
        {
            level *= 10;
        }

        // **90 and above only**, which is the whole difference from the condition blocks: below it
        // the engine is running a different shader, and its parameters are not ours to take.
        return level >= 90 && level <= DirectXLevel;
    }

    /// <summary>The DirectX support level this renderer reports to a material's conditions.</summary>
    /// <remarks>
    /// **95, which is Direct3D 11 in Source's own numbering** — the same scale `mat_dxlevel` prints,
    /// where 90 is DX9 shader model 2.0, 92 is 2.0b, 95 is 3.0 and 98 is DX10-class. A constant
    /// rather than a query, because this renderer has one backend and every capability the shipped
    /// conditions gate on is unconditionally available in D3D11; a machine-dependent value would
    /// make a material's parameters vary by GPU, which is a source of "it looks different on my
    /// computer" that nothing here needs.
    /// </remarks>
    private const int DirectXLevel = 95;

    /// <summary>Merges a patch over the material it includes.</summary>
    /// <param name="patch">The patch material.</param>
    /// <param name="included">The material it includes.</param>
    /// <returns>The included material with the patch's replacements applied.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A patch's own keys sit under <c>replace</c> or <c>insert</c> blocks in the original format;
    /// <see cref="Parse"/> flattens those into the top level, so applying the patch is a straight
    /// overlay. The shader comes from the included material, because that is what actually draws.
    ///
    /// **This comment claimed that flattening for months while the parser did not do it**, and the
    /// consequence was invisible from here: a patch parsed to <c>include</c> and nothing else, this
    /// method dropped <c>include</c> and overlaid the remaining zero keys, and the merged material
    /// was the stock one exactly. Every patch on every map, silently. Nothing in this method was
    /// wrong — it faithfully applied what it was given — which is why the bug survived a test of
    /// it: the test's fixture put the keys at the patch's top level, a shape real VMTs never use.
    /// <c>VmtPatchBlockTests</c> now uses a byte-for-byte real one.
    /// </remarks>
    public static VmtMaterial ApplyPatch(VmtMaterial patch, VmtMaterial included)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(included);

        Dictionary<string, string> merged = new(included._values, StringComparer.OrdinalIgnoreCase);

        IEnumerable<KeyValuePair<string, string>> overrides = patch._values
            .Where(pair => !pair.Key.Equals("include", StringComparison.OrdinalIgnoreCase));

        foreach (KeyValuePair<string, string> pair in overrides)
        {
            merged[pair.Key] = pair.Value;
        }

        return new VmtMaterial(included.Shader, merged);
    }

    private float Number(string key, float fallback)
    {
        string? text = Value(key);

        if (text is null)
        {
            return fallback;
        }

        // **Invariant, not current culture.** A material file always writes a point, and a machine
        // set to a comma locale reads "7.5" as 75 - a plausible number an order of magnitude out,
        // which is exactly the failure this project keeps finding.
        if (!float.TryParse(
                text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new InvalidDataException($"A material's {key} is \"{text}\", which is not a number.");
        }

        return value;
    }

    private int Integer(string key, int fallback)
    {
        string? text = Value(key);

        if (text is null)
        {
            return fallback;
        }

        if (!int.TryParse(
                text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new InvalidDataException($"A material's {key} is \"{text}\", which is not a whole number.");
        }

        return value;
    }

    /// <summary>Reads a boolean-valued parameter the way the engine reads one.</summary>
    /// <param name="key">The parameter name, including its leading <c>$</c>.</param>
    /// <returns>Whether the parameter is set to a non-zero value.</returns>
    /// <remarks>
    /// **Non-zero rather than equal to one, because that is what the material system does.** These
    /// parameters are declared <c>SHADER_PARAM_TYPE_INTEGER</c> — <c>$ssbump</c> is the visible
    /// example (<c>"whether or not to use alternate bumpmap format with height"</c>, default
    /// <c>"0"</c>) — and the flag-valued ones become <c>MATERIAL_VAR_*</c> bits set from an integer
    /// read. Nothing in that path compares against the string <c>"1"</c>.
    ///
    /// This used to be <c>Value(key) is "1"</c> for nine parameters, which agreed with the engine
    /// on every material Valve ships and disagreed on anything else: <c>"$translucent" "2"</c>
    /// draws translucent in TF2 and drew opaque here. A custom map's materials are parsed by the
    /// same code as Valve's, so "Valve always writes 1" is a statement about Valve rather than
    /// about the input this reader is given.
    ///
    /// Parsed leniently for the same reason: the engine's integer read is <c>atoi</c>-shaped, so
    /// surrounding whitespace does not stop it and trailing text does not either.
    /// </remarks>
    private bool Flag(string key)
    {
        if (Value(key) is not { } text)
        {
            return false;
        }

        ReadOnlySpan<char> digits = text.AsSpan().TrimStart();
        int end = 0;

        if (end < digits.Length && (digits[end] is '-' or '+'))
        {
            end++;
        }

        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
        {
            end++;
        }

        return int.TryParse(digits[..end], CultureInfo.InvariantCulture, out int value) && value != 0;
    }

    private (float Red, float Green, float Blue) Colour(string key)
    {
        string? text = Value(key);

        if (text is null)
        {
            return (1f, 1f, 1f);
        }

        string trimmed = text.Trim();

        // Two spellings of the same thing, both of which appear in Valve's own SHADER_PARAM
        // defaults: brackets are floats, braces are bytes. Reading a brace form as floats gives a
        // tint of 255 and saturates the surface to white.
        bool isBytes = trimmed.StartsWith('{');
        bool isFloats = trimmed.StartsWith('[');

        if (isBytes || isFloats)
        {
            trimmed = trimmed[1..^(trimmed.Length > 1 && (trimmed[^1] is '}' or ']') ? 1 : 0)];
        }

        string[] parts = trimmed.Split(
            [' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // **A single number is legal and means all three channels**, which is not a tolerance but
        // the engine's own branch. CBaseVSShader::ColorVarsToVector (BaseVSShader.cpp:681-690)
        // switches on the material var's TYPE, and a value written without brackets is a float var
        // rather than a vector one:
        //
        //     if ( pColorVar->GetType() == MATERIAL_VAR_TYPE_VECTOR )
        //         pColorVar->GetVecValue( color.Base(), 3 );
        //     else
        //         color[0] = color[1] = color[2] = pColorVar->GetFloatValue();
        //
        // Rejecting it threw InvalidDataException, which costs the caller the whole material rather
        // than the tint.
        if (parts.Length == 1 && !isBytes && !isFloats)
        {
            float single = Component(key, text, parts[0], scale: 1f);

            return (single, single, single);
        }

        if (parts.Length != 3)
        {
            throw new InvalidDataException(
                $"A material's {key} is \"{text}\", which is not three numbers.");
        }

        float scale = isBytes ? 255f : 1f;

        return (
            Component(key, text, parts[0], scale),
            Component(key, text, parts[1], scale),
            Component(key, text, parts[2], scale));
    }

    private static float Component(string key, string whole, string part, float scale)
    {
        if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new InvalidDataException(
                $"A material's {key} is \"{whole}\", and \"{part}\" is not a number.");
        }

        return value / scale;
    }

    private static string ReadToken(string text, ref int at)
    {
        if (text[at] == '"')
        {
            int end = text.IndexOf('"', at + 1);

            if (end < 0)
            {
                // The file ends inside a quoted string. Everything up to here is still usable.
                at = text.Length;
                return string.Empty;
            }

            string quoted = text[(at + 1)..end];
            at = end + 1;
            return quoted;
        }

        int start = at;

        while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] is not ('{' or '}'))
        {
            at++;
        }

        return text[start..at];
    }
}
