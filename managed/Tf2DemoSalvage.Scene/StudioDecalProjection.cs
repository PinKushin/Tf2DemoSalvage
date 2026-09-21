using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>One corner of a model decal: which of the model's vertices it rides, and where on the decal it falls.</summary>
/// <param name="Vertex">The model vertex, whose position, normal, bones and weights the decal draws with.</param>
/// <param name="U">Across the decal, 0 to 1 inside it.</param>
/// <param name="V">Down the decal, 0 to 1 inside it.</param>
public readonly record struct StudioDecalCorner(int Vertex, float U, float V);

/// <summary>
/// Projects a decal onto a posed, skinned model — `CStudioRender::AddDecal`, read out of `studiorender.dll` (B415).
/// </summary>
/// <remarks>
/// **Read from the x64 binary**, which carries no symbols; the functions are named here by what they do:
///
/// <code>
/// // AddDecal (0x180004c80) → ComputePoseToDecal (0x180006ec0)
/// fwd = −normalize( ray.delta );  right = normalize( up × fwd )   // if |…| &lt; 0.001, a second cross; then give up
/// b   = fwd × right
/// decal = [ right | −right·start ; b | −b·start ; fwd | −fwd·start ]
/// poseToDecal[ bone ] = decal · poseToWorld[ bone ]
///
/// // per mesh vertex (0x18000a9e0)
/// frontFacing = Σ w · ( poseToDecal[ bone ].row2 · normal ) ≥ 0.1            (0x18000a630)
/// u, v        = Σ w · ( poseToDecal[ bone ].row0/1 · pos + t )                 (0x18000b690)
/// inDepth     = noPokeThru ? |Σ w · ( row2 · pos + t )| &lt; radius : true
/// U, V        = u / radius · 0.5 + 0.5
///
/// // per triangle (0x180005860)
/// needs all three front facing, and any one in depth
/// outcode per corner: U &lt; 0 → 1, U &gt; 1 → 4, V &lt; 0 → 2, V &gt; 1 → 8;  all sharing a bit → rejected
/// a model with more than one bone, or with flexes: a corner inside → the whole triangle; none inside → the whole
///   triangle if the corners, clipped to the square, still leave a polygon
/// </code>
///
/// **A skinned model's decal is whole triangles of the model**, never cut: the clipped path that makes new corners
/// runs only for a one-bone model with no flexes (`numbones &gt; 1 || numflexdesc != 0` turns it off). So a decal rides
/// the same vertices the model draws and is skinned by the same bones, which is what keeps it on a moving player.
/// *Not built:* that clipped path, which no player takes.
/// </remarks>
public static class StudioDecalProjection
{
    /// <summary>How far a corner's normal must face back along the shot to take the decal — 0.1.</summary>
    public const float FacingThreshold = 0.1f;

    /// <summary>Below this a cross product is taken as no direction — 0.001.</summary>
    private const float Degenerate = 0.001f;

    /// <summary>Projects one decal onto a model's triangles.</summary>
    /// <param name="vertices">The model's vertices, a triangle list, in its own bind space.</param>
    /// <param name="poseToWorld">One row-major 3×4 matrix per bone — the skinning matrices the draw uses.</param>
    /// <param name="start">The ray's start.</param>
    /// <param name="delta">The ray's length and direction.</param>
    /// <param name="up">The decal's up, which `AddStudioDecal` passes as ( 0, 0, 1 ).</param>
    /// <param name="radius">Half the decal's size.</param>
    /// <param name="noPokeThru">Whether depth bounds the decal as well — false for a player.</param>
    /// <param name="drawn">Whether a vertex belongs to what is drawn, so an unchosen body part takes nothing.</param>
    /// <returns>The decal's corners, three per triangle; empty when the ray has no direction.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<StudioDecalCorner> Project(
        IReadOnlyList<PropVertex> vertices,
        IReadOnlyList<float[]> poseToWorld,
        Vector3 start,
        Vector3 delta,
        Vector3 up,
        float radius,
        bool noPokeThru,
        Func<int, bool>? drawn = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(poseToWorld);

        List<StudioDecalCorner> corners = [];

        if (Frame(start, delta, up) is not { } decal)
        {
            return corners;
        }

        float[][] toDecal = new float[poseToWorld.Count][];

        for (int bone = 0; bone < poseToWorld.Count; bone++)
        {
            toDecal[bone] = Concatenate(decal, poseToWorld[bone]);
        }

        float scale = radius != 0f ? 1f / radius : 1f;
        Projected[] projected = new Projected[vertices.Count];

        for (int index = 0; index < vertices.Count; index++)
        {
            projected[index] = Vertex(vertices[index], toDecal, scale, radius, noPokeThru);
        }

        for (int first = 0; first + 2 < vertices.Count; first += 3)
        {
            if (drawn is not null && !drawn(first))
            {
                continue;
            }

            Projected a = projected[first];
            Projected b = projected[first + 1];
            Projected c = projected[first + 2];

            if (!(a.Facing && b.Facing && c.Facing) || !(a.InDepth || b.InDepth || c.InDepth))
            {
                continue;
            }

            int codeA = Outcode(a.U, a.V);
            int codeB = Outcode(b.U, b.V);
            int codeC = Outcode(c.U, c.V);

            if ((codeA & codeB & codeC) != 0)
            {
                continue;
            }

            if (codeA != 0 && codeB != 0 && codeC != 0 && !SurvivesClip([(a.U, a.V), (b.U, b.V), (c.U, c.V)]))
            {
                continue;
            }

            corners.Add(new StudioDecalCorner(first, a.U, a.V));
            corners.Add(new StudioDecalCorner(first + 1, b.U, b.V));
            corners.Add(new StudioDecalCorner(first + 2, c.U, c.V));
        }

        return corners;
    }

    /// <summary>`ComputePoseToDecal`'s frame: three rows of four, or null when the ray gives no direction.</summary>
    internal static float[]? Frame(Vector3 start, Vector3 delta, Vector3 up)
    {
        Vector3 fwd = -delta;
        float length = fwd.Length();

        if (length == 0f)
        {
            return null;
        }

        fwd /= length;

        Vector3 right = new((up.Y * fwd.Z) - (up.Z * fwd.Y), (up.Z * fwd.X) - (up.X * fwd.Z), (up.X * fwd.Y) - (up.Y * fwd.X));

        if (right.Length() < Degenerate)
        {
            // The binary's second try, taken as written: a cross with the up vector's axes turned.
            right = new Vector3((up.Z * fwd.Z) - (up.X * fwd.Y), (up.X * fwd.X) - (up.Y * fwd.Z), (up.Y * fwd.Y) - (up.Z * fwd.X));

            if (right.Length() < Degenerate)
            {
                return null;
            }
        }

        right = Vector3.Normalize(right);

        Vector3 down = new((right.Z * fwd.Y) - (right.Y * fwd.Z), (right.X * fwd.Z) - (right.Z * fwd.X), (right.Y * fwd.X) - (right.X * fwd.Y));

        return
        [
            right.X, right.Y, right.Z, -Vector3.Dot(right, start),
            down.X, down.Y, down.Z, -Vector3.Dot(down, start),
            fwd.X, fwd.Y, fwd.Z, -Vector3.Dot(fwd, start),
        ];
    }

    /// <summary>`decal · bone`, both row-major 3×4.</summary>
    private static float[] Concatenate(float[] decal, float[] bone)
    {
        float[] result = new float[12];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                result[(row * 4) + column] =
                    (decal[row * 4] * bone[column]) +
                    (decal[(row * 4) + 1] * bone[4 + column]) +
                    (decal[(row * 4) + 2] * bone[8 + column]);
            }

            result[(row * 4) + 3] += decal[(row * 4) + 3];
        }

        return result;
    }

    /// <summary>One vertex in the decal's frame: its UV, and the two flags the triangle test reads.</summary>
    private readonly record struct Projected(float U, float V, bool Facing, bool InDepth);

    private static Projected Vertex(
        PropVertex vertex, float[][] toDecal, float scale, float radius, bool noPokeThru)
    {
        float facing = 0f;
        float u = 0f;
        float v = 0f;
        float depth = 0f;

        foreach ((int bone, float weight) in Influences(vertex, toDecal.Length))
        {
            float[] m = toDecal[bone];

            facing += ((m[8] * vertex.NormalX) + (m[9] * vertex.NormalY) + (m[10] * vertex.NormalZ)) * weight;
            u += ((m[0] * vertex.X) + (m[1] * vertex.Y) + (m[2] * vertex.Z) + m[3]) * weight;
            v += ((m[4] * vertex.X) + (m[5] * vertex.Y) + (m[6] * vertex.Z) + m[7]) * weight;
            depth += ((m[8] * vertex.X) + (m[9] * vertex.Y) + (m[10] * vertex.Z) + m[11]) * weight;
        }

        return new Projected(
            (u * scale * 0.5f) + 0.5f, (v * scale * 0.5f) + 0.5f, facing >= FacingThreshold, !noPokeThru || MathF.Abs(depth) < radius);
    }

    /// <summary>A vertex's bones with their weights; a vertex with none rides bone 0 whole, as a rigid mesh does.</summary>
    private static IEnumerable<(int Bone, float Weight)> Influences(PropVertex vertex, int bones)
    {
        (byte first, byte second, byte third) = vertex.Bones;
        (float w1, float w2, float w3) = vertex.Weights;

        if (w1 <= 0f && w2 <= 0f && w3 <= 0f)
        {
            yield return (Math.Min((int)first, bones - 1), 1f);
            yield break;
        }

        if (w1 > 0f && first < bones)
        {
            yield return (first, w1);
        }

        if (w2 > 0f && second < bones)
        {
            yield return (second, w2);
        }

        if (w3 > 0f && third < bones)
        {
            yield return (third, w3);
        }
    }

    private static int Outcode(float u, float v) => Side(u, 1, 4) | Side(v, 2, 8);

    private static int Side(float value, int below, int above)
    {
        if (value < 0f)
        {
            return below;
        }

        return value > 1f ? above : 0;
    }

    /// <summary>Whether a triangle clipped to the unit square still has three corners — the test before keeping it whole.</summary>
    private static bool SurvivesClip(List<(float U, float V)> polygon)
    {
        polygon = Clip(polygon, p => p.U, 0f, keepAbove: true);
        polygon = Clip(polygon, p => p.U, 1f, keepAbove: false);
        polygon = Clip(polygon, p => p.V, 0f, keepAbove: true);
        polygon = Clip(polygon, p => p.V, 1f, keepAbove: false);

        return polygon.Count >= 3;
    }

    private static List<(float U, float V)> Clip(
        List<(float U, float V)> polygon, Func<(float U, float V), float> axis, float edge, bool keepAbove)
    {
        List<(float U, float V)> kept = [];

        for (int index = 0; index < polygon.Count; index++)
        {
            (float U, float V) from = polygon[index];
            (float U, float V) to = polygon[(index + 1) % polygon.Count];
            float fromSide = keepAbove ? axis(from) - edge : edge - axis(from);
            float toSide = keepAbove ? axis(to) - edge : edge - axis(to);

            if (fromSide >= 0f)
            {
                kept.Add(from);
            }

            if ((fromSide >= 0f) != (toSide >= 0f))
            {
                float t = fromSide / (fromSide - toSide);

                kept.Add((from.U + ((to.U - from.U) * t), from.V + ((to.V - from.V) * t)));
            }
        }

        return kept;
    }
}
