using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which render list a renderable joins — <c>m_RenderGroup</c> plus <c>RENDER_FLAGS_TWOPASS</c>.</summary>
public enum LeafRenderGroup
{
    /// <summary>Not drawn: alpha 0 or <c>kRenderNone</c> (<c>clientleafsystem.cpp:1630</c>).</summary>
    None,

    /// <summary>An opaque entity, bucketed by size.</summary>
    Opaque,

    /// <summary>A translucent entity, drawn at its nearest leaf.</summary>
    Translucent,

    /// <summary>A translucent entity whose solid half also joins <c>RENDER_GROUP_OPAQUE_ENTITY</c>.</summary>
    TwoPass,
}

/// <summary>One frame's collated renderables — <c>CClientRenderablesList</c>'s entity groups.</summary>
public sealed class CollatedRenderables
{
    private readonly List<(int Handle, int Place)>[] _opaque =
        [[], [], [], []];

    /// <summary>The opaque groups, biggest bucket first, each in collation order.</summary>
    /// <param name="bucket">0 (tree size) to 3 (smaller than a crate).</param>
    /// <returns>The handles and the leaf place each was collated at.</returns>
    public IReadOnlyList<(int Handle, int Place)> Opaque(int bucket) => _opaque[bucket];

    /// <summary>The translucent group, in collation order (leaves front to back); unsorted within a leaf.</summary>
    public IReadOnlyList<(int Handle, int Place, bool TwoPass)> Translucent => _translucent;

    /// <summary>Every handle collated this frame, in collation order, whatever its group.</summary>
    public IReadOnlyList<int> Collated => _collated;

    private readonly List<(int Handle, int Place, bool TwoPass)> _translucent = [];
    private readonly List<int> _collated = [];

    internal void AddOpaque(int bucket, int handle, int place) => _opaque[bucket].Add((handle, place));

    internal void AddTranslucent(int handle, int place, bool twoPass) => _translucent.Add((handle, place, twoPass));

    internal void AddCollated(int handle) => _collated.Add(handle);

    internal void Clear()
    {
        foreach (List<(int, int)> bucket in _opaque)
        {
            bucket.Clear();
        }

        _translucent.Clear();
        _collated.Clear();
    }
}

/// <summary>
/// Renderables kept in the leaves they touch across frames, collated per visible leaf — <c>CClientLeafSystem</c> (B262).
/// </summary>
/// <remarks>
/// **The engine's structure, ported member by member** (<c>clientleafsystem.cpp</c>, source-sdk-2013):
///
/// * <see cref="AddRenderable"/> is <c>AddRenderable</c> (<c>:708</c>): a new handle, flagged changed and put on the
///   dirty list so it is linked before it is first drawn.
/// * <see cref="RenderableChanged"/> is <c>RenderableChanged</c> (<c>:1274</c>): dirty once, however often called.
///   The engine's entity calls it when it moves; this is called with the frame's box and dirties only when the box
///   differs, because the leaves a renderable is in depend on nothing else.
/// * <see cref="PreRender"/> is <c>PreRender</c> (<c>:528</c>): every dirty renderable removed from the tree, then
///   re-inserted walking the dirty list BACKWARDS (<c>:557</c>), each insert at the HEAD of its leaf's list
///   (<c>CBidirectionalSet::AddElementToBucket</c>, <c>utlbidirectionalset.h:205-208</c>) — which together leave a
///   leaf in the order its renderables were added.
/// * <see cref="BuildRenderablesList"/> is <c>BuildRenderablesList</c> (<c>:1813</c>) over
///   <c>ComputeTranslucentRenderLeaf</c> (<c>:1391</c>) and <c>CollateRenderablesInLeaf</c> (<c>:1574</c>): leaves
///   in list order, an opaque renderable collated at the first leaf it is met in (<c>m_RenderFrame2</c>,
///   <c>:1607-1613</c>), a translucent one only at its nearest listed leaf (<c>:1620</c>), the frustum test
///   (<c>:1647</c>), then the size bucket from the world box (<c>:1683-1694</c>), and a two-pass renderable added
///   to <c>RENDER_GROUP_OPAQUE_ENTITY</c> as well (<c>:1710-1713</c>) — the last bucket, not its size's.
///
/// **What is ours, and why.** A box with no extent (<see cref="WorldSpaceBounds.IsPlaced"/>) is never linked and
/// never culled; it is collated at place 0, where every such model drew before (an empty box must never cull). The
/// render group is asked once per collated renderable per frame through a callback rather than cached on the
/// handle, because the alpha that decides it is recomputed per frame here as it is by the engine's
/// <c>ComputeFxBlend</c>. The frustum is tested before the group is asked; both must pass, so the answer is the same.
/// </remarks>
public sealed class ClientLeafSystem
{
    private readonly Action<(float X, float Y, float Z), (float X, float Y, float Z), ICollection<int>> _leavesInBox;
    private readonly List<int>[] _inLeaf;
    private readonly List<Info> _renderables = [];
    private readonly Stack<int> _free = new();
    private readonly List<int> _dirty = [];
    private readonly List<int> _unplaced = [];
    private readonly List<int> _scratch = [];
    private int _frame;

    private sealed class Info
    {
        public (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Box;
        public readonly List<int> Leaves = [];
        public bool Live;
        public bool Changed;
        public bool Linked;
        public int RenderFrame2 = -1;
    }

    /// <summary>A leaf system over a tree.</summary>
    /// <param name="leafCount">How many leaves the tree has.</param>
    /// <param name="leavesInBox">Appends every leaf a world box touches — <c>EnumerateLeavesInBox</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="leavesInBox"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leafCount"/> is negative.</exception>
    public ClientLeafSystem(
        int leafCount,
        Action<(float X, float Y, float Z), (float X, float Y, float Z), ICollection<int>> leavesInBox)
    {
        ArgumentNullException.ThrowIfNull(leavesInBox);
        ArgumentOutOfRangeException.ThrowIfNegative(leafCount);

        _leavesInBox = leavesInBox;
        _inLeaf = new List<int>[leafCount];

        for (int leaf = 0; leaf < leafCount; leaf++)
        {
            _inLeaf[leaf] = [];
        }
    }

    /// <summary>How many renderables the last <see cref="PreRender"/> re-linked.</summary>
    public int Relinked { get; private set; }

    /// <summary>A leaf's renderables, head first — <c>m_RenderablesInLeaf</c>.</summary>
    /// <param name="leaf">The leaf.</param>
    /// <returns>The handles, in the order collation walks them.</returns>
    public IReadOnlyList<int> RenderablesInLeaf(int leaf) => _inLeaf[leaf];

    /// <summary>Registers a renderable, dirty so it is linked at the next <see cref="PreRender"/>.</summary>
    /// <param name="box">Its world-space box.</param>
    /// <returns>Its handle.</returns>
    public int AddRenderable((float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box)
    {
        int handle;

        if (_free.Count > 0)
        {
            handle = _free.Pop();
            _renderables[handle] = new Info();
        }
        else
        {
            handle = _renderables.Count;
            _renderables.Add(new Info());
        }

        Info info = _renderables[handle];
        info.Live = true;
        info.Box = box;
        info.Changed = true;
        _dirty.Add(handle);

        return handle;
    }

    /// <summary>Reports a renderable's box; dirties it when the box moved.</summary>
    /// <param name="handle">The renderable.</param>
    /// <param name="box">Its world-space box this frame.</param>
    public void RenderableChanged(
        int handle, (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box)
    {
        Info info = _renderables[handle];

        if (info.Box == box)
        {
            return;
        }

        info.Box = box;

        if (!info.Changed)
        {
            info.Changed = true;
            _dirty.Add(handle);
        }
    }

    /// <summary>Unlinks and frees a renderable — <c>RemoveRenderable</c> (<c>:724</c>).</summary>
    /// <param name="handle">The renderable.</param>
    public void RemoveRenderable(int handle)
    {
        Info info = _renderables[handle];

        if (!info.Live)
        {
            return;
        }

        if (info.Changed)
        {
            _dirty.Remove(handle);
        }

        RemoveFromTree(handle);
        info.Live = false;
        _free.Push(handle);
    }

    /// <summary>Re-links every dirty renderable — <c>PreRender</c> (<c>:528</c>).</summary>
    public void PreRender()
    {
        Relinked = _dirty.Count;

        for (int at = _dirty.Count; --at >= 0;)
        {
            RemoveFromTree(_dirty[at]);
        }

        for (int at = _dirty.Count; --at >= 0;)
        {
            InsertIntoTree(_dirty[at]);
        }

        foreach (int handle in _dirty)
        {
            _renderables[handle].Changed = false;
        }

        _dirty.Clear();
    }

    /// <summary>Collates the visible leaves into render groups — <c>BuildRenderablesList</c> (<c>:1813</c>).</summary>
    /// <param name="leaves">The view's leaves, nearest first.</param>
    /// <param name="frustum">The view volume; an unbuilt one culls nothing.</param>
    /// <param name="groupOf">The render group of a handle this frame.</param>
    /// <param name="into">Receives the groups; cleared here.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void BuildRenderablesList(
        IReadOnlyList<int> leaves,
        ViewFrustum frustum,
        Func<int, LeafRenderGroup> groupOf,
        CollatedRenderables into)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(groupOf);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        _frame++;

        // Ours: an unplaced box touches no leaf and is never culled, so it goes at place 0.
        foreach (int handle in _unplaced)
        {
            Collate(handle, 0, frustum, groupOf, into, cull: false);
        }

        for (int place = 0; place < leaves.Count; place++)
        {
            foreach (int handle in LeafOrEmpty(leaves[place]))
            {
                Info info = _renderables[handle];

                // `m_RenderFrame2` (:1607-1613) for the opaque, `m_RenderLeaf` (:1620) for the translucent. Both are
                // "collated once, at the first listed leaf it is in": `ComputeTranslucentRenderLeaf` (:1444-1455) sets
                // m_RenderLeaf at a renderable's FIRST encounter in the same list. So one stamp serves both. What would
                // falsify it is `RENDER_FLAGS_ALTERNATE_SORTING` (:1457), which moves the render leaf to the LAST
                // encounter — nothing in this project sets it (`EnableAlternateSorting` has no caller here).
                if (info.RenderFrame2 == _frame)
                {
                    continue;
                }

                info.RenderFrame2 = _frame;

                Collate(handle, place, frustum, groupOf, into, cull: true);
            }
        }
    }

    private void Collate(
        int handle, int place, ViewFrustum frustum, Func<int, LeafRenderGroup> groupOf, CollatedRenderables into, bool cull)
    {
        (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = _renderables[handle].Box;

        if (cull && frustum.Cull(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return;
        }

        LeafRenderGroup group = groupOf(handle);

        if (group == LeafRenderGroup.None)
        {
            return;
        }

        into.AddCollated(handle);

        if (group == LeafRenderGroup.Opaque)
        {
            into.AddOpaque(
                BucketFor(Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ)), handle, place);
            return;
        }

        bool twoPass = group == LeafRenderGroup.TwoPass;

        into.AddTranslucent(handle, place, twoPass);

        if (twoPass)
        {
            // `RENDER_GROUP_OPAQUE_ENTITY`, unbucketed: the last of the four (:1710-1713).
            into.AddOpaque(BucketCount - 1, handle, place);
        }
    }

    /// <summary>How many opaque size buckets there are.</summary>
    public const int BucketCount = 4;

    /// <summary><c>DetectBucketedRenderGroup</c> (<c>:1538</c>): 200, 80 and 30 units, <c>&gt;=</c> takes the larger.</summary>
    /// <param name="longestAxis">The world box's longest axis.</param>
    /// <returns>0 (largest) to 3.</returns>
    public static int BucketFor(float longestAxis)
    {
        ReadOnlySpan<float> thresholds = Thresholds;

        for (int bucket = 0; bucket < thresholds.Length; bucket++)
        {
            if (longestAxis >= thresholds[bucket])
            {
                return bucket;
            }
        }

        return thresholds.Length;
    }

    /// <summary><c>arrThresholds</c> (<c>:1540-1544</c>): tree, player and crate size, largest first.</summary>
    public static ReadOnlySpan<float> Thresholds => [200f, 80f, 30f];

    private List<int> LeafOrEmpty(int leaf) =>
        leaf >= 0 && leaf < _inLeaf.Length ? _inLeaf[leaf] : _scratch;

    private void InsertIntoTree(int handle)
    {
        Info info = _renderables[handle];
        (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = info.Box;

        info.Linked = true;

        if (!WorldSpaceBounds.IsPlaced(info.Box))
        {
            // At the head, as a leaf link is, so the unplaced keep the order they were added in too.
            _unplaced.Insert(0, handle);
            return;
        }

        _leavesInBox((minX, minY, minZ), (maxX, maxY, maxZ), info.Leaves);

        foreach (int leaf in info.Leaves)
        {
            if (leaf >= 0 && leaf < _inLeaf.Length)
            {
                _inLeaf[leaf].Insert(0, handle);
            }
        }
    }

    private void RemoveFromTree(int handle)
    {
        Info info = _renderables[handle];

        if (!info.Linked)
        {
            return;
        }

        foreach (int leaf in info.Leaves)
        {
            if (leaf >= 0 && leaf < _inLeaf.Length)
            {
                _inLeaf[leaf].Remove(handle);
            }
        }

        info.Leaves.Clear();
        _unplaced.Remove(handle);
        info.Linked = false;
    }
}
