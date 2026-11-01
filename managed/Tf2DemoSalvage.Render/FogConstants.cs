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
/// </remarks>
public static class FogConstants
{
    /// <summary>shaderapi's sRGB-to-linear (<c>0x18003ddf0</c>): clamped to 0..1, then the exact sRGB curve.</summary>
    /// <param name="gamma">A fog colour channel as the material system holds it, gamma 0..1.</param>
    /// <returns>The linear channel c29 carries.</returns>
    public static float SrgbDecode(float gamma)
    {
        float x = Math.Clamp(gamma, 0f, 1f);

        return x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
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
