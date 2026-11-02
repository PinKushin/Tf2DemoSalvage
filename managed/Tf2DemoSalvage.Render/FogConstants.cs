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
/// **Not modelled:** <c>r_shader_srgb 1</c>, under which height fog takes <c>pow 2.2</c>; the default 0 is taken. **Not
/// read:** the LDR side of the upload (HDR type none, <c>mat_hdr_level 0</c>) and the range branch's gate
/// (hardware-config <c>+0x1c0</c>, the ConVar at <c>0x1800a2378</c>). Only the integer-HDR path TF2 runs by default was
/// traced. The tone-map scale is applied in the shader (<c>debugModes2.y</c>), not here.
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
            SrgbDecode(on.Red),
            SrgbDecode(on.Green),
            SrgbDecode(on.Blue),

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
