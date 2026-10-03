using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>One overlay clipped to one face: the engine's overlay fragment.</summary>
/// <param name="Face">The face it lies on, in the FACES lump.</param>
/// <param name="RenderOrder">The overlay's layer, 0 to 3 (B138).</param>
/// <param name="MaterialIndex">The overlay's material.</param>
/// <param name="FirstVertex">Where its triangles start in the world vertex buffer.</param>
/// <param name="VertexCount">How many corners.</param>
/// <param name="Fade">The overlay's own fade (lump 60), or null when it never fades.</param>
public readonly record struct OverlayFragment(
    int Face,
    int RenderOrder,
    int MaterialIndex,
    int FirstVertex,
    int VertexCount,
    OverlayFade? Fade = null);

/// <summary>
/// The overlay draws for one view, queued from the surfaces the world walk reached — <c>COverlayMgr</c>'s per-frame
/// render lists (B457).
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), in disassembly; the full account is B457.**
///
/// - A surface's fragment list is built by PREPENDING (<c>0x180111b30</c>, <c>0x1801123a1</c>..<c>0x180112421</c>),
///   overlays in lump order (<c>0x18010b1c0</c>), so it lists them last first.
/// - Per sort group, <c>0x1800e5e10</c> first draws the displacements (<c>0x1800e3a90</c> → <c>0x1800c61f0</c>):
///   their sort list flattened, each displacement whose box is in view queued, then <c>RenderOverlays</c> and a
///   clear. Only then <c>0x1800da3a0</c> walks the opaque brush surfaces by material sort ID, in the order each ID
///   was first reached, and each ID's surfaces in the order reached (<c>0x1800d1140</c> appends both), queueing each
///   surface's list. A translucent surface (texinfo SURF_TRANS or a translucent material, flag 0x20,
///   <c>0x1800fa3d0</c>) is on the other list, and its overlays are drawn with it (<see cref="ForTranslucentLeaves"/>).
/// - <c>0x18010a580</c> skips a fragment faded past its maximum and PREPENDS the rest to their material's bucket,
///   prepending the bucket to the list the first time it gets one that frame.
/// - <c>COverlayMgr::RenderOverlays</c> (<c>0x180110630</c>) walks the bucket list and each bucket from the head,
///   once per render order.
///
/// **A prepended list walked from its head is the appended list walked backwards**, so this appends and reads in
/// reverse rather than keeping linked lists: the same order, with nothing to unlink.
///
/// The sort ID is <see cref="WorldFaceSpan.SortId"/>, material and lightmap page as <see cref="LightmapSortIds"/>
/// reproduces the material system's allocation.
/// </remarks>
public sealed class OverlayRenderLists
{
    private readonly IReadOnlyList<OverlayFragment> _fragments;
    private readonly IReadOnlyList<WorldFaceSpan> _spans;

    /// <summary>Each face's fragments, last built first — the surface's own list (surface +0x14).</summary>
    private readonly Dictionary<int, List<int>> _byFace = [];

    private readonly Dictionary<int, WorldFaceSpan> _spanOf = [];

    // Reused per view.
    private readonly List<int> _sortOrder = [];
    private readonly Dictionary<int, List<(int Face, bool Queued)>> _sorted = [];
    private readonly List<int> _bucketOrder = [];
    private readonly Dictionary<int, List<int>> _buckets = [];
    private readonly List<WorldBatch> _drawn = [];

    /// <summary>Prepares the queue for one world.</summary>
    /// <param name="fragments">The overlay fragments, in build order.</param>
    /// <param name="spans">The world's faces, for each face's material and flags and for the order with no walk.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OverlayRenderLists(IReadOnlyList<OverlayFragment> fragments, IReadOnlyList<WorldFaceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(spans);

        _fragments = fragments;
        _spans = spans;

        for (int at = 0; at < fragments.Count; at++)
        {
            if (!_byFace.TryGetValue(fragments[at].Face, out List<int>? list))
            {
                list = [];
                _byFace[fragments[at].Face] = list;
            }

            // Prepended, as the engine links each new fragment before the surface's head.
            list.Insert(0, at);
        }

        foreach (WorldFaceSpan span in spans)
        {
            _spanOf.TryAdd(span.Face, span);
        }
    }

    /// <summary>Whether a face carries overlays.</summary>
    /// <param name="face">The face.</param>
    /// <returns>True when some fragment lies on it.</returns>
    public bool HasOverlays(int face) => _byFace.ContainsKey(face);

    /// <summary>The opaque world's overlay draws for one view, in the engine's order: displacements', then brush.</summary>
    /// <param name="surfaces">The brush faces the walk reached, in <c>R_DrawSurface</c> order; null for a world that
    /// cannot be walked, which queues every face in buffer order.</param>
    /// <param name="displacements">The displacements the walk reached, in order; ignored with no walk.</param>
    /// <param name="eye">The view origin, for the fade; null to fade nothing.</param>
    /// <param name="blended">Whether a material is translucent or additive.</param>
    /// <returns>The draws, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blended"/> is null.</exception>
    /// <remarks>
    /// **With no walk** there is no translucent leaf pass to carry a translucent surface's overlays, so they are
    /// queued after the brush surfaces — the uncullable map's only route to the screen, and named as ours in B457.
    /// </remarks>
    /// <param name="sortGroup">Each face's water sort group (<c>0x180104530</c>); null puts every face in group 0.</param>
    public IReadOnlyList<WorldBatch> Order(
        IReadOnlyList<int>? surfaces,
        IReadOnlyList<ReachedDisplacement>? displacements,
        (float X, float Y, float Z)? eye,
        Func<int, bool> blended,
        Func<int, int>? sortGroup = null)
    {
        ArgumentNullException.ThrowIfNull(blended);

        _drawn.Clear();

        // **Group by group, 3 down to 0** (0x1800e5e10's counter from 3 over the table 0x18038ea98), each its own
        // displacement batch and brush batch.
        for (int group = VisibleWorld.SortGroups - 1; group >= 0; group--)
        {
            bool InGroup(int face) => (sortGroup?.Invoke(face) ?? 0) == group;

            // The displacements' own batch, drawn first (0x1800e3a90 → 0x1800c61f0).
            ResetSort();

            if (surfaces is null)
            {
                foreach (WorldFaceSpan span in _spans)
                {
                    if (span.Displacement >= 0 && !Translucent(span, blended) && InGroup(span.Face))
                    {
                        Sort(span, queued: true);
                    }
                }
            }
            else
            {
                foreach (ReachedDisplacement reached in displacements ?? [])
                {
                    if (_spanOf.TryGetValue(reached.Face, out WorldFaceSpan span) && !Translucent(span, blended) &&
                        InGroup(span.Face))
                    {
                        Sort(span, reached.InView);
                    }
                }
            }

            QueueSorted(eye);
            Render(_drawn);

            // The brush surfaces' batch (0x1800da3a0).
            ResetSort();

            int reachedCount = surfaces?.Count ?? _spans.Count;

            for (int at = 0; at < reachedCount; at++)
            {
                if (surfaces is null && _spans[at].Displacement >= 0)
                {
                    continue;
                }

                int face = surfaces?[at] ?? _spans[at].Face;

                if (_spanOf.TryGetValue(face, out WorldFaceSpan span) && !Translucent(span, blended) && InGroup(face))
                {
                    Sort(span, queued: true);
                }
            }

            QueueSorted(eye);

            if (surfaces is null && group == 0)
            {
                foreach (WorldFaceSpan span in _spans)
                {
                    if (span.Displacement < 0 && Translucent(span, blended))
                    {
                        Queue(span.Face, eye);
                    }
                }
            }

            Render(_drawn);
        }

        return _drawn;
    }

    /// <summary>The overlays of some surfaces drawn together: their lists queued in order, then one render.</summary>
    /// <param name="faces">The faces, in the order queued.</param>
    /// <param name="eye">The view origin, for the fade; null to fade nothing.</param>
    /// <returns>A new list of draws.</returns>
    public IReadOnlyList<WorldBatch> OrderSurfaces(IReadOnlyList<int> faces, (float X, float Y, float Z)? eye)
    {
        ArgumentNullException.ThrowIfNull(faces);

        foreach (int face in faces)
        {
            Queue(face, eye);
        }

        List<WorldBatch> drawn = [];

        Render(drawn);

        return drawn;
    }

    /// <summary>Per translucent run, the overlays drawn straight after it — <c>DrawTranslucentSurfaces</c>.</summary>
    /// <param name="runs">The view's translucent runs, from <c>WorldCulling.BlendedRuns</c> with
    /// <see cref="HasOverlays"/> as the separator.</param>
    /// <param name="eye">The view origin, for the fade; null to fade nothing.</param>
    /// <returns>One list per run, empty where nothing follows it.</returns>
    /// <remarks>
    /// **Read, engine.dll x64 <c>0x1800e4fd0</c>:** per leaf, each translucent brush surface is drawn, then its own
    /// fragment list queued and <c>RenderOverlays</c> called and cleared; then the leaf's translucent displacements are
    /// drawn and <c>0x1800c61f0</c> queues all of theirs in view and renders them once.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<WorldBatch>> ForTranslucentLeaves(
        TranslucentLeafRuns runs, (float X, float Y, float Z)? eye)
    {
        ArgumentNullException.ThrowIfNull(runs);

        IReadOnlyList<WorldBatch>[] after = new IReadOnlyList<WorldBatch>[runs.Runs.Count];
        List<int> terrain = [];

        for (int position = 0; position < runs.LeafCount; position++)
        {
            (int first, int count) = runs.Leaf(position);

            terrain.Clear();

            for (int at = first; at < first + count; at++)
            {
                after[at] = [];

                int face = runs.RunFaces[at];

                if (face >= 0 && runs.RunDisplacement[at])
                {
                    terrain.Add(face);
                }
                else if (face >= 0)
                {
                    after[at] = OrderSurfaces([face], eye);
                }

                // A group's displacements' overlays go after its last run: 0x1800c61f0 is called once per sort group.
                if (terrain.Count > 0 && (at == first + count - 1 || runs.RunGroup[at + 1] != runs.RunGroup[at]))
                {
                    after[at] = OrderSurfaces(terrain, eye);
                    terrain.Clear();
                }
            }
        }

        return after;
    }

    private static bool Translucent(WorldFaceSpan span, Func<int, bool> blended) =>
        (span.Flags & SurfaceProperties.Translucent) != 0 || blended(span.MaterialIndex);

    private void ResetSort()
    {
        foreach (List<(int Face, bool Queued)> list in _sorted.Values)
        {
            list.Clear();
        }

        _sortOrder.Clear();
    }

    /// <summary>Appends a surface to its sort ID's chain, the ID to the list on its first — <c>0x1800d1140</c>.</summary>
    private void Sort(WorldFaceSpan span, bool queued)
    {
        // The engine's sort ID — material and lightmap page (LightmapSortIds); a span without one keys by material.
        int key = span.SortId >= 0 ? span.SortId : -1 - span.MaterialIndex;

        if (!_sorted.TryGetValue(key, out List<(int Face, bool Queued)>? chain))
        {
            chain = [];
            _sorted[key] = chain;
        }

        if (chain.Count == 0)
        {
            _sortOrder.Add(key);
        }

        chain.Add((span.Face, queued));
    }

    /// <summary>Walks the sort list, queueing each surface's fragments — <c>0x1800da3a0</c>.</summary>
    private void QueueSorted((float X, float Y, float Z)? eye)
    {
        foreach (int material in _sortOrder)
        {
            foreach ((int face, bool queued) in _sorted[material])
            {
                if (queued)
                {
                    Queue(face, eye);
                }
            }
        }
    }

    /// <summary>Queues one surface's fragment list — <c>0x18010a580</c>.</summary>
    private void Queue(int face, (float X, float Y, float Z)? eye)
    {
        if (!_byFace.TryGetValue(face, out List<int>? list))
        {
            return;
        }

        foreach (int at in list)
        {
            OverlayFragment fragment = _fragments[at];

            if (eye is { } from && fragment.Fade is { } fade && fade.Alpha(from.X, from.Y, from.Z) is null)
            {
                continue;
            }

            if (!_buckets.TryGetValue(fragment.MaterialIndex, out List<int>? bucket))
            {
                bucket = [];
                _buckets[fragment.MaterialIndex] = bucket;
            }

            if (bucket.Count == 0)
            {
                _bucketOrder.Add(fragment.MaterialIndex);
            }

            bucket.Add(at);
        }
    }

    /// <summary>Draws and clears the queued buckets — <c>RenderOverlays</c> then the clear at vtable +0x20.</summary>
    private void Render(List<WorldBatch> into)
    {
        int highest = 0;

        foreach (int material in _bucketOrder)
        {
            foreach (int at in _buckets[material])
            {
                highest = Math.Max(highest, _fragments[at].RenderOrder);
            }
        }

        for (int pass = 0; pass <= highest; pass++)
        {
            for (int b = _bucketOrder.Count - 1; b >= 0; b--)
            {
                List<int> bucket = _buckets[_bucketOrder[b]];

                for (int f = bucket.Count - 1; f >= 0; f--)
                {
                    OverlayFragment fragment = _fragments[bucket[f]];

                    if (fragment.RenderOrder == pass)
                    {
                        Draw(into, fragment);
                    }
                }
            }
        }

        foreach (int material in _bucketOrder)
        {
            _buckets[material].Clear();
        }

        _bucketOrder.Clear();
    }

    /// <summary>Appends one fragment, continuing the last draw when it is the same material and fade and follows it.</summary>
    private static void Draw(List<WorldBatch> into, OverlayFragment fragment)
    {
        int last = into.Count - 1;

        if (last >= 0 &&
            into[last] is { } run &&
            run.MaterialIndex == fragment.MaterialIndex &&
            run.Fade == fragment.Fade &&
            run.FirstVertex + run.VertexCount == fragment.FirstVertex)
        {
            into[last] = run with { VertexCount = run.VertexCount + fragment.VertexCount };
            return;
        }

        into.Add(new WorldBatch(
            fragment.MaterialIndex,
            fragment.FirstVertex,
            fragment.VertexCount,
            Category: SurfaceCategory.Overlay,
            Fade: fragment.Fade));
    }
}
