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

    private readonly List<WorldWalkStep> _walk = [];

    /// <summary>The last <see cref="Leaves"/> call's walk: each accepted leaf, and each node between its children.</summary>
    /// <remarks>The order the engine reaches surfaces in, which the overlay queue is built from (B457).</remarks>
    public IReadOnlyList<WorldWalkStep> Walk => _walk;

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

        _nodeParent = new int[tree.NodeCount];
        _leafParent = new int[tree.LeafCount];
        _nodeVisFrame = new int[tree.NodeCount];
        _leafVisFrame = new int[tree.LeafCount];
        Array.Fill(_nodeParent, -1);
        Array.Fill(_leafParent, -1);
        LinkParents(0, -1, 0);
    }

    /// <summary>Each node's parent node, −1 for the root and for a node the world tree does not reach.</summary>
    private readonly int[] _nodeParent;

    /// <summary>Each leaf's parent node, −1 for a leaf the world tree does not reach (a brush model's).</summary>
    private readonly int[] _leafParent;

    /// <summary>The vis count each node was last marked with — the engine's node <c>visframe</c>.</summary>
    private readonly int[] _nodeVisFrame;

    /// <summary>The vis count each leaf was last marked with.</summary>
    private readonly int[] _leafVisFrame;

    /// <summary>The current vis count; 0 marks nothing, since every stamp starts at 0.</summary>
    /// <remarks>One increment per change of the eye's cluster, so it does not wrap in any real session.</remarks>
    private int _visFrame;

    /// <summary>The eye cluster the current marks were made for; <see cref="int.MinValue"/> before any, or after a
    /// marking that did not finish.</summary>
    private int _markedCluster = int.MinValue;

    /// <summary>Records every node's and leaf's parent, once, so marking can climb from a leaf to the root.</summary>
    /// <remarks>
    /// The parent links are per instance rather than on <see cref="BspLeafTree"/>: the tree is shared between threads
    /// (each view half's twin cull), and two small arrays per instance cost less than a lazily built shared one.
    ///
    /// **First writer wins, for nodes and leaves alike.** In a well-formed BSP every node and leaf has one parent. A
    /// malformed tree that reaches a child twice keeps the first path, so marking climbs that one; the walk may still
    /// reach the child by the other path, where it is stamped or not by the same leaf test.
    /// </remarks>
    private void LinkParents(int node, int parent, int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        if (node < 0)
        {
            int leaf = -node - 1;

            if (leaf < _leafParent.Length && _leafParent[leaf] < 0)
            {
                _leafParent[leaf] = parent;
            }

            return;
        }

        // A node already linked is a cycle or a shared child in a malformed tree; the first parent stands.
        if (node >= _nodeParent.Length || _nodeParent[node] >= 0 || (node == 0 && parent >= 0) ||
            _tree.Node(node) is not { } split)
        {
            return;
        }

        _nodeParent[node] = parent;
        LinkParents(split.Front, node, depth + 1);
        LinkParents(split.Back, node, depth + 1);
    }

    /// <summary>Marks the leaves the eye's cluster can see, and every node above them — the engine's mark-leaves.</summary>
    /// <param name="from">The eye's cluster, or −1 when it has none.</param>
    /// <remarks>
    /// engine.dll <c>0x1801c8cb0</c> (read under B262, the 2026-10-04 build): an eye cluster of −1 stamps every leaf
    /// and node (<c>0x1801c9113</c>), as <c>r_novis</c> does. *Interpolated, not yet re-read:* that a leaf in the PVS
    /// stamps each node above it, stopping at the first already stamped, and that the walk enters only stamped nodes —
    /// the Quake <c>R_MarkLeaves</c> shape (B259, `docs/RISKS.md`).
    ///
    /// **So the two eye cases walk different NODES, and that is the read.** With an eye cluster of −1 (or, ours, no vis
    /// lump) every node is stamped and a subtree holding only solid leaves is still entered, emitting its node steps;
    /// from a real cluster a solid leaf is never stamped, so such a subtree is pruned. The listed leaves are the same
    /// either way, since the walk never lists a solid leaf. The extra node steps draw nothing on a well-formed tree:
    /// <see cref="VisibleWorld.Surfaces"/> draws a node face only once a listed leaf has marked it, and the leaves that
    /// list a node face lie under that node (checked on cp_process and cp_badlands, many eyes,
    /// `Surfaces_ForAPvsPrunedWalk_AreTheUnprunedWalksInTheSameOrder`).
    ///
    /// **Ours, two readings kept from the walk this replaces.** A map with no vis lump stamps everything, since no data
    /// is not proof of invisibility; and a non-solid leaf with no cluster is stamped, after <c>PVSCheck</c>'s "we
    /// assume the sample is in the PVS" — whether the engine stamps one is not read (Quake 2 does not). On a
    /// well-formed tree the listed leaves are what the per-leaf test listed before. A child index past the leaf lump,
    /// which the old walk listed and <see cref="VisibleWorld.Surfaces"/> then indexed out of range, is now never listed.
    ///
    /// **Made again only when the eye changes cluster**, because the stamps are a function of the cluster alone. The
    /// cluster is recorded only once the stamps are complete, so a PVS row that throws mid-way is re-marked next call.
    /// </remarks>
    private void MarkLeaves(int from)
    {
        if (from == _markedCluster)
        {
            return;
        }

        _markedCluster = int.MinValue;
        _visFrame++;

        if (!_pvs.HasData || from < 0)
        {
            Array.Fill(_nodeVisFrame, _visFrame);
            Array.Fill(_leafVisFrame, _visFrame);
            _markedCluster = from;
            return;
        }

        for (int leaf = 0; leaf < _leafVisFrame.Length; leaf++)
        {
            if (_tree.Contents(leaf) == BspLeafTree.ContentsSolid)
            {
                continue;
            }

            int cluster = _tree.Cluster(leaf);

            if (cluster >= 0 && !_pvs.Visible(from, cluster))
            {
                continue;
            }

            _leafVisFrame[leaf] = _visFrame;

            for (int node = _leafParent[leaf]; node >= 0 && _nodeVisFrame[node] != _visFrame; node = _nodeParent[node])
            {
                _nodeVisFrame[node] = _visFrame;
            }
        }

        _markedCluster = from;
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
        _walk.Clear();
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

        MarkLeaves(from);

        Descend(_tree.Node(0) is null ? -1 : 0, x, y, z, frustum, 0);

        return _leaves;
    }

    /// <summary>Walks one subtree, near side first.</summary>
    /// <param name="node">A node index, or a negative leaf encoding, or −1 for nothing.</param>
    /// <param name="x">The eye, for deciding which child is nearer.</param>
    /// <param name="y">The eye.</param>
    /// <param name="z">The eye.</param>
    /// <param name="frustum">The view volume.</param>
    /// <param name="depth">How far down this walk already is.</param>
    private void Descend(
        int node, float x, float y, float z, ViewFrustum frustum, int depth)
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
            Collect(-node - 1, frustum);
            return;
        }

        // **A node no PVS-visible leaf lies under is never entered**, before its box test. *Interpolated* as
        // `R_RecursiveWorldNode`'s vis-count test (MarkLeaves says what is read). On a well-formed tree nothing is lost:
        // a node face is drawn only once a listed leaf has marked it, and those leaves lie under the node.
        if (node >= _nodeVisFrame.Length || _nodeVisFrame[node] != _visFrame)
        {
            return;
        }

        // Stryker disable once : a mutant that empties the guard body leaves 'split'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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

        Descend(children.Near, x, y, z, frustum, depth + 1);

        // **The node's own surfaces between its children** — `R_RecursiveWorldNode`, engine.dll 0x1800e0600 (B457).
        // Its side is the engine's own test, `dot − dist < 0`, which puts a point ON the plane in front.
        _walk.Add(new WorldWalkStep(node, IsNode: true, EyeBehind: side - split.Distance < 0f));

        Descend(children.Far, x, y, z, frustum, depth + 1);
    }

    /// <summary>Keeps a leaf if the camera can see it.</summary>
    /// <param name="leaf">The leaf index.</param>
    /// <param name="frustum">The view volume.</param>
    /// <remarks>
    /// **The PVS answer is the leaf's stamp from <see cref="MarkLeaves"/>**, which holds its two rules: a leaf whose
    /// cluster is negative is treated as VISIBLE rather than hidden (<c>PVSCheck</c>), and a map with NO vis lump gets
    /// no PVS filtering, which is a third case and not the same as an empty PVS. `BspVisibility.None` answers false to
    /// every query, so testing it without the `HasData` guard culls the entire world — found by the first test written
    /// against this walk. No data is not proof of invisibility.
    ///
    /// **The leaf's box is tested even when the PVS kept it**, because the two answer different
    /// questions: the PVS says what could ever be seen from here, the frustum says what is on screen
    /// now. Valve applies both.
    /// </remarks>
    private void Collect(int leaf, ViewFrustum frustum)
    {
        // **A solid leaf is never walked into** (B262): engine.dll's world walk returns on contents 1 at entry
        // (`0x1800e060f`) and before a far child (`0x1800e079c`), so `R_DrawLeaf` never lists one — whatever the PVS
        // says, including the eye-in-solid case where every leaf is marked visible (`0x1801c9113`).
        if (_tree.Contents(leaf) == BspLeafTree.ContentsSolid)
        {
            return;
        }

        if (leaf >= _leafVisFrame.Length || _leafVisFrame[leaf] != _visFrame)
        {
            return;
        }

        if (_tree.Bounds(leaf) is { } box &&
            frustum.Cull(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z))
        {
            return;
        }

        _leaves.Add(leaf);
        _walk.Add(new WorldWalkStep(leaf, IsNode: false, EyeBehind: false));

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

/// <summary>One step of the world walk: a leaf it accepted, or a node it passed between its children.</summary>
/// <param name="Index">The leaf or node index.</param>
/// <param name="IsNode">Whether this is a node.</param>
/// <param name="EyeBehind">For a node, whether the eye is behind its plane — the side its surfaces must face.</param>
public readonly record struct WorldWalkStep(int Index, bool IsNode, bool EyeBehind);
