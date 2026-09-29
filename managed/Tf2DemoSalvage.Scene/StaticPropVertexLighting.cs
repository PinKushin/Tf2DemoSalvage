using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The colour mesh the engine builds on the CPU for an unbaked static prop compiled with <c>$staticprop</c> (B429).
/// </summary>
/// <remarks>
/// **Read from `engine.dll`.** `FUN_1800f36e0` builds a static prop's colour mesh: the `.vhv` colours when the prop has
/// static lighting and they load (<c>PropModels</c>), otherwise it lights each vertex here, once. Per mesh
/// `FUN_1800ee4a0` transforms the vertex position (`FUN_180279740`) and normal (`FUN_1802796c0`) by the prop's
/// <c>AngleMatrix(GetRenderAngles, GetRenderOrigin)</c> (`FUN_180276390` — no scale), calls
/// <c>IStudioRender::ComputeLighting</c> (vtable `+0x128`, <see cref="StudioPointLighting"/>) with the handle's static
/// lighting state (<see cref="LevelLighting.StaticPropLightingAt"/>), and stores each channel as
/// <c>round( lineartovertex[ clamp( round( light · 1024 ), 0, 4095 ) ] · 255 )</c>, a byte like a `.vhv` one.
/// <c>lineartovertex</c> is `BuildGammaTable( 2.2, 2.2, 0, 2 )`'s (`0x180279a40`, from `0x1800d7890` and
/// `0x18012ecf0`): <c>min( 1, pow( i / 1024, 1 / 2.2 ) · 0.5 )</c> — `mathlib/color_conversion.cpp:248`.
///
/// Under <c>STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT</c> (`0x2000`) the engine calls
/// <c>ComputeLightingConstDirectional</c> (`+0x130`) with <c>constdirectionallightdot / 255</c> instead
/// (<see cref="ConstantDot"/>, the <c>constantDot</c> of <see cref="StudioPointLighting.At"/>).
/// </remarks>
public static class StaticPropVertexLighting
{
    /// <summary><c>mat_monitorgamma</c>'s 2.2, the gamma `BuildGammaTable` is called with (`0x400ccccd`).</summary>
    private const float Gamma = 2.2f;

    /// <summary><c>overbright == 2</c>: the table stores half the light, and the vertex-lit shader doubles it back.</summary>
    private const float OverbrightFactor = 0.5f;

    /// <summary><c>lineartovertex</c>: 4096 entries covering linear 0 to 4 in steps of 1/1024.</summary>
    private static readonly float[] LinearToVertex = BuildLinearToVertex();

    /// <summary>Whether the engine lights this model's colour mesh on the CPU: compiled static.</summary>
    /// <param name="studioFlags">The model's <c>studiohdr_t.flags</c>.</param>
    /// <returns>True when <see cref="Colours"/> is the engine's answer.</returns>
    /// <remarks>
    /// `FUN_1800eac60` builds a colour mesh only when <c>flags &amp; STUDIOHDR_FLAGS_STATIC_PROP</c>; a model without it
    /// is lit every frame (`CStaticProp::Init` `0x1802052c0` warns <c>used as a static prop, but not compiled as a static
    /// prop</c>).
    /// </remarks>
    public static bool Lights(int studioFlags) => (studioFlags & Content.Assets.StudioModelFlags.StaticProp) != 0;

    /// <summary><c>STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT</c>, `studio.h:2073`.</summary>
    public const int ConstantDirectionalLightDot = Content.Assets.StudioModelFlags.ConstantDirectionalLightDot;

    /// <summary>`FUN_1800ee4a0`'s choice: <c>constdirectionallightdot / 255</c> under flag `0x2000`, else null (N·L).</summary>
    /// <param name="studioFlags">The model's <c>studiohdr_t.flags</c>.</param>
    /// <param name="dot">Its <c>constdirectionallightdot</c> byte.</param>
    /// <returns>The dot every light takes, or null.</returns>
    public static float? ConstantDot(int studioFlags, byte dot) =>
        (studioFlags & ConstantDirectionalLightDot) != 0 ? dot / 255f : null;

    /// <summary>One channel of light as the colour mesh stores it: `FUN_1800ee4a0`'s conversion.</summary>
    /// <param name="linear">The linear light <see cref="StudioPointLighting.At"/> returned.</param>
    /// <returns>The byte.</returns>
    public static byte ToByte(float linear)
    {
        // `(uint)ROUND(x * 1024)` then `0xfff < u`: an unsigned compare, so a negative clamps to 0 and a large one to 0xfff.
        int index = (int)Math.Clamp(MathF.Round(linear * 1024f), 0f, 4095f);

        return (byte)MathF.Round(LinearToVertex[index] * 255f);
    }

    /// <summary>A placement's colour mesh, one colour per corner in the order given, as the model draw binds it.</summary>
    /// <param name="corners">The model's corners, in its own space.</param>
    /// <param name="placement">The prop's origin and angles; its scale is not applied, as `FUN_180276390` applies none.</param>
    /// <param name="lighting">The handle's static state (<see cref="LevelLighting.StaticPropLightingAt"/>).</param>
    /// <param name="sun">The sun where it reaches the handle's origin, or null.</param>
    /// <param name="constantDot"><see cref="ConstantDot"/>'s answer for the model.</param>
    /// <returns>Red, green and blue per corner, through the same overbright as a `.vhv` byte.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null.</exception>
    public static float[] Colours(
        IReadOnlyList<PropVertex> corners, PropTransform placement, PointLighting lighting, SunLight? sun, float? constantDot = null)
    {
        ArgumentNullException.ThrowIfNull(corners);

        float[] colours = new float[corners.Count * 3];

        for (int at = 0; at < corners.Count; at++)
        {
            PropVertex corner = corners[at];
            (float x, float y, float z) = placement.Apply(corner.X, corner.Y, corner.Z);
            (float nx, float ny, float nz) = placement.Rotate(corner.NormalX, corner.NormalY, corner.NormalZ);

            Vector3 light = StudioPointLighting.At(lighting, sun, new Vector3(x, y, z), new Vector3(nx, ny, nz), constantDot);

            colours[at * 3] = PropModels.FromVertexByte(ToByte(light.X));
            colours[(at * 3) + 1] = PropModels.FromVertexByte(ToByte(light.Y));
            colours[(at * 3) + 2] = PropModels.FromVertexByte(ToByte(light.Z));
        }

        return colours;
    }

    private static float[] BuildLinearToVertex()
    {
        float[] table = new float[4096];

        for (int i = 0; i < table.Length; i++)
        {
            // `(float)pow(...) * fVar12` in single precision, then clamped (`0x180279a40`).
            table[i] = Math.Min(1f, (float)Math.Pow(i / 1024.0, 1.0 / Gamma) * OverbrightFactor);
        }

        return table;
    }
}
