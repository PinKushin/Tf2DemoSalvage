using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s OV tree — its constructor <c>FUN_18009d820</c>, the node constructor <c>FUN_18009d7b0</c>, the
/// insert <c>FUN_18009ecb0</c> and the removal <c>FUN_18009efc0</c> — called in process, the oracle for <c>IvpOvTree</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary builds its own tree**: the constructor fills the level tables and makes the cell hash through vphysics' allocator,
/// and sixteen nodes are built by its node constructor. Each case files and removes them in a drawn order; after every step the
/// probe reads the answer, the node's radius and cell key, the nodes the insert found, and walks the tree from its root. Every
/// case ends by removing each node, so the next starts from an empty tree — which the probe checks.
///
/// **Two inputs are kept out of the draws**: a NaN or infinite centre, and radii far enough apart that the tree outgrows its
/// 81-entry level table. The port throws where the binary would read past the table; what the binary does there is not
/// measured. Every centre stays within a case's scale and every radius within a millionth of it.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpOvTreeConformanceTests` reads.
/// </remarks>
public sealed class VphysicsOvTreeProbe : IProbe
{
    private const long ConstructAddress = 0x18009d820;
    private const long NodeAddress = 0x18009d7b0;
    private const long InsertAddress = 0x18009ecb0;
    private const long RemoveAddress = 0x18009efc0;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 300;
    private const ulong FixtureSeed = 99880;
    private const ulong SweepSeed = 20260917;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructFunction(nint tree);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NodeFunction(nint node, nint owner);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double InsertFunction(nint tree, nint node, double radius, double outer, nint list);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemoveFunction(nint tree, nint node);

    /// <inheritdoc />
    public string Name => "vphysics-ov-tree";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's OV tree (the insert FUN_18009ecb0 and removal FUN_18009efc0 over its own constructed tree and nodes) " +
        "called in process and compared with the port; 'fixture' writes the conformance suite's cases: " +
        "vphysics-ov-tree [sweep n | fixture path]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        using Native native = new(module);

        if (!Control(output, native))
        {
            return;
        }

        if (arguments.Count >= 2 && arguments[0] == "fixture")
        {
            Fixture(output, native, arguments[1]);
            return;
        }

        Sweep(output, native, arguments.Count >= 2 && arguments[0] == "sweep"
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultSweep);
    }

    private static bool Control(TextWriter output, Native native)
    {
        Draws draws = new(SweepSeed);
        Dictionary<string, long[]> inputs = RandomCase(draws);

        for (int step = 0; step < IvpOvTreeReplay.StepCount; step++)
        {
            inputs["kind"][step] = IvpOvTreeReplay.FileStep;
            inputs["node"][step] = step % 2;
            inputs["center"][step * 3] = IvpImpactReplay.Lane(step * 0.25f);
            inputs["center"][(step * 3) + 1] = 0;
            inputs["center"][(step * 3) + 2] = 0;
            inputs["radius"][step] = IvpImpactReplay.Lane(1d);
            inputs["outer"][step] = IvpImpactReplay.Lane(1d);
        }

        Dictionary<string, long[]> binary = native.Run(inputs);
        bool overlapped = binary["found"][1] >= 1;
        IReadOnlyList<string> differences = IvpOvTreeReplay.Differences(binary, IvpOvTreeReplay.Run(inputs));

        output.WriteLine(
            $"control: two unit spheres a quarter apart — the binary found {binary["found"][1]} nodes (at least the other), the " +
            $"tree emptied afterwards: {native.Empty}; the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return overlapped && native.Empty;
    }

    private static void Sweep(TextWriter output, Native native, int count)
    {
        Draws draws = new(SweepSeed);
        int differing = 0;
        long found = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            Dictionary<string, long[]> binary = native.Run(inputs);

            if (!native.Empty)
            {
                output.WriteLine($"case {index}: the binary's tree did not empty; stopping");
                return;
            }

            foreach (long lane in binary["found"])
            {
                found += lane;
            }

            IReadOnlyList<string> differences = IvpOvTreeReplay.Differences(binary, IvpOvTreeReplay.Run(inputs));

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            if (differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} OV tree cases, {differing} differing; the binary found {found} nodes in all");
    }

    private static void Fixture(TextWriter output, Native native, string path)
    {
        Draws draws = new(FixtureSeed);

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-ov-tree probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);

            IvpOvTreeReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        int killers = 0;

        foreach ((string label, Dictionary<string, long[]> inputs) in Killers())
        {
            Dictionary<string, long[]> outputs = native.Run(inputs);

            IvpOvTreeReplay.Write(writer, new IvpReplayCase(label, inputs, outputs));
            output.WriteLine($"{label}: the binary found {string.Join(' ', outputs["found"][..3])} on its first three steps");
            killers++;
        }

        output.WriteLine($"{FixtureCases} cases and {killers} searched cases written to {path}");
    }

    /// <summary>Cases searched for the orders a sabotage round found random draws could not see.</summary>
    /// <remarks>
    /// **Cells that only touch**: two unit spheres tangent on an axis file in cells sharing a face there, and only the box test's
    /// strictness decides whether the other cell is walked — each axis, each order. **The root as the target**: a node filed
    /// into the root's own cell while the root has children walks them with the shift for a target at the cell's level. **A
    /// radius sum that rounds**: `1f + 2^−24` is `1f`, and the centres lie between the two squares.
    /// </remarks>
    private static IEnumerable<(string Label, Dictionary<string, long[]> Inputs)> Killers()
    {
        string[] axes = ["x", "y", "z"];

        for (int axis = 0; axis < 3; axis++)
        {
            float[] positive = [0f, 0f, 0f];
            float[] negative = [0f, 0f, 0f];

            positive[axis] = 1f;
            negative[axis] = -1f;
            yield return ($"touching-cells-{axes[axis]}", Steps((positive, 1d), (negative, 1d)));
            yield return ($"touching-cells-{axes[axis]}-reversed", Steps((negative, 1d), (positive, 1d)));
        }

        yield return ("root-target", Steps(([0f, 0f, 0f], 1d), ([-0.5f, -0.5f, -0.5f], 0.01d), ([0f, 0f, 0f], 1d)));
        yield return ("radius-sum-rounds", Steps(([0f, 0f, 0f], 1d), ([1f, MathF.ScaleB(1f, -12), 0f], Math.ScaleB(1d, -24))));
    }

    /// <summary>A case filing each sphere into its own node in order, the rest of its steps removing a node never filed.</summary>
    private static Dictionary<string, long[]> Steps(params (float[] Center, double Radius)[] files)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpOvTreeReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        for (int step = 0; step < IvpOvTreeReplay.StepCount; step++)
        {
            bool filing = step < files.Length;

            inputs["kind"][step] = filing ? IvpOvTreeReplay.FileStep : IvpOvTreeReplay.RemoveStep;
            inputs["node"][step] = filing ? step : IvpOvTreeReplay.NodeCount - 1;

            if (filing)
            {
                for (int lane = 0; lane < 3; lane++)
                {
                    inputs["center"][(step * 3) + lane] = IvpImpactReplay.Lane(files[step].Center[lane]);
                }

                inputs["radius"][step] = IvpImpactReplay.Lane(files[step].Radius);
                inputs["outer"][step] = IvpImpactReplay.Lane(files[step].Radius);
            }
        }

        return inputs;
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpOvTreeReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        double scale = Math.Pow(10d, draws.Below(6) - 2);
        double spread = scale * new[] { 0.5d, 2d, 10d, 100d }[draws.Below(4)];

        for (int step = 0; step < IvpOvTreeReplay.StepCount; step++)
        {
            double radius = draws.Below(10) switch
            {
                0 => 0d,
                1 => scale * 1e-6,
                _ => scale * draws.Unit(),
            };

            inputs["kind"][step] = draws.Chance(0.25) ? IvpOvTreeReplay.RemoveStep : IvpOvTreeReplay.FileStep;
            inputs["node"][step] = draws.Below(IvpOvTreeReplay.NodeCount);
            inputs["center"][step * 3] = IvpImpactReplay.Lane((float)(draws.Signed() * spread));
            inputs["center"][(step * 3) + 1] = IvpImpactReplay.Lane((float)(draws.Signed() * spread));
            inputs["center"][(step * 3) + 2] = IvpImpactReplay.Lane((float)(draws.Signed() * spread));
            inputs["radius"][step] = IvpImpactReplay.Lane(radius);
            inputs["outer"][step] = IvpImpactReplay.Lane(draws.Chance(0.7) ? radius : radius * (1d + (draws.Unit() * 4d)));
        }

        return inputs;
    }

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public double Signed() => (Unit() * 2d) - 1d;

        public bool Chance(double probability) => Unit() < probability;

        public int Below(int bound) => (int)(Unit() * bound);
    }

    /// <summary>The binary's own tree and nodes, and a found list, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int TreeSize = 0x60;
        private const int NodeSize = 0x50;
        private const int OwnerSize = 0x100;
        private const int ListSize = 0x10;
        private const int ListCapacity = 0x80;

        private readonly InsertFunction _insert;
        private readonly RemoveFunction _remove;
        private readonly nint _tree = Marshal.AllocHGlobal(TreeSize);
        private readonly nint _owner = Marshal.AllocHGlobal(OwnerSize);
        private readonly nint _list = Marshal.AllocHGlobal(ListSize);
        private readonly nint _listElements = Marshal.AllocHGlobal(ListCapacity * 8);
        private readonly nint[] _nodes = new nint[IvpOvTreeReplay.NodeCount];
        private readonly Dictionary<nint, int> _indices = [];

        public Native(nint module)
        {
            _insert = VphysicsLibrary.Function<InsertFunction>(module, InsertAddress);
            _remove = VphysicsLibrary.Function<RemoveFunction>(module, RemoveAddress);

            for (int offset = 0; offset < TreeSize; offset++)
            {
                Marshal.WriteByte(_tree, offset, 0);
            }

            VphysicsLibrary.Function<ConstructFunction>(module, ConstructAddress)(_tree);

            NodeFunction node = VphysicsLibrary.Function<NodeFunction>(module, NodeAddress);

            for (int index = 0; index < _nodes.Length; index++)
            {
                _nodes[index] = Marshal.AllocHGlobal(NodeSize);
                node(_nodes[index], _owner);
                _indices[_nodes[index]] = index;
            }
        }

        /// <summary>Whether the tree's root is null.</summary>
        public bool Empty => Marshal.ReadIntPtr(_tree, 0x58) == 0;

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = IvpOvTreeReplay.NewOutputs();

            // What FUN_18009d7b0 writes but its watcher vector, which it allocates: a case starts from unfiled nodes.
            foreach (nint unfiled in _nodes)
            {
                Marshal.WriteInt32(unfiled, 0x20, 0);
                Marshal.WriteInt64(unfiled, 0x24, 0);
                Marshal.WriteInt32(unfiled, 0x30, unchecked((int)0xbf800000));
            }

            for (int step = 0; step < IvpOvTreeReplay.StepCount; step++)
            {
                nint node = _nodes[inputs["node"][step]];

                _remove(_tree, node);
                Marshal.WriteInt16(_list, 0, ListCapacity);
                Marshal.WriteInt16(_list, 2, 0);
                Marshal.WriteIntPtr(_list, 8, _listElements);

                if (inputs["kind"][step] == IvpOvTreeReplay.FileStep)
                {
                    for (int lane = 0; lane < 3; lane++)
                    {
                        Marshal.WriteInt32(node, 0x20 + (lane * 4), unchecked((int)inputs["center"][(step * 3) + lane]));
                    }

                    outputs["result"][step] = BitConverter.DoubleToInt64Bits(_insert(
                        _tree, node, BitConverter.Int64BitsToDouble(inputs["radius"][step]),
                        BitConverter.Int64BitsToDouble(inputs["outer"][step]), _list));
                }

                outputs["filed"][step] = (uint)Marshal.ReadInt32(node, 0x30);

                nint cell = Marshal.ReadIntPtr(node, 0x10);

                if (cell != 0)
                {
                    IvpOvTreeReplay.WriteKey(
                        outputs["key"], step, Marshal.ReadInt32(cell, 0), Marshal.ReadInt32(cell, 4), Marshal.ReadInt32(cell, 8),
                        Marshal.ReadInt32(cell, 0xc), Marshal.ReadInt32(cell, 0x10));
                }

                int found = (ushort)Marshal.ReadInt16(_list, 2);
                List<int> indices = [];

                for (int index = 0; index < found; index++)
                {
                    indices.Add(_indices[Marshal.ReadIntPtr(Marshal.ReadIntPtr(_list, 8), index * 8)]);
                }

                outputs["found"][step] = found;
                outputs["found-digest"][step] = IvpOvTreeReplay.Digest(indices);
                outputs["tree-digest"][step] = TreeDigest();
            }

            foreach (nint node in _nodes)
            {
                _remove(_tree, node);
            }

            return outputs;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_list);
            Marshal.FreeHGlobal(_listElements);
        }

        private long TreeDigest()
        {
            List<int> preorder = [];
            nint root = Marshal.ReadIntPtr(_tree, 0x58);

            if (root == 0)
            {
                preorder.Add(-1);
            }
            else
            {
                Walk(root, preorder);
            }

            return IvpOvTreeReplay.Digest(preorder);
        }

        private void Walk(nint cell, List<int> preorder)
        {
            int children = (ushort)Marshal.ReadInt16(cell, 0x22);
            int nodes = (ushort)Marshal.ReadInt16(cell, 0x32);

            preorder.AddRange([
                Marshal.ReadInt32(cell, 0), Marshal.ReadInt32(cell, 4), Marshal.ReadInt32(cell, 8), Marshal.ReadInt32(cell, 0xc),
                Marshal.ReadInt32(cell, 0x10), children, nodes]);

            for (int index = 0; index < nodes; index++)
            {
                preorder.Add(_indices[Marshal.ReadIntPtr(Marshal.ReadIntPtr(cell, 0x38), index * 8)]);
            }

            for (int index = 0; index < children; index++)
            {
                Walk(Marshal.ReadIntPtr(Marshal.ReadIntPtr(cell, 0x28), index * 8), preorder);
            }
        }
    }
}
