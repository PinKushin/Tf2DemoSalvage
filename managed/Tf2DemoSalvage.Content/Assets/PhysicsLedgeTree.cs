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

    /// <summary>The node at an offset in the surface.</summary>
    /// <param name="offset">The node's offset.</param>
    /// <returns>The node.</returns>
    /// <exception cref="KeyNotFoundException">No node of the tree is there — where the engine would read whatever bytes are.</exception>
    public PhysicsLedgeTreeNode Node(int offset) => _nodes[offset];
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
