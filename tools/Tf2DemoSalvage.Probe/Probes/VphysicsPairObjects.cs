using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Two fabricated IVP objects with synthesized surfaces behind the shipped <c>vphysics.dll</c>'s polygon manager table, their mindist
/// constructor's tails detoured to recorders — what the pair mindist and pair watcher probes both hand the binary (B369, D172).
/// </summary>
/// <remarks>
/// **What is fabricated**: the environment's counters and time, its mindist manager, the objects (kind 2, resting, a surface manager on
/// table `1800eae60`, an object cache holding the case's matrix) and their cores. **What is detoured**: `FUN_1800977f0` and
/// `FUN_180097940`, the exact and phantom tails — each starts with twelve bytes of whole register saves — to recorders that name the new
/// mindist into <see cref="Events"/>. The lanes are <see cref="IvpPairMindistsReplay"/>'s.
/// </remarks>
internal sealed class VphysicsPairObjects : IDisposable
{
    private const long ExactAddress = 0x1800977f0;
    private const long PhantomAddress = 0x180097940;
    private const long PolygonTable = 0x1800eae60;

    private readonly VphysicsDetour _exact;
    private readonly VphysicsDetour _phantom;
    private readonly List<Delegate> _callbacks = [];
    private readonly List<nint> _blocks = [];
    private readonly nint[] _objects = new nint[2];
    private readonly nint[] _cores = new nint[2];
    private readonly nint[] _caches = new nint[2];
    private readonly nint[] _managers = new nint[2];
    private readonly nint[] _surfaces = new nint[2];
    private readonly int[][] _ledgeOffsets = [[], []];
    private readonly Dictionary<int, int>[] _ledgeLanes = [[], []];

    /// <summary>Builds the environment and both objects.</summary>
    /// <param name="module">The loaded <c>vphysics.dll</c>.</param>
    public VphysicsPairObjects(nint module)
    {
        nint table = VphysicsLibrary.Address(module, PolygonTable);

        _exact = new VphysicsDetour(module, ExactAddress, Keep(new TailCall((_, mindist) => Events.Add(Event(IvpPairMindistsReplay.Exact, mindist)))));
        _phantom = new VphysicsDetour(module, PhantomAddress, Keep(new TailCall((_, mindist) => Events.Add(Event(IvpPairMindistsReplay.Phantom, mindist)))));
        Environment = Block(0x200);

        nint manager = Block(0x10);

        Marshal.WriteIntPtr(manager, 8, Environment);
        Marshal.WriteIntPtr(Environment, 0x20, manager);

        for (int side = 0; side < 2; side++)
        {
            _objects[side] = Block(0x100);
            _cores[side] = Block(0x300);
            _caches[side] = Block(0xd0);
            _managers[side] = Block(0x10);
            Marshal.WriteInt32(_objects[side], 0x8, 2);
            Marshal.WriteIntPtr(_objects[side], 0x30, Environment);
            Marshal.WriteIntPtr(_objects[side], 0x70, _caches[side]);
            Marshal.WriteInt32(_objects[side], 0x78, 8);
            Marshal.WriteIntPtr(_objects[side], 0xc8, _managers[side]);
            Marshal.WriteIntPtr(_objects[side], 0xe8, _cores[side]);
            Marshal.WriteIntPtr(_caches[side], 0xc8, _objects[side]);
            Marshal.WriteIntPtr(_managers[side], 0, table);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TailCall(nint manager, nint mindist);

    /// <summary>The environment: <c>+0x20</c> its mindist manager, <c>+0xb0..0xb8</c> the mindist counters, <c>+0x188</c> the time.</summary>
    public nint Environment { get; }

    /// <summary>What the detoured tails recorded, and what a probe adds beside them.</summary>
    public List<int> Events { get; } = [];

    /// <summary>A fresh input dictionary, every field zeroed.</summary>
    /// <param name="fields">The replay's input fields.</param>
    /// <returns>The inputs.</returns>
    public static Dictionary<string, long[]> NewInputs(IReadOnlyList<IvpReplayField> fields)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in fields)
        {
            inputs[field.Name] = new long[field.Count];
        }

        return inputs;
    }

    /// <summary>A random case's two objects: each tree, extra radius, core radius and cache placement.</summary>
    /// <param name="draws">The draws.</param>
    /// <param name="inputs">The case's inputs, every field allocated.</param>
    /// <returns>Each object's terminal node lanes.</returns>
    public static List<int>[] RandomObjects(VphysicsPairDraws draws, Dictionary<string, long[]> inputs)
    {
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

        return terminals;
    }

    /// <summary>A random step's cores, each near the OTHER object's cache translation so its sphere reaches the other's surface.</summary>
    /// <param name="draws">The draws.</param>
    /// <param name="inputs">The case's inputs.</param>
    /// <param name="step">The step.</param>
    /// <param name="now">The step's time.</param>
    public static void RandomPlacement(VphysicsPairDraws draws, Dictionary<string, long[]> inputs, int step, double now)
    {
        for (int side = 0; side < 2; side++)
        {
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
    }

    /// <summary>An object, <c>+0x30</c> the environment, <c>+0x78</c> resting, <c>+0xc8</c> its surface manager, <c>+0xe8</c> its core.</summary>
    /// <param name="side">0 or 1.</param>
    /// <returns>The object's address.</returns>
    public nint Object(int side) => _objects[side];

    /// <summary>An object's core.</summary>
    /// <param name="side">0 or 1.</param>
    /// <returns>The core's address.</returns>
    public nint Core(int side) => _cores[side];

    /// <summary>A ledge of a loaded case's surface, by its node lane.</summary>
    /// <param name="side">0 or 1.</param>
    /// <param name="lane">The node lane.</param>
    /// <returns>The ledge's address.</returns>
    public nint Ledge(int side, int lane) => _surfaces[side] + _ledgeOffsets[side][lane];

    /// <summary>Lays a case's surfaces, extra radii, core radii and cache matrices into the objects, and zeroes the mindist counters.</summary>
    /// <param name="inputs">The case's inputs.</param>
    public void Load(IReadOnlyDictionary<string, long[]> inputs)
    {
        Marshal.WriteInt32(Environment, 0xb0, 0);
        Marshal.WriteInt32(Environment, 0xb4, 0);
        Marshal.WriteInt32(Environment, 0xb8, 0);

        for (int side = 0; side < 2; side++)
        {
            (byte[] bytes, _, int[] ledges) = IvpLedgeTreeReplay.Surface(inputs, IvpPairMindistsReplay.Suffix(side), IvpPairMindistsReplay.StubSize);

            _ledgeOffsets[side] = ledges;
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
    }

    /// <summary>A step's time into the environment, and each core's stepped time, position and velocity.</summary>
    /// <param name="inputs">The case's inputs.</param>
    /// <param name="step">The step.</param>
    public void Place(IReadOnlyDictionary<string, long[]> inputs, int step)
    {
        Marshal.WriteInt64(Environment, 0x188, inputs["now"][step]);

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
    }

    /// <summary>A mindist's name from its two records' features: each feature's ledge, as <c>FUN_180097510</c> finds it.</summary>
    /// <param name="kind">The event's kind, or zero for a name.</param>
    /// <param name="mindist">The mindist.</param>
    /// <returns>The lane.</returns>
    public int Event(int kind, nint mindist) =>
        IvpPairMindistsReplay.Name(kind, _ledgeLanes[0][LedgeOffset(mindist, 0x50, 0)], _ledgeLanes[1][LedgeOffset(mindist, 0x88, 1)]);

    /// <summary>Frees a loaded case's surfaces.</summary>
    public void Unload()
    {
        for (int side = 0; side < 2; side++)
        {
            Marshal.FreeHGlobal(_surfaces[side]);
            _surfaces[side] = 0;
        }
    }

    /// <summary>Keeps a callback alive for as long as the objects are.</summary>
    /// <typeparam name="T">The delegate type.</typeparam>
    /// <param name="callback">The callback.</param>
    /// <returns>The callback.</returns>
    public T Keep<T>(T callback)
        where T : Delegate
    {
        _callbacks.Add(callback);
        return callback;
    }

    /// <summary>A zeroed block freed with the objects.</summary>
    /// <param name="size">Its size in bytes.</param>
    /// <returns>Its address.</returns>
    public nint Block(int size)
    {
        nint block = Marshal.AllocHGlobal(size);

        for (int offset = 0; offset < size; offset++)
        {
            Marshal.WriteByte(block, offset, 0);
        }

        _blocks.Add(block);
        return block;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _exact.Dispose();
        _phantom.Dispose();

        foreach (nint block in _blocks)
        {
            Marshal.FreeHGlobal(block);
        }
    }

    /// <summary>A random subtree of inner and terminal nodes, children inside their parent's sphere; returns how many lanes it used.</summary>
    private static int Generate(
        VphysicsPairDraws draws, Dictionary<string, long[]> inputs, string suffix, int index, int budget, (float X, float Y, float Z) center, float radius, List<int> terminals)
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

    private int LedgeOffset(nint mindist, int featureOffset, int side)
    {
        nint triangle = Marshal.ReadIntPtr(mindist, featureOffset) & ~(nint)0xf;
        int header = Marshal.ReadInt32(triangle) & 0xfff;

        return (int)(triangle - ((header + 1) * 16) - _surfaces[side]);
    }
}

/// <summary>The pair probes' random draws, split-mix seeded.</summary>
/// <param name="seed">The seed.</param>
internal sealed class VphysicsPairDraws(ulong seed)
{
    private ulong _state = seed;

    /// <summary>A draw in [0, 1).</summary>
    /// <returns>The draw.</returns>
    public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

    /// <summary>A draw in [−1, 1).</summary>
    /// <returns>The draw.</returns>
    public double Signed() => (Unit() * 2d) - 1d;

    /// <summary>A box byte that prunes few walks.</summary>
    /// <returns>The byte.</returns>
    public int Box() => 180 + (int)(Unit() * 76);

    /// <summary>A child's centre inside half its parent's radius.</summary>
    /// <param name="center">The parent's centre.</param>
    /// <param name="radius">The parent's radius.</param>
    /// <returns>The centre.</returns>
    public (float X, float Y, float Z) Child((float X, float Y, float Z) center, float radius) =>
        (center.X + (float)(Signed() * radius * 0.5d), center.Y + (float)(Signed() * radius * 0.5d), center.Z + (float)(Signed() * radius * 0.5d));

    /// <summary>A unit quaternion.</summary>
    /// <returns>The quaternion.</returns>
    public (double X, double Y, double Z, double W) Rotation()
    {
        (double x, double y, double z, double w) = (Signed(), Signed(), Signed(), Signed() + 1e-3);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        return (x / length, y / length, z / length, w / length);
    }
}
