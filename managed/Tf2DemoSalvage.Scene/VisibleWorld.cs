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
