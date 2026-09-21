using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>A line segment flying down a fixed path — `CFXDiscreetLine`, what a `"Tracer"` dispatch draws (B415).</summary>
/// <param name="Origin">`m_vecOrigin`: where the head starts.</param>
/// <param name="Direction">`m_vecDirection`, unit length.</param>
/// <param name="Velocity">`m_fVelocity`, units a second.</param>
/// <param name="Length">`m_fLength`: how far the tail trails the head.</param>
/// <param name="ClipLength">`m_fClipLength`: where the path ends; 0 for never.</param>
/// <param name="Scale">`m_fScale`: the half-width.</param>
/// <param name="Life">`m_fLife`: seconds until `IsActive` drops it.</param>
public readonly record struct DiscreetLine(
    Vector3 Origin, Vector3 Direction, float Velocity, float Length, float ClipLength, float Scale, float Life);

/// <summary>`FX_Tracer`'s line and `CFXDiscreetLine::Draw` with `tracer_extra` at its default of 1 (B415).</summary>
/// <remarks>
/// A tracer here is not a particle system. `TracerCallback` (`fx_tracer.cpp:77`) calls `FX_Tracer`, which adds a
/// client-side effect: two quads from the tail to the head, a core and an outline twice as wide at a quarter of the
/// brightness, widened to half a pixel and faded when the distance would make them thinner.
/// </remarks>
public static class DiscreetLines
{
    /// <summary>The material `FX_Tracer` draws with.</summary>
    public const string TracerMaterial = "effects/spark";

    /// <summary>`TRACER_SPEED`, the velocity when a dispatch's `m_flScale` is zero.</summary>
    public const float TracerSpeed = 5000f;

    /// <summary>`FX_Tracer`: the line a tracer from <paramref name="start"/> to <paramref name="end"/> flies as.</summary>
    /// <param name="start">Where the bullet left.</param>
    /// <param name="end">Where it stopped.</param>
    /// <param name="velocity">Units a second.</param>
    /// <param name="random">`random->RandomFloat`.</param>
    /// <returns>The line, or null for a trip under 256 units — "Don't make short tracers."</returns>
    public static DiscreetLine? Tracer(Vector3 start, Vector3 end, float velocity, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Vector3 delta = end - start;
        float distance = delta.Length();

        if (distance < 256f)
        {
            return null;
        }

        float length = random(64f, 128f);

        // "We want the tail to finish its run as well."
        return new DiscreetLine(
            start, delta / distance, velocity, length, distance, random(0.75f, 0.9f), (distance + length) / velocity);
    }

    /// <summary>`CFXDiscreetLine::Draw` at an age, its two quads appended as corners.</summary>
    /// <param name="line">The line.</param>
    /// <param name="age">Seconds since it was added: `m_fStartTime` after this frame's `Update`.</param>
    /// <param name="viewOrigin">`CurrentViewOrigin`.</param>
    /// <param name="viewForward">`CurrentViewForward`.</param>
    /// <param name="halfScreenWidth">Half the viewport's width in pixels.</param>
    /// <param name="corners">Receives four corners per quad: tail left, tail right, head right, head left.</param>
    /// <returns>Whether anything was drawn.</returns>
    public static bool Draw(
        in DiscreetLine line,
        float age,
        Vector3 viewOrigin,
        Vector3 viewForward,
        float halfScreenWidth,
        ICollection<DetailSpriteVertex> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);

        // `IsActive` is `m_fLife > 0`.
        if (age >= line.Life)
        {
            return false;
        }

        float head = MathF.Max(0f, line.Velocity * age);
        float tail = MathF.Max(0f, head - line.Length);

        if (head <= 0f)
        {
            return false;
        }

        if (line.ClipLength > 0f)
        {
            head = MathF.Min(head, line.ClipLength);
            tail = MathF.Min(tail, line.ClipLength);
        }

        float offset = MathF.Abs(head - tail) / (line.Length > 0f ? line.Length : 0.01f);

        Vector3 end = line.Origin + (line.Direction * head);
        Vector3 start = line.Origin + (line.Direction * tail);
        Vector3 cross = Vector3.Normalize(Vector3.Cross(end - start, end - viewOrigin));

        float z = Vector3.Dot(viewForward, start - viewOrigin);
        float screenWidth = line.Scale * halfScreenWidth / z;
        float alpha = 1f;
        float scale = line.Scale;

        if (screenWidth < 0.5f)
        {
            // `RemapVal( w, 0.25, 2, 0.3, 1 )`, clamped to [0.25, 1]; then half a pixel wide at this depth.
            alpha = Math.Clamp(0.3f + ((screenWidth - 0.25f) / (2f - 0.25f) * (1f - 0.3f)), 0.25f, 1f);
            scale = 0.5f * z / halfScreenWidth;
        }

        // `float color = (int) 255.0f * flAlpha` casts the 255, not the product; `Color4ub` truncates it.
        Quad(start, end, cross, scale, MathF.Floor(255f * alpha) / 255f, offset, corners);
        Quad(start, end, cross, scale * 2f, MathF.Floor(64f * alpha) / 255f, offset, corners);

        return true;
    }

    private static void Quad(
        Vector3 start, Vector3 end, Vector3 cross, float scale, float colour, float offset, ICollection<DetailSpriteVertex> corners)
    {
        Corner(start - (cross * scale), 1f, 0f, colour, corners);
        Corner(start + (cross * scale), 0f, 0f, colour, corners);
        Corner(end + (cross * scale), 0f, offset, colour, corners);
        Corner(end - (cross * scale), 1f, offset, colour, corners);
    }

    private static void Corner(Vector3 at, float u, float v, float colour, ICollection<DetailSpriteVertex> corners) =>
        corners.Add(new DetailSpriteVertex(at.X, at.Y, at.Z, u, v, colour, colour, colour, 1f, u, v, 0f));
}
