using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>An <c>IVP_Compact_Surface</c>'s ledge tree, kept as a tree — what the polygon surface manager's radius query walks (B369).</summary>
/// <remarks>
/// **Read by the engine in place, not deserialised**: `FUN_18007afb0` walks the surface's own bytes from the root at
/// `surface+0x20` (`docs/findings/51`, *The ledges a pair is built from*). Each node is kept with its offset in the surface, so a
/// ledge is named by the same number the binary's pointer arithmetic gives.
/// </remarks>
public sealed class PhysicsLedgeTree
{
    private readonly Dictionary<int, PhysicsLedgeTreeNode> _nodes;

    internal PhysicsLedgeTree(PhysicsLedgeTreeNode root, Dictionary<int, PhysicsLedgeTreeNode> nodes)
    {
        Root = root;
        _nodes = nodes;
    }

    /// <summary>The root, at <c>surface + surface+0x20</c>.</summary>
    public PhysicsLedgeTreeNode Root { get; }

    /// <summary>The surface's mass centre, <c>surface+0x0</c>; zero for a tree not read from a surface.</summary>
    public Vector3 MassCenter { get; init; }

    /// <summary>The surface's radius, <c>surface+0x18</c>, which a core's radius is taken from (<c>18007aeb0</c>).</summary>
    public float Radius { get; init; }

    /// <summary>The collide's drag areas, <c>CollideGetOrthographicAreas</c> — see <see cref="PhysicsHull.DragAxisAreas"/>.</summary>
    /// <remarks>`(1, 1, 1)`, the collide constructor's own default, until a tagged solid's header replaces it.</remarks>
    public Vector3 DragAxisAreas { get; internal set; } = Vector3.One;

    /// <summary>The byte at <c>surface+0x1C</c>: the surface's deviation, in 250ths of <see cref="Radius"/>.</summary>
    public byte Deviation { get; init; }

    /// <summary>A tree of one terminal node, for a solid whose whole hull is a single ledge.</summary>
    /// <param name="ledge">The ledge.</param>
    /// <returns>The tree, which a surface manager can be built over.</returns>
    /// <remarks>
    /// **Every TF2 ragdoll element is one ledge** (`docs/verification`), and a surface manager is what the pair creation queries —
    /// so a single-ledge body needs a tree rather than a bare node. A genuinely compound solid needs the real tree instead.
    /// </remarks>
    public static PhysicsLedgeTree ForLedge(PhysicsLedge ledge) => new(SingleLedge(ledge), []);

    /// <summary>The node at an offset in the surface.</summary>
    /// <param name="offset">The node's offset.</param>
    /// <returns>The node.</returns>
    /// <exception cref="KeyNotFoundException">No node of the tree is there — where the engine would read whatever bytes are.</exception>
    public PhysicsLedgeTreeNode Node(int offset) => _nodes[offset];

    /// <summary>
    /// A one-node "tree" for a body whose collision is exactly one convex ledge — the common case for a ragdoll
    /// bone, which carries one <see cref="PhysicsLedge"/> per solid rather than a real ledge tree (B369).
    /// </summary>
    /// <param name="ledge">The body's one ledge.</param>
    /// <returns>A terminal node naming it, with no children.</returns>
    /// <remarks>
    /// **Not read from any file — a flat shortcut, not a tree**, matching the ordinary shape of a ragdoll's own
    /// `.phy` data: each bone's solid is one convex hull, so there is no real inner-node structure to decode in the
    /// first place. A genuinely compound body (more than one ledge) needs the real tree this method does not
    /// attempt — see `docs/HANDOFF.md`, item 3.
    ///
    /// **<see cref="PhysicsLedgeTreeNode.Box"/> is maxed out (`255, 255, 255`) rather than measured**, since no real
    /// per-node box byte exists for a synthetic node — the radius query's own box test (`IvpLedgeTree.Walk` in the
    /// animation project) against a maxed box is a no-op (it can only ever pass), so this never wrongly excludes the
    /// one ledge it names; it costs nothing beyond a query that would have matched on the sphere test alone anyway.
    /// </remarks>
    public static PhysicsLedgeTreeNode SingleLedge(PhysicsLedge ledge) =>
        new(0, ledge.Center, ledge.Radius, (255, 255, 255))
        {
            HasLedge = true,
            LedgeOffset = 0,
            Ledge = ledge,
            LedgeChildren = 0,
        };
}

/// <summary>One <c>IVP_Compact_Ledgetree_Node</c>, 0x1c bytes (B369).</summary>
public sealed class PhysicsLedgeTreeNode
{
    internal PhysicsLedgeTreeNode(int offset, Vector3 center, float radius, (byte X, byte Y, byte Z) box)
    {
        Offset = offset;
        Center = center;
        Radius = radius;
        Box = box;
    }

    /// <summary>Where the node is in the surface.</summary>
    public int Offset { get; }

    /// <summary>The bounding sphere's centre, <c>+0x8</c>.</summary>
    public Vector3 Center { get; }

    /// <summary>The bounding sphere's radius, <c>+0x14</c>.</summary>
    public float Radius { get; }

    /// <summary>The box's half-extents in 250ths of the radius, <c>+0x18..0x1a</c>.</summary>
    public (byte X, byte Y, byte Z) Box { get; }

    /// <summary>Whether <c>+0x4</c> is non-zero: an inner node's hull, or a terminal node's ledge.</summary>
    public bool HasLedge { get; internal set; }

    /// <summary>The ledge's offset in the surface, <c>node + +0x4</c>, when <see cref="HasLedge"/>.</summary>
    public int LedgeOffset { get; internal set; }

    /// <summary>
    /// The node the ledge names in its own <c>+0x4</c>, <c>ledge + ledge+0x4</c>, when <see cref="HasLedge"/>; null when that word is
    /// zero, which <c>FUN_1800b2700</c> and <c>FUN_1800b2460</c> take as naming no node.
    /// </summary>
    public int? LedgeNodeOffset { get; internal set; }

    /// <summary>
    /// The node <see cref="LedgeNodeOffset"/> names, or null when it names none — a zero word, or an offset where no node of the tree
    /// lies. The larger mindist reads its radius to choose the side it opens (<c>FUN_1800b2700</c>, <c>FUN_1800b2460</c>).
    /// </summary>
    public PhysicsLedgeTreeNode? LedgeNode { get; internal set; }

    /// <summary>
    /// The ledge, decoded as <see cref="PhysicsHull.Read(System.ReadOnlySpan{byte})"/> decodes a leaf's, when <see cref="HasLedge"/> and
    /// its bytes read as one — an inner node's hull as much as a leaf's ledge.
    /// </summary>
    public PhysicsLedge? Ledge { get; internal set; }

    /// <summary>The ledge's <c>+0x8 &amp; 3</c>, IVP's <c>has_chilren_flag</c>, when <see cref="HasLedge"/>.</summary>
    public int LedgeChildren { get; internal set; }

    /// <summary>Whether <c>+0x0</c> is zero.</summary>
    public bool IsTerminal => Right is null;

    /// <summary>The left child, inline at <c>+0x1c</c>, or null for a terminal node.</summary>
    public PhysicsLedgeTreeNode? Left { get; internal set; }

    /// <summary>The right child, <c>node + +0x0</c>, or null for a terminal node.</summary>
    public PhysicsLedgeTreeNode? Right { get; internal set; }
}
