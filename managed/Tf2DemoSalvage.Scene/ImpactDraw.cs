using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the legacy emitters' `RenderParticles` build, as sprite corners per material (B415).</summary>
/// <remarks>
/// - **`RenderParticle_ColorSizeAngle`** (`particle_util.h:303`): a square of half-width `size` in the view's right and
///   up, turned by `angle` in radians, colour and alpha rounded as `c · 254.9`. Simple and dust particles fade in over
///   16 to 64 units of view depth (`GetAlphaDistanceFade`, `CSimpleEmitter`'s near clip); flecks do not.
/// - **`Tracer_Draw`** (`c_tracer.cpp:92`): a strip from the particle along its velocity times its remaining length,
///   `width / 2` either side of the line the camera sees, clipped at the eye's plane.
/// - **`CFXQuad::Draw`** (`fx_quad.cpp`): a square lying on its surface, turned by its yaw, scaling and fading linearly.
/// </remarks>
public static class ImpactDraw
{
    /// <summary>`CSimpleEmitter`'s `m_flNearClipMin`.</summary>
    private const float NearClipMinimum = 16f;

    /// <summary>`m_flNearClipMax`.</summary>
    private const float NearClipMaximum = 64f;

    /// <summary>Adds every live particle and quad of an effect to its material's corners.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="eye">The camera's position.</param>
    /// <param name="forward">Its forward.</param>
    /// <param name="right">Its right.</param>
    /// <param name="up">Its up.</param>
    /// <param name="materialAlpha">A material's own `$alpha`.</param>
    /// <param name="into">Corners by material, added to.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Build(
        ImpactEffect effect,
        Vector3 eye,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        Func<string, float> materialAlpha,
        IDictionary<string, List<DetailSpriteVertex>> into)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(materialAlpha);
        ArgumentNullException.ThrowIfNull(into);

        foreach (ImpactEmitter emitter in effect.Emitters)
        {
            foreach (ImpactParticle particle in emitter.Particles)
            {
                List<DetailSpriteVertex> corners = Corners(into, particle.Material);
                float alpha = materialAlpha(particle.Material);

                if (emitter.Kind == ImpactEmitterKind.Trail)
                {
                    Trail(particle, eye, forward, alpha, corners);
                }
                else
                {
                    Sprite(emitter.Kind, particle, eye, forward, right, up, alpha, corners);
                }
            }
        }

        foreach (ImpactQuad quad in effect.Quads)
        {
            if (quad.Lifetime < quad.DieTime)
            {
                Quad(quad, materialAlpha(quad.Material), Corners(into, quad.Material));
            }
        }
    }

    private static List<DetailSpriteVertex> Corners(IDictionary<string, List<DetailSpriteVertex>> into, string material)
    {
        if (!into.TryGetValue(material, out List<DetailSpriteVertex>? corners))
        {
            into[material] = corners = [];
        }

        return corners;
    }

    /// <summary>A simple, dust or fleck particle: the kind's alpha and size, then `RenderParticle_ColorSizeAngle`.</summary>
    private static void Sprite(
        ImpactEmitterKind kind, ImpactParticle particle, Vector3 eye, Vector3 forward, Vector3 right, Vector3 up,
        float materialAlpha, List<DetailSpriteVertex> into)
    {
        float t = particle.Lifetime / particle.DieTime;
        float alpha;
        float size;

        switch (kind)
        {
            case ImpactEmitterKind.Fleck:
                alpha = 1f - t;
                size = particle.Size;
                break;
            case ImpactEmitterKind.Dust:
                alpha = DustAlpha(t) * Fade(particle.Position, eye, forward);
                size = Lerp(particle.StartSize, particle.EndSize, t);
                break;
            default:
                alpha = (particle.StartAlpha / 255f) + (((particle.EndAlpha / 255f) - (particle.StartAlpha / 255f)) * t);
                alpha *= Fade(particle.Position, eye, forward);
                size = Lerp(particle.StartSize, particle.EndSize, t);
                break;
        }

        if (alpha < 0.001f)
        {
            return;
        }

        (float sa, float ca) = MathF.SinCos(particle.Roll);
        float r = Byte(particle.Colour.R / 255f);
        float g = Byte(particle.Colour.G / 255f);
        float b = Byte(particle.Colour.B / 255f);
        float a = Byte(alpha) * materialAlpha;

        DetailSpriteVertex Corner(float across, float along, float u, float v)
        {
            Vector3 at = particle.Position + (right * (across * size)) + (up * (along * size));

            return new DetailSpriteVertex(at.X, at.Y, at.Z, u, v, r, g, b, a, u, v, 0f);
        }

        DetailSpriteVertex c0 = Corner(-ca + sa, -sa - ca, 0f, 1f);
        DetailSpriteVertex c1 = Corner(-ca - sa, -sa + ca, 0f, 0f);
        DetailSpriteVertex c2 = Corner(ca - sa, sa + ca, 1f, 0f);
        DetailSpriteVertex c3 = Corner(ca + sa, sa - ca, 1f, 1f);

        into.AddRange([c0, c1, c2, c0, c2, c3]);
    }

    /// <summary>`CTrailParticles::RenderParticles` and `Tracer_Draw`.</summary>
    private static void Trail(ImpactParticle particle, Vector3 eye, Vector3 forward, float materialAlpha, List<DetailSpriteVertex> into)
    {
        float scale = MathF.Max(0.01f, particle.Length * (1f - (particle.Lifetime / particle.DieTime)));
        Vector3 delta = particle.Velocity * scale;
        float width = MathF.Min(delta.Length(), particle.Width);
        Vector3 start = particle.Position;

        // `ClipTracer`: distances in front of the eye; a streak wholly behind is dropped, one crossing is cut.
        float near = Vector3.Dot(start - eye, forward);
        float far = near + Vector3.Dot(delta, forward);

        if (near <= 0f && far <= 0f)
        {
            return;
        }

        if (near <= 0f || far <= 0f)
        {
            float span = far - near;

            if (MathF.Abs(span) < 1e-3f)
            {
                return;
            }

            float fraction = -near / span;

            if (near <= 0f)
            {
                start += delta * fraction;
            }
            else
            {
                delta *= fraction;
            }
        }

        Vector3 normal = Vector3.Cross(delta, start - eye);
        float length = normal.LengthSquared();

        if (length < 1e-3f)
        {
            return;
        }

        normal *= 0.5f * width / MathF.Sqrt(length);

        (byte red, byte green, byte blue) = particle.Colour;
        float r = red / 255f;
        float g = green / 255f;
        float b = blue / 255f;
        float a = materialAlpha;

        DetailSpriteVertex v0 = Vertex(start - normal, 0f, 0f);
        DetailSpriteVertex v1 = Vertex(start + normal, 1f, 0f);
        DetailSpriteVertex v2 = Vertex(start - normal + delta, 0f, 1f);
        DetailSpriteVertex v3 = Vertex(start + normal + delta, 1f, 1f);

        // `MATERIAL_QUADS` in the order 0, 1, 3, 2.
        into.AddRange([v0, v1, v3, v0, v3, v2]);

        DetailSpriteVertex Vertex(Vector3 at, float u, float v) => new(at.X, at.Y, at.Z, u, v, r, g, b, a, u, v, 0f);
    }

    /// <summary>`CFXQuad::Draw`, with no bias and no colour fade.</summary>
    private static void Quad(ImpactQuad quad, float materialAlpha, List<DetailSpriteVertex> into)
    {
        float t = quad.Lifetime / quad.DieTime;
        float scale = quad.Scale.Start + ((quad.Scale.End - quad.Scale.Start) * t);
        float alpha = Math.Clamp(quad.Alpha.Start + ((quad.Alpha.End - quad.Alpha.Start) * t), 0f, 1f);

        if (alpha == 0f)
        {
            return;
        }

        (Vector3 across, Vector3 upward) = Perpendiculars(quad.Normal);
        float yaw = float.DegreesToRadians(quad.Yaw);
        float yaw90 = float.DegreesToRadians(quad.Yaw + 90f);
        Vector3 right = ((across * MathF.Cos(yaw)) - (upward * MathF.Sin(yaw))) * (scale * 0.5f);
        Vector3 up = ((across * MathF.Cos(yaw90)) - (upward * MathF.Sin(yaw90))) * (scale * 0.5f);
        float a = alpha * materialAlpha;

        DetailSpriteVertex v0 = Vertex(quad.Origin + right - up, 1f, 1f);
        DetailSpriteVertex v1 = Vertex(quad.Origin - right - up, 0f, 1f);
        DetailSpriteVertex v2 = Vertex(quad.Origin - right + up, 0f, 0f);
        DetailSpriteVertex v3 = Vertex(quad.Origin + right + up, 1f, 0f);

        into.AddRange([v0, v1, v2, v0, v2, v3]);

        DetailSpriteVertex Vertex(Vector3 at, float u, float v) => new(at.X, at.Y, at.Z, u, v, 1f, 1f, 1f, a, u, v, 0f);
    }

    /// <summary>`VectorVectors( forward, right, up )` (`mathlib_base.cpp`).</summary>
    private static (Vector3 Right, Vector3 Up) Perpendiculars(Vector3 forward)
    {
        // `forward[0] == 0 && forward[1] == 0`: pointing straight up or down.
        if (forward is { X: 0f, Y: 0f })
        {
            return (new Vector3(0f, -1f, 0f), new Vector3(-forward.Z, 0f, 0f));
        }

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        return (right, up);
    }

    /// <summary>`CDustParticle::UpdateAlpha`: `1 − t`, squared once it falls below 0.75.</summary>
    private static float DustAlpha(float t)
    {
        float ramp = 1f - t;

        return ramp < 0.75f ? ramp * ramp : ramp;
    }

    /// <summary>`GetAlphaDistanceFade` over `CSimpleEmitter`'s near clip.</summary>
    private static float Fade(Vector3 position, Vector3 eye, Vector3 forward)
    {
        float depth = Vector3.Dot(position - eye, forward);

        if (depth > NearClipMaximum)
        {
            return 1f;
        }

        return depth > NearClipMinimum ? (depth - NearClipMinimum) / (NearClipMaximum - NearClipMinimum) : 0f;
    }

    private static float Lerp(byte start, byte end, float t) => start + ((end - start) * t);

    /// <summary>`RoundFloatToInt( c · 254.9 )` back over 255, as the vertex colour carries it.</summary>
    private static float Byte(float value) => MathF.Round(value * 254.9f) / 255f;
}
