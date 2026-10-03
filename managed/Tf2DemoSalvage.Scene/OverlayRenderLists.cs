using System;
using System.Collections.Generic;

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
/// - <c>0x1800da3a0</c> walks the opaque surfaces by material sort ID, in the order each ID was first reached, and
///   each ID's surfaces in the order reached (<c>0x1800d1140</c> appends both), queueing each surface's list.
///   A translucent surface is on the other list (<c>R_DrawSurface</c>, <c>0x1800dfbb0</c>, flag 0x20).
/// - <c>0x18010a580</c> skips a fragment faded past its maximum and PREPENDS the rest to their material's bucket,
///   prepending the bucket to the list the first time it gets one that frame.
/// - <c>COverlayMgr::RenderOverlays</c> (<c>0x180110630</c>) walks the bucket list and each bucket from the head,
///   once per render order.
///
/// **A prepended list walked from its head is the appended list walked backwards**, so this appends and reads in
/// reverse rather than keeping linked lists: the same order, with nothing to unlink.
///
/// *Interpolated, and each named in B457:* the material sort ID is the surface's MATERIAL here — the engine's also
/// splits by lightmap page, which a single-atlas renderer has none of; and fragments on surfaces the walk does not
/// order — translucent ones, which the engine draws with the surface (<c>0x1800e4fd0</c>) — are queued after it.
/// </remarks>
public sealed class OverlayRenderLists
{
    private readonly IReadOnlyList<OverlayFragment> _fragments;
    private readonly IReadOnlyList<WorldFaceSpan> _spans;

    /// <summary>Each face's fragments, last built first — the surface's own list (surface +0x14).</summary>
    private readonly Dictionary<int, List<int>> _byFace = [];

    private readonly Dictionary<int, int> _materialOfFace = [];

    // Reused per view.
    private readonly List<int> _sortOrder = [];
    private readonly Dictionary<int, List<int>> _sorted = [];
    private readonly List<int> _translucent = [];
    private readonly List<int> _bucketOrder = [];
    private readonly Dictionary<int, List<int>> _buckets = [];
    private readonly List<WorldBatch> _drawn = [];

    /// <summary>Prepares the queue for one world.</summary>
    /// <param name="fragments">The overlay fragments, in build order.</param>
    /// <param name="spans">The world's faces, for each face's material and for the order with no walk.</param>
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
            _materialOfFace.TryAdd(span.Face, span.MaterialIndex);
        }
    }

    /// <summary>The overlay draws for one view, in the engine's order.</summary>
    /// <param name="surfaces">The faces the walk reached, in <c>R_DrawSurface</c> order; null for a world that cannot be
    /// walked, which queues every face in buffer order.</param>
    /// <param name="eye">The view origin, for the fade; null to fade nothing.</param>
    /// <param name="blended">Whether a material is translucent or additive.</param>
    /// <returns>The draws, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blended"/> is null.</exception>
    public IReadOnlyList<WorldBatch> Order(
        IReadOnlyList<int>? surfaces, (float X, float Y, float Z)? eye, Func<int, bool> blended)
    {
        ArgumentNullException.ThrowIfNull(blended);

        foreach (List<int> list in _sorted.Values)
        {
            list.Clear();
        }

        foreach (List<int> list in _buckets.Values)
        {
            list.Clear();
        }

        _sortOrder.Clear();
        _translucent.Clear();
        _bucketOrder.Clear();
        _drawn.Clear();

        // The sort list: opaque surfaces appended to their material's chain, a material appended on its first.
        int reached = surfaces?.Count ?? _spans.Count;

        for (int at = 0; at < reached; at++)
        {
            int face = surfaces is null ? _spans[at].Face : surfaces[at];

            if (!_materialOfFace.TryGetValue(face, out int material))
            {
                continue;
            }

            if (blended(material))
            {
                _translucent.Add(face);
                continue;
            }

            if (!_sorted.TryGetValue(material, out List<int>? chain))
            {
                chain = [];
                _sorted[material] = chain;
            }

            if (chain.Count == 0)
            {
                _sortOrder.Add(material);
            }

            chain.Add(face);
        }

        foreach (int material in _sortOrder)
        {
            foreach (int face in _sorted[material])
            {
                Queue(face, eye);
            }
        }

        foreach (int face in _translucent)
        {
            Queue(face, eye);
        }

        int highest = 0;

        foreach (List<int> bucket in _buckets.Values)
        {
            foreach (int at in bucket)
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
                        Draw(fragment);
                    }
                }
            }
        }

        return _drawn;
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

    /// <summary>Appends one fragment, continuing the last draw when it is the same material and fade and follows it.</summary>
    private void Draw(OverlayFragment fragment)
    {
        int last = _drawn.Count - 1;

        if (last >= 0 &&
            _drawn[last] is { } run &&
            run.MaterialIndex == fragment.MaterialIndex &&
            run.Fade == fragment.Fade &&
            run.FirstVertex + run.VertexCount == fragment.FirstVertex)
        {
            _drawn[last] = run with { VertexCount = run.VertexCount + fragment.VertexCount };
            return;
        }

        _drawn.Add(new WorldBatch(
            fragment.MaterialIndex,
            fragment.FirstVertex,
            fragment.VertexCount,
            Category: SurfaceCategory.Overlay,
            Fade: fragment.Fade));
    }
}
