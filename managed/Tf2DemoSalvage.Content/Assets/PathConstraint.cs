using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// <c>Constrain distance to path between two control points</c> — keeps each particle within a band around a curve from
/// one control point to another, along which it travels (B396).
/// </summary>
/// <remarks>
/// **`C_OP_ConstrainDistanceToPath::EnforceConstraint` and `CParticleCollection::CalculatePathValues`, read out of
/// `particles.lib`**, with the fields and defaults of the constraint's unpack table:
///
/// <code>
/// minimum distance 0, maximum distance 100, maximum distance middle −1, maximum distance end −1, travel time 10,
/// start control point number 0, end control point number 0, bulge control 0, random bulge 0, mid point position 0.5
///
/// // CalculatePathValues
/// start = CP[ start ];  end = CP[ end ];  mid = start + ( end − start ) · mid point position
/// bulge control 0:  mid += ( 2 · rand − 1 ) · random bulge, per axis, from the collection's own random seed
/// bulge control 1/2: fwd = CP[ start or end ].forward;  len = |end − start|
///                   f = len > 1e−6 ? 1 − |( end − start ) / len · fwd| : 0
///                   if |fwd| > 1e−6: mid += fwd · len · random bulge · f / |fwd|
///
/// // EnforceConstraint, four particles at a time
/// t = min( 1, ( now − born ) / max( travel time, 0.001 ) )
/// point = lerp( lerp( start, mid, t ), lerp( mid, end, t ), t )                  // a quadratic Bézier
/// max = the largest of the three maxima; unless all three agree and no particle of the four is past it,
///       max = lerp( lerp( maximum, middle, t ), lerp( middle, end, t ), t )     // an absent middle/end is the one before
/// past max → point + ( p − point ) · max / |p − point|;   inside minimum → point + ( p − point ) · minimum / |p − point|
/// </code>
///
/// **Only `XYZ` is written**, never `PREV_XYZ` (`GetWrittenAttributes` is 1): the integrator carries the correction as
/// velocity next step. The collection's random seed is this project's own zero, not the engine's per-collection draw.
/// </remarks>
public static class PathConstraint
{
    /// <summary>The <c>functionName</c> a <c>.pcf</c> uses.</summary>
    public const string Named = "Constrain distance to path between two control points";

    /// <summary>The shortest travel time, `0.001`.</summary>
    private const float ShortestTravel = 0.001f;

    /// <summary>`1e−6`, below which a length is treated as none.</summary>
    private const double Tiny = 1e-6d;

    /// <summary>`CalculatePathValues`: the path's start, middle and end at this moment.</summary>
    /// <param name="parameters">The constraint (or initializer) declaring the path.</param>
    /// <param name="points">The effect's control points by number; a missing one is the origin.</param>
    /// <returns>The three points.</returns>
    public static (Vector3 Start, Vector3 Mid, Vector3 End) PathValues(
        ParticleFunction parameters, IReadOnlyList<ParticleControlPoint> points)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(points);

        int startNumber = (int)parameters.Number("start control point number", 0d);
        int endNumber = (int)parameters.Number("end control point number", 0d);
        int bulgeControl = (int)parameters.Number("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", 0d);
        float bulge = (float)parameters.Number("random bulge", 0d);
        float midPoint = (float)parameters.Number("mid point position", 0.5d);

        ParticleControlPoint first = Point(points, startNumber);
        ParticleControlPoint last = Point(points, endNumber);
        Vector3 along = last.At - first.At;
        Vector3 mid = first.At + (along * midPoint);

        if (bulgeControl == 0)
        {
            mid += new Vector3(
                (2f * bulge * ParticleRandom.Sample(0, 0)) - bulge,
                (2f * bulge * ParticleRandom.Sample(0, 1)) - bulge,
                (2f * bulge * ParticleRandom.Sample(0, 2)) - bulge);
        }
        else
        {
            Vector3 forward = Point(points, bulgeControl == 2 ? endNumber : startNumber).Forward;
            float length = along.Length();
            float offAxis = length > Tiny ? 1f - MathF.Abs(Vector3.Dot(along / length, forward)) : 0f;
            float reach = forward.Length();

            if (reach > Tiny)
            {
                mid += forward * (length * bulge * offAxis / reach);
            }
        }

        return (first.At, mid, last.At);
    }

    /// <summary>`EnforceConstraint`: brings every particle within its band around the path.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="parameters">The declared constraint.</param>
    /// <param name="points">The effect's control points by number.</param>
    /// <returns>Whether any particle moved.</returns>
    public static bool Enforce(ParticleStore particles, ParticleFunction parameters, IReadOnlyList<ParticleControlPoint> points)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        (Vector3 start, Vector3 mid, Vector3 end) = PathValues(parameters, points);

        float minimum = (float)parameters.Number("minimum distance", 0d);
        float maximum = (float)parameters.Number("maximum distance", 100d);
        float declaredMiddle = (float)parameters.Number("maximum distance middle", -1d);
        float declaredEnd = (float)parameters.Number("maximum distance end", -1d);
        float perSecond = 1f / MathF.Max((float)parameters.Number("travel time", 10d), ShortestTravel);

        float middle = declaredMiddle >= 0f ? declaredMiddle : maximum;
        float last = declaredEnd >= 0f ? declaredEnd : middle;
#pragma warning disable S1244 // Floating point equality — the engine's own `==`, deciding whether the three maxima agree
        bool uniform = (declaredMiddle < 0f || declaredMiddle == maximum) && (declaredEnd < 0f || declaredEnd == maximum);
#pragma warning restore S1244
        float largest = MathF.Max(maximum, MathF.Max(middle, last));
        bool moved = false;

        Span<float> along = stackalloc float[4];
        Span<Vector3> towards = stackalloc Vector3[4];

        for (int group = 0; group < particles.Count; group += 4)
        {
            int lanes = Math.Min(4, particles.Count - group);
            bool anyPast = false;

            for (int lane = 0; lane < lanes; lane++)
            {
                int index = group + lane;
                float t = MathF.Min(1f, (particles.Age - particles.Born[index]) * perSecond);
                Vector3 a = start + ((mid - start) * t);
                Vector3 b = mid + ((end - mid) * t);

                along[lane] = t;
                towards[lane] = a + ((b - a) * t);
                anyPast |= Vector3.DistanceSquared(particles.Position[index], towards[lane]) > largest * largest;
            }

            for (int lane = 0; lane < lanes; lane++)
            {
                int index = group + lane;
                float t = along[lane];
                Vector3 offset = particles.Position[index] - towards[lane];
                float distanceSquared = offset.LengthSquared();
                float band = largest;

                if (!uniform && !anyPast)
                {
                    float a = maximum + ((middle - maximum) * t);
                    float b = middle + ((last - middle) * t);

                    band = a + ((b - a) * t);
                }

                bool inside = distanceSquared < minimum * minimum;
                bool past = distanceSquared > band * band;

                if (!inside && !past)
                {
                    continue;
                }

                float scale = (inside ? minimum : band) / MathF.Sqrt(distanceSquared);

                particles.Position[index] = towards[lane] + (offset * scale);
                moved = true;
            }
        }

        return moved;
    }

    private static ParticleControlPoint Point(IReadOnlyList<ParticleControlPoint> points, int number) =>
        number >= 0 && number < points.Count ? points[number] : ParticleControlPoint.Unoriented(Vector3.Zero);
}
