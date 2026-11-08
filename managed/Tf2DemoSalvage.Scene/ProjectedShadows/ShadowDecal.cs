using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>One corner of a shadow's polygon on a surface.</summary>
/// <param name="Position">Where it is drawn: on the surface, lifted 0.1 along its normal.</param>
/// <param name="Texture">u and v across the shadow (0 to 1) and the depth below the shadow's origin.</param>
public readonly record struct ShadowDecalVertex(Vector3 Position, Vector3 Texture);

/// <summary>
/// A render-to-texture shadow's polygon on one surface, and its fade — `engine.dll`'s shadow decal build (finding 107).
/// </summary>
/// <remarks>
/// **`FUN_1801238a0`, in its order**: the surface's corners into shadow texture space (`FUN_180128250`, nothing if no corner
/// is nearer than the max height), Sutherland–Hodgman clips to v &lt; 1 (`FUN_180120670`), v &gt; 0 (`FUN_18011fd50`),
/// u &gt; 0 (`FUN_18011ffc0`), u &lt; 1 (`FUN_1801203f0`) and depth &gt; 0 (`FUN_18011fbd0`), then each extra clip plane
/// keeping dot(normal, p) &lt; distance (`FUN_180120230`, the crossing by ray against plane, `FUN_18011fad0`). At least three
/// corners or nothing; each lifted 0.1 along the surface normal (`FUN_1801246f0`). Every comparison is strict, as the
/// engine's are, so a corner exactly on an edge is outside.
/// </remarks>
public static class ShadowDecal
{
    /// <summary>How far a decal is lifted off its surface, along the normal (`FUN_1801246f0`, the normal × 0.1).</summary>
    public const float Lift = 0.1f;

    /// <summary>The clip's two working polygons, kept per thread rather than made per call.</summary>
    [ThreadStatic]
    private static List<ShadowDecalVertex>? t_polygon;

    [ThreadStatic]
    private static List<ShadowDecalVertex>? t_spare;

    /// <summary>Cuts a shadow to a surface.</summary>
    /// <param name="corners">The surface's corners in the world, in winding order.</param>
    /// <param name="normal">The surface plane's normal.</param>
    /// <param name="shadow">The shadow.</param>
    /// <returns>The decal's corners, a convex fan; null when nothing of the shadow lands on the surface.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<ShadowDecalVertex>? Clip(IReadOnlyList<Vector3> corners, Vector3 normal, ShadowProjection shadow)
    {
        ArgumentNullException.ThrowIfNull(corners);
        ArgumentNullException.ThrowIfNull(shadow);

        List<ShadowDecalVertex> polygon = t_polygon ??= [];
        List<ShadowDecalVertex> spare = t_spare ??= [];
        bool nearEnough = false;

        // Whether every corner is outside one edge of the unit square or behind the origin plane: the clip against that
        // edge keeps nothing, so the answer is null without running the clips — the same answer, sooner (a shadow sweeps
        // whole displacements, hundreds of triangles, almost all of them outside it).
        bool allAbove = true;
        bool allBelow = true;
        bool allLeft = true;
        bool allRight = true;
        bool allBehind = true;

        polygon.Clear();

        for (int at = 0; at < corners.Count; at++)
        {
            Vector3 texture = shadow.WorldToTexture(corners[at]);
            nearEnough |= texture.Z < shadow.MaxHeight;
            allAbove &= 1f - texture.Y <= 0f;
            allBelow &= texture.Y <= 0f;
            allLeft &= texture.X <= 0f;
            allRight &= 1f - texture.X <= 0f;
            allBehind &= texture.Z <= 0f;
            polygon.Add(new ShadowDecalVertex(corners[at], texture));
        }

        if (!nearEnough || allAbove || allBelow || allLeft || allRight || allBehind)
        {
            return null;
        }

        ClipAxis(polygon, spare, static v => 1f - v.Texture.Y);
        ClipAxis(spare, polygon, static v => v.Texture.Y);
        ClipAxis(polygon, spare, static v => v.Texture.X);
        ClipAxis(spare, polygon, static v => 1f - v.Texture.X);
        ClipAxis(polygon, spare, static v => v.Texture.Z);
        (List<ShadowDecalVertex> current, List<ShadowDecalVertex> next) = (spare, polygon);

        for (int plane = 0; plane < shadow.ClipPlanes.Count; plane++)
        {
            (Vector3 planeNormal, float distance) = shadow.ClipPlanes[plane];
            ClipPlane(current, next, planeNormal, distance);
            (current, next) = (next, current);
        }

        if (current.Count < 3)
        {
            return null;
        }

        Vector3 lift = normal * Lift;
        ShadowDecalVertex[] decal = new ShadowDecalVertex[current.Count];

        for (int at = 0; at < current.Count; at++)
        {
            decal[at] = current[at] with { Position = current[at].Position + lift };
        }

        return decal;
    }

    /// <summary>
    /// A decal corner's color byte, all four channels the same — `FUN_180121970` with the render info of `0x180123830`.
    /// </summary>
    /// <param name="depth">The corner's depth below the shadow's origin.</param>
    /// <param name="falloffOffset">Where the fade starts (<see cref="ShadowProjection.FalloffStart"/>).</param>
    /// <param name="maxDistance">The shadow's max distance (<see cref="ShadowProjection.MaxHeight"/>).</param>
    /// <param name="falloffAmount">How far the fade goes, out of 255 (<see cref="ShadowProjection.MaxFalloffAmount"/>).</param>
    /// <param name="bias">The shadow's falloff bias, 0 to 255.</param>
    /// <returns>The byte the <c>Shadow</c> shader subtracts from the coverage.</returns>
    public static byte Fade(float depth, float falloffOffset, float maxDistance, float falloffAmount, int bias)
    {
        float range = maxDistance - falloffOffset;
        float oneOverRange = range > 0f ? 1f / range : 1f;
        float value = MathF.Max(depth - falloffOffset, 0f) * oneOverRange;
        value = (value * falloffAmount) + bias;

        return (byte)((int)MathF.Min(value, 255f) & 0xff);
    }

    /// <summary>One Sutherland–Hodgman pass keeping <c>inside(v) &gt; 0</c>, crossings by the inside measure (`FUN_18011fd50`).</summary>
    private static void ClipAxis(List<ShadowDecalVertex> polygon, List<ShadowDecalVertex> kept, Func<ShadowDecalVertex, float> inside)
    {
        kept.Clear();

        if (polygon.Count == 0)
        {
            return;
        }

        ShadowDecalVertex previous = polygon[^1];
        float previousSide = inside(previous);

        for (int at = 0; at < polygon.Count; at++)
        {
            ShadowDecalVertex vertex = polygon[at];
            float side = inside(vertex);

            if ((side > 0f) != (previousSide > 0f))
            {
                kept.Add(Lerp(previous, vertex, previousSide / (previousSide - side)));
            }

            if (side > 0f)
            {
                kept.Add(vertex);
            }

            previous = vertex;
            previousSide = side;
        }
    }

    /// <summary>`FUN_180120230`: keeps dot(normal, p) &lt; distance; a crossing by the ray from one corner to the next (`FUN_18011fad0`).</summary>
    private static void ClipPlane(List<ShadowDecalVertex> polygon, List<ShadowDecalVertex> kept, Vector3 normal, float distance)
    {
        kept.Clear();

        if (polygon.Count == 0)
        {
            return;
        }

        ShadowDecalVertex previous = polygon[^1];
        bool previousIn = Vector3.Dot(normal, previous.Position) < distance;

        for (int at = 0; at < polygon.Count; at++)
        {
            ShadowDecalVertex vertex = polygon[at];
            bool isIn = Vector3.Dot(normal, vertex.Position) < distance;

            if (isIn != previousIn)
            {
                Vector3 delta = vertex.Position - previous.Position;
                float t = (distance - Vector3.Dot(normal, previous.Position)) / Vector3.Dot(normal, delta);
                kept.Add(Lerp(previous, vertex, t));
            }

            if (isIn)
            {
                kept.Add(vertex);
            }

            previous = vertex;
            previousIn = isIn;
        }
    }

    private static ShadowDecalVertex Lerp(ShadowDecalVertex from, ShadowDecalVertex to, float t) =>
        new(Vector3.Lerp(from.Position, to.Position, t), Vector3.Lerp(from.Texture, to.Texture, t));
}
