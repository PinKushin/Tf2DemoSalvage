using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene;

/// <summary>The view a beam is drawn for — <c>CurrentViewOrigin</c> and its three axes.</summary>
/// <param name="Origin">Where the camera is.</param>
/// <param name="Forward">Its forward axis.</param>
/// <param name="Right">Its right axis.</param>
/// <param name="Up">Its up axis.</param>
/// <remarks>
/// **One view, because this viewer draws one.** The engine reads <c>CurrentView*</c> in the strip builders and
/// <c>MainView*</c> in <c>CalcSegOrigin</c> (`beamdraw.cpp:453`); they differ only inside a reflection or a monitor
/// view, which beams are not drawn into here.
/// </remarks>
public readonly record struct BeamView(Vector3 Origin, Vector3 Forward, Vector3 Right, Vector3 Up);

/// <summary>
/// The beam geometry of <c>game/client/beamdraw.cpp</c>, and the two noise generators of <c>view_beams.cpp</c> —
/// everything a beam is drawn with between choosing its points and handing them to <see cref="BeamSegDraw"/>.
/// </summary>
/// <remarks>
/// **Only what an ENTITY beam can reach is here.** <c>CViewRenderBeams::DrawBeam( C_Beam* )</c> maps the five entity
/// types onto <c>TE_BEAMPOINTS</c>, <c>TE_BEAMLASER</c> and <c>TE_BEAMSPLINE</c> (`view_beams.cpp:2280-2338`), so
/// <c>DrawSegs</c>, <c>DrawSplineSegs</c> and <c>DrawHalo</c> are the whole of what it draws. <c>DrawTeslaSegs</c>,
/// <c>DrawDisk</c>, <c>DrawCylinder</c>, <c>DrawRing</c> and <c>DrawBeamFollow</c> are reached only by temp-entity
/// beams, and not one <c>C_TEBaseBeam</c> arrives in any of the 59 corpus demos (`entity-census`, docs/findings/72).
/// </remarks>
public static class BeamDraw
{
    /// <summary><c>NOISE_DIVISIONS</c> — the noise table is this many plus one floats (`beamdraw.h:19`).</summary>
    public const int NoiseDivisions = 128;

    /// <summary><c>FBEAM_SINENOISE</c>.</summary>
    public const int SineNoiseFlag = 0x10;

    /// <summary><c>FBEAM_SHADEIN</c>.</summary>
    public const int ShadeInFlag = 0x40;

    /// <summary><c>FBEAM_SHADEOUT</c>.</summary>
    public const int ShadeOutFlag = 0x80;

    /// <summary><c>FBEAM_NOTILE</c>.</summary>
    public const int NoTileFlag = 0x200;

    /// <summary>Fractal noise over a power-of-two span — <c>Noise</c> (`view_beams.cpp:186`).</summary>
    /// <param name="noise">The table, of which <c>[0]</c> and <c>[divs]</c> are the fixed ends.</param>
    /// <param name="divs">The span, halved at each level.</param>
    /// <param name="scale">The level's amplitude; each level below takes half.</param>
    /// <param name="random"><c>beamRandom</c>, a <c>CUniformRandomStream</c>.</param>
    /// <remarks>
    /// <code>
    /// noise[ div2 ] = (noise[0] + noise[divs]) * 0.5 + scale * beamRandom.RandomFloat(-1, 1);
    /// if ( div2 &gt; 1 ) { Noise( &amp;noise[div2], div2, scale * 0.5 ); Noise( noise, div2, scale * 0.5 ); }
    /// </code>
    ///
    /// **The upper half is filled FIRST**, which decides which random number lands where.
    /// </remarks>
    public static void Noise(Span<float> noise, int divs, float scale, UniformRandomStream random)
    {
        ArgumentNullException.ThrowIfNull(random);

        int half = divs >> 1;

        if (divs < 2)
        {
            return;
        }

        noise[half] = (float)(((noise[0] + noise[divs]) * 0.5) + (scale * random.RandomFloat(-1f, 1f)));

        if (half > 1)
        {
            Noise(noise[half..], half, scale * 0.5f, random);
            Noise(noise, half, scale * 0.5f, random);
        }
    }

    /// <summary>Half a sine wave across the table — <c>SineNoise</c> (`view_beams.cpp:204`).</summary>
    /// <param name="noise">The table.</param>
    /// <param name="divs">How many entries to fill, from zero.</param>
    public static void SineNoise(Span<float> noise, int divs)
    {
        float frequency = 0f;
        float step = (float)(Math.PI / divs);

        for (int index = 0; index < divs; index++)
        {
            noise[index] = (float)Math.Sin(frequency);
            frequency += step;
        }
    }

    /// <summary>A straight beam as a strip — <c>DrawSegs</c> (`beamdraw.cpp:223-416`).</summary>
    /// <param name="noise">The beam's noise table, <see cref="NoiseDivisions"/> + 1 long.</param>
    /// <param name="source">Where it starts.</param>
    /// <param name="delta">From start to end.</param>
    /// <param name="startWidth">Its width at the start, half of what the strip spans.</param>
    /// <param name="endWidth">Its width at the end.</param>
    /// <param name="scale">The noise amplitude.</param>
    /// <param name="frequency">The beam's <c>freq</c>, which scrolls the texture and phases sine noise.</param>
    /// <param name="speed">The scroll speed.</param>
    /// <param name="segments">How many points to place before the overlap and noise corrections.</param>
    /// <param name="flags">The <c>FBEAM_*</c> bits.</param>
    /// <param name="colour">The colour before shading, 0 to 1.</param>
    /// <param name="fadeLength">How far a shade runs, in world units.</param>
    /// <param name="view">The camera.</param>
    /// <param name="into">Where the strip's points go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>
    /// **The width doubles on the way into the strip**: <c>curSeg.m_flWidth = startWidth * 2</c>, and
    /// <c>CBeamSegDraw</c> reaches half of that either side — so a beam's "width" is its half-width (`:398-405`).
    ///
    /// **Too many segments for the width are cut back**, so a wide beam does not fold over itself:
    /// <c>if ( length*div &lt; flMaxWidth * 1.414 ) segments = (int)(length / (flMaxWidth * 1.414)) + 1</c>.
    ///
    /// **Shading is a brightness on the vertex colour and nothing else** — <c>VectorScale( color, brightness,
    /// curSeg.m_vColor )</c> with alpha held at one. Whether that colour ever reaches a pixel is the MATERIAL's question:
    /// the Sprite shader's additive mode ignores vertex colour unless the material says otherwise.
    /// </remarks>
    public static void DrawSegs(
        ReadOnlySpan<float> noise,
        Vector3 source,
        Vector3 delta,
        float startWidth,
        float endWidth,
        float scale,
        float frequency,
        float speed,
        int segments,
        int flags,
        Vector3 colour,
        float fadeLength,
        in BeamView view,
        ICollection<BeamSegment> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (segments < 2)
        {
            return;
        }

        float length = delta.Length();
        float maximumWidth = MathF.Max(startWidth, endWidth) * 0.5f;
        float div = (float)(1.0 / (segments - 1));

        if (length * div < maximumWidth * 1.414)
        {
            // Too many segments: they would overlap, so take fewer.
            segments = (int)(length / (maximumWidth * 1.414)) + 1;

            if (segments < 2)
            {
                segments = 2;
            }
        }

        if (segments > NoiseDivisions)
        {
            segments = NoiseDivisions;
        }

        div = (float)(1.0 / (segments - 1));
        length *= 0.01f;

        float vStep = (flags & NoTileFlag) != 0 ? div : length * div;
        float vLast = Fmod(frequency * speed);

        if ((flags & SineNoiseFlag) != 0)
        {
            if (segments < 16)
            {
                segments = 16;
                div = (float)(1.0 / (segments - 1));
            }

            scale *= 100f;
            length = (float)(segments * (1.0 / 10));
        }
        else
        {
            scale *= length;
        }

        int noiseStep = (int)((NoiseDivisions - 1) * div * 65536.0f);
        int noiseIndex = 0;

        float brightness = (flags & ShadeInFlag) != 0 ? 0f : 1f;

        // `fadeFraction = clamp( fadeLength / delta.Length(), 1.e-6f, 1.f )` — "This code generates NANs when
        // fadeFraction is zero!", Valve's own note, hence the floor.
        float fadeFraction = Math.Clamp(fadeLength / delta.Length(), 1e-6f, 1f);

        Vector3 perpendicular = Perpendicular(delta, view.Forward);

        for (int index = 0; index < segments; index++)
        {
            float fraction = index * div;

            brightness = Shade(flags, fraction, fadeFraction, brightness);

            Vector3 position = source + (fraction * delta);

            if (scale != 0f)
            {
                float factor = noise[noiseIndex >> 16] * scale;

                if ((flags & SineNoiseFlag) != 0)
                {
                    (float sine, float cosine) = MathF.SinCos((fraction * MathF.PI * length) + frequency);

                    position += factor * sine * view.Up;

                    // Rotate the noise along the perpendicular axis a bit to keep the bolt from looking diagonal.
                    position += factor * cosine * view.Right;
                }
                else
                {
                    position += factor * perpendicular;
                }
            }

            // `endWidth == startWidth` is an exact compare in the engine, kept exact here.
            float width = Same(startWidth, endWidth)
                ? startWidth * 2f
                : ((fraction * (endWidth - startWidth)) + startWidth) * 2f;

            into.Add(new BeamSegment(position, colour * brightness, vLast, width, 1f));

            vLast += vStep;
            noiseIndex += noiseStep;
        }
    }

    /// <summary>A beam through several points on a Catmull-Rom curve — <c>DrawSplineSegs</c> (`beamdraw.cpp:657-967`).</summary>
    /// <param name="noise">The noise table.</param>
    /// <param name="attachments">The points, at least two.</param>
    /// <param name="startWidth">The width at the first point.</param>
    /// <param name="endWidth">The width the last span tends to.</param>
    /// <param name="scale">The noise amplitude, which the engine compounds span by span.</param>
    /// <param name="frequency">The beam's <c>freq</c>.</param>
    /// <param name="speed">The scroll speed.</param>
    /// <param name="segments">Points per span.</param>
    /// <param name="flags">The <c>FBEAM_*</c> bits.</param>
    /// <param name="colour">The colour, 0 to 1.</param>
    /// <param name="view">The camera.</param>
    /// <param name="into">Where the strip's points go — one strip across every span.</param>
    /// <returns>Where the halo goes and its colour: the end of the last span, coloured as that span was.</returns>
    /// <remarks>
    /// **The shade is computed and thrown away, so its fade length is not an input here.** Inside the per-point loop
    /// the engine works out a fresh <c>brightness</c> from <c>fadeLength</c> for <c>FBEAM_SHADEIN</c> and
    /// <c>FBEAM_SHADEOUT</c>, but the segment's colour was set ONCE before the loop from the span's starting brightness
    /// and is never written again — so a spline beam is uniformly lit, or uniformly black under <c>FBEAM_SHADEIN</c>.
    /// Ported as it behaves.
    ///
    /// **`scale` compounds across spans**: each pass does <c>scale = scale * length</c> on the value the previous span
    /// left, not on the argument. Also kept.
    ///
    /// **The halo block inside the loop is <c>if (false &amp;&amp; pHaloMaterial)</c>** (`:918`) and is dead; only the halo
    /// at the end of the last span draws.
    /// </remarks>
    public static (Vector3 HaloAt, Vector3 HaloColour) DrawSplineSegs(
        ReadOnlySpan<float> noise,
        ReadOnlySpan<Vector3> attachments,
        float startWidth,
        float endWidth,
        float scale,
        float frequency,
        float speed,
        int segments,
        int flags,
        Vector3 colour,
        in BeamView view,
        ICollection<BeamSegment> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (segments < 2 || attachments.Length < 2)
        {
            return (attachments.Length > 0 ? attachments[^1] : Vector3.Zero, Vector3.Zero);
        }

        if (segments > NoiseDivisions)
        {
            segments = NoiseDivisions;
        }

        if ((flags & SineNoiseFlag) != 0 && segments < 16)
        {
            segments = 16;
        }

        int count = attachments.Length;
        float widthStep = Same(startWidth, endWidth) ? 0f : (endWidth - startWidth) / count;

        Vector3 end = attachments[0];
        Vector3 scaledColour = colour;

        for (int span = 0; span < count - 1; span++)
        {
            Vector3 before = span == 0 ? attachments[0] : attachments[span - 1];
            Vector3 start = attachments[span];

            end = attachments[span + 1];

            Vector3 after = span + 2 >= count - 1 ? attachments[span + 1] : attachments[span + 2];

            float length = (end - start).Length() * 0.01f;

            // Don't lose all of the noise/texture on short beams.
            if (length < 0.5f)
            {
                length = 0.5f;
            }

            float div = (float)(1.0 / (segments - 1));
            float vStep = length * div;
            float vLast = Fmod(frequency * speed);

            if ((flags & SineNoiseFlag) != 0)
            {
                scale *= 100f;
                length = (float)(segments * (1.0 / 10));
            }
            else
            {
                scale *= length;
            }

            int noiseStep = (int)((NoiseDivisions - 1) * div * 65536.0f);
            int noiseIndex = (flags & SineNoiseFlag) != 0 ? 0 : noiseStep;

            float brightness = (flags & ShadeInFlag) != 0 ? 0f : 1f;

            scaledColour = colour * brightness;

            float startSegmentWidth = startWidth + (widthStep * span);
            float endSegmentWidth = startWidth + (widthStep * (span + 1));

            for (int index = 1; index < segments; index++)
            {
                float fraction = index * div;
                Vector3 position = CatmullRom(before, start, end, after, fraction);

                if (scale != 0f)
                {
                    float factor = noise[noiseIndex >> 16] * scale;

                    if ((flags & SineNoiseFlag) != 0)
                    {
                        (float sine, float cosine) = MathF.SinCos((fraction * MathF.PI * length) + frequency);

                        position += factor * sine * view.Up;
                        position += factor * cosine * view.Right;
                    }
                    else
                    {
                        position += factor * view.Up;

                        // Rotate the noise along the perpendicular axis a bit to keep the bolt from looking diagonal.
                        factor = noise[noiseIndex >> 16] * scale * MathF.Cos((fraction * MathF.PI * 3f) + frequency);
                        position += factor * view.Right;
                    }
                }

                float width = Same(startWidth, endWidth)
                    ? startWidth * 2f
                    : ((fraction * (endSegmentWidth - startSegmentWidth)) + startSegmentWidth) * 2f;

                into.Add(new BeamSegment(position, scaledColour, vLast, width, 1f));

                vLast += vStep;
                noiseIndex += noiseStep;
            }
        }

        return (end, scaledColour);
    }

    /// <summary>A view-facing quad at a point — <c>DrawHalo</c> (`beamdraw.cpp:67-120`).</summary>
    /// <param name="source">Its centre.</param>
    /// <param name="scale">Half its size.</param>
    /// <param name="colour">Its colour; the alpha is one (<c>Color3fv</c>).</param>
    /// <param name="view">The camera.</param>
    /// <param name="into">Where the six corners go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <remarks>
    /// The corners and their texture coordinates in the engine's order — <c>(−up −right)</c> at <c>(0,1)</c>,
    /// <c>(+up −right)</c> at <c>(0,0)</c>, <c>(+up +right)</c> at <c>(1,0)</c>, <c>(−up +right)</c> at <c>(1,1)</c> — as two
    /// triangles. **Square, from the view's own axes**: unlike an entity sprite it has no orientation and no origin
    /// offset, and its size is the halo scale alone.
    /// </remarks>
    public static void DrawHalo(Vector3 source, float scale, Vector3 colour, in BeamView view, ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        Vector3 up = view.Up * scale;
        Vector3 right = view.Right * scale;

        DetailSpriteVertex bottomLeft = Corner(source - up - right, 0f, 1f, colour);
        DetailSpriteVertex topLeft = Corner(source + up - right, 0f, 0f, colour);
        DetailSpriteVertex topRight = Corner(source + up + right, 1f, 0f, colour);
        DetailSpriteVertex bottomRight = Corner(source - up + right, 1f, 1f, colour);

        into.Add(bottomLeft);
        into.Add(topLeft);
        into.Add(topRight);

        into.Add(bottomLeft);
        into.Add(topRight);
        into.Add(bottomRight);
    }

    /// <summary><c>Catmull_Rom_Spline</c> (`mathlib_base.cpp`), term by term as the engine sums them.</summary>
    public static Vector3 CatmullRom(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, float t)
    {
        float squared = t * t * 0.5f;
        float cubed = t * squared;

        t *= 0.5f;

        Vector3 output = Vector3.Zero;

        output += p1 * -cubed;
        output += p2 * (cubed * 3f);
        output += p3 * (cubed * -3f);
        output += p4 * cubed;

        output += p1 * (squared * 2f);
        output += p2 * (squared * -5f);
        output += p3 * (squared * 4f);
        output += p4 * -squared;

        output += p1 * -t;
        output += p3 * t;

        return output + p2;
    }

    /// <summary>
    /// <c>RemapVal</c> (`mathlib.h`): <c>val</c> carried from <c>[A, B]</c> to <c>[C, D]</c>, with the engine's own answer
    /// for an empty range.
    /// </summary>
    public static float RemapVal(float value, float a, float b, float c, float d)
    {
        if (Same(a, b))
        {
            return value >= b ? d : c;
        }

        return c + ((d - c) * (value - a) / (b - a));
    }

    /// <summary>The engine's exact float compare — the same value, bit for bit.</summary>
    internal static bool Same(float first, float second) =>
        BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);

    /// <summary><c>fmod( freq * speed, 1 )</c>: the float product, widened, its truncated remainder — C#'s <c>%</c> on doubles.</summary>
    private static float Fmod(float product) => (float)((double)product % 1.0);

    /// <summary>The <c>FBEAM_SHADEIN</c> / <c>FBEAM_SHADEOUT</c> brightness at a fraction of the beam, clamped.</summary>
    private static float Shade(int flags, float fraction, float fadeFraction, float brightness)
    {
        bool shadeIn = (flags & ShadeInFlag) != 0;
        bool shadeOut = (flags & ShadeOutFlag) != 0;

        if (shadeIn && shadeOut)
        {
            brightness = fraction < 0.5f
                ? 2f * (fraction / fadeFraction)
                : 2f * (1f - (fraction / fadeFraction));
        }
        else if (shadeIn)
        {
            brightness = fraction / fadeFraction;
        }
        else if (shadeOut)
        {
            brightness = 1f - (fraction / fadeFraction);
        }

        return Math.Clamp(brightness, 0f, 1f);
    }

    /// <summary><c>ComputeBeamPerpendicular</c>: the view's forward crossed with the beam's direction, normalized.</summary>
    private static Vector3 Perpendicular(Vector3 delta, Vector3 forward) =>
        SafeNormalize(Vector3.Cross(forward, SafeNormalize(delta)));

    /// <summary><c>VectorNormalize</c>, which leaves a zero vector zero rather than making it NaN.</summary>
    internal static Vector3 SafeNormalize(Vector3 vector)
    {
        float length = vector.Length();

        return length > 0f ? vector / length : Vector3.Zero;
    }

    /// <summary>A halo corner; <c>Color3fv</c> packs the colour to bytes like every other vertex colour.</summary>
    private static DetailSpriteVertex Corner(Vector3 at, float u, float v, Vector3 colour) =>
        new(
            at.X, at.Y, at.Z, u, v,
            BeamSegDraw.Packed(colour.X), BeamSegDraw.Packed(colour.Y), BeamSegDraw.Packed(colour.Z), 1f,
            u, v, 0f);
}
