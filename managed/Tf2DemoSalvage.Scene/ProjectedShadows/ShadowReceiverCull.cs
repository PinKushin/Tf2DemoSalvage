using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>
/// Whether a shadow skips a renderable that could receive it — <c>CClientShadowMgr::CullReceiver</c>
/// (`clientshadowmgr.cpp:3297-3433`), with <c>ComputeSeparatingPlane</c> (`collisionutils.cpp:2105-2464`) (B702).
/// </summary>
/// <remarks>
/// **In order**: the receiver's bounding sphere (<c>ComputeBoundingSphere</c>) taken into shadow space must meet the box
/// <c>(±size/2, ±size/2, 0…maxDist)</c>; then a plane separating receiver from caster — the line between their spheres'
/// centres when the spheres do not meet, else the first of fifteen box axes that separates their oriented boxes with a
/// tolerance of 3 — and the receiver must lie on the side the shadow is cast toward. No separating plane, no cull.
/// </remarks>
public static class ShadowReceiverCull
{
    /// <summary>`ComputeSeparatingPlane`'s tolerance in <c>CClientShadowMgr::ComputeSeparatingPlane</c> (`:3289`).</summary>
    private const float Tolerance = 3f;

    /// <summary>Whether the shadow skips the receiver.</summary>
    /// <param name="shadow">The shadow.</param>
    /// <param name="receiver">The receiver: render origin, angles and bounds.</param>
    /// <param name="source">The shadow's caster, the same.</param>
    /// <returns>True when the engine would not add the shadow to it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shadow"/> is null.</exception>
    public static bool Culls(ShadowProjection shadow, ShadowEntity receiver, ShadowEntity source)
    {
        ArgumentNullException.ThrowIfNull(shadow);

        (Vector3 origin, float radius) = BoundingSphere(receiver);
        Vector3 local = shadow.WorldToShadow(origin);
        Vector3 shadowMin = new(-shadow.Size.X * 0.5f, -shadow.Size.Y * 0.5f, 0f);
        Vector3 shadowMax = new(shadow.Size.X * 0.5f, shadow.Size.Y * 0.5f, shadow.MaxHeight);

        if (!BoxMeetsSphere(shadowMin, shadowMax, local, radius))
        {
            return true;
        }

        (Vector3 sourceOrigin, float sourceRadius) = BoundingSphere(source);
        float radiusSum = sourceRadius + radius;
        Vector3? normal = Vector3.DistanceSquared(origin, sourceOrigin) <= radiusSum * radiusSum
            ? SeparatingPlane(receiver, source)
            : origin - sourceOrigin;

        if (normal is not { } plane)
        {
            return false;
        }

        float shadowDot = Vector3.Dot(shadow.Direction, plane);
        float receiverDot = Vector3.Dot(plane, origin);
        float sourceDot = Vector3.Dot(plane, sourceOrigin);

        return shadowDot > 0f ? receiverDot <= sourceDot : receiverDot >= sourceDot;
    }

    /// <summary>
    /// `IsBoxIntersectingSphere` (`collisionutils.cpp:259-301`): the squared distance from the centre to the box under r² —
    /// strictly, so a sphere that only touches the box does not meet it.
    /// </summary>
    /// <param name="min">The box's lower corner.</param>
    /// <param name="max">Its upper corner.</param>
    /// <param name="centre">The sphere's centre.</param>
    /// <param name="radius">Its radius.</param>
    /// <returns>Whether they meet.</returns>
    public static bool BoxMeetsSphere(Vector3 min, Vector3 max, Vector3 centre, float radius) =>
        Vector3.DistanceSquared(centre, Vector3.Clamp(centre, min, max)) < radius * radius;

    /// <summary><c>ComputeBoundingSphere</c> (`:3225-3248`): the bounds' centroid placed, half their diagonal.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The centre and radius.</returns>
    public static (Vector3 Centre, float Radius) BoundingSphere(ShadowEntity entity)
    {
        Vector3 centroid = (entity.Mins + entity.Maxs) * 0.5f;
        (Vector3 forward, Vector3 left, Vector3 up) = ShadowProjection.Basis(entity.RenderAngles);

        return (
            entity.RenderOrigin + (centroid.X * forward) + (centroid.Y * left) + (centroid.Z * up),
            (entity.Maxs - entity.Mins).Length() * 0.5f);
    }

    /// <summary>
    /// `ComputeSeparatingPlane` for two oriented boxes (`collisionutils.cpp:2158-2441`), box 1 the receiver and box 2 the
    /// caster: the normal of the first plane found, in the engine's order, or null when they overlap on every axis.
    /// </summary>
    /// <param name="first">Box 1.</param>
    /// <param name="second">Box 2.</param>
    /// <returns>The plane's normal, not normalised, or null.</returns>
    public static Vector3? SeparatingPlane(ShadowEntity first, ShadowEntity second)
    {
        Span<Vector3> a = stackalloc Vector3[3];
        Span<Vector3> b = stackalloc Vector3[3];
        (a[0], a[1], a[2]) = ShadowProjection.Basis(first.RenderAngles);
        (b[0], b[1], b[2]) = ShadowProjection.Basis(second.RenderAngles);

        Vector3 size1 = (first.Maxs - first.Mins) * 0.5f;
        Vector3 size2 = (second.Maxs - second.Mins) * 0.5f;
        Vector3 between = BoundingSphere(second).Centre - BoundingSphere(first).Centre;

        // box2ToBox1 = worldToBox1 · box2ToWorld: rotation m[i][j] = a_i · b_j, stored row by row; translation the
        // centroids' difference on box 1's axes.
        Span<float> m = stackalloc float[9];
        Span<float> abs = stackalloc float[9];
        Span<float> o = stackalloc float[3];
        Span<float> s1 = [size1.X, size1.Y, size1.Z];
        Span<float> s2 = [size2.X, size2.Y, size2.Z];

        for (int i = 0; i < 3; i++)
        {
            o[i] = Vector3.Dot(a[i], between);

            for (int j = 0; j < 3; j++)
            {
                m[(i * 3) + j] = Vector3.Dot(a[i], b[j]);
                abs[(i * 3) + j] = MathF.Abs(m[(i * 3) + j]);
            }
        }

        // Box 1's faces.
        for (int i = 0; i < 3; i++)
        {
            float sum = s1[i] + (abs[i * 3] * s2[0]) + (abs[(i * 3) + 1] * s2[1]) + (abs[(i * 3) + 2] * s2[2]);

            if (MathF.Abs(o[i]) + Tolerance > sum)
            {
                return a[i];
            }
        }

        // Box 2's faces.
        for (int j = 0; j < 3; j++)
        {
            float sum = s2[j] + (abs[j] * s1[0]) + (abs[3 + j] * s1[1]) + (abs[6 + j] * s1[2]);
            float projected = (m[j] * o[0]) + (m[3 + j] * o[1]) + (m[6 + j] * o[2]);

            if (MathF.Abs(projected) + Tolerance > sum)
            {
                return b[j];
            }
        }

        // The nine edge pairs, box 1's edge i against box 2's edge j, skipped when the two are parallel.
        for (int i = 0; i < 3; i++)
        {
            int i1 = (i + 1) % 3;
            int i2 = (i + 2) % 3;

            for (int j = 0; j < 3; j++)
            {
                if (abs[(i * 3) + j] >= 1f - 1e-3f)
                {
                    continue;
                }

                int j1 = (j + 1) % 3;
                int j2 = (j + 2) % 3;

                float sum = (s1[i1] * abs[(i2 * 3) + j]) + (s1[i2] * abs[(i1 * 3) + j])
                    + (s2[j1] * abs[(i * 3) + j2]) + (s2[j2] * abs[(i * 3) + j1]);
                float projected = (o[i2] * m[(i1 * 3) + j]) - (o[i1] * m[(i2 * 3) + j]);

                if (MathF.Abs(projected) + Tolerance > sum)
                {
                    return Vector3.Cross(a[i], b[j]);
                }
            }
        }

        return null;
    }
}
