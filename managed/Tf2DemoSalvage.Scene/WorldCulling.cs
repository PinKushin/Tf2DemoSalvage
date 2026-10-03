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
    private readonly WorldVisibility _skyVisibility;
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

        // **And a second visibility too, for the same reason and a sharper one.** `WorldVisibility`
        // holds `VisibleByLeaf`, which the ENTITY cull reads as "what the world decided" — so a
        // second query from the sky camera through the same object would tell the entity cull that
        // the player can see whatever the sky room can, and hide every entity in the level.
        _skyVisibility = new WorldVisibility(tree, pvs);
        _tree = tree;

        TotalLeaves = tree.LeafCount;

        _positionByLeaf = new int[tree.LeafCount];
        Array.Fill(_positionByLeaf, -1);

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
    /// is drawing — `docs/memory/instrument-bugs-outnumber-decoder-bugs.md#one-camera-or-the-cull-lies`, applied to visibility instead of
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

    /// <summary>The runs making up the 3D skybox room, seen from the sky view's own eye.</summary>
    /// <param name="x">The SKY view's position — not the player's.</param>
    /// <param name="y">The sky view's position.</param>
    /// <param name="z">The sky view's position.</param>
    /// <param name="frustum">The sky view's volume.</param>
    /// <returns>The room's runs, valid until the next call; empty when there is no sky room.</returns>
    /// <remarks>
    /// **Visibility is computed from the SKY CAMERA, and getting that wrong makes the whole feature
    /// silently draw nothing.** `CSkyboxView::DrawInternal` sets vis up at the sky camera's own
    /// origin before it draws (<c>viewrender.cpp:4900</c>):
    ///
    /// <code>
    ///   render-&gt;ViewSetupVis( false, 1, &amp;m_pSky3dParams-&gt;origin.Get() );
    /// </code>
    ///
    /// under a comment admitting it should not have to. **Measured before this was understood:
    /// partitioning the PLAYER's PVS gave `sky area 1 contributes 0 runs` on
    /// `koth_harvest_final` — correct, and useless. A player standing in the map can never see
    /// into a room built ten thousand units away, so the room is in nobody's PVS but its own.
    ///
    /// **The area filter stays**, because a vis query from inside the sky room can still reach
    /// leaves outside it, and those belong to the main pass.
    /// </remarks>
    public IReadOnlyList<WorldBatch> SkyRunsFrom(float x, float y, float z, ViewFrustum frustum)
    {
        if (!CanCull || SkyArea < 0)
        {
            return [];
        }

        IReadOnlyList<int> seen = _skyVisibility.Leaves(x, y, z, frustum);

        _skyLeaves.Clear();

        for (int at = 0; at < seen.Count; at++)
        {
            if (_tree.Area(seen[at]) == SkyArea)
            {
                _skyLeaves.Add(seen[at]);
            }
        }

        return _skyLeaves.Count > 0 ? _skySurfaces.Batches(_skyLeaves, frustum) : [];
    }

    /// <summary>What kind of sky the eye can see — <c>ComputeSkyboxVisibility</c>.</summary>
    /// <param name="x">The MAIN view's eye, which is what Valve passes.</param>
    /// <param name="y">The main view's eye.</param>
    /// <param name="z">The main view's eye.</param>
    /// <returns>Which sky the leaf there sees.</returns>
    /// <remarks>
    /// <c>CSkyboxView::ComputeSkyboxVisibility</c> is
    /// <c>engine-&gt;IsSkyboxVisibleFromPoint( origin )</c> (<c>viewrender.cpp:4775</c>), and
    /// <c>origin</c> at that point is still the MAIN view's — the sky camera's offset is applied
    /// later, inside <c>DrawInternal</c>. Asking from the sky's eye instead would answer whether
    /// the sky room can see a sky face, which is not the question.
    /// </remarks>
    public SkyboxVisibility SkyVisibleFrom(float x, float y, float z) =>
        _tree.SkyboxVisibleFrom(x, y, z);

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
    /// <param name="twoSided">Whether a material is <c>$nocull</c>, for the overlay walk's leaf test (B457); null for none.</param>
    public IReadOnlyList<WorldBatch>? Batches(
        float x, float y, float z, ViewFrustum frustum, Func<int, bool>? twoSided = null)
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

        IReadOnlyList<WorldBatch> runs = _surfaces.Batches(_mainLeaves, frustum);

        // **The same walk, in the order it reached the surfaces**, for the overlay queue (B457) — without the sky
        // room's leaves, which this view does not draw.
        _mainWalk.Clear();

        foreach (WorldWalkStep step in _visibility.Walk)
        {
            if (step.IsNode || SkyArea < 0 || _tree.Area(step.Index) != SkyArea)
            {
                _mainWalk.Add(step);
            }
        }

        Surfaces = _surfaces.Surfaces(_mainWalk, x, y, z, frustum, twoSided);

        // Each main leaf's place in the list, for the translucent pass (B426): the previous view's places cleared.
        for (int at = 0; at < _placed.Count; at++)
        {
            _positionByLeaf[_placed[at]] = -1;
        }

        _placed.Clear();

        for (int at = 0; at < _mainLeaves.Count; at++)
        {
            if (_mainLeaves[at] >= 0 && _mainLeaves[at] < _positionByLeaf.Length)
            {
                _positionByLeaf[_mainLeaves[at]] = at;
                _placed.Add(_mainLeaves[at]);
            }
        }

        _lastFrustum = frustum;

        int drawn = 0;

        for (int at = 0; at < runs.Count; at++)
        {
            drawn += runs[at].VertexCount;
        }

        Corners = (drawn, _worldCorners);

        return runs;
    }

    /// <summary>The BSP leaf at a place in the last view's front-to-back list (B434).</summary>
    /// <param name="place">The place.</param>
    /// <returns>The leaf, or −1 for a place outside the list.</returns>
    public int LeafAt(int place) => place >= 0 && place < _mainLeaves.Count ? _mainLeaves[place] : -1;

    private readonly List<WorldWalkStep> _mainWalk = [];

    /// <summary>The faces the last <see cref="Batches"/> walk reached, in <c>R_DrawSurface</c> order; null before one.</summary>
    /// <remarks>What the overlay queue is built from (B457) — see <see cref="VisibleWorld.Surfaces"/>.</remarks>
    public IReadOnlyList<int>? Surfaces { get; private set; }

    /// <summary>The displacements the same walk reached, in order (B457).</summary>
    public IReadOnlyList<ReachedDisplacement> Displacements => _surfaces.Displacements;

    private readonly List<int> _placed = [];
    private readonly int[] _positionByLeaf;
    private ViewFrustum _lastFrustum;

    /// <summary>The last view's translucent and additive world runs, by leaf place — the engine's per-leaf alpha lists.</summary>
    /// <param name="blended">Whether a material index is translucent or additive.</param>
    /// <returns>The runs over the leaves <see cref="Batches"/> last drew, valid until the next call; null when this map cannot be culled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blended"/> is null.</exception>
    /// <remarks>See <see cref="VisibleWorld.BlendedByLeaf"/> (B426).</remarks>
    /// <param name="separate">Whether a face carries overlays, so its run is its own (B457); null for none.</param>
    public TranslucentLeafRuns? BlendedRuns(Func<int, bool> blended, Func<int, bool>? separate = null) =>
        CanCull ? _surfaces.BlendedByLeaf(_mainLeaves, _lastFrustum, blended, _positionByLeaf, separate) : null;

    /// <summary>The place in the last view's leaf list of the nearest leaf a box touches, or −1.</summary>
    /// <param name="minX">The box, in world space.</param>
    /// <param name="minY">The box, in world space.</param>
    /// <param name="minZ">The box, in world space.</param>
    /// <param name="maxX">The box, in world space.</param>
    /// <param name="maxY">The box, in world space.</param>
    /// <param name="maxZ">The box, in world space.</param>
    /// <returns>A translucent entity's <c>m_iWorldListInfoLeaf</c> (<c>ComputeTranslucentRenderLeaf</c>, B426).</returns>
    public int PositionOf(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =>
        _tree.NearestRank(minX, minY, minZ, maxX, maxY, maxZ, _positionByLeaf);
}
