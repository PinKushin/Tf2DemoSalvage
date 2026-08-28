using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What of the world to draw from a given eye — the engine's <c>BuildWorldLists</c>, end to end.
/// </summary>
/// <remarks>
/// **One question, because the renderer should not have to ask two.** <see cref="WorldVisibility"/>
/// finds the leaves and <see cref="VisibleWorld"/> turns them into runs; keeping them apart is what
/// makes each testable, and joining them here is what stops a caller pairing one map's leaves with
/// another map's spans.
///
/// **A map that cannot be culled answers null rather than an empty list**, and the distinction is
/// the whole safety of this. Null means "draw what you already had"; an empty list means "the eye
/// can see nothing", which is a legitimate answer for a camera facing into the void and a black
/// screen if it is returned by mistake. Conflating them would turn every unsupported map into a
/// blank window.
/// </remarks>
public sealed class WorldCulling
{
    private readonly WorldVisibility _visibility;
    private readonly VisibleWorld _surfaces;

    /// <summary>Prepares culling for one map.</summary>
    /// <param name="tree">The map's nodes, planes and leaves.</param>
    /// <param name="pvs">Its visibility lump, or <see cref="BspVisibility.None"/>.</param>
    /// <param name="leafFaces">Its LEAFFACES lump, or <see cref="BspLeafFaces.None"/>.</param>
    /// <param name="spans">Where each face's triangles are, from the world build.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public WorldCulling(
        BspLeafTree tree,
        BspVisibility pvs,
        BspLeafFaces leafFaces,
        IReadOnlyList<WorldFaceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(spans);

        _visibility = new WorldVisibility(tree, pvs);
        _surfaces = new VisibleWorld(spans, tree, leafFaces);

        TotalLeaves = tree.LeafCount;

        int corners = 0;

        for (int at = 0; at < spans.Count; at++)
        {
            corners += spans[at].VertexCount;
        }

        _worldCorners = corners;
    }

    private readonly int _worldCorners;

    /// <summary>Whether this map carried what culling needs.</summary>
    public bool CanCull => _surfaces.CanCull;

    /// <summary>How many surfaces no leaf names, and are therefore culled by their own box.</summary>
    public int UnreachableSpans => _surfaces.UnreachableSpans;

    /// <summary>How many leaves the last call found, for the log.</summary>
    public int LeafCount { get; private set; }

    /// <summary>How many leaves the map has, so the count above can be read as a fraction.</summary>
    /// <remarks>
    /// **Without this the visible count is uninterpretable, and the first real measurement proved
    /// it.** A log line reading *"4257 leaves visible"* says nothing on its own: it is most of a
    /// small map and a fraction of a large one, and the difference is whether the cull is working.
    /// </remarks>
    public int TotalLeaves { get; }

    /// <summary>How many corners the last call's runs cover, against the whole world's.</summary>
    /// <remarks>
    /// **The number that actually says what was saved.** Runs and batches are draw calls; corners
    /// are the work. A cull that halves the geometry while slightly increasing the draw-call count
    /// is a win, and counting only the calls would read it as a loss.
    /// </remarks>
    public (int Drawn, int Total) Corners { get; private set; }

    /// <summary>The world runs to draw from one eye, or null when this map cannot be culled.</summary>
    /// <param name="x">The eye's world position — the vis origin.</param>
    /// <param name="y">The eye's world position.</param>
    /// <param name="z">The eye's world position.</param>
    /// <param name="frustum">The view volume.</param>
    /// <returns>Runs to draw, valid until the next call, or null to draw everything.</returns>
    /// <remarks>
    /// **Called on a view change rather than per frame**, like the camera it derives from: the
    /// answer is a function of the eye and the frustum and nothing else, so a still camera would
    /// get the same runs back for the cost of walking the tree again.
    /// </remarks>
    public IReadOnlyList<WorldBatch>? Batches(float x, float y, float z, ViewFrustum frustum)
    {
        if (!CanCull)
        {
            return null;
        }

        IReadOnlyList<int> leaves = _visibility.Leaves(x, y, z, frustum);

        LeafCount = leaves.Count;

        IReadOnlyList<WorldBatch> runs = _surfaces.Batches(leaves, frustum);

        int drawn = 0;

        for (int at = 0; at < runs.Count; at++)
        {
            drawn += runs[at].VertexCount;
        }

        Corners = (drawn, _worldCorners);

        return runs;
    }
}
