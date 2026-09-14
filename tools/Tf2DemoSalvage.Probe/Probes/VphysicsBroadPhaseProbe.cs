using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s broad phase — <c>FUN_180098880</c> and <c>FUN_180096eb0</c> — called in process over eight
/// fabricated objects with the binary's own OV tree, range manager, nodes and min-lists, the oracle for <c>IvpBroadPhase</c>
/// (B369, D172).
/// </summary>
/// <remarks>
/// **What the binary builds itself**: the tree (`FUN_18009d820`), the range manager (`FUN_1800a0420` with policy 1), each object's
/// hull min-list (`FUN_1800aae10`) and every node, made and freed by its own `FUN_180096eb0` and destructor. **What the probe
/// fabricates**: the environment's fields, the objects and cores the broad phase reads, and three tables of managed callbacks — the
/// filter, two creators and their watchers — which log each call the broad phase makes out and do what the port's
/// <see cref="IvpBroadPhaseReplay"/> twins do, registering watchers through the binary's `FUN_18009de20` and taking them off through
/// its `FUN_18009ef40`. Every case ends by deleting each node through its own table, so the next starts from an empty tree.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpBroadPhaseConformanceTests` reads.
/// </remarks>
public sealed class VphysicsBroadPhaseProbe : IProbe
{
    private const long RefileAddress = 0x180098880;
    private const long RebuildAddress = 0x180096eb0;
    private const long TreeAddress = 0x18009d820;
    private const long RangeAddress = 0x1800a0420;
    private const long MinListAddress = 0x1800aae10;
    private const long RegisterAddress = 0x18009de20;
    private const long UnregisterAddress = 0x18009ef40;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 200;
    private const ulong FixtureSeed = 180098;
    private const ulong SweepSeed = 20260918;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObjectCall(nint manager, nint collisionObject);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint Construct(nint block);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructRange(nint manager, nint environment, int policy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructMinList(nint list, int capacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NodeWatcher(nint node, nint watcher);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint DeletingDestructor(nint self, int flag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FilterCall(nint filter, nint first, nint second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateCall(nint creator, nint first, nint second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemovedCall(nint creator, nint removed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObjectsCall(nint watcher, nint pair);

    /// <inheritdoc />
    public string Name => "vphysics-broad-phase";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's broad phase (FUN_180098880, FUN_180096eb0) called in process over fabricated objects and managed filter, " +
        "creator and watcher tables, compared with the port; 'fixture' writes the conformance suite's cases: " +
        "vphysics-broad-phase [sweep n | fixture path]";

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

            writer.WriteLine("# Written by the vphysics-broad-phase probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpBroadPhaseReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        Draws sweep = new(SweepSeed);
        int differing = 0;
        long events = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep);
            Dictionary<string, long[]> binary = native.Run(inputs);

            foreach (long lane in binary["event-count"])
            {
                events += lane;
            }

            IReadOnlyList<string> differences = IvpBroadPhaseReplay.Differences(binary, IvpBroadPhaseReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} broad phase cases, {differing} differing; the binary made {events} calls out in all");
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpBroadPhaseReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        for (int index = 0; index < IvpBroadPhaseReplay.ObjectCount; index++)
        {
            inputs["friction-group"][index] = index > 0 && draws.Unit() < 0.2 ? (int)(draws.Unit() * index) : index;
            inputs["hull-time"][index] = IvpImpactReplay.Lane(draws.Unit() * 5d);
            inputs["hull-gradient"][index] = IvpImpactReplay.Lane((float)(draws.Unit() * 5d));
            inputs["hull-value"][index] = IvpImpactReplay.Lane((float)(draws.Unit() * 50d));

            int mask = 0;

            for (int other = 0; other < IvpBroadPhaseReplay.ObjectCount; other++)
            {
                mask |= draws.Unit() < 0.8 ? 1 << other : 0;
            }

            inputs["filter"][index] = mask;
        }

        inputs["step"][0] = IvpImpactReplay.Lane(draws.Unit() < 0.8 ? 1d / 66d : 0.001d + (draws.Unit() * 0.05d));

        double now = 5d + (draws.Unit() * 5d);
        double spread = 0.5d + (draws.Unit() * 4d);

        for (int step = 0; step < IvpBroadPhaseReplay.StepCount; step++)
        {
            bool building = step < IvpBroadPhaseReplay.ObjectCount;
            double roll = draws.Unit();
            int kind = IvpBroadPhaseReplay.Refile;

            if (building || roll < 0.15)
            {
                kind = IvpBroadPhaseReplay.Rebuild;
            }
            else if (roll < 0.3)
            {
                kind = IvpBroadPhaseReplay.RefileCreating;
            }

            int flags = draws.Unit() < 0.7 ? 1 + (int)(draws.Unit() * 7) : 0;

            flags |= draws.Unit() < 0.1 ? 0x200 : 0;
            flags |= draws.Unit() < 0.1 ? 0x400 : 0;

            double bitsRoll = draws.Unit();
            int bits = 0;

            if (bitsRoll < 0.1)
            {
                bits = 0x2;
            }
            else if (bitsRoll < 0.2)
            {
                bits = 0x10;
            }

            now += draws.Unit() * 0.05d;
            inputs["kind"][step] = kind;
            inputs["object"][step] = building ? step : (int)(draws.Unit() * IvpBroadPhaseReplay.ObjectCount);
            inputs["now"][step] = IvpImpactReplay.Lane(now);
            inputs["radius"][step] = IvpImpactReplay.Lane((float)(0.05d + (draws.Unit() * 1.5d)));
            inputs["linear"][step] = IvpImpactReplay.Lane(draws.Unit() < 0.3 ? 0f : (float)(draws.Unit() * 10d));
            inputs["surface"][step] = IvpImpactReplay.Lane(draws.Unit() < 0.3 ? 0f : (float)(draws.Unit() * 10d));
            inputs["flags"][step] = flags;
            inputs["core-bits"][step] = bits;

            for (int lane = 0; lane < 3; lane++)
            {
                inputs["center"][(step * 3) + lane] = IvpImpactReplay.Lane(((draws.Unit() * 2d) - 1d) * spread);
            }
        }

        return inputs;
    }

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));
    }

    /// <summary>The environment, objects, cores and callback tables, in unmanaged memory, and the binary's own pieces over them.</summary>
    private sealed class Native : IDisposable
    {
        private const int EnvironmentSize = 0x200;
        private const int ObjectSize = 0x100;
        private const int CoreSize = 0x300;
        private const int WatcherSize = 0x20;
        private const int TableSlots = 8;
        private const int MinListCapacity = 16;

        private readonly ObjectCall _refile;
        private readonly ObjectCall _rebuild;
        private readonly NodeWatcher _register;
        private readonly NodeWatcher _unregister;

        /// <summary>The delegates the fabricated tables point at, held so the collector cannot take them while the binary calls.</summary>
        private readonly List<Delegate> _callbacks = [];

        private readonly List<nint> _blocks = [];
        private readonly nint _environment;
        private readonly nint _manager;
        private readonly nint[] _creators = new nint[2];
        private readonly nint _watcherTable;
        private readonly nint[] _objects = new nint[IvpBroadPhaseReplay.ObjectCount];
        private readonly nint[] _cores = new nint[IvpBroadPhaseReplay.ObjectCount];
        private readonly nint[] _groups = new nint[IvpBroadPhaseReplay.ObjectCount];
        private readonly Dictionary<nint, int> _indices = [];
        private readonly Dictionary<nint, (nint First, nint Second)> _watchers = [];
        private readonly List<int> _events = [];

        private Dictionary<string, long[]> _inputs = [];

        public Native(nint module)
        {
            _refile = VphysicsLibrary.Function<ObjectCall>(module, RefileAddress);
            _rebuild = VphysicsLibrary.Function<ObjectCall>(module, RebuildAddress);
            _register = VphysicsLibrary.Function<NodeWatcher>(module, RegisterAddress);
            _unregister = VphysicsLibrary.Function<NodeWatcher>(module, UnregisterAddress);


            _environment = Block(EnvironmentSize);
            _manager = Block(0x10);
            Marshal.WriteIntPtr(_manager, 8, _environment);
            Marshal.WriteIntPtr(_environment, 0x20, _manager);

            nint tree = Block(0x60);

            VphysicsLibrary.Function<Construct>(module, TreeAddress)(tree);
            Marshal.WriteIntPtr(_environment, 0x28, tree);

            nint range = Block(0x68);

            VphysicsLibrary.Function<ConstructRange>(module, RangeAddress)(range, _environment, 1);
            Marshal.WriteIntPtr(_environment, 0x38, range);

            Marshal.WriteIntPtr(_environment, 0x30, Instance(Table((0, Pointer(new FilterCall(Filter))))));

            nint creatorTable = Table((4, Pointer(new RemovedCall(Removed))), (5, Pointer(new CreateCall(Create))));
            nint creatorArray = Block(16);

            for (int index = 0; index < _creators.Length; index++)
            {
                _creators[index] = Instance(creatorTable);
                Marshal.WriteIntPtr(creatorArray, index * 8, _creators[index]);
            }

            Marshal.WriteInt16(_environment, 0x1c8, (short)_creators.Length);
            Marshal.WriteInt16(_environment, 0x1ca, (short)_creators.Length);
            Marshal.WriteIntPtr(_environment, 0x1d0, creatorArray);

            nint deleting = Pointer(new DeletingDestructor(DeletingCall));

            _watcherTable = Table((0, deleting), (2, Pointer(new ObjectsCall(WatcherObjects))), (4, deleting));

            ConstructMinList minList = VphysicsLibrary.Function<ConstructMinList>(module, MinListAddress);

            for (int index = 0; index < _objects.Length; index++)
            {
                _objects[index] = Block(ObjectSize);
                _cores[index] = Block(CoreSize);
                _groups[index] = Block(0x10);
                _indices[_objects[index]] = index;
                Marshal.WriteIntPtr(_objects[index], 0x30, _environment);
                Marshal.WriteIntPtr(_objects[index], 0xe8, _cores[index]);
                minList(_objects[index] + 0xa0, MinListCapacity);
            }
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            _inputs = inputs;

            Dictionary<string, long[]> outputs = IvpBroadPhaseReplay.NewOutputs();

            Marshal.WriteInt32(_environment, 0xc0, 0);
            Marshal.WriteInt32(_manager, 0, 0);
            Marshal.WriteInt64(_environment, 0x108, inputs["step"][0]);

            for (int index = 0; index < _objects.Length; index++)
            {
                Marshal.WriteIntPtr(_objects[index], 0xf0, _groups[inputs["friction-group"][index]]);
                Marshal.WriteInt64(_objects[index], 0x80, inputs["hull-time"][index]);
                Marshal.WriteInt32(_objects[index], 0x88, unchecked((int)inputs["hull-gradient"][index]));
                Marshal.WriteInt32(_objects[index], 0x90, unchecked((int)inputs["hull-value"][index]));
            }

            for (int step = 0; step < IvpBroadPhaseReplay.StepCount; step++)
            {
                int index = (int)inputs["object"][step];
                nint collisionObject = _objects[index];
                nint core = _cores[index];

                _events.Clear();
                Marshal.WriteInt64(_environment, 0x188, inputs["now"][step]);
                Marshal.WriteInt16(core, 0, (short)inputs["core-bits"][step]);
                Marshal.WriteInt32(core, 0x4, unchecked((int)inputs["radius"][step]));

                for (int lane = 0; lane < 3; lane++)
                {
                    Marshal.WriteInt64(core, 0xf0 + (lane * 8), inputs["center"][(step * 3) + lane]);
                }

                Marshal.WriteInt32(core, 0x1dc, unchecked((int)inputs["linear"][step]));
                Marshal.WriteInt32(core, 0x254, unchecked((int)inputs["surface"][step]));
                Marshal.WriteInt32(collisionObject, 0x78, (int)inputs["flags"][step]);

                switch (inputs["kind"][step])
                {
                    case IvpBroadPhaseReplay.Rebuild:
                        _rebuild(_manager, collisionObject);
                        break;
                    case IvpBroadPhaseReplay.RefileCreating:
                        Marshal.WriteInt32(_manager, 0, 1);
                        _refile(_manager, collisionObject);
                        Marshal.WriteInt32(_manager, 0, 0);
                        break;
                    default:
                        _refile(_manager, collisionObject);
                        break;
                }

                IvpBroadPhaseReplay.RecordEvents(outputs, step, _events);

                nint node = Marshal.ReadIntPtr(collisionObject, 0xd8);

                if (node != 0)
                {
                    for (int lane = 0; lane < 3; lane++)
                    {
                        outputs["node-center"][(step * 3) + lane] = (uint)Marshal.ReadInt32(node, 0x20 + (lane * 4));
                    }

                    outputs["node-radius"][step] = (uint)Marshal.ReadInt32(node, 0x30);

                    nint cell = Marshal.ReadIntPtr(node, 0x10);

                    if (cell != 0)
                    {
                        IvpOvTreeReplay.WriteKey(
                            outputs["key"], step, Marshal.ReadInt32(cell, 0), Marshal.ReadInt32(cell, 4), Marshal.ReadInt32(cell, 8),
                            Marshal.ReadInt32(cell, 0xc), Marshal.ReadInt32(cell, 0x10));
                    }

                    nint hull = Marshal.ReadIntPtr(node, 0x18);

                    if (hull != 0)
                    {
                        int slot = (ushort)Marshal.ReadInt16(node, 0x8);
                        nint entries = Marshal.ReadIntPtr(hull, 0x28);

                        outputs["hull-key"][step] = (uint)Marshal.ReadInt32(entries, (slot * 0x18) + 8);
                    }
                }

                outputs["runs"][step] = Marshal.ReadInt32(_environment, 0xc0);

                for (int other = 0; other < _objects.Length; other++)
                {
                    nint otherNode = Marshal.ReadIntPtr(_objects[other], 0xd8);

                    outputs["watchers"][(step * IvpBroadPhaseReplay.ObjectCount) + other] = otherNode == 0 ? 0 : (ushort)Marshal.ReadInt16(otherNode, 0x42);
                }
            }

            foreach (nint collisionObject in _objects)
            {
                nint node = Marshal.ReadIntPtr(collisionObject, 0xd8);

                if (node != 0)
                {
                    Marshal.GetDelegateForFunctionPointer<DeletingDestructor>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(node), 0x20))(node, 1);
                    Marshal.WriteIntPtr(collisionObject, 0xd8, 0);
                }
            }

            if (_watchers.Count > 0 || Marshal.ReadIntPtr(Marshal.ReadIntPtr(_environment, 0x28), 0x58) != 0)
            {
                throw new InvalidOperationException("A case left watchers or a tree root behind.");
            }

            return outputs;
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }
        }

        private int Filter(nint filter, nint first, nint second)
        {
            int a = _indices[first];
            int b = _indices[second];

            _events.Add(IvpBroadPhaseReplay.Event(IvpBroadPhaseReplay.Filtered, 0, a, b));
            return (int)((_inputs["filter"][a] >> b) & 1);
        }

        private nint Create(nint creator, nint first, nint second)
        {
            int index = Array.IndexOf(_creators, creator);
            int a = _indices[first];
            int b = _indices[second];

            if (IvpBroadPhaseReplay.Declines(index, a, b))
            {
                _events.Add(IvpBroadPhaseReplay.Event(IvpBroadPhaseReplay.Declined, index, a, b));
                return 0;
            }

            _events.Add(IvpBroadPhaseReplay.Event(IvpBroadPhaseReplay.Created, index, a, b));

            nint watcher = Marshal.AllocHGlobal(WatcherSize);

            Marshal.WriteIntPtr(watcher, 0, _watcherTable);
            Marshal.WriteInt32(watcher, 0x18, -1);
            Marshal.WriteInt32(watcher, 0x1c, -1);
            _watchers[watcher] = (first, second);
            _register(Marshal.ReadIntPtr(first, 0xd8), watcher);
            _register(Marshal.ReadIntPtr(second, 0xd8), watcher);
            return watcher;
        }

        private void Removed(nint creator, nint removed)
        {
            _events.Add(IvpBroadPhaseReplay.Event(IvpBroadPhaseReplay.Removed, Array.IndexOf(_creators, creator), _indices[removed], 0));

            nint node = Marshal.ReadIntPtr(removed, 0xd8);

            if (node == 0)
            {
                return;
            }

            for (int place = (ushort)Marshal.ReadInt16(node, 0x42) - 1; place >= 0; place--)
            {
                DeleteWatcher(Marshal.ReadIntPtr(Marshal.ReadIntPtr(node, 0x48), place * 8));
            }
        }

        private nint DeletingCall(nint watcher, int flag)
        {
            DeleteWatcher(watcher);
            return flag == 0 ? watcher : 0;
        }

        private void DeleteWatcher(nint watcher)
        {
            (nint first, nint second) = _watchers[watcher];

            _events.Add(IvpBroadPhaseReplay.Event(IvpBroadPhaseReplay.Deleted, 0, _indices[first], _indices[second]));
            _unregister(Marshal.ReadIntPtr(first, 0xd8), watcher);
            _unregister(Marshal.ReadIntPtr(second, 0xd8), watcher);
            _watchers.Remove(watcher);
            Marshal.FreeHGlobal(watcher);
        }

        private nint Pointer(Delegate callback)
        {
            _callbacks.Add(callback);
            return Marshal.GetFunctionPointerForDelegate(callback);
        }

        private void WatcherObjects(nint watcher, nint pair)
        {
            (nint first, nint second) = _watchers[watcher];

            Marshal.WriteIntPtr(pair, 0, first);
            Marshal.WriteIntPtr(pair, 8, second);
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

        private nint Table(params (int Slot, nint Function)[] slots)
        {
            nint table = Block(TableSlots * 8);

            foreach ((int slot, nint function) in slots)
            {
                Marshal.WriteIntPtr(table, slot * 8, function);
            }

            return table;
        }

        private nint Instance(nint table)
        {
            nint instance = Block(0x10);

            Marshal.WriteIntPtr(instance, 0, table);
            return instance;
        }
    }
}
