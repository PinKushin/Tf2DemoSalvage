using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

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

    private VmtMaterial(string shader, Dictionary<string, string> values)
    {
        Shader = shader;
        _values = values;
    }

    /// <summary>The shader the material uses, such as <c>LightMappedGeneric</c>.</summary>
    public string Shader { get; }

    /// <summary>Whether this is a patch that includes another material.</summary>
    public bool IsPatch => Shader.Equals("Patch", StringComparison.OrdinalIgnoreCase);

    /// <summary>The material a patch is based on, or null.</summary>
    public string? Include => Value("include");

    /// <summary>The texture drawn on the surface, without extension, or null.</summary>
    public string? BaseTexture => Value("$basetexture");

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
    public bool IsAlphaTested => Value("$alphatest") is "1";

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

            if (Value("$translucent") is "1" || Value("$vertexalpha") is "1")
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
    public bool IsAdditive => Value("$additive") is "1";

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
    public float DetailScale => Number("$detailscale", 4f);

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
    public bool IsSelfShadowingBump => Value("$ssbump") is "1";

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
        int depth = 0;
        int at = 0;

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
                depth++;
                at++;
                pendingKey = null;
            }
            else if (character == '}')
            {
                depth--;
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

                if (depth == 0)
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
                    // Only the top-level block's keys describe the surface. A Proxies block or a
                    // shader fallback carries its own $basetexture that is not the one to draw.
                    if (depth == 1)
                    {
                        values[pendingKey] = token;
                    }

                    pendingKey = null;
                }
            }
        }

        return new VmtMaterial(shader, values);
    }

    /// <summary>Merges a patch over the material it includes.</summary>
    /// <param name="patch">The patch material.</param>
    /// <param name="included">The material it includes.</param>
    /// <returns>The included material with the patch's replacements applied.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A patch's own keys sit under <c>replace</c> or <c>insert</c> blocks in the original format;
    /// this reader flattens those into the top level, so applying the patch is a straight overlay.
    /// The shader comes from the included material, because that is what actually draws.
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
