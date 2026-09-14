using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// The polygon surface manager's radius query (<c>FUN_18007ada0</c> and its walk <c>FUN_18007afb0</c>) as lanes of bits: a
/// synthesized ledge tree, four queries, and the ledges each found (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-ledge-tree` probe from the shipped `vphysics.dll` and
/// replayed by `IvpLedgeTreeConformanceTests`. **The tree is laid out as the compiler lays one out** — the 0x30-byte surface
/// header, a 0x10-byte stub for each ledge, then the nodes in preorder, a left child inline at `+0x1c` — by <see cref="Surface"/>,
/// which both the probe and the port read: the probe hands those bytes to the binary, and the port reads them through
/// <see cref="PhysicsHull.Tree"/>, so the reader is pinned with the walk. The walk never reads a ledge's triangles, so a stub
/// carries only the back-offset to its node and the children bits; a `ledge-back` lane other than −1 points a stub at another
/// node, which a compiler never writes but which settles which of the two a query beneath the ledge starts from.
/// </remarks>
public static class IvpLedgeTreeReplay
{
    /// <summary>The most nodes a case's tree holds.</summary>
    public const int NodeCount = 31;

    /// <summary>How many queries a case asks.</summary>
    public const int QueryCount = 4;

    /// <summary>How many of a query's ledges are carried by offset; the rest are in its digest.</summary>
    public const int CarriedLedges = 8;

    /// <summary>A node lane that holds no node.</summary>
    public const int Unused = -1;

    /// <summary>An inner node with its own hull ledge.</summary>
    public const int InnerWithLedge = 0;

    /// <summary>An inner node without a ledge.</summary>
    public const int Inner = 1;

    /// <summary>A terminal node, which always has a ledge.</summary>
    public const int Terminal = 2;

    private const int SurfaceHeader = 0x30;
    private const int LedgeStub = 0x10;
    private const int NodeSize = 0x1c;
    private const int LedgeTreeOffset = 0x20;

    /// <summary>What a case is given: the nodes in preorder, and the queries.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("kind", IvpReplayKind.Whole32, NodeCount),
        new("center", IvpReplayKind.Real32, NodeCount * 3),
        new("radius", IvpReplayKind.Real32, NodeCount),
        new("box", IvpReplayKind.Whole32, NodeCount),
        new("ledge-back", IvpReplayKind.Whole32, NodeCount),
        new("query-center", IvpReplayKind.Real64, QueryCount * 3),
        new("query-radius", IvpReplayKind.Real64, QueryCount),
        new("query-ledge", IvpReplayKind.Whole32, QueryCount),
    ];

    /// <summary>What each query leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("found", IvpReplayKind.Whole32, QueryCount),
        new("ledges", IvpReplayKind.Whole32, QueryCount * CarriedLedges),
        new("digest", IvpReplayKind.Real64, QueryCount),
    ];

    /// <summary>The surface bytes a case's lanes describe.</summary>
    /// <param name="inputs">The case's inputs.</param>
    /// <returns>The <c>IVP_Compact_Surface</c> and everything after it, and each node's offset within it (−1 for an unused lane).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">The kinds do not describe a tree in preorder.</exception>
    public static (byte[] Surface, int[] Nodes, int[] Ledges) Surface(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        int nodes = 0;
        int ledges = 0;

        while (nodes < NodeCount && IvpImpactReplay.Whole32(inputs, "kind", nodes) != Unused)
        {
            ledges += IvpImpactReplay.Whole32(inputs, "kind", nodes) == Inner ? 0 : 1;
            nodes++;
        }

        int tree = SurfaceHeader + (ledges * LedgeStub);
        byte[] surface = new byte[tree + (nodes * NodeSize)];
        int[] nodeOffsets = new int[NodeCount];
        int[] ledgeOffsets = new int[NodeCount];
        int ledge = SurfaceHeader;

        Array.Fill(nodeOffsets, -1);
        Array.Fill(ledgeOffsets, -1);
        BitConverter.TryWriteBytes(surface.AsSpan(LedgeTreeOffset), tree);

        if (nodes == 0 || Place(inputs, surface, 0, tree, nodeOffsets) != nodes)
        {
            throw new InvalidDataException("A ledge tree case's kinds do not describe a tree in preorder.");
        }

        for (int index = 0; index < nodes; index++)
        {
            int at = nodeOffsets[index];
            int kind = IvpImpactReplay.Whole32(inputs, "kind", index);

            for (int lane = 0; lane < 3; lane++)
            {
                BitConverter.TryWriteBytes(surface.AsSpan(at + 8 + (lane * 4)), IvpImpactReplay.Whole32(inputs, "center", (index * 3) + lane));
            }

            BitConverter.TryWriteBytes(surface.AsSpan(at + 0x14), IvpImpactReplay.Whole32(inputs, "radius", index));
            BitConverter.TryWriteBytes(surface.AsSpan(at + 0x18), IvpImpactReplay.Whole32(inputs, "box", index) & 0xffffff);

            if (kind != Inner)
            {
                int back = IvpImpactReplay.Whole32(inputs, "ledge-back", index);

                ledgeOffsets[index] = ledge;
                BitConverter.TryWriteBytes(surface.AsSpan(at + 4), ledge - at);
                BitConverter.TryWriteBytes(surface.AsSpan(ledge + 4), (back == Unused ? at : nodeOffsets[back]) - ledge);
                BitConverter.TryWriteBytes(surface.AsSpan(ledge + 8), kind == InnerWithLedge ? 1 : 0);
                ledge += LedgeStub;
            }
        }

        return (surface, nodeOffsets, ledgeOffsets);
    }

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">The surface does not read as a tree.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        (byte[] surface, int[] nodes, _) = Surface(inputs);
        PhysicsLedgeTree tree = PhysicsHull.Tree(surface) ?? throw new InvalidDataException("A synthesized surface read as no tree.");
        Dictionary<string, long[]> outputs = NewOutputs();

        for (int query = 0; query < QueryCount; query++)
        {
            List<PhysicsLedgeTreeNode> found = [];
            int ledge = IvpImpactReplay.Whole32(inputs, "query-ledge", query);
            (double X, double Y, double Z) center = (
                IvpImpactReplay.Real64(inputs, "query-center", query * 3),
                IvpImpactReplay.Real64(inputs, "query-center", (query * 3) + 1),
                IvpImpactReplay.Real64(inputs, "query-center", (query * 3) + 2));

            IvpLedgeTree.LedgesWithin(
                tree, ledge == Unused ? null : tree.Node(nodes[ledge]), center, IvpImpactReplay.Real64(inputs, "query-radius", query), found);

            Record(outputs, query, found.ConvertAll(node => node.LedgeOffset));
        }

        return outputs;
    }

    /// <summary>A zeroed output dictionary.</summary>
    internal static Dictionary<string, long[]> NewOutputs()
    {
        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in Outputs)
        {
            outputs[field.Name] = new long[field.Count];
        }

        return outputs;
    }

    /// <summary>A query's ledges, by their offsets in the surface, into its lanes.</summary>
    internal static void Record(Dictionary<string, long[]> outputs, int query, List<int> ledges)
    {
        ulong hash = 14695981039346656037UL;

        for (int index = 0; index < ledges.Count; index++)
        {
            if (index < CarriedLedges)
            {
                outputs["ledges"][(query * CarriedLedges) + index] = ledges[index];
            }

            hash = unchecked((hash ^ (uint)ledges[index]) * 1099511628211UL);
        }

        outputs["found"][query] = ledges.Count;
        outputs["digest"][query] = unchecked((long)hash);
    }

    /// <summary>Every lane where two readings of the same case differ, named.</summary>
    /// <param name="expected">What the binary left.</param>
    /// <param name="actual">What the port left.</param>
    /// <returns>One line per differing lane; empty when the two agree.</returns>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, long[]> expected, IReadOnlyDictionary<string, long[]> actual) =>
        IvpImpactReplay.Differences(Outputs, expected, actual);

    /// <summary>Writes one case in the fixture's form.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="replay">The case.</param>
    public static void Write(TextWriter writer, IvpReplayCase replay) => IvpImpactReplay.Write(Inputs, Outputs, writer, replay);

    /// <summary>Reads every case a fixture holds.</summary>
    /// <param name="reader">The fixture.</param>
    /// <returns>The cases, in file order.</returns>
    public static IReadOnlyList<IvpReplayCase> Parse(TextReader reader) => IvpImpactReplay.Parse(Inputs, Outputs, reader);

    /// <summary>Lays out the subtree whose root is lane <paramref name="index"/> at <paramref name="at"/>; returns the lane after it.</summary>
    private static int Place(IReadOnlyDictionary<string, long[]> inputs, byte[] surface, int index, int at, int[] offsets)
    {
        if (index >= NodeCount || IvpImpactReplay.Whole32(inputs, "kind", index) == Unused)
        {
            return -1;
        }

        offsets[index] = at;

        if (IvpImpactReplay.Whole32(inputs, "kind", index) == Terminal)
        {
            return index + 1;
        }

        int right = Place(inputs, surface, index + 1, at + NodeSize, offsets);

        if (right < 0)
        {
            return -1;
        }

        BitConverter.TryWriteBytes(surface.AsSpan(at), (right - index) * NodeSize);

        return Place(inputs, surface, right, at + ((right - index) * NodeSize), offsets);
    }
}
