using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Core.Assets;

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

    /// <summary>Whether the surface needs alpha blending rather than being drawn opaque.</summary>
    /// <remarks>
    /// Either key means blending. `$alphatest` is the cheaper cutout form — grates and chain-link
    /// fences — and `$translucent` is real blending; for a map overview both mean "do not draw this
    /// as a solid".
    /// </remarks>
    public bool IsTransparent =>
        Value("$translucent") is "1" || Value("$alphatest") is "1";

    /// <summary>Whether the material is drawn by ADDING its colour to what is behind it.</summary>
    /// <remarks>
    /// **Black contributes nothing under additive blending, which is the whole point.** Source
    /// returns BT_ADD for <c>$additive</c>, so a light cone under a lamp brightens what it covers
    /// and its dark parts disappear. Drawn opaque instead, the same cone is a solid black shape -
    /// measured on cp_process_f12, where <c>props_lights/light_cone_farm_32</c> carries baked
    /// lighting of exactly 0.000 and every lamp in the map wears one.
    /// </remarks>
    public bool IsAdditive => Value("$additive") is "1";

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
