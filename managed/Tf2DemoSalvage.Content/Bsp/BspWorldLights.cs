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
/// <param name="Radius">Cutoff distance, beyond which the light contributes nothing. Zero means
/// no cutoff, and the falloff alone decides where it stops mattering.</param>
/// <param name="StopDot">Cosine at which a spotlight's penumbra begins.</param>
/// <param name="StopDot2">Cosine at which it ends; outside this the spotlight is dark.</param>
/// <param name="Exponent">Shapes the penumbra falloff between the two cosines.</param>
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
    float Exponent = 0f);

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
    public static IReadOnlyList<BspWorldLight> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> lump = BspLumpData.Read(file, header.Lump(BspLumpIndex.WorldLights)).Span;

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

            lights.Add(new BspWorldLight(
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
                Float(entry, BspStructLayout.WorldLightExponentOffset)));
        }

        DecodeLog.Note(
            "assets",
            $"{lights.Count} world lights, {lights.Count(light => light.Kind == WorldLightKind.SkyLight)} of them sky");

        return lights;
    }

    /// <summary>The map's sun, when it has one.</summary>
    /// <param name="lights">Every light, as <see cref="Read"/> returned them.</param>
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
