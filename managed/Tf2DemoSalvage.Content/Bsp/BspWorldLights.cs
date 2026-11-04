using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Diagnostics;

using static Tf2DemoSalvage.Content.Bsp.BspStructLayout;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>What kind of light a world light is.</summary>
/// <remarks>
/// Valve's <c>emittype_t</c> from <c>bspfile.h</c>, in its own order — the value is stored as an
/// integer in the lump, so the order is the format rather than a choice.
/// </remarks>
public enum WorldLightKind
{
    /// <summary>A surface emitting light, treated as a 90 degree spotlight.</summary>
    Surface = 0,

    /// <summary>A point light with falloff.</summary>
    Point,

    /// <summary>A spotlight with a penumbra.</summary>
    Spotlight,

    /// <summary>The sun: directional, no falloff, and only where the sky is visible.</summary>
    SkyLight,

    /// <summary>A quake-style light, kept for completeness.</summary>
    QuakeLight,

    /// <summary>The sky's ambient contribution.</summary>
    SkyAmbient,
}

/// <summary>One light the map compiler recorded.</summary>
/// <param name="Origin">Where it is, for the lights that have a position.</param>
/// <param name="Intensity">Its colour and strength, in linear light.</param>
/// <param name="Normal">Which way it points; the direction the sun travels.</param>
/// <param name="Kind">Which sort of light it is.</param>
/// <param name="ConstantAttenuation">First falloff term; see the remarks.</param>
/// <param name="LinearAttenuation">Second falloff term, multiplied by distance.</param>
/// <param name="QuadraticAttenuation">Third falloff term, multiplied by distance squared.</param>
/// <param name="Radius">The distance <see cref="LocalLights"/>' admission tests against a box. A lump's 0 is replaced at load
/// (<see cref="BspWorldLights.AsLoaded"/>); one still 0 after that is refused from any box it stands outside.</param>
/// <param name="StopDot">Cosine at which a spotlight's penumbra begins.</param>
/// <param name="StopDot2">Cosine at which it ends; outside this the spotlight is dark.</param>
/// <param name="Exponent">Shapes the penumbra falloff between the two cosines.</param>
/// <param name="Style">`dworldlight_t.style`: the light style it answers to, 0 for always on.</param>
/// <param name="Cluster">`dworldlight_t.cluster`: the PVS cluster vrad placed it in; −1 when not read.</param>
/// <param name="Flags">`dworldlight_t.flags`: the `DWL_FLAGS_` bits.</param>
/// <param name="DynamicSlot">
/// Which converted dlight slot this is, −1 for a light of the map. The engine writes each dlight to one fixed entry of `DAT_180775730`
/// (`FUN_1801b9350`), so a dlight's identity is its slot, whatever the slot holds now (B676).
/// </param>
/// <param name="LumpIndex">Which entry of the lump this is, −1 for a light built by hand: a world light's identity, the engine's pointer (B676).</param>
/// <remarks>
/// **The falloff is Valve's, stated inline in <c>bspfile.h</c> rather than inferred:**
/// <c>1 / (constant_attn + linear_attn * dist + quadratic_attn * dist^2)</c>, for
/// <c>emit_spotlight</c> and <c>emit_point</c>.
///
/// These were not read before this, and their absence is why an indoor prop draws dark: a model
/// takes its leaf's ambient cube plus up to four local lights, and with no falloff terms there was
/// no way to evaluate a local light at all. The sun was the only direct light applied, so anything
/// out of daylight was lit by the bounce term alone.
/// </remarks>
public readonly record struct BspWorldLight(
    (float X, float Y, float Z) Origin,
    (float Red, float Green, float Blue) Intensity,
    (float X, float Y, float Z) Normal,
    WorldLightKind Kind,
    float ConstantAttenuation = 0f,
    float LinearAttenuation = 0f,
    float QuadraticAttenuation = 0f,
    float Radius = 0f,
    float StopDot = 0f,
    float StopDot2 = 0f,
    float Exponent = 0f,
    int Style = 0,
    int Cluster = -1,
    int Flags = 0,
    int DynamicSlot = -1,
    int LumpIndex = -1)
{
    /// <summary>`DWL_FLAGS_INAMBIENTCUBE` (`bspfile.h:966`): vrad put this light in the per-leaf ambient cubes.</summary>
    public bool InAmbientCube => (Flags & 1) != 0;
}

/// <summary>
/// The lights a map was compiled with, including its sun.
/// </summary>
/// <remarks>
/// **This is where the missing brightness is.** A model takes the ambient cube of its leaf, which
/// is bounced and sky light only — <c>istudiorender.h</c> describes the cube as "ambient, and
/// lights that aren't in locallight[]". The direct lights are these, and outdoors the one that
/// matters is <c>emit_skylight</c>: <c>bspfile.h</c> calls it a "directional light with no falloff
/// (surface must trace to SKY texture)".
///
/// Without it a health pack in daylight renders as though it were in shade, because it is being
/// lit by the shade term alone.
///
/// **The parenthesis in Valve's comment is the hard part.** A sky light reaches only what can see
/// the sky, so applying it everywhere lights the inside of every building. The engine answers that
/// in its light cache by tracing; this reader only reports what the map holds.
/// </remarks>
public static class BspWorldLights
{
    /// <summary>Reads every light the compiler recorded.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The lights, in file order; empty when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or the lump is malformed.</exception>
    public static IReadOnlyList<BspWorldLight> Read(ReadOnlyMemory<byte> file) => Read(file, BspHdr.Enabled);

    /// <summary><see cref="Read(ReadOnlyMemory{byte})"/> with HDR on or off stated — `mat_hdr_level` at least 2, or not (B537).</summary>
    /// <param name="file">The whole map file.</param>
    /// <param name="hdrEnabled">Whether the HDR type is not `HDR_TYPE_NONE`.</param>
    /// <returns>The lights, in file order; empty when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or the lump is malformed.</exception>
    public static IReadOnlyList<BspWorldLight> Read(ReadOnlyMemory<byte> file, bool hdrEnabled)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        // **`LUMP_WORLDLIGHTS_HDR` when HDR is on and the map has it**, as `CModelLoader_Map_LoadModel` (`engine.dll`
        // 0x1800ffc30) loads; vrad writes its intensities from each light's `_lightHDR`.
        ReadOnlySpan<byte> hdr = hdrEnabled ? BspLumpData.Read(file, header.Lump(BspLumpIndex.WorldLightsHdr)).Span : default;
        bool hdrLump = !hdr.IsEmpty;
        ReadOnlySpan<byte> lump = hdrLump ? hdr : BspLumpData.Read(file, header.Lump(BspLumpIndex.WorldLights)).Span;

        if (lump.IsEmpty)
        {
            DecodeLog.Lost(
                "assets", "the map carries no world lights, so nothing will receive direct light");

            return [];
        }

        List<BspWorldLight> lights = new(lump.Length / WorldLightStride);

        for (int at = 0; at + WorldLightStride <= lump.Length; at += WorldLightStride)
        {
            ReadOnlySpan<byte> entry = lump[at..];

            lights.Add(AsLoaded(new BspWorldLight(
                (Float(entry, 0), Float(entry, 4), Float(entry, 8)),
                (Float(entry, 12), Float(entry, 16), Float(entry, 20)),
                (Float(entry, 24), Float(entry, 28), Float(entry, 32)),
                (WorldLightKind)BinaryPrimitives.ReadInt32LittleEndian(entry[40..]),
                Float(entry, BspStructLayout.WorldLightConstantAttenuationOffset),
                Float(entry, BspStructLayout.WorldLightLinearAttenuationOffset),
                Float(entry, BspStructLayout.WorldLightQuadraticAttenuationOffset),
                Float(entry, BspStructLayout.WorldLightRadiusOffset),
                Float(entry, BspStructLayout.WorldLightStopDotOffset),
                Float(entry, BspStructLayout.WorldLightStopDot2Offset),
                Float(entry, BspStructLayout.WorldLightExponentOffset),
                BinaryPrimitives.ReadInt32LittleEndian(entry[BspStructLayout.WorldLightStyleOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[BspStructLayout.WorldLightClusterOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[BspStructLayout.WorldLightFlagsOffset..]),
                LumpIndex: lights.Count), hdrLump));
        }

        DecodeLog.Note(
            "assets",
            $"{lights.Count} world lights, {lights.Count(light => light.Kind == WorldLightKind.SkyLight)} of them sky");

        return lights;
    }

    /// <summary>A light as `Mod_LoadWorldlights` leaves it in memory: `engine.dll` `FUN_1801042d0`, once, at load.</summary>
    /// <param name="light">The light as the lump holds it.</param>
    /// <param name="hdrLump">Whether it came from `LUMP_WORLDLIGHTS_HDR`: `CModelLoader_Map_LoadModel` passes 1 for lump 0x36, 0 for lump 0xf.</param>
    /// <returns>The light with its terms and radius replaced.</returns>
    /// <remarks>
    /// A spot's and a point's all-zero attenuation becomes quadratic 1, and a spot's exponent of 0 becomes 1. **A surface light is not rewritten**:
    /// the loop has no case for it, and `FUN_1800efe40` never reads its terms. Then any radius under 1 goes to `FUN_1800cbdd0`, which keeps one
    /// that is not 0, and for 0 answers where the falloff reaches 1/66.67 of the intensity's length on the HDR lump, 1/33.33 on the other, or
    /// 2000 where no such distance exists. Every kind, so a surface light's is 2000.
    /// </remarks>
    public static BspWorldLight AsLoaded(BspWorldLight light, bool hdrLump)
    {
        if (light.Kind == WorldLightKind.Spotlight && light.Exponent == 0f)
        {
            light = light with { Exponent = 1f };
        }

        if (light is { Kind: WorldLightKind.Spotlight or WorldLightKind.Point, ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 0f })
        {
            light = light with { QuadraticAttenuation = 1f };
        }

        return light.Radius == 0f ? light with { Radius = ComputedRadius(light, hdrLump) } : light;
    }

    private static float ComputedRadius(BspWorldLight light, bool hdrLump)
    {
        float scale = hdrLump ? 66.66667f : 33.333336f;
        float intensity = MathF.Sqrt(
            (light.Intensity.Red * light.Intensity.Red) + (light.Intensity.Green * light.Intensity.Green) + (light.Intensity.Blue * light.Intensity.Blue));
        float linear = light.LinearAttenuation;
        float quadratic = light.QuadraticAttenuation;

        if (quadratic == 0f)
        {
            return linear != 0f ? ((scale * intensity) - light.ConstantAttenuation) / linear : 2000f;
        }

        float discriminant = (linear * linear) - ((light.ConstantAttenuation - (scale * intensity)) * quadratic * 4f);

        return discriminant >= 0f ? MathF.Max(0f, (MathF.Sqrt(discriminant) - linear) * (0.5f / quadratic)) : 2000f;
    }

    /// <summary>The map's sun, when it has one.</summary>
    /// <param name="lights">Every light, as <see cref="Read(ReadOnlyMemory{byte})"/> returned them.</param>
    /// <returns>The brightest sky light, or <c>null</c> when the map has none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lights"/> is null.</exception>
    /// <remarks>
    /// **The brightest, because a map may carry more than one.** <c>emit_skylight</c> is written
    /// for the sun and can also appear for sky ambience; taking the strongest picks the one that
    /// casts the shadows a player would call sunlight.
    ///
    /// An indoor map legitimately has none, and answers null rather than a black light — the
    /// difference between "no sun" and "a sun that contributes nothing" matters to whoever reads
    /// the log.
    /// </remarks>
    public static BspWorldLight? Sun(IReadOnlyList<BspWorldLight> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);

        BspWorldLight? brightest = null;
        float strongest = 0f;

        foreach (BspWorldLight light in lights)
        {
            if (light.Kind != WorldLightKind.SkyLight)
            {
                continue;
            }

            float strength =
                light.Intensity.Red + light.Intensity.Green + light.Intensity.Blue;

            if (strength > strongest)
            {
                strongest = strength;
                brightest = light;
            }
        }

        return brightest;
    }

    private static float Float(ReadOnlySpan<byte> entry, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(entry[at..]);
}
