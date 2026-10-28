using System;
using System.Collections.Generic;
using System.Diagnostics;

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
/// open. No occlusion queries and no `func_occluder`. Both make the result more conservative, the
/// safe direction: this keeps leaves the engine would drop. **Two cases, both outside a TF2 main view
/// as read, could go the other way:** a view with several vis origins
/// (`ViewCustomVisibility_t::m_rgVisOrigins`, `viewrender.cpp:1900-1903`), whose clusters the engine
/// ORs together and this does not model; and an eye cluster past the PVS row count, which the engine
/// answers with an all-ones row (`0x18016ca26`) and <see cref="BspVisibility.Visible"/> with false — a
/// malformed map only.
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
    /// **Ours: first writer wins, for nodes and leaves alike** — malformed trees only. In a well-formed BSP every node
    /// and leaf has one parent, and the rule never applies. A malformed tree that reaches a child twice keeps the first
    /// path, so marking climbs that one. The engine's parent links come from its loader, not read in this build; a
    /// recursive Quake-style `Mod_SetParent` would be last writer wins.
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
    /// **Read from engine.dll (x64, installed 2026-10-07), in disassembly** (B259, `docs/RISKS.md`, which quotes it):
    ///
    /// * the view's cluster list holds ONE cluster per vis origin: `Map_VisSetup` (`0x1801caad0`, reached straight from
    ///   `ViewSetupVisEx` at `0x1800da247`) clamps the origin count to 32, and per origin stores
    ///   `CM_LeafCluster(CM_PointLeafnum(origin))` (`0x1801cab4b`, `0x1801cab75`, `0x1801cab7a`) — no second point
    ///   above or below water. The TF2 main view passes one origin (`viewrender.cpp:1908`), so one cluster, as here;
    /// * the mark pass `0x1801ca5f0` returns at once when that list is the one it last marked (`0x1801ca6c8`); otherwise
    ///   it increments `r_visframecount` (`0x1801ca6d5`, `[0x1807784b4]`);
    /// * any view cluster of −1, or `r_novis`, stamps every leaf and every node (`0x1801caa53`-`0x1801caac8`);
    /// * otherwise the eye's PVS row is fetched by `CM_Vis` (`0x18016c8e0`), which fills the row with ONES when the map
    ///   has no vis data or the cluster is out of range (`0x18016c96a`, `0x18016ca26`-`0x18016ca35`), and
    ///   `0x1801cacd0` walks every leaf: one whose cluster is −1 is skipped (`0x1801cadb6`), one whose PVS bit is clear
    ///   is skipped (`0x1801cadcf`), and the rest are stamped (`0x1801caddd`) and climb their parents, stamping each,
    ///   until a parent already stamped this count (`0x1801caee0`-`0x1801caffb`). There is NO contents test.
    ///
    /// **What a compiled map has, so which of these cases are real** (`utils/vbsp`, source-sdk-2013): every SOLID world
    /// leaf gets cluster −1 (`prtfile.cpp:185-187`) and every brush-model leaf does too (`writebsp.cpp:128-139`); every
    /// non-solid world leaf is numbered (`prtfile.cpp:190`). So on a real map a solid leaf is never stamped from a
    /// cluster, and the leaves the cluster −1 skip drops are ones the world walk never reaches. The solid-leaf-in-the-PVS
    /// case is synthetic only.
    ///
    /// **So the two eye cases walk different NODES.** From an eye in no cluster every node is stamped and a subtree of
    /// only solid leaves is still entered; from a real cluster it is not. The listed leaves follow the walk, which
    /// returns on a solid leaf at entry. The node steps draw nothing extra on a well-formed tree:
    /// <see cref="VisibleWorld.Surfaces"/> draws a node face only once a listed leaf has marked it, and those leaves lie
    /// under that node (checked on cp_process and cp_badlands, many eyes,
    /// `Surfaces_ForAPvsPrunedWalk_AreTheUnprunedWalksInTheSameOrder`).
    ///
    /// **A map with no vis lump** is the all-ones row: every clustered leaf stamped, cluster −1 still skipped. A child
    /// index past the leaf lump, which the old walk listed and <see cref="VisibleWorld.Surfaces"/> then indexed out of
    /// range, is never listed (ours, malformed trees only).
    ///
    /// **Made again only when the eye changes cluster**, as the engine's own early return does. The cluster is
    /// recorded only once the stamps are complete, so a PVS row that throws mid-way is re-marked next call.
    /// </remarks>
    private void MarkLeaves(int from)
    {
        if (from == _markedCluster)
        {
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();

        _markedCluster = int.MinValue;
        _visFrame++;

        if (from < 0)
        {
            Array.Fill(_nodeVisFrame, _visFrame);
            Array.Fill(_leafVisFrame, _visFrame);
            _markedCluster = from;
            MarkTicks = Stopwatch.GetTimestamp() - startedAt;
            return;
        }

        for (int leaf = 0; leaf < _leafVisFrame.Length; leaf++)
        {
            // No contents test (a solid leaf in the PVS is stamped too; the walk returns on it at entry), and a leaf
            // with no cluster is skipped — `0x1801cadb6`. No vis data is `CM_Vis`'s all-ones row.
            int cluster = _tree.Cluster(leaf);

            if (cluster < 0 || (_pvs.HasData && !_pvs.Visible(from, cluster)))
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
        MarkTicks = Stopwatch.GetTimestamp() - startedAt;
    }

    /// <summary>The <see cref="Stopwatch"/> ticks the last <see cref="Leaves"/> call spent re-marking — 0 when the eye
    /// stayed in the cluster it last marked (the hit path), the mark pass's cost when it moved (the miss path).</summary>
    /// <remarks>Timed where the marking runs and carried out, so the rate line reads the work done (B243).</remarks>
    public long MarkTicks { get; private set; }

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
        MarkTicks = 0;

        if (_tree.IsEmpty)
        {
            return _leaves;
        }

        // **An eye that cannot be placed in a cluster gets no PVS filtering at all** — only the frustum. Read from
        // engine.dll: a view cluster of −1 stamps every leaf and node (`0x1801ca712`, then `0x1801caa53`). The free
        // camera is in solid space routinely, and this is why it does not go blind there.
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

        // **A node no stamped leaf lies under is never entered**, before its box test: `R_RecursiveWorldNode`
        // (`0x1800e1800`, engine.dll of 2026-10-07) compares `visframe` with `r_visframecount` at `0x1800e1850` and
        // returns, and only then clips (`0x1800e188c`). On a well-formed tree nothing drawn is lost: a node face is
        // drawn only once a listed leaf has marked it, and those leaves lie under the node.
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

        // **The node's own surfaces between its children** — `R_RecursiveWorldNode`, engine.dll `0x1800e1800` in the
        // build installed 2026-10-07 (`0x1800e0600` in the build B457 read): near child at `0x1800e1915`, the node's
        // surfaces `0x1800e191a`-`0x1800e198e`, then the far child. Its side is the engine's own test, `dot − dist < 0`
        // (`0x1800e18fd`-`0x1800e190a`), which puts a point ON the plane in front.
        _walk.Add(new WorldWalkStep(node, IsNode: true, EyeBehind: side - split.Distance < 0f));

        Descend(children.Far, x, y, z, frustum, depth + 1);
    }

    /// <summary>Keeps a leaf if the camera can see it.</summary>
    /// <param name="leaf">The leaf index.</param>
    /// <param name="frustum">The view volume.</param>
    /// <remarks>
    /// **The PVS answer is the leaf's stamp from <see cref="MarkLeaves"/>**, which holds its rules: from a real
    /// cluster a leaf with no cluster is NOT stamped (engine.dll `0x1801cadb6` — `vrad`'s `PVSCheck` treats one as
    /// visible, but that is the lighting compiler's rule, not the renderer's), and a map with NO vis lump is the
    /// engine's all-ones row (`0x18016ca26`), which is not the same as an empty PVS. `BspVisibility.None` answers false
    /// to every query, so testing it without the `HasData` guard culls the entire world.
    ///
    /// **The leaf's box is tested even when the PVS kept it**, because the two answer different
    /// questions: the PVS says what could ever be seen from here, the frustum says what is on screen
    /// now. Valve applies both.
    /// </remarks>
    private void Collect(int leaf, ViewFrustum frustum)
    {
        // **A solid leaf is never walked into** (B262): engine.dll's world walk returns on contents 1 at entry
        // (`0x1800e180f` in the 2026-10-07 build) and before a far child (`0x1800e199c`), so `R_DrawLeaf` never lists
        // one — whatever its stamp, since marking has no contents test.
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
