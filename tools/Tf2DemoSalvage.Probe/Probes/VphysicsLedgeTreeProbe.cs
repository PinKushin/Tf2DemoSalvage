using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s polygon surface manager — slot 4 of table <c>1800eae60</c>, <c>FUN_18007ada0</c> — asked for
/// the ledges within a radius of synthesized ledge trees, in process, the oracle for <c>IvpLedgeTree</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The manager is the binary's own 16 bytes**: its table, then the surface pointer, as `FUN_18000b750` allocates it. The surface
/// is <see cref="IvpLedgeTreeReplay.Surface"/>'s layout of random trees — inner nodes with and without a hull, children placed
/// inside their parent's sphere, a few NaN spheres — and each case asks four queries around the root, some starting beneath a
/// hull, a few with a NaN, infinite, zero or negative radius.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpLedgeTreeConformanceTests` reads.
/// </remarks>
public sealed class VphysicsLedgeTreeProbe : IProbe
{
    private const long ManagerTable = 0x1800eae60;
    private const long RadiusAddress = 0x18007ada0;

    private const int DefaultSweep = 20_000;
    private const int FixtureCases = 300;
    private const ulong FixtureSeed = 180077;
    private const ulong SweepSeed = 20260915;

    private const int ListCapacity = 256;

    private static readonly double[] SpecialRadii = [double.NaN, double.PositiveInfinity, 0d, -1d];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RadiusFunction(nint manager, nint center, double radius, nint ledge, nint unusedFifth, nint unusedSixth, nint list);

    /// <inheritdoc />
    public string Name => "vphysics-ledge-tree";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's polygon surface manager radius query (FUN_18007ada0 over synthesized ledge trees) called in process and " +
        "compared with the port; 'fixture' writes the conformance suite's cases: vphysics-ledge-tree [sweep n | fixture path]";

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

        if (arguments.Count >= 2 && arguments[0] == "fixture")
        {
            Draws draws = new(FixtureSeed);

            using StreamWriter writer = File.CreateText(arguments[1]);

            writer.WriteLine("# Written by the vphysics-ledge-tree probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpLedgeTreeReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        Draws sweep = new(SweepSeed);
        int differing = 0;
        long found = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep);
            Dictionary<string, long[]> binary = native.Run(inputs);

            foreach (long lane in binary["found"])
            {
                found += lane;
            }

            IReadOnlyList<string> differences = IvpLedgeTreeReplay.Differences(binary, IvpLedgeTreeReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} ledge tree cases, {differing} differing; the binary found {found} ledges in all");
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpLedgeTreeReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        Array.Fill(inputs["kind"], IvpLedgeTreeReplay.Unused);

        float rootRadius = (float)Math.Pow(10d, (draws.Unit() * 2d) - 1d);
        (float X, float Y, float Z) root = (draws.Signed(10f), draws.Signed(10f), draws.Signed(10f));
        List<int> hulls = [];

        Generate(draws, inputs, 0, IvpLedgeTreeReplay.NodeCount, root, rootRadius, hulls);

        for (int query = 0; query < IvpLedgeTreeReplay.QueryCount; query++)
        {
            double radius = draws.Unit() < 0.05
                ? SpecialRadii[(int)(draws.Unit() * SpecialRadii.Length)]
                : rootRadius * Math.Pow(10d, (draws.Unit() * 2d) - 1.5d);

            for (int lane = 0; lane < 3; lane++)
            {
                float axis = lane switch { 0 => root.X, 1 => root.Y, _ => root.Z };

                inputs["query-center"][(query * 3) + lane] = IvpImpactReplay.Lane(axis + (draws.Signed(1f) * rootRadius * 0.8d));
            }

            inputs["query-radius"][query] = IvpImpactReplay.Lane(radius);
            inputs["query-ledge"][query] = hulls.Count > 0 && draws.Unit() < 0.4 ? hulls[(int)(draws.Unit() * hulls.Count)] : IvpLedgeTreeReplay.Unused;
        }

        return inputs;
    }

    /// <summary>A random subtree at lane <paramref name="index"/> using at most <paramref name="budget"/> lanes; returns how many it used.</summary>
    private static int Generate(
        Draws draws, Dictionary<string, long[]> inputs, int index, int budget, (float X, float Y, float Z) center, float radius, List<int> hulls)
    {
        bool terminal = budget < 3 || draws.Unit() < 0.3;
        int kind = draws.Unit() < 0.3 ? IvpLedgeTreeReplay.InnerWithLedge : IvpLedgeTreeReplay.Inner;
        bool nan = draws.Unit() < 0.02;

        if (terminal)
        {
            kind = IvpLedgeTreeReplay.Terminal;
        }

        inputs["kind"][index] = kind;
        inputs["center"][index * 3] = IvpImpactReplay.Lane(center.X);
        inputs["center"][(index * 3) + 1] = IvpImpactReplay.Lane(center.Y);
        inputs["center"][(index * 3) + 2] = IvpImpactReplay.Lane(nan ? float.NaN : center.Z);
        inputs["radius"][index] = IvpImpactReplay.Lane(radius);
        inputs["box"][index] = draws.Box() | (draws.Box() << 8) | (draws.Box() << 16);

        if (kind == IvpLedgeTreeReplay.InnerWithLedge)
        {
            hulls.Add(index);
        }

        if (terminal)
        {
            return 1;
        }

        int leftBudget = 1 + (2 * (int)(draws.Unit() * ((budget - 1) / 2)));
        int left = Generate(draws, inputs, index + 1, leftBudget, Child(draws, center, radius), radius * (0.2f + (0.6f * (float)draws.Unit())), hulls);

        return 1 + left + Generate(
            draws, inputs, index + 1 + left, budget - 1 - left, Child(draws, center, radius), radius * (0.2f + (0.6f * (float)draws.Unit())), hulls);
    }

    private static (float X, float Y, float Z) Child(Draws draws, (float X, float Y, float Z) center, float radius) =>
        (center.X + draws.Signed(radius * 0.6f), center.Y + draws.Signed(radius * 0.6f), center.Z + draws.Signed(radius * 0.6f));

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public float Signed(float scale) => (float)((Unit() * 2d) - 1d) * scale;

        /// <summary>A box byte: nine tenths from 150–255, so the box prunes less than the sphere, and a tenth anywhere.</summary>
        public int Box() => Unit() < 0.1 ? (int)(Unit() * 256) : 150 + (int)(Unit() * 106);
    }

    /// <summary>The binary's surface manager, a query point and a big vector, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private readonly RadiusFunction _radius;
        private readonly nint _table;
        private readonly nint _manager = Marshal.AllocHGlobal(16);
        private readonly nint _center = Marshal.AllocHGlobal(24);
        private readonly nint _list = Marshal.AllocHGlobal(16);
        private readonly nint _elements = Marshal.AllocHGlobal(ListCapacity * 8);

        public Native(nint module)
        {
            _radius = VphysicsLibrary.Function<RadiusFunction>(module, RadiusAddress);
            _table = VphysicsLibrary.Address(module, ManagerTable);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            (byte[] bytes, _, int[] ledges) = IvpLedgeTreeReplay.Surface(inputs);
            Dictionary<string, long[]> outputs = IvpLedgeTreeReplay.NewOutputs();
            nint surface = Marshal.AllocHGlobal(bytes.Length);

            try
            {
                Marshal.Copy(bytes, 0, surface, bytes.Length);
                Marshal.WriteIntPtr(_manager, 0, _table);
                Marshal.WriteIntPtr(_manager, 8, surface);

                for (int query = 0; query < IvpLedgeTreeReplay.QueryCount; query++)
                {
                    int ledge = (int)inputs["query-ledge"][query];
                    List<int> found = [];

                    for (int lane = 0; lane < 3; lane++)
                    {
                        Marshal.WriteInt64(_center, lane * 8, inputs["query-center"][(query * 3) + lane]);
                    }

                    Marshal.WriteInt32(_list, 0, ListCapacity);
                    Marshal.WriteInt32(_list, 4, 0);
                    Marshal.WriteIntPtr(_list, 8, _elements);

                    _radius(
                        _manager, _center, BitConverter.Int64BitsToDouble(inputs["query-radius"][query]),
                        ledge == IvpLedgeTreeReplay.Unused ? 0 : surface + ledges[ledge], 0, 0, _list);

                    for (int index = 0; index < Marshal.ReadInt32(_list, 4); index++)
                    {
                        found.Add((int)(Marshal.ReadIntPtr(_elements, index * 8) - surface));
                    }

                    IvpLedgeTreeReplay.Record(outputs, query, found);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(surface);
            }

            return outputs;
        }

        public void Dispose()
        {
            foreach (nint block in new[] { _manager, _center, _list, _elements })
            {
                Marshal.FreeHGlobal(block);
            }
        }
    }
}
