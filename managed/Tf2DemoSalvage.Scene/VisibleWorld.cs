using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

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

        _claimed = new int[highest + 1];
    }

    private readonly int[] _faceStart;
    private readonly int[] _faceSpans;

    /// <summary>Which blended gather last gave each face to a leaf — a frame number, as <see cref="_stamped"/> is.</summary>
    private readonly int[] _claimed;

    private int _claimFrame;

    private readonly List<WorldBatch> _blendedRuns = [];
    private readonly List<int> _blendedStarts = [];
    private readonly List<(int Position, int Span)> _placedUnreachable = [];

    /// <summary>The blended runs for one set of visible leaves, grouped by each leaf's place in that list.</summary>
    /// <param name="leaves">Visible leaf indices, front to back, as <see cref="WorldVisibility.Leaves"/> returns.</param>
    /// <param name="frustum">The view volume, for the surfaces no leaf names.</param>
    /// <param name="blended">Whether a material index is translucent or additive.</param>
    /// <param name="positionByLeaf">Each leaf's place in <paramref name="leaves"/>, −1 when absent; for placing the surfaces no leaf names.</param>
    /// <returns>The runs, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="leaves"/> or <paramref name="blended"/> is null.</exception>
    /// <remarks>
    /// **Per leaf because the engine draws them per leaf** (<c>DrawTranslucentWorldAndDetailPropsInLeaves</c>,
    /// <c>viewrender.cpp:4313</c>), between the translucent entities (B426).
    ///
    /// **A face goes with the NEAREST visible leaf naming it**, once. *Interpolated:* the engine adds a translucent
    /// surface to a leaf's list while its world walk visits it, which this port does not reproduce; both put it on the
    /// camera's side of the surface. **Within a leaf, the last face first**: <c>engine.dll</c> <c>0x1800e4fd0</c>
    /// (<c>DrawTranslucentSurfaces</c>) draws a leaf's brush surfaces from the end of its list down, and its
    /// displacements after them. The list here is the leaf's LEAFFACES order, not the engine's walk order —
    /// *interpolated*. A displacement is placed in the nearest listed leaf its box touches, or the nearest leaf of
    /// all when it touches none.
    /// </remarks>
    public TranslucentLeafRuns BlendedByLeaf(
        IReadOnlyList<int> leaves,
        ViewFrustum frustum,
        Func<int, bool> blended,
        ReadOnlySpan<int> positionByLeaf)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(blended);

        _blendedRuns.Clear();
        _blendedStarts.Clear();
        _placedUnreachable.Clear();
        _claimFrame++;

        for (int at = 0; at < _unreachable.Length; at++)
        {
            WorldFaceSpan span = _spans[_unreachable[at]];

            if (!blended(span.MaterialIndex) ||
                frustum.Cull(span.Min.X, span.Min.Y, span.Min.Z, span.Max.X, span.Max.Y, span.Max.Z))
            {
                continue;
            }

            int position = _tree.NearestRank(
                span.Min.X, span.Min.Y, span.Min.Z, span.Max.X, span.Max.Y, span.Max.Z, positionByLeaf);

            _placedUnreachable.Add((Math.Max(position, 0), _unreachable[at]));
        }

        _placedUnreachable.Sort();

        int placed = 0;

        for (int position = 0; position < leaves.Count; position++)
        {
            _blendedStarts.Add(_blendedRuns.Count);

            (int first, int count) = _tree.LeafFaces(leaves[position]);

            for (int entry = count - 1; entry >= 0; entry--)
            {
                int face = _leafFaces.Face(first + entry);

                if (face < 0 || face >= _claimed.Length || _claimed[face] == _claimFrame)
                {
                    continue;
                }

                _claimed[face] = _claimFrame;

                for (int at = _faceStart[face]; at < _faceStart[face + 1]; at++)
                {
                    AddBlended(_faceSpans[at], blended);
                }
            }

            for (; placed < _placedUnreachable.Count && _placedUnreachable[placed].Position == position; placed++)
            {
                AddBlended(_placedUnreachable[placed].Span, blended);
            }
        }

        _blendedStarts.Add(_blendedRuns.Count);

        return new TranslucentLeafRuns(_blendedRuns, _blendedStarts);
    }

    /// <summary>Appends a blended span to the current leaf's runs, continuing the last run when it follows it.</summary>
    private void AddBlended(int index, Func<int, bool> blended)
    {
        WorldFaceSpan span = _spans[index];

        if (!blended(span.MaterialIndex))
        {
            return;
        }

        int last = _blendedRuns.Count - 1;
        WorldBatch run = last >= 0 ? _blendedRuns[last] : default;

        if (last >= _blendedStarts[^1] &&
            run.MaterialIndex == span.MaterialIndex &&
            run.Category == span.Category &&
            run.FirstVertex + run.VertexCount == span.FirstVertex)
        {
            _blendedRuns[last] = run with { VertexCount = run.VertexCount + span.VertexCount };
            return;
        }

        _blendedRuns.Add(new WorldBatch(
            span.MaterialIndex, span.FirstVertex, span.VertexCount, Category: span.Category));
    }

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
