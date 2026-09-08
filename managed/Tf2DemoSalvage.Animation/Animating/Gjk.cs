using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The closest-points distance algorithm IVP's own mindist geometry is built on (B58, D149).
/// </summary>
/// <remarks>
/// **Not decompiled, and not Valve's.** `docs/findings/51` reads `FUN_180096680` as a warm-started
/// GJK/EPA solver — a simplex built entirely out of calls to each shape's own support function. GJK
/// itself (Gilbert–Johnson–Keerthi, 1988) is published academic computational geometry, not IVP's
/// intellectual property; nothing decompiled is reproduced here, only the SHAPE of what the engine
/// does, which is what this project's own decompiler rule asks for.
///
/// **Two shapes, described only by their support functions — <c>direction ↦ extreme point</c> —
/// which is exactly what <see cref="IvpWorldLedge.Support"/> now provides and what any future body
/// hull needs the same method for.** This is deliberately decoupled from <see cref="IvpRigidBody"/>:
/// wiring it into the live contact solve is a separate, measured step, not this one.
///
/// **Scoped to SEPARATED and near-touching shapes — no tetrahedron case, no EPA.** A 3D GJK that
/// must prove containment needs a fourth simplex point and a penetration-depth solve on top of it;
/// this project already has that regime covered by <c>IvpWorldCollision.Touching</c> and
/// <c>IvpWorldCollision.Penetration</c>, both plane-based and already measured against real
/// corpses. What neither of those can do — ask a QUESTION OF TWO SHAPES AT ONCE rather than one
/// point against a world — is what this exists for, and that question only needs the distance
/// half. A degenerate case where the origin is not clearly outside the working simplex reports a
/// <see cref="GjkResult"/> with <c>Distance</c> of zero rather than guessing a penetration depth it
/// cannot prove.
/// </remarks>
/// <summary>The closest pair of points between two shapes, and the gap between them.</summary>
/// <param name="PointOnA">The nearest point on the first shape, in the space it was queried in.</param>
/// <param name="PointOnB">The nearest point on the second shape, in the same space.</param>
/// <param name="Normal">
/// The direction from B toward A, normalised — zero when <paramref name="Distance"/> is zero, since
/// a point has no direction to itself.
/// </param>
/// <param name="Distance">How far apart the two closest points are. Never negative.</param>
public readonly record struct GjkResult(Vector3 PointOnA, Vector3 PointOnB, Vector3 Normal, float Distance);

public static class Gjk
{
    /// <summary>Finds the closest points between two convex shapes, given only their support functions.</summary>
    /// <param name="supportA">Direction ↦ the first shape's extreme point that way.</param>
    /// <param name="supportB">Direction ↦ the second shape's extreme point that way.</param>
    /// <returns>The closest pair, or null if it did not converge inside the iteration budget.</returns>
    /// <exception cref="ArgumentNullException">Either support function is null.</exception>
    public static GjkResult? Distance(Func<Vector3, Vector3> supportA, Func<Vector3, Vector3> supportB)
    {
        ArgumentNullException.ThrowIfNull(supportA);
        ArgumentNullException.ThrowIfNull(supportB);

        List<(Vector3 P, Vector3 A, Vector3 B)> simplex =
        [
            Support(supportA, supportB, Vector3.UnitZ),
        ];

        Vector3 closestP = simplex[0].P;
        Vector3 closestA = simplex[0].A;
        Vector3 closestB = simplex[0].B;

        for (int iteration = 0; iteration < MaximumIterations; iteration++)
        {
            Vector3 direction = -closestP;

            if (direction.LengthSquared() < ConvergenceEpsilon * ConvergenceEpsilon)
            {
                // The working simplex already touches the origin: the two shapes touch or overlap.
                // Reported as zero distance rather than guessing a depth this algorithm cannot prove.
                return new GjkResult(closestA, closestB, Vector3.Zero, 0f);
            }

            (Vector3 P, Vector3 A, Vector3 B) candidate = Support(supportA, supportB, direction);

            // **Termination is "no further progress toward the origin", not a fixed point count —
            // OR the candidate is a support point the simplex already holds.** The second half is
            // not an optimisation; without it this cycles forever. A tied support query (several
            // vertices equally extreme along the current direction — the common case at a box
            // corner or, here, a whole tied face) can keep re-offering the SAME vertex on every
            // iteration once the simplex has already used it to reach the true closest point. That
            // vertex genuinely improves the raw dot product test by a hair — floating-point noise
            // on coordinates in the thousands, not real progress — while `Reduce` correctly
            // recognises it contributes nothing and leaves the simplex unchanged. The two checks
            // disagreed forever; measured hanging at the iteration cap on a real box every time.
            bool alreadyHeld = false;

            foreach ((Vector3 P, Vector3 A, Vector3 B) held in simplex)
            {
                if ((held.P - candidate.P).LengthSquared() < DuplicateEpsilon * DuplicateEpsilon)
                {
                    alreadyHeld = true;
                    break;
                }
            }

            if (alreadyHeld ||
                Vector3.Dot(candidate.P, direction) - Vector3.Dot(closestP, direction) < ConvergenceEpsilon)
            {
                Vector3 normal = closestP.LengthSquared() > ConvergenceEpsilon * ConvergenceEpsilon
                    ? Vector3.Normalize(closestP)
                    : Vector3.Zero;

                return new GjkResult(closestA, closestB, normal, closestP.Length());
            }

            simplex.Add(candidate);

            (closestP, closestA, closestB, simplex) = Reduce(simplex);
        }

        return null;
    }

    private static (Vector3 P, Vector3 A, Vector3 B) Support(
        Func<Vector3, Vector3> supportA, Func<Vector3, Vector3> supportB, Vector3 direction)
    {
        Vector3 a = supportA(direction);
        Vector3 b = supportB(-direction);

        return (a - b, a, b);
    }

    /// <summary>Collapses the working simplex to whichever feature is nearest the origin.</summary>
    /// <remarks>
    /// **Capped at three points, deliberately.** A general 3D GJK reduces a tetrahedron by testing
    /// containment against all four of its face planes; skipping that (see the type remarks) means
    /// this never needs to hold a fourth point at all — when adding one would, every 3-of-4 subset
    /// is tried instead and the nearest wins, which finds the same answer for every SEPARATED case
    /// without the containment test a penetrating case would need.
    /// </remarks>
    private static (Vector3 P, Vector3 A, Vector3 B, List<(Vector3 P, Vector3 A, Vector3 B)> Simplex) Reduce(
        List<(Vector3 P, Vector3 A, Vector3 B)> simplex)
    {
        if (simplex.Count == 1)
        {
            return (simplex[0].P, simplex[0].A, simplex[0].B, simplex);
        }

        if (simplex.Count == 2)
        {
            return ReduceSegment(simplex[0], simplex[1]);
        }

        (Vector3 P, Vector3 A, Vector3 B, List<(Vector3 P, Vector3 A, Vector3 B)> Simplex) best = default;
        float bestDistanceSquared = float.MaxValue;

        foreach (List<(Vector3 P, Vector3 A, Vector3 B)> subset in Triangles(simplex))
        {
            (Vector3 P, Vector3 A, Vector3 B, List<(Vector3 P, Vector3 A, Vector3 B)> Simplex) found =
                ReduceTriangle(subset[0], subset[1], subset[2]);

            float distanceSquared = found.P.LengthSquared();

            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = found;
            }
        }

        return best;
    }

    /// <summary>Every 3-point subset of a 3- or 4-point simplex — itself, or each leave-one-out triple.</summary>
    private static IEnumerable<List<(Vector3 P, Vector3 A, Vector3 B)>> Triangles(
        List<(Vector3 P, Vector3 A, Vector3 B)> simplex)
    {
        if (simplex.Count == 3)
        {
            yield return simplex;
            yield break;
        }

        for (int skip = 0; skip < simplex.Count; skip++)
        {
            List<(Vector3 P, Vector3 A, Vector3 B)> subset = [];

            for (int index = 0; index < simplex.Count; index++)
            {
                if (index != skip)
                {
                    subset.Add(simplex[index]);
                }
            }

            yield return subset;
        }
    }

    private static (Vector3, Vector3, Vector3, List<(Vector3 P, Vector3 A, Vector3 B)>) ReduceSegment(
        (Vector3 P, Vector3 A, Vector3 B) start, (Vector3 P, Vector3 A, Vector3 B) end)
    {
        Vector3 segment = end.P - start.P;
        float lengthSquared = segment.LengthSquared();

        if (lengthSquared < DegenerateLength)
        {
            return (start.P, start.A, start.B, [start]);
        }

        float t = Math.Clamp(-Vector3.Dot(start.P, segment) / lengthSquared, 0f, 1f);

        Vector3 closest = start.P + (segment * t);
        Vector3 closestA = start.A + ((end.A - start.A) * t);
        Vector3 closestB = start.B + ((end.B - start.B) * t);

        List<(Vector3 P, Vector3 A, Vector3 B)> kept = t switch
        {
            <= 0f => [start],
            >= 1f => [end],
            _ => [start, end],
        };

        return (closest, closestA, closestB, kept);
    }

    /// <summary>The standard closest-point-on-triangle-to-a-point test, specialised to the origin.</summary>
    /// <remarks>
    /// Ericson, *Real-Time Collision Detection* §5.1.5 — published, general-purpose computational
    /// geometry, the same status as the rest of this file.
    /// </remarks>
    private static (Vector3, Vector3, Vector3, List<(Vector3 P, Vector3 A, Vector3 B)>) ReduceTriangle(
        (Vector3 P, Vector3 A, Vector3 B) first,
        (Vector3 P, Vector3 A, Vector3 B) second,
        (Vector3 P, Vector3 A, Vector3 B) third)
    {
        Vector3 a = first.P;
        Vector3 b = second.P;
        Vector3 c = third.P;

        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = -a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);

        if (d1 <= 0f && d2 <= 0f)
        {
            return (a, first.A, first.B, [first]);
        }

        Vector3 bp = -b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);

        if (d3 >= 0f && d4 <= d3)
        {
            return (b, second.A, second.B, [second]);
        }

        float vc = (d1 * d4) - (d3 * d2);

        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);

            return (
                a + (ab * v),
                first.A + ((second.A - first.A) * v),
                first.B + ((second.B - first.B) * v),
                [first, second]);
        }

        Vector3 cp = -c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);

        if (d6 >= 0f && d5 <= d6)
        {
            return (c, third.A, third.B, [third]);
        }

        float vb = (d5 * d2) - (d1 * d6);

        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);

            return (
                a + (ac * w),
                first.A + ((third.A - first.A) * w),
                first.B + ((third.B - first.B) * w),
                [first, third]);
        }

        float va = (d3 * d6) - (d5 * d4);

        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));

            return (
                b + ((c - b) * w),
                second.A + ((third.A - second.A) * w),
                second.B + ((third.B - second.B) * w),
                [second, third]);
        }

        float denom = 1f / (va + vb + vc);
        float vv = vb * denom;
        float ww = vc * denom;

        return (
            a + (ab * vv) + (ac * ww),
            first.A + ((second.A - first.A) * vv) + ((third.A - first.A) * ww),
            first.B + ((second.B - first.B) * vv) + ((third.B - first.B) * ww),
            [first, second, third]);
    }

    /// <summary>How many support-point additions to try before giving up.</summary>
    private const int MaximumIterations = 32;

    /// <summary>Below this, a search direction, a gap, or a step of progress counts as zero.</summary>
    /// <remarks>
    /// **Scaled to Source units, not to a unit sphere.** GJK is usually described over shapes near
    /// the origin at scale 1; a corpse and a floor brush sit at coordinates in the thousands, so the
    /// progress test — a difference of two dot products of vectors that size — carries noise many
    /// times larger than `1e-4` would allow. Measured: at that tolerance a genuinely convergent
    /// query against a real box never satisfied it and ran out the iteration budget every time.
    /// `1e-2` is still four orders of magnitude below anything a corpse's rest position could show.
    /// </remarks>
    private const float ConvergenceEpsilon = 1e-2f;

    /// <summary>Below this, a candidate support point counts as one the simplex already holds.</summary>
    private const float DuplicateEpsilon = 1e-2f;

    /// <summary>Below this squared length, a segment's two ends count as the same point.</summary>
    private const float DegenerateLength = 1e-12f;
}
