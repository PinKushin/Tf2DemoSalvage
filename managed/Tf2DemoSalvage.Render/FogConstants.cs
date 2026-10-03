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
/// **Interpolated: the gamma-to-linear curve.** The conversion lives in the closed shader API. Its
/// string table names <c>m_bFogColorSpecifiedInLinearSpace</c>, so a gamma colour IS converted; the
/// curve used here is mathlib's <c>GammaToLinearFullRange</c>, <c>pow(x, 2.2)</c>
/// (color_conversion.cpp:264), which is Source's own and not the sRGB one.
/// </remarks>
public static class FogConstants
{
    /// <summary>Source's gamma, <c>GammaToLinearFullRange</c>.</summary>
    private const float Gamma = 2.2f;

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
            MathF.Pow(on.Red, Gamma),
            MathF.Pow(on.Green, Gamma),
            MathF.Pow(on.Blue, Gamma),
            1f,
            start / range,
            0f,
            on.MaxDensity,
            1f / range,
        ];
    }
}
