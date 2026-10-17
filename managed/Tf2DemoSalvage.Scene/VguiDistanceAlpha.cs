using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A `$distancealpha` material's parameters: the shape read from a distance field, its soft edge, outline and glow —
/// TF2's HUD backgrounds (`materials/hud/ammo_blue_bg.vmt` and its kin).
/// </summary>
/// <remarks>
/// Read as `UnlitGeneric` reads them (unlitgeneric_dx9.cpp:50-69 declares them; vertexlitgeneric_dx9_helper.cpp:156-159
/// sets the four defaults; every other unset float is 0, per <c>a-shader-param-default-string-is-not-applied</c>). The
/// shader's use of them is `vertexlit_and_unlit_generic_ps2x.fxc:257-327`, drawn in the VGUI renderer.
/// </remarks>
/// <param name="Detail">The `$detail` texture, or null.</param>
/// <param name="FromDetail">`$distancealphafromdetail`: the field is the detail's alpha rather than the base's.</param>
/// <param name="DetailBlendMode">`$detailblendmode`.</param>
/// <param name="DetailBlendFactor">`$detailblendfactor`, 1 unless set (:87).</param>
/// <param name="SoftMask">`$softedges`.</param>
/// <param name="SoftStart">`$edgesoftnessstart`, 0.5 unless set (:156).</param>
/// <param name="SoftEnd">`$edgesoftnessend`, 0.5 unless set (:157).</param>
/// <param name="ScaleEdges">`$scaleedgesoftnessbasedonscreenres`.</param>
/// <param name="Outline">`$outline`.</param>
/// <param name="OutlineColor">`$outlinecolor`.</param>
/// <param name="OutlineAlpha">`$outlinealpha`, 1 unless set (:159).</param>
/// <param name="OutlineStart0">`$outlinestart0`.</param>
/// <param name="OutlineStart1">`$outlinestart1`.</param>
/// <param name="OutlineEnd0">`$outlineend0`.</param>
/// <param name="OutlineEnd1">`$outlineend1`.</param>
/// <param name="ScaleOutline">`$scaleoutlinesoftnessbasedonscreenres`.</param>
/// <param name="Glow">`$glow`.</param>
/// <param name="GlowColor">`$glowcolor`.</param>
/// <param name="GlowAlpha">`$glowalpha`, 1 unless set (:158).</param>
/// <param name="GlowStart">`$glowstart`.</param>
/// <param name="GlowEnd">`$glowend`.</param>
/// <param name="GlowX">`$glowx`, the glow's texture offset.</param>
/// <param name="GlowY">`$glowy`.</param>
public sealed record VguiDistanceAlpha(
    MapTexture? Detail,
    bool FromDetail,
    int DetailBlendMode,
    float DetailBlendFactor,
    bool SoftMask,
    float SoftStart,
    float SoftEnd,
    bool ScaleEdges,
    bool Outline,
    (float Red, float Green, float Blue) OutlineColor,
    float OutlineAlpha,
    float OutlineStart0,
    float OutlineStart1,
    float OutlineEnd0,
    float OutlineEnd1,
    bool ScaleOutline,
    bool Glow,
    (float Red, float Green, float Blue) GlowColor,
    float GlowAlpha,
    float GlowStart,
    float GlowEnd,
    float GlowX,
    float GlowY)
{
    /// <summary>The material's distance-alpha parameters, or null when it does not set `$distancealpha`.</summary>
    /// <param name="value">A VMT key's value, or null when the material does not name it.</param>
    /// <param name="detail">The material's resolved `$detail` texture, or null.</param>
    /// <returns>The parameters, or null.</returns>
    public static VguiDistanceAlpha? Read(Func<string, string?> value, MapTexture? detail = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Flag(value, "$distancealpha"))
        {
            return null;
        }

        return new VguiDistanceAlpha(
            detail,
            Flag(value, "$distancealphafromdetail"),
            (int)Number(value, "$detailblendmode", 0f),
            Number(value, "$detailblendfactor", 1f),
            Flag(value, "$softedges"),
            Number(value, "$edgesoftnessstart", 0.5f),
            Number(value, "$edgesoftnessend", 0.5f),
            Flag(value, "$scaleedgesoftnessbasedonscreenres"),
            Flag(value, "$outline"),
            Vector(value, "$outlinecolor"),
            Number(value, "$outlinealpha", 1f),
            Number(value, "$outlinestart0", 0f),
            Number(value, "$outlinestart1", 0f),
            Number(value, "$outlineend0", 0f),
            Number(value, "$outlineend1", 0f),
            Flag(value, "$scaleoutlinesoftnessbasedonscreenres"),
            Flag(value, "$glow"),
            Vector(value, "$glowcolor"),
            Number(value, "$glowalpha", 1f),
            Number(value, "$glowstart", 0f),
            Number(value, "$glowend", 0f),
            Number(value, "$glowx", 0f),
            Number(value, "$glowy", 0f));
    }

    /// <summary>Pixel-shader constants c5 to c9 as the helper sets them (vertexlitgeneric_dx9_helper.cpp:995-1066).</summary>
    /// <param name="backBufferWidth">`GetBackBufferDimensions` width.</param>
    /// <param name="backBufferHeight">Its height.</param>
    /// <returns>Twenty floats: glow (offset, start, end), glow colour and alpha, mask range, outline colour and alpha,
    /// and the outline's four values ordered start0, end1, end0, start1.</returns>
    public float[] Constants(int backBufferWidth, int backBufferHeight)
    {
        float softStart = SoftStart;
        float softEnd = SoftEnd;
        float outlineStart1 = OutlineStart1;
        float outlineEnd1 = OutlineEnd1;

        if (ScaleEdges || ScaleOutline)
        {
            float scale = MathF.Max(0.5f, MathF.Max(1024f / backBufferWidth, 768f / backBufferHeight));

            if (ScaleEdges)
            {
                float mid = 0.5f * (softStart + softEnd);
                softStart = Math.Clamp(mid + (scale * (softStart - mid)), 0.05f, 0.99f);
                softEnd = Math.Clamp(mid + (scale * (softEnd - mid)), 0.05f, 0.99f);
            }

            if (ScaleOutline)
            {
                // "shrink the soft part of the outline, enlarging hard part" (:1026).
                float midStart = 0.5f * (outlineStart1 + OutlineStart0);
                outlineStart1 = Math.Clamp(midStart + (scale * (outlineStart1 - midStart)), 0.05f, 0.99f);
                float midEnd = 0.5f * (outlineEnd1 + OutlineEnd0);
                outlineEnd1 = Math.Clamp(midEnd + (scale * (outlineEnd1 - midEnd)), 0.05f, 0.99f);
            }
        }

        return
        [
            GlowX, GlowY, GlowStart, GlowEnd,
            GlowColor.Red, GlowColor.Green, GlowColor.Blue, GlowAlpha,
            softStart, softEnd, 0f, 0f,
            OutlineColor.Red, OutlineColor.Green, OutlineColor.Blue, OutlineAlpha,
            OutlineStart0, outlineEnd1, OutlineEnd0, outlineStart1,
        ];
    }

    private static bool Flag(Func<string, string?> value, string key) =>
        value(key) is { } text && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) && number != 0f;

    private static float Number(Func<string, string?> value, string key, float unset) =>
        value(key) is { } text && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : unset;

    /// <summary>A VEC3: `[r g b]` as written, or `{r g b}` in 0-255 (KeyValues' colour form).</summary>
    private static (float, float, float) Vector(Func<string, string?> value, string key)
    {
        if (value(key) is not { } text)
        {
            return (0f, 0f, 0f);
        }

        string trimmed = text.Trim();
        float divisor = trimmed.StartsWith('{') ? 255f : 1f;
        string[] parts = trimmed.Trim('[', ']', '{', '}').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float At(int index) =>
            index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                ? number / divisor
                : 0f;

        return (At(0), At(1), At(2));
    }
}
