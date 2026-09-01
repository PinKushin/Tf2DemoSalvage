using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which leaves of the map a view can see — the engine's <c>WorldListInfo_t</c> leaf list.
/// </summary>
/// <remarks>
/// **The unit of world visibility is the LEAF, and that is Valve's decision rather than a
/// convenience.** `BuildWorldLists` fills a `WorldListInfo_t` whose payload is `m_LeafCount` and
/// `m_pLeafList` (`public/ivrenderview.h:87`, under the comment *"Describes the leaves to be
/// rendered this view"*). Surfaces are read out of the leaves afterwards; nothing upstream is keyed
/// by surface.
///
/// **Two filters, in this order, because they cost differently.** A leaf is dropped when the
/// camera's cluster cannot see the leaf's cluster (the PVS, computed offline by `vvis`), and then
/// when the leaf's own box lies outside the view frustum. The PVS test is a bit lookup; the frustum
/// test is six plane tests. Cheap first.
///
/// **The tree is WALKED rather than scanned, which is what `dnode_t`'s cull box exists for.** Every
/// node carries `short mins[3]` marked *"for frustom culling"* — Valve's spelling — so one test
/// rejects an entire subtree. A scan over every leaf would give the same answer and pay for it on a
/// map where most of the world is behind you.
///
/// **The near child is visited first, which gives the list front-to-back order.** That is the
/// property the classic BSP walk exists for and the reason the camera's side of each plane is
/// computed rather than the children being taken in file order. The traversal itself is engine-side
/// and not published in `source-sdk-2013`; what is published is the data it walks, and the cull
/// boxes on nodes and the leaf-keyed output together only make sense for this shape.
///
/// **What this does NOT do, and why each is deliberate.** No area portals — their open/shut state
/// is server-side and a demo does not carry it, so a viewer that guessed would hide rooms that are
/// open. No occlusion queries and no `func_occluder`. Both make the result strictly more
/// conservative, which is the safe direction: this may keep a leaf the engine would drop, never the
/// reverse.
/// </remarks>
public sealed class WorldVisibility
{
    private readonly BspLeafTree _tree;
    private readonly BspVisibility _pvs;

    /// <summary>Reused between views so a moving camera does not allocate a list a frame.</summary>
    private readonly List<int> _leaves = [];

    /// <summary>The same answer as <see cref="Leaves"/>, indexed BY leaf rather than listed.</summary>
    /// <remarks>
    /// **Two shapes of one answer, because two callers ask opposite questions.** The world draw
    /// walks the list — "which leaves do I draw" — and the entity cull tests membership — "is THIS
    /// leaf visible", six hundred times a frame against a box that may touch several. Scanning the
    /// list for each would be the quadratic read
    /// `docs/memory/per-item-apis-hide-quadratic-reads.md` is about.
    ///
    /// Grown to the leaf count on first use and cleared per view, so a moving camera allocates
    /// nothing.
    /// </remarks>
    private bool[] _visibleByLeaf = [];

    /// <summary>How deep the walk may go before it gives up on a malformed tree.</summary>
    /// <remarks>
    /// **A bound rather than trust, matching <see cref="BspLeafTree.LeafAt"/>'s own loop guard.** A
    /// BSP with a cycle in it would recurse until the stack ran out, and a viewer that crashes on
    /// one map is worse than one that draws part of it. Real trees are logarithmic in leaf count:
    /// even a pathological map is nowhere near this.
    /// </remarks>
    private const int MaximumDepth = 256;

    /// <summary>Which leaves the last <see cref="Leaves"/> call accepted, indexed by leaf.</summary>
    /// <remarks>
    /// For the entity cull, which asks "is this leaf visible" rather than "list the visible leaves"
    /// (B254). Empty before the first view is set, and an empty span culls nothing — the same
    /// direction of safety <see cref="ViewFrustum.Cull"/> takes for an unbuilt frustum.
    /// </remarks>
    public ReadOnlySpan<bool> VisibleByLeaf => _visibleByLeaf;

    /// <summary>Builds a visibility query over one map.</summary>
    /// <param name="tree">The map's nodes, planes and leaves.</param>
    /// <param name="visibility">The PVS, or <see cref="BspVisibility.None"/> for a map without one.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public WorldVisibility(BspLeafTree tree, BspVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(visibility);

        _tree = tree;
        _pvs = visibility;
    }

    /// <summary>The leaves a camera can see, nearest first.</summary>
    /// <param name="x">The eye's world position — the vis origin.</param>
    /// <param name="y">The eye's world position.</param>
    /// <param name="z">The eye's world position.</param>
    /// <param name="frustum">The view volume; an unbuilt one applies no frustum test.</param>
    /// <returns>Leaf indices, in front-to-back order. Valid until the next call.</returns>
    /// <remarks>
    /// **The vis origin is the view origin**, one point, as `CViewRender::SetupVis` uses it. The
    /// array form of `ViewSetupVisEx` exists for portals and mirrors, which merge several
    /// viewpoints; nothing here needs one yet.
    ///
    /// **The returned list is REUSED.** A caller that needs to keep it must copy it. This is called
    /// on every view change and returning a fresh list each time would allocate a few kilobytes a
    /// frame for nothing.
    /// </remarks>
    public IReadOnlyList<int> Leaves(float x, float y, float z, ViewFrustum frustum)
    {
        _leaves.Clear();
        Array.Clear(_visibleByLeaf);

        if (_tree.IsEmpty)
        {
            return _leaves;
        }

        // **Valve's `PVSCheck` rule, applied to the CAMERA rather than to a leaf.** An eye that
        // cannot be placed in a cluster gets no PVS filtering at all — only the frustum. The free
        // camera is in solid space routinely, and a viewer that culled the whole map there would
        // show a black screen at exactly the moment the user flew through a wall.
        //
        // Valve's own rule is stated for the target cluster — *"we assume the sample is in the
        // PVS"* — and the engine's behaviour for an unplaceable VIEW origin is not published. This
        // is the conservative reading of it: never cull what cannot be proved invisible.
        int from = _tree.ClusterAt(x, y, z);

        Descend(_tree.Node(0) is null ? -1 : 0, from, x, y, z, frustum, 0);

        return _leaves;
    }

    /// <summary>Walks one subtree, near side first.</summary>
    /// <param name="node">A node index, or a negative leaf encoding, or −1 for nothing.</param>
    /// <param name="from">The camera's cluster, or −1 when it has none.</param>
    /// <param name="x">The eye, for deciding which child is nearer.</param>
    /// <param name="y">The eye.</param>
    /// <param name="z">The eye.</param>
    /// <param name="frustum">The view volume.</param>
    /// <param name="depth">How far down this walk already is.</param>
    private void Descend(
        int node, int from, float x, float y, float z, ViewFrustum frustum, int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        // **A negative child is a LEAF, encoded as −(leaf + 1)** — Valve's note on `dnode_t`. So −1
        // is leaf 0, which is the solid leaf every map has and which a reader treating negative as
        // "absent" would silently keep descending past.
        if (node < 0)
        {
            Collect(-node - 1, from, frustum);
            return;
        }

        if (_tree.Node(node) is not { } split)
        {
            return;
        }

        // The node's own cull box, which rejects everything below it in one test.
        if (frustum.Cull(
            split.Min.X, split.Min.Y, split.Min.Z, split.Max.X, split.Max.Y, split.Max.Z))
        {
            return;
        }

        // **Which side the eye is on decides which child is NEARER**, and the near one is walked
        // first so the leaf list comes out front to back. `LeafAt` uses the same comparison for
        // the same plane, including where a point exactly on the plane belongs.
        float side = (split.NormalX * x) + (split.NormalY * y) + (split.NormalZ * z);

        (int Near, int Far) children = side <= split.Distance
            ? (split.Back, split.Front)
            : (split.Front, split.Back);

        Descend(children.Near, from, x, y, z, frustum, depth + 1);
        Descend(children.Far, from, x, y, z, frustum, depth + 1);
    }

    /// <summary>Keeps a leaf if the camera can see it.</summary>
    /// <param name="leaf">The leaf index.</param>
    /// <param name="from">The camera's cluster, or −1 when it has none.</param>
    /// <param name="frustum">The view volume.</param>
    /// <remarks>
    /// **`PVSCheck`'s rule for the target, stated the way Valve states it**: a leaf whose cluster is
    /// negative is treated as VISIBLE rather than hidden. Those are the solid leaves `vvis` gave no
    /// cluster to, and getting this backwards is the difference between a map and an empty room.
    ///
    /// **A map with NO vis lump gets no PVS filtering, which is a third case and not the same as an
    /// empty PVS.** `BspVisibility.None` answers false to every query — correctly, since it has no
    /// table and nothing is in it — so testing it without the `HasData` guard culls the entire
    /// world. Found by the first test written against this walk, which returned an empty leaf list
    /// for a tree plainly in view. A map compiled without `vvis`, or one whose lump failed to read,
    /// must draw everything: no data is not proof of invisibility.
    ///
    /// **The leaf's box is tested even when the PVS kept it**, because the two answer different
    /// questions: the PVS says what could ever be seen from here, the frustum says what is on screen
    /// now. Valve applies both.
    /// </remarks>
    private void Collect(int leaf, int from, ViewFrustum frustum)
    {
        int cluster = _tree.Cluster(leaf);

        if (_pvs.HasData && from >= 0 && cluster >= 0 && !_pvs.Visible(from, cluster))
        {
            return;
        }

        if (_tree.Bounds(leaf) is { } box &&
            frustum.Cull(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z))
        {
            return;
        }

        _leaves.Add(leaf);

        // **Both shapes filled at the one place a leaf is accepted**, so they cannot disagree about
        // what is visible. Two separate walks would be two chances to answer differently, and the
        // entity cull would then hide things the world draws.
        if (leaf >= _visibleByLeaf.Length)
        {
            Array.Resize(ref _visibleByLeaf, Math.Max(leaf + 1, _visibleByLeaf.Length * 2));
        }

        _visibleByLeaf[leaf] = true;
    }
}
