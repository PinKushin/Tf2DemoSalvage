using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's OV tree as lanes of bits — a sequence of inserts (<c>FUN_18009ecb0</c>) and removals (<c>FUN_18009efc0</c>) over sixteen
/// nodes, each step's answer, the node's cell key, the nodes it found and a digest of the whole tree (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-ov-tree` probe from the shipped `vphysics.dll` and
/// replayed by `IvpOvTreeConformanceTests`. **The tree's shape is carried as a digest** — FNV-1a over every cell, root first and
/// children in order: its key, its child and node counts, and its nodes' indices — and the nodes found as a digest of their indices
/// in order, so a difference names the step and the lane but not the cell; the probe's sweep prints no more either.
/// </remarks>
public static class IvpOvTreeReplay
{
    /// <summary>How many nodes a case files.</summary>
    public const int NodeCount = 16;

    /// <summary>How many steps a case takes.</summary>
    public const int StepCount = 24;

    /// <summary>A step that files its node — taking it out first when it is filed, as the broad phase does.</summary>
    public const int FileStep = 0;

    /// <summary>A step that takes its node out.</summary>
    public const int RemoveStep = 1;

    private const ulong Offset = 14695981039346656037UL;

    private const ulong Prime = 1099511628211UL;

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("kind", IvpReplayKind.Whole32, StepCount),
        new("node", IvpReplayKind.Whole32, StepCount),
        new("center", IvpReplayKind.Real32, StepCount * 3),
        new("radius", IvpReplayKind.Real64, StepCount),
        new("outer", IvpReplayKind.Real64, StepCount),
    ];

    /// <summary>What each step leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("result", IvpReplayKind.Real64, StepCount),
        new("filed", IvpReplayKind.Real32, StepCount),
        new("key", IvpReplayKind.Whole32, StepCount * 5),
        new("found", IvpReplayKind.Whole32, StepCount),
        new("found-digest", IvpReplayKind.Real64, StepCount),
        new("tree-digest", IvpReplayKind.Real64, StepCount),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        IvpOvTree tree = new();
        IvpOvNode[] nodes = new IvpOvNode[NodeCount];
        Dictionary<IvpOvNode, int> indices = new(ReferenceEqualityComparer.Instance);
        Dictionary<string, long[]> outputs = NewOutputs();

        for (int index = 0; index < NodeCount; index++)
        {
            nodes[index] = new IvpOvNode();
            indices[nodes[index]] = index;
        }

        for (int step = 0; step < StepCount; step++)
        {
            IvpOvNode node = nodes[IvpImpactReplay.Whole32(inputs, "node", step)];
            List<IvpOvNode> found = [];

            tree.Remove(node);

            if (IvpImpactReplay.Whole32(inputs, "kind", step) == FileStep)
            {
                node.Center = IvpImpactReplay.Vector(inputs, "center", step * 3);
                outputs["result"][step] = IvpImpactReplay.Lane(tree.Insert(
                    node, IvpImpactReplay.Real64(inputs, "radius", step), IvpImpactReplay.Real64(inputs, "outer", step), found));
            }

            outputs["filed"][step] = IvpImpactReplay.Lane(node.Radius);

            if (node.Cell is { } cell)
            {
                WriteKey(outputs["key"], step, cell.X, cell.Y, cell.Z, cell.Level, cell.Exponent);
            }

            outputs["found"][step] = found.Count;
            outputs["found-digest"][step] = Digest(found.ConvertAll(other => indices[other]));
            outputs["tree-digest"][step] = TreeDigest(tree.Root, indices);
        }

        return outputs;
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

    /// <summary>A step's cell key into the key lanes.</summary>
    internal static void WriteKey(long[] lanes, int step, int x, int y, int z, int level, int exponent)
    {
        lanes[step * 5] = x;
        lanes[(step * 5) + 1] = y;
        lanes[(step * 5) + 2] = z;
        lanes[(step * 5) + 3] = level;
        lanes[(step * 5) + 4] = exponent;
    }

    /// <summary>FNV-1a over a sequence of ints, as a lane.</summary>
    internal static long Digest(IEnumerable<int> values)
    {
        ulong hash = Offset;

        foreach (int value in values)
        {
            hash = Mix(hash, value);
        }

        return unchecked((long)hash);
    }

    private static long TreeDigest(IvpOvCell? root, Dictionary<IvpOvNode, int> indices)
    {
        List<int> preorder = [];

        if (root is null)
        {
            preorder.Add(-1);
        }
        else
        {
            Walk(root, indices, preorder);
        }

        return Digest(preorder);
    }

    private static void Walk(IvpOvCell cell, Dictionary<IvpOvNode, int> indices, List<int> preorder)
    {
        preorder.AddRange([cell.X, cell.Y, cell.Z, cell.Level, cell.Exponent, cell.Children.Count, cell.Nodes.Count]);

        foreach (IvpOvNode node in cell.Nodes)
        {
            preorder.Add(indices[node]);
        }

        foreach (IvpOvCell child in cell.Children)
        {
            Walk(child, indices, preorder);
        }
    }

    private static ulong Mix(ulong hash, int value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash = unchecked((hash ^ (byte)(value >> shift)) * Prime);
        }

        return hash;
    }
}
