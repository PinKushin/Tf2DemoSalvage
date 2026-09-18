using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The polygon surface manager's radius query — slot 4 of table <c>1800eae60</c>, <c>FUN_18007ada0</c>, and its walk
/// <c>FUN_18007afb0</c> — which names the ledges a pair of objects is built from (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The ledges a pair is built from*). A node whose ledge is within reach
/// answers that ledge and nothing beneath it. Pinned by the `vphysics-ledge-tree` probe (`IvpLedgeTreeConformanceTests`).
/// </remarks>
public static class IvpLedgeTree
{
    /// <summary><c>DAT_1800eedb0</c>, <c>0x3f70624dd2f1a9fc</c>: a box byte's share of the radius, 1/250.</summary>
    private static readonly double BoxUnit = BitConverter.Int64BitsToDouble(0x3f70624dd2f1a9fc);

    /// <summary>Every ledge within a radius of a point — <c>FUN_18007ada0</c>.</summary>
    /// <param name="tree">The surface's ledge tree.</param>
    /// <param name="ledge">A node whose ledge the query starts beneath, or null to start at the root.</param>
    /// <param name="center">The point, in the object's frame.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="into">Where each node whose ledge is found goes, in the order the engine appends them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> or <paramref name="into"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The ledge names no node or a terminal one, or a terminal node has no ledge — where the engine reads bytes that are not a node or not
    /// a ledge.
    /// </exception>
    /// <remarks>
    /// <code>
    /// no ledge → FUN_18007afb0(the root)
    /// a ledge → its node (ledge + ledge+0x4):  FUN_18007afb0(node+0x1c), then FUN_18007afb0(node + node+0x0)
    /// </code>
    /// </remarks>
    public static void LedgesWithin(
        PhysicsLedgeTree tree, PhysicsLedgeTreeNode? ledge, (double X, double Y, double Z) center, double radius,
        ICollection<PhysicsLedgeTreeNode> into)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(into);

        if (ledge is null)
        {
            Walk(tree.Root, center, radius, into);
            return;
        }

        PhysicsLedgeTreeNode node = tree.Node(
            ledge.LedgeNodeOffset ?? throw new InvalidOperationException("A radius query starts beneath a ledge that names no node, where the engine reads the ledge as one."));

        if (node.Left is null || node.Right is null)
        {
            throw new InvalidOperationException("A radius query starts beneath a terminal node, where the engine reads past it.");
        }

        Walk(node.Left, center, radius, into);
        Walk(node.Right, center, radius, into);
    }

    /// <summary><c>FUN_18007afb0</c>: a node's subtree, the left child recursed and the right walked in place.</summary>
    /// <remarks>
    /// <code>
    /// d = (double)centre − point per axis;  ((d.y² + d.x²) + d.z²) > ((double)r_node + r)² → return      -- a NaN walks on
    /// loop:  s = (float)((double)r_node · 0.004);  any |d| ≥ (double)((float)box · s) + r → return        -- a NaN walks on
    ///     a ledge → it is appended, return;  terminal → the node read as a ledge (only a malformed tree)
    ///     the left child walked;  node = the right child;  the sphere test again
    /// </code>
    /// </remarks>
    private static void Walk(PhysicsLedgeTreeNode node, (double X, double Y, double Z) center, double radius, ICollection<PhysicsLedgeTreeNode> into)
    {
        double x = node.Center.X - center.X;
        double y = node.Center.Y - center.Y;
        double z = node.Center.Z - center.Z;

        if (Distance(x, y, z) > Reach(node, radius))
        {
            return;
        }

        while (true)
        {
            float unit = (float)IvpMath.Mulsd(node.Radius, BoxUnit);

            if (Math.Abs(x) >= (double)(node.Box.X * unit) + radius ||
                Math.Abs(y) >= (double)(node.Box.Y * unit) + radius ||
                Math.Abs(z) >= (double)(node.Box.Z * unit) + radius)
            {
                return;
            }

            if (node.HasLedge)
            {
                into.Add(node);
                return;
            }

            if (node.Left is null || node.Right is null)
            {
                throw new InvalidOperationException("A terminal ledge-tree node has no ledge, where the engine reads the node as one.");
            }

            Walk(node.Left, center, radius, into);
            node = node.Right;
            x = node.Center.X - center.X;
            y = node.Center.Y - center.Y;
            z = node.Center.Z - center.Z;

            if (Distance(x, y, z) > Reach(node, radius))
            {
                return;
            }
        }
    }

    /// <summary><c>(d.y² + d.x²) + d.z²</c>, each square with its own operand the destination.</summary>
    private static double Distance(double x, double y, double z) =>
        IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, y), IvpMath.Mulsd(x, x)), IvpMath.Mulsd(z, z));

    /// <summary><c>((double)r_node + r)²</c>, the widened node radius the destination.</summary>
    private static double Reach(PhysicsLedgeTreeNode node, double radius)
    {
        double reach = IvpMath.Addsd(node.Radius, radius);

        return IvpMath.Mulsd(reach, reach);
    }
}
