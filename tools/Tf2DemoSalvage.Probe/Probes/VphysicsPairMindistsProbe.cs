using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s pair mindist refresh, <c>FUN_180096680</c>, called in process over two fabricated objects with
/// synthesized surfaces behind the binary's own polygon manager table — its constructor's tails detoured to recorders — the oracle
/// for <c>IvpPairMindists</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary does the allocating**: each new mindist is its own 0xe0 bytes, built by `FUN_180096680` and `FUN_1800975d0` and freed
/// by its own destructor `FUN_180096250`. **What the probe fabricates**: the environment's counters and time, the objects (kind 2,
/// resting, a surface manager on table `1800eae60`, an object cache holding the case's matrix), their cores, the pair vector, and a
/// delegator table whose slot 0 takes a mindist out by its back-index as `FUN_1800b5fd0` does. **What the probe detours**:
/// `FUN_1800977f0` and `FUN_180097940`, the exact and phantom tails, to recorders — each starts with twelve bytes of whole register
/// saves. Every case ends by deleting the pair's mindists through their own tables.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpPairMindistsConformanceTests` reads.
/// </remarks>
public sealed class VphysicsPairMindistsProbe : IProbe
{
    private const long RefreshAddress = 0x180096680;
    private const long ExactAddress = 0x1800977f0;
    private const long PhantomAddress = 0x180097940;
    private const long PolygonTable = 0x1800eae60;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 200;
    private const ulong FixtureSeed = 180096;
    private const ulong SweepSeed = 20260919;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RefreshCall(
        nint first, nint second, double gap, nint pair, nint firstLedge, nint secondLedge, nint firstRoot, nint secondRoot, nint delegator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TailCall(nint manager, nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemovedCall(nint delegator, nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint DeletingDestructor(nint self, int flag);

    /// <inheritdoc />
    public string Name => "vphysics-pair-mindists";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's pair mindist refresh (FUN_180096680, its constructor's tails detoured) over synthesized surfaces, compared " +
        "with the port; 'fixture' writes the conformance suite's cases: vphysics-pair-mindists [sweep n | fixture path]";

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

            writer.WriteLine("# Written by the vphysics-pair-mindists probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpPairMindistsReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        Draws sweep = new(SweepSeed);
        int differing = 0;
        long made = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep);
            Dictionary<string, long[]> binary = native.Run(inputs);

            made += binary["created"][IvpPairMindistsReplay.StepCount - 1];

            IReadOnlyList<string> differences = IvpPairMindistsReplay.Differences(binary, IvpPairMindistsReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} pair mindist cases, {differing} differing; the binary made {made} mindists in all");
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpPairMindistsReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        List<int>[] terminals = [[], []];

        for (int side = 0; side < 2; side++)
        {
            string suffix = IvpPairMindistsReplay.Suffix(side);
            float rootRadius = (float)(0.5d + draws.Unit());

            Array.Fill(inputs["kind" + suffix], IvpLedgeTreeReplay.Unused);
            Generate(draws, inputs, suffix, 0, IvpPairMindistsReplay.NodeCount, (0f, 0f, 0f), rootRadius, terminals[side]);
            inputs["extra"][side] = IvpImpactReplay.Lane((float)(draws.Unit() * 0.05d));
            inputs["core-radius"][side] = IvpImpactReplay.Lane((float)(0.3d + (draws.Unit() * 0.7d)));

            (double X, double Y, double Z, double W) rotation = draws.Rotation();

            inputs["cache-rotation"][side * 4] = IvpImpactReplay.Lane(rotation.X);
            inputs["cache-rotation"][(side * 4) + 1] = IvpImpactReplay.Lane(rotation.Y);
            inputs["cache-rotation"][(side * 4) + 2] = IvpImpactReplay.Lane(rotation.Z);
            inputs["cache-rotation"][(side * 4) + 3] = IvpImpactReplay.Lane(rotation.W);

            for (int lane = 0; lane < 3; lane++)
            {
                inputs["cache-translation"][(side * 3) + lane] = IvpImpactReplay.Lane(draws.Signed() * (side == 0 ? 0.5d : 1.5d));
            }
        }

        double now = 10d + draws.Unit();

        for (int step = 0; step < IvpPairMindistsReplay.StepCount; step++)
        {
            now += draws.Unit() * 0.02d;
            inputs["now"][step] = IvpImpactReplay.Lane(now);
            inputs["gap"][step] = IvpImpactReplay.Lane(draws.Unit() * 0.3d);

            for (int side = 0; side < 2; side++)
            {
                // Each core sits near the OTHER object's cache translation, so its sphere reaches the other's surface.
                int other = 1 - side;
                int at = (step * 2) + side;

                inputs["stepped"][at] = IvpImpactReplay.Lane(now - (draws.Unit() * 0.02d));

                for (int lane = 0; lane < 3; lane++)
                {
                    double target = BitConverter.Int64BitsToDouble(inputs["cache-translation"][(other * 3) + lane]);

                    inputs["position"][(at * 3) + lane] = IvpImpactReplay.Lane(target + (draws.Signed() * 0.8d));
                    inputs["velocity"][(at * 3) + lane] = IvpImpactReplay.Lane((float)(draws.Signed() * 2d));
                }
            }

            inputs["given-first"][step] = terminals[0].Count > 0 && draws.Unit() < 0.15 ? terminals[0][(int)(draws.Unit() * terminals[0].Count)] : -1;
            inputs["given-second"][step] = terminals[1].Count > 0 && draws.Unit() < 0.15 ? terminals[1][(int)(draws.Unit() * terminals[1].Count)] : -1;
        }

        return inputs;
    }

    /// <summary>A random subtree of inner and terminal nodes, children inside their parent's sphere; returns how many lanes it used.</summary>
    private static int Generate(
        Draws draws, Dictionary<string, long[]> inputs, string suffix, int index, int budget, (float X, float Y, float Z) center, float radius, List<int> terminals)
    {
        bool terminal = budget < 3 || draws.Unit() < 0.35;

        inputs["kind" + suffix][index] = terminal ? IvpLedgeTreeReplay.Terminal : IvpLedgeTreeReplay.Inner;
        inputs["center" + suffix][index * 3] = IvpImpactReplay.Lane(center.X);
        inputs["center" + suffix][(index * 3) + 1] = IvpImpactReplay.Lane(center.Y);
        inputs["center" + suffix][(index * 3) + 2] = IvpImpactReplay.Lane(center.Z);
        inputs["radius" + suffix][index] = IvpImpactReplay.Lane(radius);
        inputs["box" + suffix][index] = draws.Box() | (draws.Box() << 8) | (draws.Box() << 16);

        if (terminal)
        {
            terminals.Add(index);
            return 1;
        }

        int leftBudget = 1 + (2 * (int)(draws.Unit() * ((budget - 1) / 2)));
        int left = Generate(draws, inputs, suffix, index + 1, leftBudget, draws.Child(center, radius), radius * (0.3f + (0.5f * (float)draws.Unit())), terminals);

        return 1 + left + Generate(
            draws, inputs, suffix, index + 1 + left, budget - 1 - left, draws.Child(center, radius), radius * (0.3f + (0.5f * (float)draws.Unit())), terminals);
    }

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public double Signed() => (Unit() * 2d) - 1d;

        public int Box() => 180 + (int)(Unit() * 76);

        public (float X, float Y, float Z) Child((float X, float Y, float Z) center, float radius) =>
            (center.X + (float)(Signed() * radius * 0.5d), center.Y + (float)(Signed() * radius * 0.5d), center.Z + (float)(Signed() * radius * 0.5d));

        public (double X, double Y, double Z, double W) Rotation()
        {
            (double x, double y, double z, double w) = (Signed(), Signed(), Signed(), Signed() + 1e-3);
            double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

            return (x / length, y / length, z / length, w / length);
        }
    }

    /// <summary>The environment, objects, caches, cores, surface managers, pair vector and delegator, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int PairCapacity = 0x100;

        private readonly RefreshCall _refresh;
        private readonly VphysicsDetour _exact;
        private readonly VphysicsDetour _phantom;
        private readonly List<Delegate> _callbacks = [];
        private readonly List<nint> _blocks = [];
        private readonly nint _environment;
        private readonly nint _pair;
        private readonly nint _pairElements;
        private readonly nint _delegator;
        private readonly nint[] _objects = new nint[2];
        private readonly nint[] _cores = new nint[2];
        private readonly nint[] _caches = new nint[2];
        private readonly nint[] _managers = new nint[2];
        private readonly nint[] _surfaces = new nint[2];
        private readonly Dictionary<int, int>[] _ledgeLanes = [[], []];
        private readonly List<int> _events = [];

        public Native(nint module)
        {
            _refresh = VphysicsLibrary.Function<RefreshCall>(module, RefreshAddress);
            nint table = VphysicsLibrary.Address(module, PolygonTable);
            _exact = new VphysicsDetour(module, ExactAddress, Keep(new TailCall((_, mindist) => _events.Add(Event(IvpPairMindistsReplay.Exact, mindist)))));
            _phantom = new VphysicsDetour(module, PhantomAddress, Keep(new TailCall((_, mindist) => _events.Add(Event(IvpPairMindistsReplay.Phantom, mindist)))));

            _environment = Block(0x200);

            nint manager = Block(0x10);

            Marshal.WriteIntPtr(manager, 8, _environment);
            Marshal.WriteIntPtr(_environment, 0x20, manager);

            _pair = Block(0x10);
            _pairElements = Block(PairCapacity * 8);
            Marshal.WriteInt16(_pair, 0, PairCapacity);
            Marshal.WriteIntPtr(_pair, 8, _pairElements);

            nint delegatorTable = Block(8 * 4);

            Marshal.WriteIntPtr(delegatorTable, 0, Marshal.GetFunctionPointerForDelegate(Keep(new RemovedCall(Removed))));
            _delegator = Block(0x10);
            Marshal.WriteIntPtr(_delegator, 0, delegatorTable);

            for (int side = 0; side < 2; side++)
            {
                _objects[side] = Block(0x100);
                _cores[side] = Block(0x300);
                _caches[side] = Block(0xd0);
                _managers[side] = Block(0x10);
                Marshal.WriteInt32(_objects[side], 0x8, 2);
                Marshal.WriteIntPtr(_objects[side], 0x30, _environment);
                Marshal.WriteIntPtr(_objects[side], 0x70, _caches[side]);
                Marshal.WriteInt32(_objects[side], 0x78, 8);
                Marshal.WriteIntPtr(_objects[side], 0xc8, _managers[side]);
                Marshal.WriteIntPtr(_objects[side], 0xe8, _cores[side]);
                Marshal.WriteIntPtr(_caches[side], 0xc8, _objects[side]);
                Marshal.WriteIntPtr(_managers[side], 0, table);
            }
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = IvpPairMindistsReplay.NewOutputs();
            int[][] ledgeOffsets = new int[2][];

            Marshal.WriteInt32(_environment, 0xb0, 0);
            Marshal.WriteInt32(_environment, 0xb4, 0);
            Marshal.WriteInt32(_environment, 0xb8, 0);

            for (int side = 0; side < 2; side++)
            {
                (byte[] bytes, _, int[] ledges) = IvpLedgeTreeReplay.Surface(inputs, IvpPairMindistsReplay.Suffix(side), IvpPairMindistsReplay.StubSize);

                ledgeOffsets[side] = ledges;
                _surfaces[side] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, _surfaces[side], bytes.Length);
                Marshal.WriteIntPtr(_managers[side], 8, _surfaces[side]);
                _ledgeLanes[side].Clear();

                for (int lane = 0; lane < IvpPairMindistsReplay.NodeCount; lane++)
                {
                    if (ledges[lane] >= 0)
                    {
                        _ledgeLanes[side][ledges[lane]] = lane;
                    }
                }

                Marshal.WriteInt32(_objects[side], 0xe0, unchecked((int)inputs["extra"][side]));
                Marshal.WriteInt32(_cores[side], 0x4, unchecked((int)inputs["core-radius"][side]));

                (double X, double Y, double Z, double W) rotation = (
                    BitConverter.Int64BitsToDouble(inputs["cache-rotation"][side * 4]), BitConverter.Int64BitsToDouble(inputs["cache-rotation"][(side * 4) + 1]),
                    BitConverter.Int64BitsToDouble(inputs["cache-rotation"][(side * 4) + 2]), BitConverter.Int64BitsToDouble(inputs["cache-rotation"][(side * 4) + 3]));
                (double X, double Y, double Z) translation = (
                    BitConverter.Int64BitsToDouble(inputs["cache-translation"][side * 3]), BitConverter.Int64BitsToDouble(inputs["cache-translation"][(side * 3) + 1]),
                    BitConverter.Int64BitsToDouble(inputs["cache-translation"][(side * 3) + 2]));
                IvpMatrix matrix = IvpMatrix.FromRotation(rotation, translation);
                double[] cells = [matrix.M0, matrix.M1, matrix.M2, 0d, matrix.M4, matrix.M5, matrix.M6, 0d, matrix.M8, matrix.M9, matrix.M10, 0d, translation.X, translation.Y, translation.Z];

                for (int cell = 0; cell < cells.Length; cell++)
                {
                    Marshal.WriteInt64(_caches[side], 0x40 + (cell * 8), BitConverter.DoubleToInt64Bits(cells[cell]));
                }
            }

            for (int step = 0; step < IvpPairMindistsReplay.StepCount; step++)
            {
                _events.Clear();
                Marshal.WriteInt64(_environment, 0x188, inputs["now"][step]);

                for (int side = 0; side < 2; side++)
                {
                    int at = (step * 2) + side;

                    Marshal.WriteInt64(_cores[side], 0x1d0, inputs["stepped"][at]);

                    for (int lane = 0; lane < 3; lane++)
                    {
                        Marshal.WriteInt64(_cores[side], 0x150 + (lane * 8), inputs["position"][(at * 3) + lane]);
                        Marshal.WriteInt32(_cores[side], 0x170 + (lane * 4), unchecked((int)inputs["velocity"][(at * 3) + lane]));
                    }
                }

                int givenFirst = (int)inputs["given-first"][step];
                int givenSecond = (int)inputs["given-second"][step];

                _refresh(
                    _objects[0], _objects[1], BitConverter.Int64BitsToDouble(inputs["gap"][step]), _pair,
                    givenFirst < 0 ? 0 : _surfaces[0] + ledgeOffsets[0][givenFirst], givenSecond < 0 ? 0 : _surfaces[1] + ledgeOffsets[1][givenSecond],
                    0, 0, _delegator);

                List<int> names = [];

                for (int index = 0; index < (ushort)Marshal.ReadInt16(_pair, 2); index++)
                {
                    names.Add(Event(0, Marshal.ReadIntPtr(_pairElements, index * 8)));
                }

                IvpPairMindistsReplay.Record(outputs, "pair", step, names);
                IvpPairMindistsReplay.Record(outputs, "event", step, _events);
                outputs["live"][step] = Marshal.ReadInt32(_environment, 0xb0);
                outputs["created"][step] = Marshal.ReadInt32(_environment, 0xb4);
                outputs["deleted"][step] = Marshal.ReadInt32(_environment, 0xb8);
            }

            for (int index = (ushort)Marshal.ReadInt16(_pair, 2) - 1; index >= 0; index--)
            {
                nint mindist = Marshal.ReadIntPtr(_pairElements, index * 8);

                Marshal.GetDelegateForFunctionPointer<DeletingDestructor>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(mindist), 0))(mindist, 1);
            }

            for (int side = 0; side < 2; side++)
            {
                Marshal.FreeHGlobal(_surfaces[side]);
            }

            return outputs;
        }

        public void Dispose()
        {
            _exact.Dispose();
            _phantom.Dispose();

            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }
        }

        /// <summary>A mindist's name from its two records' features: each feature's ledge, as <c>FUN_180097510</c> finds it.</summary>
        private int Event(int kind, nint mindist) =>
            IvpPairMindistsReplay.Name(kind, _ledgeLanes[0][LedgeOffset(mindist, 0x50, 0)], _ledgeLanes[1][LedgeOffset(mindist, 0x88, 1)]);

        private int LedgeOffset(nint mindist, int featureOffset, int side)
        {
            nint triangle = Marshal.ReadIntPtr(mindist, featureOffset) & ~(nint)0xf;
            int header = Marshal.ReadInt32(triangle) & 0xfff;

            return (int)(triangle - ((header + 1) * 16) - _surfaces[side]);
        }

        private void Removed(nint delegator, nint mindist)
        {
            _events.Add(Event(IvpPairMindistsReplay.Removed, mindist));

            int count = (ushort)Marshal.ReadInt16(_pair, 2);
            int first = Marshal.ReadInt32(mindist, 0x18);
            int place = first >= 0 && first < count && Marshal.ReadIntPtr(_pairElements, first * 8) == mindist ? first : Marshal.ReadInt32(mindist, 0x1c);

            count--;
            Marshal.WriteInt16(_pair, 2, (short)count);

            if (count > place)
            {
                nint moved = Marshal.ReadIntPtr(_pairElements, count * 8);

                Marshal.WriteIntPtr(_pairElements, place * 8, moved);
                Marshal.WriteInt32(moved, Marshal.ReadInt32(moved, 0x18) == count ? 0x18 : 0x1c, place);
            }

            Marshal.WriteInt32(mindist, Marshal.ReadInt32(mindist, 0x18) == place ? 0x18 : 0x1c, -1);
        }

        private T Keep<T>(T callback)
            where T : Delegate
        {
            _callbacks.Add(callback);
            return callback;
        }

        private nint Block(int size)
        {
            nint block = Marshal.AllocHGlobal(size);

            for (int offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(block, offset, 0);
            }

            _blocks.Add(block);
            return block;
        }
    }
}
