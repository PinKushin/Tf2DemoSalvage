using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Render;

/// <summary>The fog constants the world shader reads, packed as Valve's shader API packs them (B139).</summary>
/// <remarks>
/// <c>CRendering3dView::EnableWorldFog</c> (viewrender.cpp:4708) hands the material system a colour in
/// gamma 0..1, a start, an end and a max density; the pixel shader reads <c>g_LinearFogColor</c>
/// (common_ps_fxc.h:45) and <c>fogParams</c> as <c>CalcRangeFog</c> unpacks them
/// (common_ps_fxc.h:250): x start/(end−start), y the water height, z max density, w 1/(end−start).
///
/// **The gamma-to-linear curve is the exact sRGB decode, read** (B653; <c>shaderapidx9.dll</c> <c>0x180025a50</c>, the c29
/// upload, converts on the CPU): under range fog with sRGB writes on, as every lit shader sets them, and under height fog
/// at the default <c>r_shader_srgb 0</c>, each channel goes through <c>0x18003ddf0</c>. <c>pow 2.2</c> is only the
/// height path at <c>r_shader_srgb 1</c>. Addresses: <c>docs/measurements/2026-11-02-fog-colour-curve-plan.md</c>.
///
/// **The colour c29 starts from is a BYTE** (fog-open-items plan): materialsystem's <c>FogColor3f</c>/<c>FogColor3fv</c>
/// (<c>0x18002bed0</c>/<c>0x18002bf40</c>, the queued context's vtable at <c>0x1800b6da8</c>) store
/// <c>clamp( (int)( x · 255 ), 0, 255 )</c>, truncated, and shaderapi's fog-mode applier (<c>0x180014200</c>) hands the
/// upload that byte times 1/255 (<see cref="StoredChannel"/>). **The LDR side is the same curve** (<c>0x180025a50</c> has
/// no HDR branch but the tone-map multiply, which only <c>HDR_TYPE_INTEGER</c> takes). The outer gate is DX level 90 or
/// more; the range gate is a hardware-config caps bit and <c>r_pixelfog</c> (default 1, the ConVar at
/// <c>0x1800a2340</c>). The tone-map scale is applied in the shader (<c>debugModes2.y</c>), not here, and is 1 outside
/// integer HDR (<c>Device3D</c>).
/// </remarks>
public static class FogConstants
{
    /// <summary>The knee, <c>0x180082924</c> = <c>0x3D25AEE6</c>, 0.04045; the binary's compare sends the knee itself linear.</summary>
    private const float Knee = 0.04045f;

    /// <summary>1/12.92 as the binary multiplies by it, <c>0x180082930</c> = <c>0x3D9E8391</c>.</summary>
    private const float LinearSlope = 0.0773993805f;

    /// <summary>1/1.055, <c>0x18008294c</c> = <c>0x3F72A76F</c>.</summary>
    private const float CurveScale = 0.947867334f;

    /// <summary>0.055/1.055, <c>0x18008292c</c> = <c>0x3D55891A</c>.</summary>
    private const float CurveOffset = 0.0521327034f;

    /// <summary>The exponent, <c>0x180082988</c>, 2.4 as a double from a float.</summary>
    private const double CurveExponent = 2.4000000953674316;

    /// <summary>
    /// shaderapi's sRGB-to-linear (<c>0x18003ddf0</c>), constant for constant: clamp to 0..1 (MAXSS/MINSS), then
    /// <c>x ≤ 0.04045 ? x · (1/12.92) : pow( x · (1/1.055) + 0.055/1.055, 2.4 )</c>, the pow in double. The textbook sRGB
    /// curve, with no table. (Read 2026-11-02.)
    /// </summary>
    /// <param name="gamma">A fog colour channel as the material system holds it, gamma 0..1.</param>
    /// <returns>The linear channel c29 carries.</returns>
    public static float SrgbDecode(float gamma)
    {
        float x = Math.Clamp(gamma, 0f, 1f);

        return x <= Knee ? x * LinearSlope : (float)Math.Pow((x * CurveScale) + CurveOffset, CurveExponent);
    }

    /// <summary>One over 255 as shaderapi multiplies a stored byte by it (<c>0x180014200</c>).</summary>
    private const float ByteScale = 1f / 255f;

    /// <summary><c>pow</c>'s exponent on the height branch at <c>r_shader_srgb 1</c>: 2.2 as a double from a float (<c>0x18007e0d8</c>).</summary>
    private const double ShaderSrgbExponent = 2.200000047683716;

    /// <summary>The fog byte <c>FogColor3fv</c> stores: <c>clamp( (int)( x · 255 ), 0, 255 )</c>, truncated (<c>0x18002bf40</c>).</summary>
    /// <param name="gamma">A fog colour channel, gamma 0..1.</param>
    /// <returns>The byte <c>GetFogColor</c> hands back.</returns>
    public static int StoredByte(float gamma) => Math.Clamp((int)(gamma * 255f), 0, 255);

    /// <summary>c29's raw channel before any conversion: the stored byte times 1/255 (<c>0x180014200</c>).</summary>
    /// <param name="gamma">A fog colour channel, gamma 0..1.</param>
    /// <returns>The channel the upload starts from.</returns>
    public static float StoredChannel(float gamma) => StoredByte(gamma) * ByteScale;

    /// <summary>
    /// The hardware-config query <c>0x180004bc0</c> (vtable <c>+0x1f0</c>, <c>NeedsShaderSRGBConversion</c>): 0 at
    /// <c>r_shader_srgb 0</c>, 1 at 1, and any other value a caps bit (<c>+0x5c0</c> bit 6). That bit is taken as 0, the
    /// hardware sRGB every DX9-class card offers (*interpolated*: the bit is not named).
    /// </summary>
    /// <param name="shaderSrgb"><c>r_shader_srgb</c>, default 0.</param>
    /// <returns>Whether shaderapi converts in the shader rather than by hardware sRGB.</returns>
    public static bool NeedsShaderSrgbConversion(int shaderSrgb) => shaderSrgb == 1;

    /// <summary>
    /// A height-fog (water) colour channel as c29 carries it (<c>0x180025b1f</c>-<c>0x180025be7</c>): the stored byte, then
    /// the sRGB decode, or <c>pow( x, 2.2 )</c> in double when <see cref="NeedsShaderSrgbConversion"/>. No sRGB-write test
    /// on this branch, so a modulating draw takes it too.
    /// </summary>
    /// <param name="gamma">A fog colour channel, gamma 0..1.</param>
    /// <param name="shaderSrgb"><c>r_shader_srgb</c>.</param>
    /// <returns>The linear channel.</returns>
    public static float HeightFogChannel(float gamma, int shaderSrgb)
    {
        float stored = StoredChannel(gamma);

        return NeedsShaderSrgbConversion(shaderSrgb) ? (float)Math.Pow(stored, ShaderSrgbExponent) : SrgbDecode(stored);
    }

    /// <summary>Two float4s: linear colour with w = 1 when fog is on, then the range parameters.</summary>
    /// <param name="fog">The fog in force, or null for <c>MATERIAL_FOG_NONE</c>.</param>
    /// <param name="distanceScale">
    /// What the distances are multiplied by — one for the main view, <c>1 / m_skybox3d.scale</c> for
    /// the 3D skybox (viewrender.cpp:4824).
    /// </param>
    /// <returns>Eight floats; all zero when there is no fog.</returns>
    public static float[] For(SceneFog? fog, float distanceScale = 1f)
    {
        if (fog is not { } on)
        {
            return new float[8];
        }

        float start = on.Start * distanceScale;
        float range = (on.End * distanceScale) - start;

        return
        [
            SrgbDecode(StoredChannel(on.Red)),
            SrgbDecode(StoredChannel(on.Green)),
            SrgbDecode(StoredChannel(on.Blue)),

            // Which PIXEL_FOG_TYPE the shader runs (common_ps_fxc.h:69-71): 1 range, 2 radial, and
            // 0 — every float of a null fog — off.
            on.Radial ? 2f : 1f,
            start / range,
            0f,
            on.MaxDensity,
            1f / range,
        ];
    }
}
