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
    private readonly VisibleWorld _skySurfaces;
    private readonly BspLeafTree _tree;
    private readonly List<int> _mainLeaves = [];
    private readonly List<int> _skyLeaves = [];

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

        // **A SECOND instance rather than a second call, because `VisibleWorld` reuses one list.**
        // It clears and refills the same buffer per call, so running the sky pass through the same
        // object would leave the main pass holding the sky's runs — the world would vanish and the
        // sky would be drawn twice. A fault that only exists once both passes do.
        _skySurfaces = new VisibleWorld(spans, tree, leafFaces);
        _tree = tree;

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

    /// <summary>Which leaves the last cull accepted, indexed by leaf, for the entity cull.</summary>
    /// <remarks>
    /// **The same answer the world draw used, passed along rather than recomputed** (B254). An
    /// entity cull that walked the tree a second time would be free to disagree with the world about
    /// what is visible, and the visible failure of that is an entity hidden inside a room the viewer
    /// is drawing — `docs/memory/one-camera-or-the-cull-lies.md`, applied to visibility instead of
    /// to the camera.
    /// </remarks>
    public ReadOnlySpan<bool> VisibleByLeaf => _visibility.VisibleByLeaf;

    /// <summary>How many corners the last call's runs cover, against the whole world's.</summary>
    /// <remarks>
    /// **The number that actually says what was saved.** Runs and batches are draw calls; corners
    /// are the work. A cull that halves the geometry while slightly increasing the draw-call count
    /// is a win, and counting only the calls would read it as a loss.
    /// </remarks>
    public (int Drawn, int Total) Corners { get; private set; }

    /// <summary>Which BSP area holds the map's 3D skybox room, or −1 when it has none.</summary>
    /// <remarks>
    /// **The area of the leaf the map's <c>sky_camera</c> stands in.** Measured on the corpus:
    /// `koth_harvest_final` puts it in area 1, holding 9 of 2074 leaves; `cp_fulgur` in area 16,
    /// holding 18 of 14264. A small room in both, which is what makes the area the discriminator —
    /// an area holding most of the map would mean filtering by it deletes the level.
    ///
    /// **−1 means every leaf is drawn by the main pass**, which is what every map without a
    /// `sky_camera` needs and what this viewer did for every map before the sky pass existed.
    /// </remarks>
    public int SkyArea { get; init; } = -1;

    /// <summary>The runs making up the 3D skybox room, from the last cull.</summary>
    /// <remarks>
    /// Empty rather than null when there is no sky room, so a caller draws nothing without having
    /// to distinguish "no sky" from "not culled yet" — the distinction that matters is on
    /// <see cref="Batches"/>, whose null means "draw everything".
    /// </remarks>
    public IReadOnlyList<WorldBatch> SkyBatches { get; private set; } = [];

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

        // **Valve swaps the AREA BITS between the two views, and this is the same split stated as
        // two lists** (`viewrender.cpp:4877`). The sky pass sets exactly one area bit and draws
        // that room; the main view draws with the ordinary bits and therefore does not.
        //
        // **Both halves are load-bearing and only one is obvious.** Without the sky pass the
        // miniature room is missing; without excluding it from the MAIN pass it is still out there
        // in the world at its literal size, which is the half B152 is actually about.
        _mainLeaves.Clear();
        _skyLeaves.Clear();

        for (int at = 0; at < leaves.Count; at++)
        {
            if (SkyArea >= 0 && _tree.Area(leaves[at]) == SkyArea)
            {
                _skyLeaves.Add(leaves[at]);
            }
            else
            {
                _mainLeaves.Add(leaves[at]);
            }
        }

        SkyBatches = _skyLeaves.Count > 0
            ? _skySurfaces.Batches(_skyLeaves, frustum)
            : [];

        IReadOnlyList<WorldBatch> runs = _surfaces.Batches(_mainLeaves, frustum);

        int drawn = 0;

        for (int at = 0; at < runs.Count; at++)
        {
            drawn += runs[at].VertexCount;
        }

        Corners = (drawn, _worldCorners);

        return runs;
    }
}
