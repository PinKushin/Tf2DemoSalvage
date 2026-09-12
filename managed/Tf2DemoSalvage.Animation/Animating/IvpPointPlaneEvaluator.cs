namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The signed distance from a hull vertex of one body to a face of another — the point-plane evaluator
/// IVP's time-of-impact search drives to its target (B369).
/// </summary>
/// <remarks>
/// **Filled by the vertex-face routine `FUN_1800a1b50` and measured by `FUN_1800a3470`**, slot 0 of vtable
/// `1800fe720`, both read from the disassembly (`docs/findings/51`, *The point-plane evaluator*). The engine's
/// evaluator also carries the pair's maximum approach speed and its inverse at `+0x08` and `+0x10`; those
/// belong to the root finder and arrive with it.
/// </remarks>
/// <param name="Vertex">The vertex, in its own body's frame (<c>+0x28</c>).</param>
/// <param name="Normal">The face's normal, in the face's body's frame (<c>+0x48</c>).</param>
/// <param name="PlanePoint">The face's own first point, in the face's body's frame (<c>+0x68</c>).</param>
public readonly record struct IvpPointPlaneEvaluator(
    (double X, double Y, double Z) Vertex,
    (double X, double Y, double Z) Normal,
    (double X, double Y, double Z) PlanePoint)
{
    /// <summary>Fills the evaluator for a vertex and a face, as <c>FUN_1800a1b50</c> does.</summary>
    /// <param name="vertex">The vertex, from its body's ledge points.</param>
    /// <param name="first">The face's own point, from the other body's ledge points.</param>
    /// <param name="second">The start of the face's next edge.</param>
    /// <param name="third">The start of the edge after that.</param>
    /// <returns>The evaluator.</returns>
    /// <remarks>
    /// **The normal is scaled to unit length and the answer thrown away** — `FUN_18006e080`'s return value
    /// is never read — so a degenerate face keeps a zero normal and measures every vertex at zero. Both
    /// points are widened from float as they are stored.
    /// </remarks>
    public static IvpPointPlaneEvaluator ForFace(
        (float X, float Y, float Z) vertex,
        (float X, float Y, float Z) first,
        (float X, float Y, float Z) second,
        (float X, float Y, float Z) third)
    {
        (double X, double Y, double Z) normal = IvpVector.FaceNormal(first, second, third);
        _ = IvpVector.TryScaleToUnitLength(ref normal);

        return new IvpPointPlaneEvaluator(
            Vertex: (vertex.X, vertex.Y, vertex.Z),
            Normal: normal,
            PlanePoint: (first.X, first.Y, first.Z));
    }

    /// <summary>How far the vertex is in front of the face — <c>FUN_1800a3470</c>.</summary>
    /// <param name="vertexBody">Where the vertex's body is.</param>
    /// <param name="faceBody">Where the face's body is.</param>
    /// <returns>The signed distance; negative behind the face.</returns>
    /// <remarks>
    /// **The vertex goes through a call to `FUN_180070bc0`; the plane point and normal are inlined**, with the
    /// same grouping as <see cref="IvpMatrix.ToWorld"/> and <see cref="IvpMatrix.Rotate"/>, and the dot adds
    /// its `x` and `y` terms before `z`.
    /// </remarks>
    public double Distance(IvpMatrix vertexBody, IvpMatrix faceBody)
    {
        (double X, double Y, double Z) vertex = vertexBody.ToWorld(Vertex);
        (double X, double Y, double Z) plane = faceBody.ToWorld(PlanePoint);
        (double X, double Y, double Z) normal = faceBody.Rotate(Normal);

        return ((vertex.X - plane.X) * normal.X)
            + ((vertex.Y - plane.Y) * normal.Y)
            + ((vertex.Z - plane.Z) * normal.Z);
    }
}
