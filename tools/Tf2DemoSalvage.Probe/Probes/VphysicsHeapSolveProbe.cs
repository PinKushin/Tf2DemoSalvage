using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s many-contact heap solve — <c>FUN_1800a9bf0</c>, which sorts a friction system's contacts and
/// solves them with <c>FUN_1800a9520</c>, <c>FUN_1800aa5c0</c> and everything under it — called in process on a fabricated system,
/// the oracle for <c>IvpFrictionSystem</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** Cores, objects, contact points, records, each movable core's share, the pairs, the system, the
/// environment with its limits, arena and anomaly manager, and the PSI event are written at the offsets `docs/findings/51` reads.
/// The tolerance block is settled by the binary's own `FUN_180098fd0`, run as the port's `IvpCollisionTolerance` runs it. The
/// anomaly manager's slot 5 is a managed callback giving the case's answer.
///
/// **The filing pass is kept out of reach**: every gap is below `block[0x47]`, no record is outside, and every object's friction
/// core is a zeroed core with bit 0 clear — so `FUN_1800a9bf0` neither drops nor moves a contact, which `IvpFrictionSystem` does
/// not carry yet.
///
/// **The control first**: a lone contact whose core closes on an immovable one at five metres a second must be pushed — by
/// `FUN_180084490`, which leaves the streak alone.
///
/// **Modes.** With no mode, or `sweep n`, random systems — a quarter of them seeded with NaNs of both signs, signalling NaNs and
/// infinities — are compared lane by lane; `fixture path` writes the cases `IvpHeapSolveConformanceTests` reads.
/// </remarks>
public sealed class VphysicsHeapSolveProbe : IProbe
{
    private const long PriorityZeroAddress = 0x180084320;
    private const long SettleAddress = 0x180098fd0;
    private const long BlockAddress = 0x18012d540;

    private const int DefaultSweep = 20_000;
    private const int FixtureCases = 200;
    private const ulong FixtureSeed = 18010;
    private const ulong SweepSeed = 20260915;

    private static readonly long[] FloatPoisons =
    [
        0x7fc00000, 0xffc00000, 0x7fc00123, 0x7f800001, 0xffa00000, 0x7f800000, 0xff800000,
    ];

    private static readonly long[] DoublePoisons =
    [
        0x7ff8000000000000, unchecked((long)0xfff8000000000000UL), 0x7ff8000000000123, unchecked((long)0xfff0000000000000UL),
    ];

    /// <summary>Streaks worth starting from: none, short runs either way, the pull limit, and a push run whose low byte is zero.</summary>
    private static readonly int[] Streaks = [0, 0, 0, -1, -1, -2, 1, 2, 8, 9, 10, -256, -300];

    /// <summary>
    /// Sweep cases, by index in the <see cref="SweepSeed"/> stream, that a port broken on one rule got wrong where the fixture's
    /// random cases did not — found by sweeping the binary against each sabotage, so the fixture regenerates them.
    /// </summary>
    /// <remarks>
    /// `67` and `179` pull a contact (the push `MAXSD`'d to zero) and change the active rows (a streak copy tested `!= 0`, not
    /// `&gt; 0`); `326` the active rows alone; `5768` and `8571` take a pull streak of 8 to 9, which `&gt;= 9` would reset.
    /// </remarks>
    private static readonly int[] Killers = [67, 179, 326, 5768, 8571];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SolveFunction(nint controller, nint simulationEvent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SettleFunction(nint block, double tolerance, double gravity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ContactsFunction(nint manager, nint cores, int count);

    /// <inheritdoc />
    public string Name => "vphysics-heap-solve";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's many-contact heap solve (FUN_1800a9bf0: the sort, FUN_1800a9520's system, FUN_1800aa5c0's solve and the " +
        "freeze past 150 contacts) called in process on fabricated friction systems and compared with the port; 'fixture' writes " +
        "the conformance suite's cases: vphysics-heap-solve [sweep n | fixture path]";

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
        output.WriteLine(
            $"block[0x43]: the binary 0x{(uint)BitConverter.SingleToInt32Bits(native.ContactGap):x8}, " +
            $"the port 0x{(uint)BitConverter.SingleToInt32Bits(IvpCollisionTolerance.ContactGapInMetres):x8}");

        ulong state = SweepSeed;
        Dictionary<string, long[]> inputs = RandomCase(ref state, native, poison: false);

        inputs["cores"][0] = 2;
        inputs["contacts"][0] = 1;
        inputs["core-flags"][0] = 0;
        inputs["core-flags"][1] = 2;
        inputs["contact-cores"][0] = 0;
        inputs["contact-cores"][1] = 1;
        inputs["contact-streak"][0] = 0;
        inputs["contact-gap"][0] = IvpImpactReplay.Lane(native.ContactGap);
        Floats(inputs["record-normal"], 0, (0d, 1d, 0d));
        Floats(inputs["core-velocity"], 0, (0d, 5d, 0d));
        inputs["max-velocity"][0] = IvpImpactReplay.Lane(0f);
        inputs["max-spin"][0] = IvpImpactReplay.Lane(0f);

        Dictionary<string, long[]> binary = native.Run(inputs);
        float push = BitConverter.Int32BitsToSingle(unchecked((int)binary["normal-push"][0]));
        bool pushed = push > 0f && binary["streak"][0] == 0;
        IReadOnlyList<string> differences = IvpHeapSolveReplay.Differences(binary, IvpHeapSolveReplay.Run(inputs));

        output.WriteLine(
            $"control: the binary {(pushed ? "pushed" : "DID NOT push")} the closing contact ({push}); the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return pushed;
    }

    private static void Sweep(TextWriter output, Native native, int count)
    {
        ulong state = SweepSeed;
        int differing = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, native, poison: true);
            Dictionary<string, long[]> binary = native.Run(inputs);
            IReadOnlyList<string> differences = IvpHeapSolveReplay.Differences(binary, IvpHeapSolveReplay.Run(inputs));

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

        output.WriteLine($"{count} heap-solve cases, {differing} differing");
    }

    private static void Fixture(TextWriter output, Native native, string path)
    {
        ulong state = FixtureSeed;

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-heap-solve probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, native, poison: true);

            IvpHeapSolveReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        foreach (int freezes in new[] { 0, 1 })
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, native, poison: false);

            inputs["contacts"][0] = IvpHeapSolveReplay.MostContacts;
            inputs["copies"][0] = IvpHeapSolveReplay.MostCopies - 1;
            inputs["freezes"][0] = freezes;
            FillContacts(ref state, native, inputs, IvpHeapSolveReplay.MostContacts, IvpImpactReplay.Whole32(inputs, "cores", 0));
            IvpHeapSolveReplay.Write(writer, new IvpReplayCase($"crowded-{freezes}", inputs, native.Run(inputs)));
        }

        Dictionary<string, long[]> mixed = Mixed(ref state, native);

        IvpHeapSolveReplay.Write(writer, new IvpReplayCase("crowded-mixed", mixed, native.Run(mixed)));

        HashSet<int> wanted = [.. Killers];
        ulong sweep = SweepSeed;
        int last = Killers.Length == 0 ? -1 : Killers.Max();

        for (int index = 0; index <= last; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref sweep, native, poison: true);

            if (wanted.Contains(index))
            {
                IvpHeapSolveReplay.Write(writer, new IvpReplayCase($"killer-{index}", inputs, native.Run(inputs)));
            }
        }

        output.WriteLine($"{FixtureCases} random, 3 crowded and {Killers.Length} killer cases written to {path}");
    }

    /// <summary>
    /// A frozen heap whose first movable core is in one pair with the other movable core and one with an immovable core named
    /// second — so only the pair test's check of BOTH cores keeps that core's velocity.
    /// </summary>
    private static Dictionary<string, long[]> Mixed(ref ulong state, Native native)
    {
        Dictionary<string, long[]> inputs = RandomCase(ref state, native, poison: false);

        inputs["cores"][0] = 3;
        inputs["core-flags"][0] = 0;
        inputs["core-flags"][1] = 0;
        inputs["core-flags"][2] = 2;
        inputs["contacts"][0] = IvpHeapSolveReplay.MostContacts;
        inputs["copies"][0] = IvpHeapSolveReplay.MostCopies - 1;
        inputs["freezes"][0] = 1;
        FillContacts(ref state, native, inputs, IvpHeapSolveReplay.MostContacts, 3);

        for (int c = 0; c < IvpHeapSolveReplay.MostContacts; c++)
        {
            inputs["contact-cores"][2 * c] = 0;
            inputs["contact-cores"][(2 * c) + 1] = 1 + (c % 2);
        }

        Floats(inputs["core-velocity"], 0, (1d, 2d, 3d));
        Floats(inputs["core-spin"], 0, (4d, 5d, 6d));

        return inputs;
    }

    private static Dictionary<string, long[]> RandomCase(ref ulong state, Native native, bool poison)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpHeapSolveReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        int cores = 2 + Below(ref state, IvpHeapSolveReplay.MostCores - 1);
        int contacts = 1 + Below(ref state, IvpHeapSolveReplay.MostContacts);
        double inverseStep = Chance(ref state, 0.6) ? 66d : 10d + (Unit(ref state) * 190d);

        inputs["inverse-step"][0] = IvpImpactReplay.Lane(inverseStep);
        inputs["max-velocity"][0] = IvpImpactReplay.Lane(Limit(ref state, 100f));
        inputs["max-spin"][0] = IvpImpactReplay.Lane(Limit(ref state, 0.5f));
        inputs["min-friction-mass"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.8) ? 10f : (float)(Unit(ref state) * 100d));
        inputs["gravity"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.7) ? 9.8044f : (float)(Signed(ref state) * 20d));
        inputs["event"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.8) ? (float)inverseStep : (float)(Unit(ref state) * 100d));
        inputs["freezes"][0] = Below(ref state, 2);
        inputs["cores"][0] = cores;
        inputs["contacts"][0] = contacts;
        inputs["copies"][0] = 1;

        for (int k = 0; k < cores; k++)
        {
            float mass = 0.1f + ((float)Unit(ref state) * 100f);

            inputs["core-flags"][k] = (Chance(ref state, 0.3) ? 2 : 0) | (Chance(ref state, 0.3) ? 1 : 0);
            inputs["core-mass"][k] = IvpImpactReplay.Lane(mass);
            inputs["core-inverse-mass"][k] = IvpImpactReplay.Lane(Chance(ref state, 0.8) ? 1f / mass : (float)Unit(ref state));

            for (int lane = 0; lane < 3; lane++)
            {
                float inertia = 0.001f + ((float)Unit(ref state) * 10f);

                inputs["core-inertia"][(3 * k) + lane] = IvpImpactReplay.Lane(inertia);
                inputs["core-inverse-inertia"][(3 * k) + lane] = IvpImpactReplay.Lane(1f / inertia);
            }

            foreach (string vector in IvpHeapSolveReplay.VectorNames)
            {
                if (!vector.StartsWith("pending", StringComparison.Ordinal) || !Chance(ref state, 0.3))
                {
                    FillFloats(ref state, inputs["core-" + vector], 3 * k, 3);
                }
            }
        }

        FillContacts(ref state, native, inputs, contacts, cores);

        if (poison && Chance(ref state, 0.25))
        {
            Poison(ref state, inputs);
        }

        return inputs;
    }

    private static void FillContacts(ref ulong state, Native native, Dictionary<string, long[]> inputs, int contacts, int cores)
    {
        float room = native.DropGap - native.ContactGap;

        for (int c = 0; c < contacts; c++)
        {
            int first = Below(ref state, cores);
            float gap = native.ContactGap + (float)(Signed(ref state) * 0.05d);

            if (!(gap < native.DropGap))
            {
                gap = native.ContactGap + ((float)Unit(ref state) * room * 0.99f);
            }

            inputs["contact-cores"][2 * c] = first;
            inputs["contact-cores"][(2 * c) + 1] = (first + 1 + Below(ref state, cores - 1)) % cores;
            inputs["contact-gap"][c] = IvpImpactReplay.Lane(gap);
            inputs["contact-streak"][c] = Streaks[Below(ref state, Streaks.Length)];
            Floats(inputs["record-normal"], 3 * c, Direction(ref state));
            FillFloats(ref state, inputs["record-first-turn"], 3 * c, 3);
            FillFloats(ref state, inputs["record-second-turn"], 3 * c, 3);
            float inverseMass = 0.01f + ((float)Unit(ref state) * 10f);

            inputs["record-inverse-mass"][c] = IvpImpactReplay.Lane(inverseMass);
            inputs["record-virtual-mass"][c] = IvpImpactReplay.Lane(1f / inverseMass);
        }
    }

    /// <summary>A few float lanes poisoned — never a gap made infinite, which the filing pass would drop.</summary>
    private static void Poison(ref ulong state, Dictionary<string, long[]> inputs)
    {
        List<(string Name, int Lane)> floats = [];

        foreach (IvpReplayField field in IvpHeapSolveReplay.Inputs)
        {
            for (int lane = 0; field.Kind == IvpReplayKind.Real32 && field.Name != "record-virtual-mass" && lane < field.Count; lane++)
            {
                floats.Add((field.Name, lane));
            }
        }

        int poisons = 1 + Below(ref state, 4);

        for (int poison = 0; poison < poisons; poison++)
        {
            (string name, int lane) = floats[Below(ref state, floats.Count)];
            long bits = FloatPoisons[Below(ref state, FloatPoisons.Length)];

            inputs[name][lane] = name == "contact-gap" ? GapPoison(bits, IvpImpactReplay.Whole32(inputs, "contacts", 0) == 1) : bits;
        }

        if (Chance(ref state, 0.1))
        {
            inputs["inverse-step"][0] = DoublePoisons[Below(ref state, DoublePoisons.Length)];
        }

        for (int c = 0; c < IvpHeapSolveReplay.MostContacts; c++)
        {
            inputs["record-virtual-mass"][c] = IvpImpactReplay.Lane(1f / IvpImpactReplay.Real32(inputs, "record-inverse-mass", c));
        }
    }

    /// <summary>
    /// A poison fit for a gap: never infinite, which a heap's filing pass drops, and for a lone contact never a NaN either, which
    /// <c>FUN_180084490</c>'s <c>!(block[0x47] &gt; gap)</c> drops — the drop is not carried by the port yet.
    /// </summary>
    private static long GapPoison(long bits, bool lone)
    {
        long finite = bits == 0x7f800000 ? 0x7fc00000 : bits;

        return lone && float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)finite)))
            ? IvpImpactReplay.Lane(float.NegativeInfinity)
            : finite;
    }

    private static float Limit(ref ulong state, float typical)
    {
        int shape = Below(ref state, 10);

        if (shape == 0)
        {
            return 0f;
        }

        if (shape == 1)
        {
            return -typical;
        }

        return typical * (float)(0.01 + (Unit(ref state) * 2d));
    }

    private static void FillFloats(ref ulong state, long[] lanes, int start, int count)
    {
        double magnitude = Magnitude(ref state);

        for (int lane = start; lane < start + count; lane++)
        {
            lanes[lane] = IvpImpactReplay.Lane((float)(Signed(ref state) * magnitude));
        }
    }

    private static void Floats(long[] lanes, int start, (double X, double Y, double Z) vector)
    {
        lanes[start] = IvpImpactReplay.Lane((float)vector.X);
        lanes[start + 1] = IvpImpactReplay.Lane((float)vector.Y);
        lanes[start + 2] = IvpImpactReplay.Lane((float)vector.Z);
    }

    private static (double X, double Y, double Z) Direction(ref ulong state)
    {
        double x = Signed(ref state);
        double y = Signed(ref state);
        double z = Signed(ref state);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z));

        return length > 1e-6 ? (x / length, y / length, z / length) : (0d, 0d, 1d);
    }

    private static double Magnitude(ref ulong state) => Math.Pow(10d, Below(ref state, 7) - 3);

    private static int Below(ref ulong state, int bound) => (int)(VphysicsLibrary.SplitMix(ref state) % (ulong)bound);

    private static bool Chance(ref ulong state, double probability) => Unit(ref state) < probability;

    private static double Unit(ref ulong state) => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state));

    private static double Signed(ref ulong state) => (Unit(ref state) * 2d) - 1d;

    /// <summary>A friction system and everything it reaches, in unmanaged memory at the engine's offsets.</summary>
    private sealed class Native : IDisposable
    {
        private const int CoreSize = 0x270;
        private const int ObjectSize = 0x100;
        private const int InfoSize = 0x18;
        private const int ContactSize = 0xd0;
        private const int RecordSize = 0x110;
        private const int PairSize = 0x50;
        private const int SystemSize = 0x90;
        private const int EnvironmentSize = 0x200;
        private const int LimitsSize = 0x40;
        private const int ArenaSize = 0x28;
        private const int ArenaBytes = 8 << 20;
        private const int EventSize = 0x18;
        private const int UnitSize = 0x48;
        private const int VtableSlots = 8;
        private const int Listed = IvpHeapSolveReplay.MostContacts * IvpHeapSolveReplay.MostCopies;
        private const int Cores = IvpHeapSolveReplay.MostCores;
        private const int MostPairs = Cores * (Cores - 1) / 2;

        private static readonly byte[] Zeroes = new byte[EnvironmentSize * 4];

        private readonly List<nint> _blocks = [];
        private readonly SolveFunction _solve;
        private readonly ContactsFunction _contactsExceeded;
        private readonly nint _block;
        private readonly nint[] _cores = new nint[Cores];
        private readonly nint[] _objects = new nint[Cores];
        private readonly nint[] _infos = new nint[Cores];
        private readonly nint[] _infoElements = new nint[Cores];
        private readonly nint[] _contacts = new nint[Listed];
        private readonly nint[] _records = new nint[Listed];
        private readonly nint[] _pairs = new nint[MostPairs];
        private readonly nint _frictionCore;
        private readonly nint _system;
        private readonly nint _coreList;
        private readonly nint _movableList;
        private readonly nint _pairList;
        private readonly nint _environment;
        private readonly nint _limits;
        private readonly nint _arena;
        private readonly nint _arenaBuffer;
        private readonly nint _manager;
        private readonly nint _event;
        private readonly nint _unit;
        private int _freezes;

        public Native(nint module)
        {
            _solve = VphysicsLibrary.Function<SolveFunction>(module, PriorityZeroAddress);
            _contactsExceeded = ContactsExceeded;

            SettleFunction settle = VphysicsLibrary.Function<SettleFunction>(module, SettleAddress);

            _block = VphysicsLibrary.Address(module, BlockAddress);
            settle(_block, IvpCollisionTolerance.Metres, 0d);
            settle(_block, BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_block, 0x4)), 0d);

            for (int k = 0; k < Cores; k++)
            {
                _cores[k] = Allocate(CoreSize);
                _objects[k] = Allocate(ObjectSize);
                _infos[k] = Allocate(InfoSize);
                _infoElements[k] = Allocate(Listed * 8);
            }

            for (int place = 0; place < Listed; place++)
            {
                _contacts[place] = Allocate(ContactSize);
                _records[place] = Allocate(RecordSize);
            }

            for (int pair = 0; pair < MostPairs; pair++)
            {
                _pairs[pair] = Allocate(PairSize);
            }

            _frictionCore = Allocate(CoreSize);
            _system = Allocate(SystemSize);
            _coreList = Allocate(Cores * 8);
            _movableList = Allocate(Cores * 8);
            _pairList = Allocate(MostPairs * 8);
            _environment = Allocate(EnvironmentSize);
            _limits = Allocate(LimitsSize);
            _arena = Allocate(ArenaSize);
            _arenaBuffer = Marshal.AllocHGlobal(ArenaBytes);
            _blocks.Add(_arenaBuffer);
            _manager = Allocate(0x20);
            _event = Allocate(EventSize);
            _unit = Allocate(UnitSize);

            nint vtable = Allocate(VtableSlots * 8);

            Marshal.WriteIntPtr(vtable, 5 * 8, Marshal.GetFunctionPointerForDelegate(_contactsExceeded));
            Marshal.WriteIntPtr(_manager, 0, vtable);
        }

        /// <summary><c>block[0x43]</c> as the binary settled it.</summary>
        public float ContactGap => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_block, 0x10c));

        /// <summary><c>block[0x47]</c> as the binary settled it: the gap the filing pass drops a contact at.</summary>
        public float DropGap => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_block, 0x11c));

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            int coreCount = IvpImpactReplay.Whole32(inputs, "cores", 0);
            int contactCount = IvpImpactReplay.Whole32(inputs, "contacts", 0);
            int listed = contactCount * IvpImpactReplay.Whole32(inputs, "copies", 0);

            _freezes = IvpImpactReplay.Whole32(inputs, "freezes", 0) != 0 ? 1 : 0;
            WriteEnvironment(inputs);

            int movable = 0;

            for (int k = 0; k < coreCount; k++)
            {
                WriteCore(inputs, k);
                Marshal.WriteIntPtr(_coreList, k * 8, _cores[k]);

                if ((Marshal.ReadByte(_cores[k]) & 2) == 0)
                {
                    Marshal.WriteIntPtr(_movableList, movable++ * 8, _cores[k]);
                }
            }

            Dictionary<nint, int> places = [];
            int[] shares = new int[Cores];
            List<(int First, int Second)> pairs = [];

            for (int place = 0; place < listed; place++)
            {
                int k = place % contactCount;
                int first = IvpImpactReplay.Whole32(inputs, "contact-cores", 2 * k);
                int second = IvpImpactReplay.Whole32(inputs, "contact-cores", (2 * k) + 1);

                WriteContact(inputs, place, listed, k, first, second);
                places[_contacts[place]] = place;

                foreach (int side in new[] { first, second })
                {
                    if ((Marshal.ReadByte(_cores[side]) & 2) == 0)
                    {
                        Marshal.WriteIntPtr(_infoElements[side], shares[side]++ * 8, _contacts[place]);
                    }
                }

                if (!pairs.Exists(pair => (pair.First == first && pair.Second == second) || (pair.First == second && pair.Second == first)))
                {
                    Zero(_pairs[pairs.Count], PairSize);
                    Marshal.WriteIntPtr(_pairs[pairs.Count], 0x38, _cores[first]);
                    Marshal.WriteIntPtr(_pairs[pairs.Count], 0x40, _cores[second]);
                    Marshal.WriteIntPtr(_pairList, pairs.Count * 8, _pairs[pairs.Count]);
                    pairs.Add((first, second));
                }
            }

            for (int k = 0; k < coreCount; k++)
            {
                Marshal.WriteInt16(_infos[k], 0, (short)Listed);
                Marshal.WriteInt16(_infos[k], 2, (short)shares[k]);
            }

            Zero(_system, SystemSize);
            Marshal.WriteIntPtr(_system, 0x8, _environment);
            Marshal.WriteIntPtr(_system, 0x40, listed > 0 ? _contacts[0] : 0);
            Marshal.WriteInt16(_system, 0x48, (short)Cores);
            Marshal.WriteInt16(_system, 0x4a, (short)coreCount);
            Marshal.WriteIntPtr(_system, 0x50, _coreList);
            Marshal.WriteInt16(_system, 0x58, (short)Cores);
            Marshal.WriteInt16(_system, 0x5a, (short)movable);
            Marshal.WriteIntPtr(_system, 0x60, _movableList);
            Marshal.WriteInt16(_system, 0x68, (short)MostPairs);
            Marshal.WriteInt16(_system, 0x6a, (short)pairs.Count);
            Marshal.WriteIntPtr(_system, 0x70, _pairList);
            Marshal.WriteInt16(_system, 0x78, (short)coreCount);
            Marshal.WriteInt16(_system, 0x7a, (short)listed);

            Marshal.WriteIntPtr(_system, 0x18, _system);
            _solve(_system + 0x10, _event);

            return Read(places, listed, coreCount);
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();

            // The binary holds a raw pointer to the callback, so its delegate must outlive every call made through the table.
            GC.KeepAlive(_contactsExceeded);
        }

        private int ContactsExceeded(nint manager, nint cores, int count) => _freezes;

        private void WriteEnvironment(Dictionary<string, long[]> inputs)
        {
            Zero(_environment, EnvironmentSize);
            Zero(_limits, LimitsSize);
            Zero(_arena, ArenaSize);
            Zero(_event, EventSize);
            Zero(_unit, UnitSize);
            Marshal.WriteIntPtr(_event, 0x10, _unit);

            Marshal.WriteIntPtr(_environment, 0x40, _manager);
            Marshal.WriteIntPtr(_environment, 0x48, _limits);
            Marshal.WriteIntPtr(_environment, 0xf0, _arena);
            Marshal.WriteInt64(_environment, 0x110, inputs["inverse-step"][0]);
            Marshal.WriteInt32(_environment, 0x138, unchecked((int)inputs["gravity"][0]));
            Marshal.WriteInt32(_limits, 0xc, unchecked((int)inputs["max-velocity"][0]));
            Marshal.WriteInt32(_limits, 0x14, unchecked((int)inputs["max-spin"][0]));
            Marshal.WriteInt32(_limits, 0x1c, unchecked((int)inputs["min-friction-mass"][0]));

            Marshal.WriteIntPtr(_arena, 0x0, _arenaBuffer);
            Marshal.WriteIntPtr(_arena, 0x8, _arenaBuffer);
            Marshal.WriteIntPtr(_arena, 0x10, (_arenaBuffer + 0x27) & ~(nint)0x1f);
            Marshal.WriteIntPtr(_arena, 0x18, _arenaBuffer + ArenaBytes);
            Marshal.WriteInt32(_arena, 0x24, ArenaBytes - 0x40);

            Marshal.WriteInt32(_event, 0x4, unchecked((int)inputs["event"][0]));
            Zero(_frictionCore, CoreSize);
        }

        private void WriteCore(Dictionary<string, long[]> inputs, int k)
        {
            nint core = _cores[k];
            int flags = IvpImpactReplay.Whole32(inputs, "core-flags", k);

            Zero(core, CoreSize);
            Zero(_objects[k], ObjectSize);
            Zero(_infos[k], InfoSize);

            Marshal.WriteByte(core, 0, (byte)flags);
            Marshal.WriteIntPtr(core, 0x10, _environment);
            WriteLanes(core, 0x20, inputs["core-inertia"], 3 * k, 3);
            WriteLanes(core, 0x2c, inputs["core-mass"], k, 1);
            WriteLanes(core, 0x40, inputs["core-inverse-inertia"], 3 * k, 3);
            WriteLanes(core, 0x4c, inputs["core-inverse-mass"], k, 1);
            WriteLanes(core, 0x110, inputs["core-pending-spin"], 3 * k, 3);
            WriteLanes(core, 0x120, inputs["core-pending-velocity"], 3 * k, 3);
            WriteLanes(core, 0x130, inputs["core-spin"], 3 * k, 3);
            WriteLanes(core, 0x140, inputs["core-velocity"], 3 * k, 3);

            if ((flags & 2) == 0)
            {
                Marshal.WriteIntPtr(core, 0x60, _infos[k]);
                Marshal.WriteIntPtr(_infos[k], 0x8, _infoElements[k]);
                Marshal.WriteIntPtr(_infos[k], 0x10, _system);
            }

            Marshal.WriteIntPtr(_objects[k], 0xe8, core);
            Marshal.WriteIntPtr(_objects[k], 0xf0, _frictionCore);
        }

        private void WriteContact(Dictionary<string, long[]> inputs, int place, int listed, int k, int first, int second)
        {
            nint contact = _contacts[place];
            nint record = _records[place];

            Zero(contact, ContactSize);
            Zero(record, RecordSize);

            Marshal.WriteIntPtr(contact, 0x0, place + 1 < listed ? _contacts[place + 1] : 0);
            Marshal.WriteIntPtr(contact, 0x8, place > 0 ? _contacts[place - 1] : 0);
            Marshal.WriteIntPtr(contact, 0x20, _objects[first]);
            Marshal.WriteIntPtr(contact, 0x48, _objects[second]);
            Marshal.WriteIntPtr(contact, 0x70, record);
            WriteLanes(contact, 0x8c, inputs["contact-gap"], k, 1);
            Marshal.WriteByte(contact, 0x90, 20);
            Marshal.WriteInt16(contact, 0x92, (short)IvpImpactReplay.Whole32(inputs, "contact-streak", k));
            Marshal.WriteIntPtr(contact, 0xc0, _system);

            WriteLanes(record, 0x20, inputs["record-normal"], 3 * k, 3);
            WriteLanes(record, 0x90, inputs["record-virtual-mass"], k, 1);
            WriteLanes(record, 0x94, inputs["record-inverse-mass"], k, 1);
            Marshal.WriteIntPtr(record, 0x98, (Marshal.ReadByte(_cores[first]) & 2) == 0 ? _cores[first] : 0);
            Marshal.WriteIntPtr(record, 0xa0, (Marshal.ReadByte(_cores[second]) & 2) == 0 ? _cores[second] : 0);
            WriteLanes(record, 0xf0, inputs["record-first-turn"], 3 * k, 3);
            WriteLanes(record, 0x100, inputs["record-second-turn"], 3 * k, 3);
        }

        private Dictionary<string, long[]> Read(Dictionary<nint, int> places, int listed, int coreCount)
        {
            Dictionary<string, long[]> full = IvpHeapSolveReplay.NewContactLanes(listed);
            int position = 0;

            for (nint contact = Marshal.ReadIntPtr(_system, 0x40); contact != 0 && position < listed; contact = Marshal.ReadIntPtr(contact, 0))
            {
                full["order"][position++] = places.TryGetValue(contact, out int place) ? place : -1;
            }

            for (int place = 0; place < listed; place++)
            {
                nint contact = _contacts[place];
                nint record = _records[place];

                full["normal-push"][place] = (uint)Marshal.ReadInt32(contact, 0x88);
                full["streak"][place] = Marshal.ReadInt16(contact, 0x92);
                full["record-index"][place] = Marshal.ReadInt16(record, 0x70);
                full["record-first-share"][place] = ShareOwner(Marshal.ReadIntPtr(record, 0x78));
                full["record-second-share"][place] = ShareOwner(Marshal.ReadIntPtr(record, 0x80));
                full["record-streak"][place] = Marshal.ReadInt32(record, 0x88);
                full["record-gap"][place] = (uint)Marshal.ReadInt32(record, 0x8c);
            }

            Dictionary<string, long[]> outputs = IvpHeapSolveReplay.Finish(full);
            long[] flags = new long[Cores];
            int[] offsets = [0x140, 0x130, 0x120, 0x110];
            IReadOnlyList<string> vectors = IvpHeapSolveReplay.VectorNames;

            for (int vector = 0; vector < vectors.Count; vector++)
            {
                long[] lanes = new long[3 * Cores];

                for (int k = 0; k < coreCount; k++)
                {
                    for (int lane = 0; lane < 3; lane++)
                    {
                        lanes[(3 * k) + lane] = (uint)Marshal.ReadInt32(_cores[k], offsets[vector] + (4 * lane));
                    }
                }

                outputs[IvpHeapSolveReplay.SolvedCore + vectors[vector]] = lanes;
            }

            for (int k = 0; k < coreCount; k++)
            {
                flags[k] = Marshal.ReadByte(_cores[k]) & 1;
            }

            outputs["core-flag-bit0"] = flags;
            outputs["counts"] = [Marshal.ReadInt16(_system, 0x7a), Marshal.ReadInt16(_system, 0x7c)];

            return outputs;
        }

        private int ShareOwner(nint share) => share == 0 ? -1 : Array.IndexOf(_infos, share);

        private static void WriteLanes(nint block, int offset, long[] lanes, int start, int count)
        {
            for (int lane = 0; lane < count; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[start + lane]));
            }
        }

        private static void Zero(nint block, int size) => Marshal.Copy(Zeroes, 0, block, size);

        private nint Allocate(int size)
        {
            nint block = Marshal.AllocHGlobal(size);

            _blocks.Add(block);
            Zero(block, size);

            return block;
        }
    }
}
