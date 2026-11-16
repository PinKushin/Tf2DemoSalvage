using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// `CTextureCompositor` (closed `materialsystem.dll`, `ctexturecompositor.cpp`; described by hand in
/// `docs/findings/111-the-war-paint-compositor.md`) with its per-stage shader (`materialsystem/stdshaders/compositor*`,
/// published): a paint kit recipe resolved into the RGBA8 image TF2 makes the weapon's `$basetexture` (B754).
/// </summary>
/// <remarks>
/// <para>
/// **Run on the CPU, one stage at a time, as the GPU runs it one full-screen pass at a time**: each stage that draws
/// writes its own `RGBA8888` render target through sRGB writes (compositor.cpp, `EnableSRGBWrite( true )`), so every
/// intermediate is quantized to 8-bit sRGB and linearized again when its parent reads it (`EnableSRGBRead`, every sampler
/// but a select stage's groups texture). The ps2.0b path is the one taken (four inputs a pass, levels uploaded as
/// given).
/// </para>
/// <para>
/// *Interpolated, each named where it is decided:* the GPU's own sRGB conversion and filtering are done here with the
/// exact sRGB curve and bilinear filtering between mips chosen by the affine's footprint (a GPU may approximate either);
/// texel centres are sampled at pixel centres; the final copy is RGBA8 where the engine compresses to runtime DXT5 with
/// mips (findings 111 §3.8).
/// </para>
/// </remarks>
public sealed class TextureCompositor
{
    private readonly Stage _root;

    private TextureCompositor(Stage root, int width, int height, int team, ulong seed)
    {
        _root = root;
        Width = width;
        Height = height;
        Team = team;
        Seed = seed;
    }

    /// <summary>The composite's width.</summary>
    public int Width { get; }

    /// <summary>The composite's height.</summary>
    public int Height { get; }

    /// <summary>The team the lookups choose their red or blue texture by (2 red, 3 blue).</summary>
    public int Team { get; }

    /// <summary>The 64-bit seed: `GetOriginalID()`, or the item's custom paint kit seed.</summary>
    public ulong Seed { get; }

    /// <summary>
    /// `CreateTextureCompositor( w, h, name, team, seed, kv, flags )` (findings 111 §1.1): the variables substituted, the
    /// first subtree parsed as the stage tree, and the tree randomized; null where the engine's creation fails.
    /// </summary>
    /// <param name="recipe">The `wear_level_N` recipe.</param>
    /// <param name="width">The composite's width.</param>
    /// <param name="height">The composite's height.</param>
    /// <param name="team">The item's team.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The compositor, or null.</returns>
    public static TextureCompositor? Create(PaintKitNode recipe, int width, int height, int team, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (Substitute(recipe) is not { } expanded)
        {
            return null;
        }

        // `GetFirstTrueSubKey`: the first subkey with children; any later subtrees are ignored (§1.1 step 5).
        PaintKitNode? first = null;

        foreach (PaintKitNode child in expanded.Children)
        {
            if (child.Value is null)
            {
                first = child;
                break;
            }
        }

        if (first is null || ParseNodes([first]) is not { Count: 1 } stages)
        {
            return null;
        }

        Stage root = new CopyStage();
        root.Children.Add(stages[0]);

        // §2.1-2.2: the seed's even bits seed stream 0, its odd bits stream 1; the tree draws pre-order, alternating.
        (uint seedA, uint seedB) = SplitSeed(seed);
        UniformRandomStream[] streams = [new(), new()];
        streams[0].SetSeed(unchecked((int)seedA));
        streams[1].SetSeed(unchecked((int)seedB));
        int index = 0;
        root.Randomize(streams[index]);
        RandomizeSiblings(root.Children, ref index, streams);

        return new TextureCompositor(root, width, height, team, seed);
    }

    /// <summary>
    /// `GetSeed` (findings 111 §2.1): bit 2i of the seed to bit i of seed A ("seedhi"), bit 2i+1 to bit i of seed B
    /// ("seedlo").
    /// </summary>
    /// <param name="seed">The 64-bit seed.</param>
    /// <returns>The two 32-bit seeds.</returns>
    public static (uint SeedA, uint SeedB) SplitSeed(ulong seed)
    {
        uint a = 0;
        uint b = 0;

        for (int i = 0; i < 32; i++)
        {
            a |= (uint)((seed >> (2 * i)) & 1) << i;
            b |= (uint)((seed >> ((2 * i) + 1)) & 1) << i;
        }

        return (a, b);
    }

    /// <summary>
    /// The composite's cache name (findings 111 §4.2): the team appears only when a lookup names a blue texture.
    /// </summary>
    /// <param name="name">`"%d_%d_wear_%02d"` of item, kit and wear.</param>
    /// <param name="flags">The creation flags (0 from the weapon proxy).</param>
    /// <returns>The texture name.</returns>
    public string TextureName(string name, uint flags)
    {
        (uint seedA, uint seedB) = SplitSeed(Seed);
        string team = _root.IsTeamSpecific() ? "_team" + Team.ToString(CultureInfo.InvariantCulture) : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"proc/texcomp/{name}_flags{flags:x8}_seedhi{seedA:x8}_seedlo{seedB:x8}{team}_w{Width}_h{Height}");
    }

    /// <summary>Every texture the tree loads, in tree order, for the caller to read before <see cref="Composite"/>.</summary>
    /// <returns>The texture names.</returns>
    public IReadOnlyList<string> TextureNames()
    {
        List<string> names = [];
        _root.CollectTextures(names);
        return names;
    }

    /// <summary>
    /// Resolve every stage and copy the top stage's render target (§3): the composite's RGBA8 texels, sRGB-encoded as the
    /// render target holds them, or null where the engine's compositor fails (a texture that does not load, a lookup
    /// directly under the root).
    /// </summary>
    /// <param name="load">A texture by name, or null where it does not load.</param>
    /// <returns>The texels, or null.</returns>
    public byte[]? Composite(Func<string, CompositorTexture?> load)
    {
        ArgumentNullException.ThrowIfNull(load);

        Context context = new(Width, Height, Team, load);
        Stage top = _root.Children[0];

        // The copy takes only the child's render target: its levels and matrix are dropped (§3.8).
        return top.Resolve(context) is { Target: { } target } ? target : null;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Parsing (findings 111 §1)

    /// <summary>`$name` definitions applied to every non-`$` leaf (§1.3); null on an unknown or unclosed reference.</summary>
    private static PaintKitNode? Substitute(PaintKitNode recipe)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

        foreach (PaintKitNode child in recipe.Children)
        {
            if (child.Name.StartsWith('$'))
            {
                if (child.Value is null)
                {
                    // "All variable definitions must be simple strings".
                    return null;
                }

                variables[child.Name[1..]] = child.Value;
            }
        }

        PaintKitNode copy = new(recipe.Name);

        foreach (PaintKitNode child in recipe.Children)
        {
            if (!child.Name.StartsWith('$'))
            {
                if (Copy(child, variables) is not { } copied)
                {
                    return null;
                }

                copy.Add(copied);
            }
        }

        return copy;
    }

    private static PaintKitNode? Copy(PaintKitNode node, Dictionary<string, string> variables)
    {
        PaintKitNode copy = new(node.Name);

        if (node.Value is not null)
        {
            if (Expand(node.Value, variables, 0) is not { } value)
            {
                return null;
            }

            copy.Value = value;
            return copy;
        }

        foreach (PaintKitNode child in node.Children)
        {
            if (Copy(child, variables) is not { } copied)
            {
                return null;
            }

            copy.Add(copied);
        }

        return copy;
    }

    /// <summary>Each `$[name]` replaced by the variable's value, itself expanded; no cycle guard is seen, so a depth cap stands in.</summary>
    private static string? Expand(string value, Dictionary<string, string> variables, int depth)
    {
        if (depth > 64)
        {
            return null;
        }

        StringBuilder result = new();
        int at = 0;

        while (at < value.Length)
        {
            int open = value.IndexOf("$[", at, StringComparison.Ordinal);

            if (open < 0)
            {
                result.Append(value, at, value.Length - at);
                break;
            }

            result.Append(value, at, open - at);
            int close = value.IndexOf(']', open + 2);

            // "Variable $[%s missing closing bracket ]." / "Couldn't find variable named $%s".
            if (close < 0 || !variables.TryGetValue(value[(open + 2)..close], out string? replacement)
                || Expand(replacement, variables, depth + 1) is not { } expanded)
            {
                return null;
            }

            result.Append(expanded);
            at = close + 1;
        }

        return result.ToString();
    }

    /// <summary>`ParseNodes` (§1.4): every node a stage by its name; null if any is not.</summary>
    private static List<Stage>? ParseNodes(IReadOnlyList<PaintKitNode> nodes)
    {
        List<Stage> stages = [];
        bool failed = false;

        foreach (PaintKitNode node in nodes)
        {
            // Matched case-insensitively (§1.4).
            Stage? stage = node.Name.ToUpperInvariant() switch
            {
                "TEXTURE_LOOKUP" => LookupStage.Parse(node),
                "COMBINE_ADD" => CombineStage.Parse(node, CombineMode.Add),
                "COMBINE_LERP" => CombineStage.Parse(node, CombineMode.Lerp),
                "COMBINE_MULTIPLY" => CombineStage.Parse(node, CombineMode.Multiply),
                "SELECT" => SelectStage.Parse(node),
                "APPLY_STICKER" => StickerStage.Parse(node),
                _ => null,
            };

            if (stage is null)
            {
                // "Compositor Error: Unexpected key '%s' while parsing definition." The other siblings still parse.
                failed = true;
                continue;
            }

            stages.Add(stage);
        }

        return failed ? null : stages;
    }

    /// <summary>The engine's exact float comparison (`==` in the stage code and the shader), on purpose: -0 equals 0.</summary>
    private static bool Same(float a, float b) => a.CompareTo(b) == 0;

    /// <summary>`ParseRangeFromKV` (§1.6): `sscanf( "%f %f" )`, one number giving (v, v); none is fatal.</summary>
    private static (float Low, float High)? Range(string value)
    {
        int at = 0;

        if (ScanFloat(value, ref at) is not { } low)
        {
            return null;
        }

        return ScanFloat(value, ref at) is { } high ? (low, high) : (low, low);
    }

    /// <summary>One `%f`: leading white space skipped, the longest float prefix taken.</summary>
    private static float? ScanFloat(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int start = at;
        int end = at;

        if (end < text.Length && text[end] is '+' or '-')
        {
            end++;
        }

        bool digits = false;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
            digits = true;
        }

        if (end < text.Length && text[end] == '.')
        {
            end++;

            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
                digits = true;
            }
        }

        if (!digits)
        {
            return null;
        }

        if (end < text.Length && text[end] is 'e' or 'E')
        {
            int exponent = end + 1;

            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                end = exponent;

                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }
            }
        }

        at = end;
        return float.Parse(text.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>The `adjust_*` keys every drawing stage shares: black and offset ÷255, gamma inverted (§1.6).</summary>
    private sealed class Levels
    {
        public (float Low, float High) Black { get; private set; }

        public (float Low, float High) Offset { get; private set; } = (1f, 1f);

        public (float Low, float High) Gamma { get; private set; } = (1f, 1f);

        /// <summary>Whether <paramref name="node"/> is one of the three keys; false in <paramref name="failed"/> for a bad value.</summary>
        public bool TryParse(PaintKitNode node, ref bool failed)
        {
            string key = node.Name.ToUpperInvariant();

            if (key is not ("ADJUST_BLACK" or "ADJUST_OFFSET" or "ADJUST_GAMMA"))
            {
                return false;
            }

            if (node.Value is null || Range(node.Value) is not var (low, high))
            {
                failed = true;
                return true;
            }

            const float OneOver255 = 0.003921569f;

            switch (key)
            {
                case "ADJUST_BLACK":
                    Black = (low * OneOver255, high * OneOver255);
                    break;
                case "ADJUST_OFFSET":
                    Offset = (low * OneOver255, high * OneOver255);
                    break;
                default:
                    if (Same(low, 0f) || Same(high, 0f))
                    {
                        failed = true;
                        return true;
                    }

                    Gamma = (1f / low, 1f / high);
                    break;
            }

            return true;
        }

        /// <summary>Black, offset, gamma, in that order (§2.3): black, black + offset, gamma.</summary>
        public (float Black, float White, float Gamma) Draw(UniformRandomStream stream)
        {
            float black = stream.RandomFloat(Black.Low, Black.High);
            float offset = stream.RandomFloat(Offset.Low, Offset.High);
            float gamma = stream.RandomFloat(Gamma.Low, Gamma.High);
            return (black, black + offset, gamma);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The stages (findings 111 §0, §3)

    private enum CombineMode
    {
        Multiply,
        Add,
        Lerp,
    }

    /// <summary>A stage's output to its parent: a texture or a target, the levels, and the 2x3 affine (§3.0).</summary>
    private sealed record Output(CompositorTexture? Texture, byte[]? Target, float Black, float White, float Gamma, Affine Matrix);

    /// <summary>The 2D affine `A * (u, v, 1)` the vertex shader applies (compositor_vs20.fxc, `SetVertexShaderMatrix2x4`).</summary>
    private readonly record struct Affine(float M00, float M01, float M02, float M10, float M11, float M12)
    {
        public static Affine Identity => new(1f, 0f, 0f, 0f, 1f, 0f);

        public (float U, float V) Apply(float u, float v) => ((M00 * u) + (M01 * v) + M02, (M10 * u) + (M11 * v) + M12);
    }

    private sealed record Context(int Width, int Height, int Team, Func<string, CompositorTexture?> Load);

    private abstract class Stage
    {
        public List<Stage> Children { get; } = [];

        /// <summary>Slot 12: draw this stage's random values; true when it drew (and the stream toggles).</summary>
        public abstract bool Randomize(UniformRandomStream stream);

        /// <summary>Slot 10: this stage's output, or null when it fails.</summary>
        public abstract Output? Resolve(Context context);

        /// <summary>Slot 11, over the tree (§4.2).</summary>
        public virtual bool IsTeamSpecific()
        {
            foreach (Stage child in Children)
            {
                if (child.IsTeamSpecific())
                {
                    return true;
                }
            }

            return false;
        }

        public virtual void CollectTextures(List<string> names)
        {
            foreach (Stage child in Children)
            {
                child.CollectTextures(names);
            }
        }

        /// <summary>Every key not in the stage's own table is a child stage (§1.4).</summary>
        protected static bool ParseChildren(Stage stage, List<PaintKitNode> rest)
        {
            if (rest.Count == 0)
            {
                return true;
            }

            if (ParseNodes(rest) is not { } children)
            {
                return false;
            }

            stage.Children.AddRange(children);
            return true;
        }
    }

    /// <summary>`Randomize`'s traversal (§2.2): each sibling, then its children, toggling the stream after a stage that drew.</summary>
    private static void RandomizeSiblings(List<Stage> siblings, ref int index, UniformRandomStream[] streams)
    {
        foreach (Stage stage in siblings)
        {
            if (stage.Randomize(streams[index]))
            {
                index = (index + 1) % streams.Length;
            }

            RandomizeSiblings(stage.Children, ref index, streams);
        }
    }

    /// <summary>`CTCCopyStage`, the root: no draws; it copies its child's render target (§3.8).</summary>
    private sealed class CopyStage : Stage
    {
        public override bool Randomize(UniformRandomStream stream) => false;

        public override Output? Resolve(Context context) => Children[0].Resolve(context);
    }

    /// <summary>`texture_lookup` (§1.5, §2.3, §3.1, §3.2): no draw of its own; its texture, levels and matrix go to the parent.</summary>
    private sealed class LookupStage : Stage
    {
        private string _texture = string.Empty;
        private string _red = string.Empty;
        private string _blue = string.Empty;
        private readonly Levels _levels = new();
        private (float Low, float High) _rotation;
        private (float Low, float High) _translateU;
        private (float Low, float High) _translateV;
        private (float Low, float High) _scale = (1f, 1f);
        private (float Low, float High) _flipU;
        private (float Low, float High) _flipV;
        private (float Black, float White, float Gamma) _drawn = (0f, 1f, 1f);
        private Affine _matrix = Affine.Identity;

        public static LookupStage? Parse(PaintKitNode node)
        {
            LookupStage stage = new();
            List<PaintKitNode> rest = [];
            bool failed = false;

            foreach (PaintKitNode key in node.Children)
            {
                if (stage._levels.TryParse(key, ref failed))
                {
                    continue;
                }

                if (!stage.TryParseKey(key.Name.ToUpperInvariant(), key.Value, ref failed))
                {
                    rest.Add(key);
                }
            }

            return !failed && ParseChildren(stage, rest) ? stage : null;
        }

        public override bool Randomize(UniformRandomStream stream)
        {
            // §2.3: the flips (a draw only for a real range), translation, rotation, scale, then the levels.
            bool flipU = Flip(_flipU, stream);
            bool flipV = Flip(_flipV, stream);
            float tu = stream.RandomFloat(_translateU.Low, _translateU.High);
            float tv = stream.RandomFloat(_translateV.Low, _translateV.High);
            float rotation = stream.RandomFloat(_rotation.Low, _rotation.High);
            float scale = stream.RandomFloat(_scale.Low, _scale.High);
            _drawn = _levels.Draw(stream);
            _matrix = UvMatrix(rotation, scale, flipU, flipV, tu, tv);
            return true;
        }

        /// <summary>One of the lookup's own keys (§1.5); false for a key that is a child stage.</summary>
        private bool TryParseKey(string name, string? value, ref bool failed)
        {
            switch (name)
            {
                case "TEXTURE":
                    _texture = value ?? string.Empty;
                    return true;
                case "TEXTURE_RED":
                    _red = value ?? string.Empty;
                    return true;
                case "TEXTURE_BLUE":
                    _blue = value ?? string.Empty;
                    return true;
                default:
                    break;
            }

            if (name is not ("ROTATION" or "TRANSLATE_U" or "TRANSLATE_V" or "SCALE_UV" or "FLIP_U" or "FLIP_V"))
            {
                return false;
            }

            if (value is null || Range(value) is not { } range)
            {
                failed = true;
                return true;
            }

            switch (name)
            {
                case "ROTATION":
                    _rotation = range;
                    break;
                case "TRANSLATE_U":
                    _translateU = range;
                    break;
                case "TRANSLATE_V":
                    _translateV = range;
                    break;
                case "SCALE_UV":
                    _scale = range;
                    break;
                case "FLIP_U":
                    _flipU = range;
                    break;
                default:
                    _flipV = range;
                    break;
            }

            return true;
        }

        private static bool Flip((float Low, float High) range, UniformRandomStream stream) =>
            Same(range.Low, range.High) ? (int)range.Low != 0 : stream.RandomInt(0, 1) != 0;

        /// <summary>
        /// The lookup's UV matrix (§3.2): `R( rotation° ) * diag( ±scale, ±scale )`, the translation passed through it,
        /// repacked as a 2x3 affine.
        /// </summary>
        private static Affine UvMatrix(float rotation, float scale, bool flipU, bool flipV, float tu, float tv)
        {
            float angle = rotation * 0.017453292f;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            float su = (flipU ? -1f : 1f) * scale;
            float sv = (flipV ? -1f : 1f) * scale;
            float m00 = c * su;
            float m01 = -s * sv;
            float m10 = s * su;
            float m11 = c * sv;
            return new Affine(m00, m01, (m00 * tu) + (m01 * tv), m10, m11, (m10 * tu) + (m11 * tv));
        }

        public override bool IsTeamSpecific() => _blue.Length > 0 || base.IsTeamSpecific();

        public override void CollectTextures(List<string> names)
        {
            foreach (string name in (string[])[_texture, _red, _blue])
            {
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            base.CollectTextures(names);
        }

        public override Output? Resolve(Context context)
        {
            CompositorTexture? texture = _texture.Length > 0 ? context.Load(_texture) : null;
            CompositorTexture? red = _red.Length > 0 ? context.Load(_red) : null;
            CompositorTexture? blue = _blue.Length > 0 ? context.Load(_blue) : null;

            // A named texture that does not load is fatal; so is neither texture nor red/blue (§3.1).
            if ((_texture.Length > 0 && texture is null) || (_red.Length > 0 && red is null) || (_blue.Length > 0 && blue is null)
                || (texture is null && red is null && blue is null))
            {
                return null;
            }

            CompositorTexture? chosen = context.Team switch
            {
                2 when red is not null => red,
                3 when blue is not null => blue,
                _ => texture,
            };

            return chosen is null ? null : new Output(chosen, null, _drawn.Black, _drawn.White, _drawn.Gamma, _matrix);
        }
    }

    /// <summary>`combine_add` / `combine_lerp` / `combine_multiply` (§1.5, §2.3, §3.4): its UV keys parse and do nothing.</summary>
    private sealed class CombineStage(CombineMode mode) : Stage
    {
        private readonly Levels _levels = new();
        private (float Black, float White, float Gamma) _drawn = (0f, 1f, 1f);

        public static CombineStage? Parse(PaintKitNode node, CombineMode mode)
        {
            CombineStage stage = new(mode);
            List<PaintKitNode> rest = [];
            bool failed = false;

            foreach (PaintKitNode key in node.Children)
            {
                if (stage._levels.TryParse(key, ref failed))
                {
                    continue;
                }

                // Parsed and stored, never read by Randomize or Resolve (§1.5).
                if (key.Name.ToUpperInvariant() is "ROTATION" or "TRANSLATE_U" or "TRANSLATE_V" or "SCALE_UV" or "FLIP_U" or "FLIP_V")
                {
                    failed |= key.Value is null || Range(key.Value) is null;
                    continue;
                }

                rest.Add(key);
            }

            return !failed && ParseChildren(stage, rest) ? stage : null;
        }

        public override bool Randomize(UniformRandomStream stream)
        {
            _drawn = _levels.Draw(stream);
            return true;
        }

        public override Output? Resolve(Context context)
        {
            List<Output> inputs = [];

            foreach (Stage child in Children)
            {
                if (child.Resolve(context) is not { } output)
                {
                    return null;
                }

                inputs.Add(output);
            }

            byte[] target = DrawPass(context, inputs, srgbFirstInput: true, (colors, count) => mode switch
            {
                CombineMode.Multiply => Multiply(colors, count),
                CombineMode.Add => Add(colors, count),
                _ => count == 3 ? Lerp(colors[0], colors[1], colors[2].X) : new Color(1f, 0f, 0f, 1f),
            });

            return new Output(null, target, _drawn.Black, _drawn.White, _drawn.Gamma, Affine.Identity);
        }

        /// <summary>`MULTIPLY`: every input's product, `cSafeColor` 1 for a missing one.</summary>
        private static Color Multiply(Color[] colors, int count)
        {
            Color result = new(1f, 1f, 1f, 1f);

            for (int i = 0; i < 4; i++)
            {
                Color color = i < count ? colors[i] : new Color(1f, 1f, 1f, 1f);
                result = new Color(result.X * color.X, result.Y * color.Y, result.Z * color.Z, result.W * color.W);
            }

            return result;
        }

        /// <summary>`ADD`: every input's sum, `cSafeColor` 0 for a missing one.</summary>
        private static Color Add(Color[] colors, int count)
        {
            Color result = default;

            for (int i = 0; i < Math.Min(count, 4); i++)
            {
                result = new Color(result.X + colors[i].X, result.Y + colors[i].Y, result.Z + colors[i].Z, result.W + colors[i].W);
            }

            return result;
        }

        /// <summary>`main_lerp`: the first input toward the second by the third's red.</summary>
        private static Color Lerp(Color a, Color b, float t) =>
            new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t), a.W + ((b.W - a.W) * t));
    }

    /// <summary>`select` (§1.5, §3.6): the groups texture's value, in sixteenths, against up to sixteen selectors.</summary>
    private sealed class SelectStage : Stage
    {
        private string _groups = string.Empty;
        private readonly List<int> _selectors = [];

        public static SelectStage? Parse(PaintKitNode node)
        {
            SelectStage stage = new();
            List<PaintKitNode> rest = [];

            foreach (PaintKitNode key in node.Children)
            {
                switch (key.Name.ToUpperInvariant())
                {
                    case "GROUPS":
                        stage._groups = key.Value ?? string.Empty;
                        break;
                    case "SELECT":
                        // `GetInt`, and a seventeenth is dropped with a warning.
                        if (stage._selectors.Count < 16)
                        {
                            stage._selectors.Add(GetInt(key.Value));
                        }

                        break;
                    default:
                        rest.Add(key);
                        break;
                }
            }

            return ParseChildren(stage, rest) ? stage : null;
        }

        public override bool Randomize(UniformRandomStream stream) => false;

        public override void CollectTextures(List<string> names)
        {
            if (_groups.Length > 0)
            {
                names.Add(_groups);
            }

            base.CollectTextures(names);
        }

        public override Output? Resolve(Context context)
        {
            if (context.Load(_groups) is not { } groups)
            {
                return null;
            }

            // compositor.cpp: each selector times 1/16; the shader matches round( v * 255 / 16 ) against round( selector ).
            float[] selectors = new float[16];

            for (int i = 0; i < _selectors.Count; i++)
            {
                selectors[i] = _selectors[i] * (1f / 16f);
            }

            Output input = new(groups, null, 0f, 1f, 1f, Affine.Identity);
            byte[] target = DrawPass(context, [input], srgbFirstInput: false, (colors, count) =>
            {
                if (count != 1)
                {
                    return new Color(1f, 1f, 0f, 1f);
                }

                float test = MathF.Round(colors[0].X * 255f / 16f, MidpointRounding.AwayFromZero);

                foreach (float selector in selectors)
                {
                    if (!Same(selector, 0f) && Same(MathF.Round(selector, MidpointRounding.AwayFromZero), test))
                    {
                        return new Color(1f, 1f, 1f, 1f);
                    }
                }

                return new Color(0f, 0f, 0f, 0f);
            });

            return new Output(null, target, 0f, 1f, 1f, Affine.Identity);
        }

        /// <summary>`KeyValues::GetInt` of a string: `atoi`.</summary>
        private static int GetInt(string? value)
        {
            if (value is null)
            {
                return 0;
            }

            int at = 0;
            return ScanFloat(value, ref at) is { } number ? (int)number : 0;
        }
    }

    /// <summary>`apply_sticker` (§1.5, §2.3, §3.7): one weighted sticker blended over the child by its alpha.</summary>
    private sealed class StickerStage : Stage
    {
        private readonly List<(float Weight, string Base, string Spec)> _stickers = [];
        private (float X, float Y)? _bottomLeft;
        private (float X, float Y)? _topLeft;
        private (float X, float Y)? _topRight;
        private readonly Levels _levels = new();
        private (float Black, float White, float Gamma) _drawn = (0f, 1f, 1f);
        private int _chosen;
        private Affine _matrix = Affine.Identity;

        public static StickerStage? Parse(PaintKitNode node)
        {
            StickerStage stage = new();
            List<PaintKitNode> rest = [];
            bool failed = false;

            foreach (PaintKitNode key in node.Children)
            {
                if (stage._levels.TryParse(key, ref failed))
                {
                    continue;
                }

                switch (key.Name.ToUpperInvariant())
                {
                    case "STICKER":
                        stage._stickers.Add(Sticker(key));
                        break;
                    case "DEST_BL":
                        failed |= !Point(key, out stage._bottomLeft);
                        break;
                    case "DEST_TL":
                        failed |= !Point(key, out stage._topLeft);
                        break;
                    case "DEST_TR":
                        failed |= !Point(key, out stage._topRight);
                        break;
                    default:
                        rest.Add(key);
                        break;
                }
            }

            // All three corners, and fewer than two children (§1.5).
            failed |= stage._bottomLeft is null || stage._topLeft is null || stage._topRight is null;
            return !failed && ParseChildren(stage, rest) && stage.Children.Count < 2 ? stage : null;
        }

        /// <summary>`weight` (default 1), `base`, and `spec`, derived from `base` when absent (§1.5).</summary>
        private static (float Weight, string Base, string Spec) Sticker(PaintKitNode node)
        {
            float weight = 1f;
            string baseName = string.Empty;
            string? spec = null;

            foreach (PaintKitNode key in node.Children)
            {
                switch (key.Name.ToUpperInvariant())
                {
                    case "WEIGHT":
                        int at = 0;
                        weight = key.Value is null ? 0f : ScanFloat(key.Value, ref at) ?? 0f;
                        break;
                    case "BASE":
                        baseName = key.Value ?? string.Empty;
                        break;
                    case "SPEC":
                        spec = key.Value ?? string.Empty;
                        break;
                    default:
                        break;
                }
            }

            // `StripExtension( base ) + "_s" + extension without its dot`.
            int dot = baseName.LastIndexOf('.');
            int slash = Math.Max(baseName.LastIndexOf('/'), baseName.LastIndexOf('\\'));
            spec ??= dot > slash ? baseName[..dot] + "_s" + baseName[(dot + 1)..] : baseName + "_s";
            return (weight, baseName, spec);
        }

        /// <summary>Exactly two floats, `%f %f` (§1.5).</summary>
        private static bool Point(PaintKitNode node, out (float X, float Y)? point)
        {
            point = null;

            if (node.Value is null)
            {
                return false;
            }

            int at = 0;

            if (ScanFloat(node.Value, ref at) is not { } x || ScanFloat(node.Value, ref at) is not { } y)
            {
                return false;
            }

            point = (x, y);
            return true;
        }

        public override bool Randomize(UniformRandomStream stream)
        {
            // §2.3: the weighted pick (strict "<"), then black, offset, gamma.
            float total = 0f;

            foreach ((float weight, string _, string _) in _stickers)
            {
                total += weight;
            }

            float pick = stream.RandomFloat(0f, total);
            _chosen = 0;

            for (int i = 0; i < _stickers.Count; i++)
            {
                if (pick < _stickers[i].Weight)
                {
                    _chosen = i;
                    break;
                }

                pick -= _stickers[i].Weight;
            }

            _drawn = _levels.Draw(stream);
            // Parse refuses a sticker stage without all three corners.
            _matrix = _bottomLeft is { } bottomLeft && _topLeft is { } topLeft && _topRight is { } topRight
                ? StickerMatrix(bottomLeft, topLeft, topRight)
                : Affine.Identity;
            return true;
        }

        public override void CollectTextures(List<string> names)
        {
            foreach ((float _, string baseName, string spec) in _stickers)
            {
                names.Add(baseName);
                names.Add(spec);
            }

            base.CollectTextures(names);
        }

        public override Output? Resolve(Context context)
        {
            if (_stickers.Count == 0)
            {
                return null;
            }

            (float _, string baseName, string specName) = _stickers[_chosen];
            List<Output> inputs = [];

            if (Children.Count > 0)
            {
                if (Children[0].Resolve(context) is not { } child)
                {
                    return null;
                }

                inputs.Add(child);
            }

            if (baseName.Length > 0)
            {
                if (context.Load(baseName) is not { } baseTexture)
                {
                    return null;
                }

                inputs.Add(new Output(baseTexture, null, 0f, 1f, 1f, _matrix));
            }

            // A spec that does not load becomes the engine's "black" (§3.7).
            if (specName.Length > 0)
            {
                CompositorTexture spec = context.Load(specName) ?? Black;
                inputs.Add(new Output(spec, null, 0f, 1f, 1f, _matrix));
            }

            byte[] target = DrawPass(context, inputs, srgbFirstInput: true, (colors, count) =>
            {
                // compositor_ps2x.fxc `main_simple`, BLEND: color0 is what lies under, color1 the sticker, color2 its spec.
                Color under = count > 0 ? colors[0] : default;
                Color sticker = count > 1 ? colors[1] : default;
                float spec = count > 2 ? colors[2].X : 0f;
                float a = sticker.W;

                return new Color(
                    ((1f - a) * under.X) + (a * sticker.X),
                    ((1f - a) * under.Y) + (a * sticker.Y),
                    ((1f - a) * under.Z) + (a * sticker.Z),
                    ((1f - a) * under.W) + (a * spec));
            });

            return new Output(null, target, _drawn.Black, _drawn.White, _drawn.Gamma, Affine.Identity);
        }

        private static readonly CompositorTexture Black = new([(1, 1, new byte[] { 0, 0, 0, 255 })]);

    /// <summary>
    /// The sticker's matrix (§3.7): the inverse of `Translate( TL ) * RotateZ( angle ) * Scale( sgn * w, h )`, mapping the
    /// destination to sticker space with (0, 0) at TL, (1, 0) at TR and (0, 1) at BL.
    /// </summary>
    private static Affine StickerMatrix((float X, float Y) bottomLeft, (float X, float Y) topLeft, (float X, float Y) topRight)
    {
        float dblx = bottomLeft.X - topLeft.X;
        float dbly = bottomLeft.Y - topLeft.Y;
        float h = MathF.Sqrt((dblx * dblx) + (dbly * dbly));
        float dtrx = topRight.X - topLeft.X;
        float dtry = topRight.Y - topLeft.Y;
        float w = MathF.Sqrt((dtrx * dtrx) + (dtry * dtry));
        float angle = MathF.Acos(dbly / h) * 57.29578f * (dblx < 0f ? 1f : -1f);
        float sign = (((topLeft.Y - topRight.Y) * dblx) + (dtrx * dbly)) > 0f ? 1f : -1f;

        float radians = angle * 0.017453292f;
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);

        // M = T * R * S as a 2x3 affine; its inverse by the 2x2 determinant (the 4x4 Gauss-Jordan of the engine reduces to it).
        float m00 = c * sign * w;
        float m01 = -s * h;
        float m10 = s * sign * w;
        float m11 = c * h;
        float determinant = (m00 * m11) - (m01 * m10);

        if (MathF.Abs(determinant) < 1e-5f)
        {
            return Affine.Identity;
        }

        float i00 = m11 / determinant;
        float i01 = -m01 / determinant;
        float i10 = -m10 / determinant;
        float i11 = m00 / determinant;
        return new Affine(i00, i01, -((i00 * topLeft.X) + (i01 * topLeft.Y)), i10, i11, -((i10 * topLeft.X) + (i11 * topLeft.Y)));
    }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The pass (compositor.cpp `DrawCompositorStage_ps20b`, compositor_ps2x.fxc, compositor_vs20.fxc)

    /// <summary>A linear RGBA colour.</summary>
    private readonly record struct Color(float X, float Y, float Z, float W);

    /// <summary>
    /// One full-screen pass: each pixel's inputs sampled through their affines, their levels applied, combined, then written
    /// to an RGBA8 target through sRGB writes. At most four inputs (`textureCountThisPass = Min( count, 4 )`).
    /// </summary>
    private static byte[] DrawPass(Context context, List<Output> inputs, bool srgbFirstInput, Func<Color[], int, Color> combine)
    {
        int width = context.Width;
        int height = context.Height;
        int count = Math.Min(inputs.Count, 4);
        byte[] target = new byte[width * height * 4];
        Color[] colors = new Color[4];
        Sampler[] samplers = new Sampler[count];

        for (int i = 0; i < count; i++)
        {
            samplers[i] = new Sampler(inputs[i], width, height, srgb: i != 0 || srgbFirstInput);
        }

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;

                for (int i = 0; i < count; i++)
                {
                    Output input = inputs[i];
                    (float su, float sv) = input.Matrix.Apply(u, v);
                    colors[i] = AdjustLevels(samplers[i].Sample(su, sv), input.Black, input.White, input.Gamma);
                }

                Color result = combine(colors, count);
                int at = ((y * width) + x) * 4;
                target[at] = Encode(result.X);
                target[at + 1] = Encode(result.Y);
                target[at + 2] = Encode(result.Z);
                target[at + 3] = Quantize(result.W);
            }
        }

        return target;
    }

    /// <summary>`AdjustLevels` (compositor_ps2x.fxc:723-738): Photoshop levels in sRGB, unless (0, 1, 1).</summary>
    private static Color AdjustLevels(Color color, float black, float white, float gamma)
    {
        if (Same(black, 0f) && Same(white, 1f) && Same(gamma, 1f))
        {
            return color;
        }

        static float Channel(float value, float black, float white, float gamma, bool srgb)
        {
            float encoded = srgb ? LinearToSrgb(value) : value;
            float scaled = Math.Clamp((encoded - black) / (white - black), 0f, 1f);
            float adjusted = MathF.Pow(scaled, gamma);
            return Math.Clamp(srgb ? SrgbToLinear(adjusted) : adjusted, 0f, 1f);
        }

        return new Color(
            Channel(color.X, black, white, gamma, true),
            Channel(color.Y, black, white, gamma, true),
            Channel(color.Z, black, white, gamma, true),
            Channel(color.W, black, white, gamma, false));
    }

    private static float LinearToSrgb(float value) =>
        value <= 0.0031308f ? 12.92f * value : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    /// <summary>An sRGB write: the linear value clamped, encoded, rounded to 8 bits.</summary>
    private static byte Encode(float linear) => Quantize(LinearToSrgb(Math.Clamp(linear, 0f, 1f)));

    private static byte Quantize(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

    /// <summary>A sampler over a source texture (wrapped, bilinear, mip chosen by the affine's footprint) or a target.</summary>
    private sealed class Sampler
    {
        private readonly IReadOnlyList<(int Width, int Height, byte[] Pixels)> _levels;
        private readonly bool _srgb;
        private readonly float _lod;

        public Sampler(Output input, int targetWidth, int targetHeight, bool srgb)
        {
            _srgb = srgb;
            _levels = input.Texture?.Levels ?? [(targetWidth, targetHeight, input.Target ?? [])];

            // The footprint of one target pixel in the top level's texels; a target has one level.
            (int topWidth, int topHeight, byte[] _) = _levels[0];
            Affine m = input.Matrix;
            float dx = MathF.Sqrt(Square(m.M00 * topWidth / targetWidth) + Square(m.M10 * topHeight / targetWidth));
            float dy = MathF.Sqrt(Square(m.M01 * topWidth / targetHeight) + Square(m.M11 * topHeight / targetHeight));
            _lod = Math.Clamp(MathF.Log2(Math.Max(Math.Max(dx, dy), 1e-8f)), 0f, _levels.Count - 1);
        }

        private static float Square(float value) => value * value;

        public Color Sample(float u, float v)
        {
            int low = (int)MathF.Floor(_lod);
            int high = Math.Min(low + 1, _levels.Count - 1);
            float blend = _lod - low;
            Color a = Bilinear(_levels[low], u, v);

            if (blend <= 0f || high == low)
            {
                return a;
            }

            Color b = Bilinear(_levels[high], u, v);
            return new Color(
                a.X + ((b.X - a.X) * blend), a.Y + ((b.Y - a.Y) * blend), a.Z + ((b.Z - a.Z) * blend), a.W + ((b.W - a.W) * blend));
        }

        private Color Bilinear((int Width, int Height, byte[] Pixels) level, float u, float v)
        {
            float x = (u * level.Width) - 0.5f;
            float y = (v * level.Height) - 0.5f;
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            float fx = x - x0;
            float fy = y - y0;
            Color c00 = Texel(level, x0, y0);
            Color c10 = Texel(level, x0 + 1, y0);
            Color c01 = Texel(level, x0, y0 + 1);
            Color c11 = Texel(level, x0 + 1, y0 + 1);

            static float Mix(float a, float b, float c, float d, float fx, float fy) =>
                ((a * (1f - fx)) + (b * fx)) * (1f - fy) + (((c * (1f - fx)) + (d * fx)) * fy);

            return new Color(
                Mix(c00.X, c10.X, c01.X, c11.X, fx, fy),
                Mix(c00.Y, c10.Y, c01.Y, c11.Y, fx, fy),
                Mix(c00.Z, c10.Z, c01.Z, c11.Z, fx, fy),
                Mix(c00.W, c10.W, c01.W, c11.W, fx, fy));
        }

        private Color Texel((int Width, int Height, byte[] Pixels) level, int x, int y)
        {
            // Wrap: no clamp flag is set on any compositor texture or target (§4.1).
            x = ((x % level.Width) + level.Width) % level.Width;
            y = ((y % level.Height) + level.Height) % level.Height;
            int at = ((y * level.Width) + x) * 4;

            if (at + 3 >= level.Pixels.Length)
            {
                return default;
            }

            float r = level.Pixels[at] / 255f;
            float g = level.Pixels[at + 1] / 255f;
            float b = level.Pixels[at + 2] / 255f;
            float a = level.Pixels[at + 3] / 255f;
            return _srgb ? new Color(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b), a) : new Color(r, g, b, a);
        }
    }
}
