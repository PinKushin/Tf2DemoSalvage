using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A displacement the world walk reached (B457).</summary>
/// <param name="Face">Its face.</param>
/// <param name="InView">Whether its box survives the frustum — whether its overlays are queued (<c>0x1800c61f0</c>).</param>
public readonly record struct ReachedDisplacement(int Face, bool InView);

/// <summary>
/// The world surfaces a view can see, gathered from its visible leaves into drawable runs.
/// </summary>
/// <remarks>
/// **The second half of `BuildWorldLists`.** <see cref="WorldVisibility"/> answers which leaves are
/// in view; this turns that into the surfaces to draw. The engine does the same two steps in one
/// pass — it accumulates surfaces into per-material lists as it walks — and they are separated here
/// because the walk is testable against a hand-built tree and the gather is testable against
/// hand-built spans, where one combined function would be testable against neither.
///
/// **A face is named by every leaf it touches, so a stamp is required rather than optional.** A wall
/// spanning a doorway appears in the leaves on both sides; gathering without a mark draws it twice,
/// which for opaque geometry is invisible and wasteful and for anything blended is wrong. Valve
/// marks the surface as it adds it and skips it afterwards, and the mark is a FRAME NUMBER rather
/// than a boolean so it never has to be cleared — clearing thirteen thousand flags a frame would
/// cost more than the gather.
///
/// **The output is ordinary <see cref="WorldBatch"/> runs, which is what makes this cheap to
/// adopt.** The renderer's existing opaque path takes a list of batches and binds a material per
/// batch; it does not care whether the list was built once at load or rebuilt this frame.
///
/// **Why runs merge at all.** The build appends one material group at a time, so spans arrive in
/// buffer order and a material's faces are adjacent. Walking spans in that order and merging
/// neighbours that survived produces a handful of runs per material rather than one per face — no
/// sorting, no index buffer, and the same draw-call count as before wherever a whole material
/// survives.
/// </remarks>
public sealed class VisibleWorld
{
    private readonly IReadOnlyList<WorldFaceSpan> _spans;
    private readonly BspLeafTree _tree;
    private readonly BspLeafFaces _leafFaces;

    /// <summary>Which frame each face was last gathered in — Valve's surface vis-frame.</summary>
    /// <remarks>
    /// **A frame number rather than a flag, so nothing has to be reset.** Indexed by face, sized
    /// from the highest face any span names. Faces the world build dropped — tool materials, faces
    /// outside the play area, brush-entity faces — simply never appear in a span and so are never
    /// gathered, which is the correct outcome and needs no separate list.
    /// </remarks>
    private readonly int[] _stamped;

    private readonly List<WorldBatch> _batches = [];

    private int _frame;

    /// <summary>Prepares a gather over one map's surfaces.</summary>
    /// <param name="spans">Where each face's triangles are, in buffer order.</param>
    /// <param name="tree">The map's leaves, for their face ranges.</param>
    /// <param name="leafFaces">The LEAFFACES lump the ranges index.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public VisibleWorld(
        IReadOnlyList<WorldFaceSpan> spans, BspLeafTree tree, BspLeafFaces leafFaces)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(leafFaces);

        _spans = spans;
        _tree = tree;
        _leafFaces = leafFaces;

        int highest = -1;

        for (int at = 0; at < spans.Count; at++)
        {
            highest = Math.Max(highest, spans[at].Face);
        }

        // **Sized by the highest face a span names, not by the map's face count.** The build drops
        // faces — tool materials, brush-entity faces, anything outside the play area — and a face it
        // dropped can never be gathered because no span refers to it. Sizing from the spans means
        // the stamp array is exactly as large as it can usefully be, and a leaf naming a dropped
        // face is skipped by the bounds check rather than by a second list.
        _stamped = new int[highest + 1];

        // **Which spans no leaf can reach, worked out once.** `EmitLeaf` builds a leaf's face list
        // from its portals and its detail faces; a DISPLACEMENT is neither, so not one of them
        // appears. Measured on cp_process: 12,306 of 12,306 brush spans reachable, 0 of 60 terrain
        // spans — 36,864 corners, a quarter of the world's geometry, and all of the ground.
        //
        // These are culled by their own box against the frustum and never by the PVS, which is the
        // only safe thing to do with a surface whose visibility the tree cannot answer for.
        HashSet<int> named = [];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = leafFaces.Face(first + entry);

                if (face >= 0)
                {
                    named.Add(face);
                }
            }
        }

        List<int> unreachable = [];

        for (int at = 0; at < spans.Count; at++)
        {
            if (!named.Contains(spans[at].Face))
            {
                unreachable.Add(at);
            }
        }

        _unreachable = [.. unreachable];

        // Which spans each face owns, for the blended gather: a face's spans are
        // _faceSpans[_faceStart[face] .. _faceStart[face + 1]).
        _faceStart = new int[highest + 2];

        for (int at = 0; at < spans.Count; at++)
        {
            _faceStart[spans[at].Face + 1]++;
        }

        for (int face = 0; face <= highest; face++)
        {
            _faceStart[face + 1] += _faceStart[face];
        }

        _faceSpans = new int[spans.Count];

        int[] filled = new int[highest + 1];

        for (int at = 0; at < spans.Count; at++)
        {
            int face = spans[at].Face;

            _faceSpans[_faceStart[face] + filled[face]++] = at;
        }

        // **The engine's water bit** (0x180100b60): every surface of a leaf with a water data ID, except
        // displacements and warp faces (0x10800).
        _underwater = new bool[highest + 1];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            if (tree.WaterDataId(leaf) == -1)
            {
                continue;
            }

            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = leafFaces.Face(first + entry);

                if (Span(face) is { } span && span.Displacement < 0 && (span.Flags & SurfaceProperties.Warp) == 0)
                {
                    _underwater[face] = true;
                }
            }
        }

        // **Each leaf's displacements, as the collision loader files them** (0x18016fa20, 0x18016f470, 0x180170210):
        // every displacement in DISPINFO order, walked down the tree by its box, appended to each leaf it reaches.
        List<int>[] byLeaf = new List<int>[tree.LeafCount];
        List<int> reached = [];

        foreach (int at in Enumerable.Range(0, spans.Count)
                     .Where(at => spans[at].Displacement >= 0 && _faceSpans[_faceStart[spans[at].Face]] == at)
                     .OrderBy(at => spans[at].Displacement))
        {
            reached.Clear();
            tree.LeavesTouchingBox(spans[at].Min, spans[at].Max, reached);

            foreach (int leaf in reached.Where(leaf => leaf >= 0 && leaf < byLeaf.Length))
            {
                (byLeaf[leaf] ??= []).Add(at);
            }
        }

        _leafDisplacementStart = new int[tree.LeafCount + 1];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            _leafDisplacementStart[leaf + 1] = _leafDisplacementStart[leaf] + (byLeaf[leaf]?.Count ?? 0);
        }

        _leafDisplacements = [.. byLeaf.Where(list => list is not null).SelectMany(list => list)];

        // **The water sort group** (0x180104530, at load). 0x180100b60 walks the tree, skips a leaf whose contents are
        // exactly CONTENTS_SOLID, and ORs 0x20000 (a water leaf) or 0x40000 into every surface it lists other than a
        // displacement or a water surface (0x10800), then into every displacement on its displacement list.
        const int Wet = 1, Dry = 2;
        byte[] water = new byte[highest + 1];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            if (tree.Contents(leaf) == SolidOnly)
            {
                continue;
            }

            byte bit = tree.WaterDataId(leaf) == -1 ? (byte)Dry : (byte)Wet;
            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = leafFaces.Face(first + entry);

                if (Span(face) is { } span && span.Displacement < 0 && (span.Flags & SurfaceProperties.Warp) == 0)
                {
                    water[face] |= bit;
                }
            }

            foreach (int at in byLeaf[leaf] ?? [])
            {
                water[spans[at].Face] |= bit;
            }
        }

        // Then the group: a water surface 3, both bits 2, under water alone 1, anything else 0.
        _sortGroup = new byte[highest + 1];

        for (int at = 0; at < spans.Count; at++)
        {
            int face = spans[at].Face;

            _sortGroup[face] = (spans[at].Flags & SurfaceProperties.Warp) != 0
                ? (byte)3
                : water[face] switch
                {
                    Wet | Dry => (byte)2,
                    Wet => (byte)1,
                    _ => (byte)0,
                };
        }
    }

    /// <summary>A leaf's contents when it is solid and nothing else — the value <c>0x180100b60</c> skips.</summary>
    private const int SolidOnly = 1;

    private readonly byte[] _sortGroup;

    /// <summary>A face's water sort group (<c>MAT_SORT_GROUP_*</c>, <c>ivrenderview.h:50</c>), as <c>0x180104530</c> assigns it.</summary>
    /// <param name="face">The face.</param>
    /// <returns>0 above water, 1 under water, 2 intersecting the water's surface, 3 the water surface; 0 for a face the
    /// build dropped.</returns>
    public int SortGroup(int face) => face >= 0 && face < _sortGroup.Length ? _sortGroup[face] : 0;

    private readonly int[] _faceStart;
    private readonly int[] _faceSpans;

    private readonly List<WorldBatch> _blendedRuns = [];
    private readonly List<int> _blendedStarts = [];

    /// <summary>The last <see cref="Surfaces"/> walk's translucent and additive runs, grouped by leaf place (B261).</summary>
    /// <param name="blended">Whether a material index is translucent or additive.</param>
    /// <param name="separate">Whether a face carries overlays, which <c>0x1800e4fd0</c> draws straight after that face
    /// (B457), so its run is its own and names it; null for none.</param>
    /// <returns>The runs, one group per leaf the walk reached, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blended"/> is null.</exception>
    /// <remarks>
    /// **Per leaf because the engine draws them per leaf** (<c>DrawTranslucentWorldAndDetailPropsInLeaves</c>,
    /// <c>viewrender.cpp:4313</c>), between the translucent entities (B426).
    ///
    /// **A surface joins the leaf the walk is in when it reaches it** (B261). <c>R_DrawLeaf</c> (<c>0x1800df9d0</c>)
    /// appends its leaf to the world list first (<c>0x1800e8820</c>), and <c>R_DrawSurface</c> (<c>0x1800dfbb0</c>)
    /// appends a TRANS surface to the chain of list entry <c>count − 1</c> — as <c>0x1800db7b0</c> does a translucent
    /// displacement. So a node surface goes with the last leaf of the near subtree, and a surface the walk does not
    /// draw — facing away, already marked — joins no leaf. **Within a leaf, sort group by group, 0 to 3, and in each the
    /// last surface first**, then the group's displacements in the order reached: <c>0x1800e4fd0</c>
    /// (<c>DrawTranslucentSurfaces</c>), its outer loop over the group table <c>0x18038ea98</c>.
    ///
    /// A displacement whose box is out of view is left out, where the engine draws it; it is off screen either way.
    /// </remarks>
    public TranslucentLeafRuns BlendedByLeaf(Func<int, bool> blended, Func<int, bool>? separate = null)
    {
        ArgumentNullException.ThrowIfNull(blended);

        _separate = separate;
        _blendedRuns.Clear();
        _blendedStarts.Clear();
        _runFaces.Clear();
        _runDisplacement.Clear();
        _runGroup.Clear();

        int surface = 0;
        int displacement = 0;

        for (int place = 0; place < _walkLeaves; place++)
        {
            _blendedStarts.Add(_blendedRuns.Count);

            int first = surface;
            int firstDisplacement = displacement;

            while (surface < _surfaces.Count && _surfacePlace[surface] == place)
            {
                surface++;
            }

            while (displacement < _displacements.Count && _displacementPlace[displacement] == place)
            {
                displacement++;
            }

            // Group by group, 0 to 3 (0x1800e4fd0 walks the table 0x18038ea98 forwards).
            for (int group = 0; group < SortGroups; group++)
            {
                for (int at = surface - 1; at >= first; at--)
                {
                    if (SortGroup(_surfaces[at]) == group)
                    {
                        AddFace(_surfaces[at], blended, isDisplacement: false);
                    }
                }

                for (int at = firstDisplacement; at < displacement; at++)
                {
                    if (_displacements[at].InView && SortGroup(_displacements[at].Face) == group)
                    {
                        AddFace(_displacements[at].Face, blended, isDisplacement: true);
                    }
                }
            }
        }

        _blendedStarts.Add(_blendedRuns.Count);

        return new TranslucentLeafRuns(_blendedRuns, _blendedStarts, _runFaces, _runDisplacement, _runGroup);
    }

    /// <summary>How many water sort groups there are — <c>MAX_MAT_SORT_GROUPS</c>, <c>ivrenderview.h:57</c>.</summary>
    public const int SortGroups = 4;

    private readonly List<int> _runGroup = [];

    /// <summary>Appends every span of one face.</summary>
    private void AddFace(int face, Func<int, bool> blended, bool isDisplacement)
    {
        for (int at = _faceStart[face]; at < _faceStart[face + 1]; at++)
        {
            AddBlended(_faceSpans[at], blended, isDisplacement);
        }
    }

    private Func<int, bool>? _separate;
    private readonly List<int> _runFaces = [];
    private readonly List<bool> _runDisplacement = [];

    /// <summary>Appends a blended span to the current leaf's runs, continuing the last run when it follows it.</summary>
    private void AddBlended(int index, Func<int, bool> blended, bool displacement)
    {
        WorldFaceSpan span = _spans[index];

        if (!blended(span.MaterialIndex))
        {
            return;
        }

        bool own = _separate?.Invoke(span.Face) ?? false;
        int last = _blendedRuns.Count - 1;
        WorldBatch run = last >= 0 ? _blendedRuns[last] : default;

        if (!own &&
            last >= _blendedStarts[^1] &&
            _runFaces[last] < 0 &&
            _runGroup[last] == SortGroup(span.Face) &&
            _runDisplacement[last] == displacement &&
            run.MaterialIndex == span.MaterialIndex &&
            run.Category == span.Category &&
            run.FirstVertex + run.VertexCount == span.FirstVertex)
        {
            _blendedRuns[last] = run with { VertexCount = run.VertexCount + span.VertexCount };
            return;
        }

        _blendedRuns.Add(new WorldBatch(
            span.MaterialIndex, span.FirstVertex, span.VertexCount, Category: span.Category));
        _runFaces.Add(own ? span.Face : -1);
        _runDisplacement.Add(displacement);
        _runGroup.Add(SortGroup(span.Face));
    }

    private readonly List<int> _surfaces = [];

    /// <summary>Which surfaces walk last marked each face — a frame number, as <see cref="_stamped"/> is.</summary>
    private int[]? _marked;

    private int _markFrame;

    private readonly List<ReachedDisplacement> _displacements = [];

    /// <summary>The displacements the last <see cref="Surfaces"/> walk reached, in the order reached (B457).</summary>
    public IReadOnlyList<ReachedDisplacement> Displacements => _displacements;

    /// <summary>The faces a walk reaches, in the order the engine hands them to <c>R_DrawSurface</c> (B457).</summary>
    /// <param name="walk">The walk, as <see cref="WorldVisibility.Walk"/> records it, without the leaves this view does not draw.</param>
    /// <param name="x">The eye.</param>
    /// <param name="y">The eye.</param>
    /// <param name="z">The eye.</param>
    /// <param name="frustum">The view volume, for each displacement's own box (<c>0x1800c0f90</c>).</param>
    /// <param name="twoSided">Whether a material is <c>$nocull</c> — the surface's NOCULL bit; null for none.</param>
    /// <returns>Brush face indices, valid until the next call; the displacements are <see cref="Displacements"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="walk"/> is null.</exception>
    /// <remarks>
    /// **Read from engine.dll (x64, live), in disassembly.** <c>R_DrawLeaf</c> (<c>0x1800df9d0</c>) first reaches the
    /// leaf's displacements (<c>0x1800db7b0</c>: each not yet marked, in the leaf's list order), then marks its node
    /// surfaces, then marks and draws each other surface not already marked that is NOCULL (0x200) or whose UNFLIPPED
    /// plane the eye is in front of, <c>dot − dist ≥ −0.01</c>. <c>R_RecursiveWorldNode</c> (<c>0x1800e0600</c>)
    /// draws, between a node's children, each of its surfaces marked this frame that is in a water leaf (0x20000,
    /// <c>0x180100b60</c>) or whose plane-back flag equals the eye's side.
    ///
    /// A displacement is reached whatever its box; its box decides only whether its overlays are queued
    /// (<c>0x1800c61f0</c>), which is why <see cref="ReachedDisplacement.InView"/> rides along rather than filtering.
    /// </remarks>
    public IReadOnlyList<int> Surfaces(
        IReadOnlyList<WorldWalkStep> walk,
        float x,
        float y,
        float z,
        ViewFrustum frustum = default,
        Func<int, bool>? twoSided = null)
    {
        ArgumentNullException.ThrowIfNull(walk);

        _marked ??= new int[_stamped.Length];
        _markFrame++;
        _surfaces.Clear();
        _displacements.Clear();
        _surfacePlace.Clear();
        _displacementPlace.Clear();
        _walkLeaves = 0;

        foreach (WorldWalkStep step in walk)
        {
            if (step.IsNode)
            {
                if (_tree.Node(step.Index) is not { } node)
                {
                    continue;
                }

                for (int face = node.FirstFace; face < node.FirstFace + node.FaceCount; face++)
                {
                    if (Span(face) is { } span && _marked[face] == _markFrame &&
                        (_underwater[face] || span.PlaneBack == step.EyeBehind))
                    {
                        _surfaces.Add(face);
                        _surfacePlace.Add(_walkLeaves - 1);
                    }
                }

                continue;
            }

            // The leaf joins the world list before anything it reaches (0x1800e8820, called first by R_DrawLeaf).
            _walkLeaves++;

            for (int at = _leafDisplacementStart[step.Index]; at < _leafDisplacementStart[step.Index + 1]; at++)
            {
                WorldFaceSpan terrain = _spans[_leafDisplacements[at]];

                if (_marked[terrain.Face] == _markFrame)
                {
                    continue;
                }

                _marked[terrain.Face] = _markFrame;
                _displacements.Add(new ReachedDisplacement(
                    terrain.Face,
                    !frustum.Cull(terrain.Min.X, terrain.Min.Y, terrain.Min.Z, terrain.Max.X, terrain.Max.Y, terrain.Max.Z)));
                _displacementPlace.Add(_walkLeaves - 1);
            }

            (int first, int count) = _tree.LeafFaces(step.Index);

            for (int entry = 0; entry < count; entry++)
            {
                int face = _leafFaces.Face(first + entry);

                if (Span(face) is not { } span || _marked[face] == _markFrame)
                {
                    continue;
                }

                _marked[face] = _markFrame;

                if (!span.OnNode &&
                    ((twoSided?.Invoke(span.MaterialIndex) ?? false) ||
                     (span.Plane.X * x) + (span.Plane.Y * y) + (span.Plane.Z * z) - span.Plane.Distance >= -0.01f))
                {
                    _surfaces.Add(face);
                    _surfacePlace.Add(_walkLeaves - 1);
                }
            }
        }

        return _surfaces;
    }

    /// <summary>Per reached surface, the place of the leaf the walk was in — its world-list entry (B261).</summary>
    private readonly List<int> _surfacePlace = [];

    /// <summary>Per reached displacement, the place of the leaf that reached it (B261).</summary>
    private readonly List<int> _displacementPlace = [];

    /// <summary>How many leaves the last walk drew — the engine's world-list count.</summary>
    private int _walkLeaves;

    /// <summary>Per face, whether it lies in a water leaf — the engine's 0x20000 (<c>0x180100b60</c>).</summary>
    private readonly bool[] _underwater;

    /// <summary>Each leaf's displacement spans, by DISPINFO index: leaf L holds
    /// <c>_leafDisplacements[_leafDisplacementStart[L] .. _leafDisplacementStart[L + 1])</c>.</summary>
    private readonly int[] _leafDisplacementStart;

    private readonly int[] _leafDisplacements;

    /// <summary>A face's span, or null for a face the build dropped.</summary>
    private WorldFaceSpan? Span(int face) =>
        face >= 0 && face < _stamped.Length && _faceStart[face] < _faceStart[face + 1]
            ? _spans[_faceSpans[_faceStart[face]]]
            : null;

    /// <summary>Spans no leaf names, which must be culled by their own box or not at all.</summary>
    private readonly int[] _unreachable;

    /// <summary>How many spans the leaf lists cannot reach.</summary>
    /// <remarks>
    /// **Reported so a map where this is LARGE is visible as a fact rather than as a missing
    /// wall.** Sixty is displacements. Twelve thousand would mean the leaf-face lump was misread,
    /// and the picture would look perfect — because everything unreachable is drawn — while the cull
    /// quietly did nothing.
    /// </remarks>
    public int UnreachableSpans => _unreachable.Length;

    /// <summary>Whether this map carried enough to cull its world at all.</summary>
    /// <remarks>
    /// **Three things have to be present and any one missing means draw everything.** Without spans
    /// there is no face-to-vertex map; without leaf face ranges a leaf names nothing; without the
    /// LEAFFACES lump the ranges point at nothing. A caller asks this once and falls back to the
    /// batches built at load — which is slower and always correct.
    /// </remarks>
    public bool CanCull => _spans.Count > 0 && _leafFaces.HasData && _tree.LeafCount > 0;

    /// <summary>The runs to draw for one set of visible leaves.</summary>
    /// <param name="leaves">Visible leaf indices, as <see cref="WorldVisibility.Leaves"/> returns.</param>
    /// <param name="frustum">The view volume, for the surfaces no leaf names.</param>
    /// <returns>Drawable runs, merged where they are adjacent. Valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="leaves"/> is null.</exception>
    /// <remarks>
    /// **Three passes, and the order of them is what keeps this linear.** Every visible leaf's faces
    /// are stamped with this frame's number; then the spans no leaf can reach are stamped if their
    /// own box is on screen; then the spans are walked once in buffer order, keeping the stamped
    /// ones and merging neighbours. Gathering in leaf order instead would give runs in visibility
    /// order, which is scattered across the buffer, and would need a sort to put them back.
    ///
    /// **The returned list is REUSED**, like the leaf list before it. A caller that needs to keep it
    /// must copy it.
    /// </remarks>
    public IReadOnlyList<WorldBatch> Batches(IReadOnlyList<int> leaves, ViewFrustum frustum)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        _batches.Clear();
        _frame++;

        for (int at = 0; at < leaves.Count; at++)
        {
            (int first, int count) = _tree.LeafFaces(leaves[at]);

            for (int entry = 0; entry < count; entry++)
            {
                int face = _leafFaces.Face(first + entry);

                if (face >= 0 && face < _stamped.Length)
                {
                    _stamped[face] = _frame;
                }
            }
        }

        // **The surfaces no leaf speaks for, tested against their own boxes.** Displacements are the
        // whole of this set on a TF2 map, and they are the ground. The frustum alone, never the PVS:
        // a surface the tree cannot place is a surface whose potential visibility nothing knows, so
        // the only defensible filter is whether it is on screen.
        for (int at = 0; at < _unreachable.Length; at++)
        {
            WorldFaceSpan span = _spans[_unreachable[at]];

            if (!frustum.Cull(
                span.Min.X, span.Min.Y, span.Min.Z, span.Max.X, span.Max.Y, span.Max.Z))
            {
                _stamped[span.Face] = _frame;
            }
        }

        WorldBatch? open = null;

        for (int at = 0; at < _spans.Count; at++)
        {
            WorldFaceSpan span = _spans[at];

            if (_stamped[span.Face] != _frame)
            {
                continue;
            }

            // Merge onto the run in hand when this face continues it: same material, same category,
            // and its vertices begin exactly where the run ends.
            if (open is { } run &&
                run.MaterialIndex == span.MaterialIndex &&
                run.Category == span.Category &&
                run.FirstVertex + run.VertexCount == span.FirstVertex)
            {
                open = run with { VertexCount = run.VertexCount + span.VertexCount };

                continue;
            }

            if (open is { } finished)
            {
                _batches.Add(finished);
            }

            open = new WorldBatch(
                span.MaterialIndex, span.FirstVertex, span.VertexCount, Category: span.Category);
        }

        if (open is { } last)
        {
            _batches.Add(last);
        }

        return _batches;
    }
}
