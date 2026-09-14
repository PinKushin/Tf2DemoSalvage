using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s pair watcher — made through the default creator's slot 5 (<c>FUN_1800a06f0</c>), refreshed through
/// its hull records' slot 1 (<c>FUN_1800b6170</c>), and ended through its own slot 0 (<c>FUN_1800b5e80</c>) or the creator's slot 4
/// (<c>FUN_1800a07a0</c>) — called in process over two fabricated objects, the oracle for <c>IvpPairWatcher</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary builds and frees the watcher, its pair vector and every mindist itself**, and its range manager (`FUN_1800a0420` with
/// policy 1) and each object's hull min-list (`FUN_1800aae10`). **What the probe fabricates** beside <see cref="VphysicsPairObjects"/>: the
/// creator (8 bytes on table `1800fe6e8`), each object's hull manager fields and an OV node holding its object and a watcher list.
/// **Each node is filed in its object's hull manager first** (`FUN_18009de80`), as the broad phase files it before any creator runs: a
/// watcher's record keyed at 1e20 walks the list from its first entry, and on an empty list — whose minimum the constructor leaves at
/// 1e10 — that entry is 0xffff and the walk faults. Every case must leave both nodes without watchers and both min-lists holding only
/// the node, or the probe stops.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpPairWatcherConformanceTests` reads.
/// </remarks>
public sealed class VphysicsPairWatcherProbe : IProbe
{
    private const long CreatorTable = 0x1800fe6e8;
    private const long RangeAddress = 0x1800a0420;
    private const long MinListAddress = 0x1800aae10;
    private const long MinListRemoveAddress = 0x1800ab1b0;
    private const long FileNodeAddress = 0x18009de80;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 200;
    private const ulong FixtureSeed = 1800065;
    private const ulong SweepSeed = 20260920;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructRange(nint manager, nint environment, int policy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructMinList(nint list, int capacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemoveCall(nint list, int slot);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FileNodeCall(nint node, nint hull, double gap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateCall(nint creator, nint first, nint second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemovedCall(nint creator, nint removed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PassedCall(nint record, nint manager, float overshoot);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint DeletingDestructor(nint self, int flag);

    /// <inheritdoc />
    public string Name => "vphysics-pair-watcher";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's pair watcher (made by FUN_1800a06f0, refreshed by FUN_1800b6170, ended by FUN_1800b5e80 or FUN_1800a07a0) " +
        "over synthesized surfaces, compared with the port; 'fixture' writes the conformance suite's cases: " +
        "vphysics-pair-watcher [sweep n | fixture path]";

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
            VphysicsPairDraws draws = new(FixtureSeed);

            using StreamWriter writer = File.CreateText(arguments[1]);

            writer.WriteLine("# Written by the vphysics-pair-watcher probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpPairWatcherReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        VphysicsPairDraws sweep = new(SweepSeed);
        int differing = 0;
        long made = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep);
            Dictionary<string, long[]> binary = native.Run(inputs);

            made += binary["created"][IvpPairWatcherReplay.StepCount - 1];

            IReadOnlyList<string> differences = IvpPairWatcherReplay.Differences(binary, IvpPairWatcherReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} pair watcher cases, {differing} differing; the binary made {made} mindists in all");
    }

    private static Dictionary<string, long[]> RandomCase(VphysicsPairDraws draws)
    {
        Dictionary<string, long[]> inputs = VphysicsPairObjects.NewInputs(IvpPairWatcherReplay.Inputs);

        VphysicsPairObjects.RandomObjects(draws, inputs);

        double now = 10d + draws.Unit();

        inputs["step"][0] = IvpImpactReplay.Lane(0.005d + (draws.Unit() * 0.015d));

        for (int side = 0; side < 2; side++)
        {
            inputs["hull-time"][side] = IvpImpactReplay.Lane(now - (draws.Unit() * 0.5d));
            inputs["hull-gradient"][side] = IvpImpactReplay.Lane((float)(draws.Unit() * 2d));
            inputs["hull-value"][side] = IvpImpactReplay.Lane((float)(draws.Unit() * 0.5d));
            inputs["node-gap"][side] = IvpImpactReplay.Lane(draws.Unit() * 2d);
        }

        for (int step = 0; step < IvpPairWatcherReplay.StepCount; step++)
        {
            now += draws.Unit() * 0.02d;
            inputs["now"][step] = IvpImpactReplay.Lane(now);
            VphysicsPairObjects.RandomPlacement(draws, inputs, step, now);

            for (int side = 0; side < 2; side++)
            {
                inputs["linear"][(step * 2) + side] = IvpImpactReplay.Lane((float)(draws.Unit() * 2d));
                inputs["surface"][(step * 2) + side] = IvpImpactReplay.Lane((float)draws.Unit());
            }

            inputs["passed"][step] = draws.Unit() < 0.5 ? 0 : 1;
        }

        inputs["ending"][0] = (int)(draws.Unit() * 3);
        return inputs;
    }

    /// <summary>The two objects, their hull min-lists and nodes, the range manager and the creator, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int NodeCapacity = 4;
        private const int MinListCapacity = 16;

        private readonly VphysicsPairObjects _objects;
        private readonly FileNodeCall _fileNode;
        private readonly RemoveCall _remove;
        private readonly nint _creator;
        private readonly nint[] _nodes = new nint[2];

        public Native(nint module)
        {
            _objects = new VphysicsPairObjects(module);
            _fileNode = VphysicsLibrary.Function<FileNodeCall>(module, FileNodeAddress);
            _remove = VphysicsLibrary.Function<RemoveCall>(module, MinListRemoveAddress);

            nint range = _objects.Block(0x68);

            VphysicsLibrary.Function<ConstructRange>(module, RangeAddress)(range, _objects.Environment, 1);
            Marshal.WriteIntPtr(_objects.Environment, 0x38, range);
            _creator = _objects.Block(0x10);
            Marshal.WriteIntPtr(_creator, 0, VphysicsLibrary.Address(module, CreatorTable));

            ConstructMinList minList = VphysicsLibrary.Function<ConstructMinList>(module, MinListAddress);

            for (int side = 0; side < 2; side++)
            {
                minList(_objects.Object(side) + 0xa0, MinListCapacity);
                _nodes[side] = _objects.Block(0x50);
                Marshal.WriteIntPtr(_nodes[side], 0x38, _objects.Object(side));
                Marshal.WriteInt16(_nodes[side], 0x40, NodeCapacity);
                Marshal.WriteIntPtr(_nodes[side], 0x48, _objects.Block(NodeCapacity * 8));
                Marshal.WriteIntPtr(_objects.Object(side), 0xd8, _nodes[side]);
            }
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = IvpPairWatcherReplay.NewOutputs();
            nint environment = _objects.Environment;
            nint watcher = 0;

            _objects.Load(inputs);
            Marshal.WriteInt32(environment, 0xbc, 0);
            Marshal.WriteInt64(environment, 0x108, inputs["step"][0]);

            for (int side = 0; side < 2; side++)
            {
                nint collisionObject = _objects.Object(side);

                Marshal.WriteInt64(collisionObject, 0x80, inputs["hull-time"][side]);
                Marshal.WriteInt32(collisionObject, 0x88, unchecked((int)inputs["hull-gradient"][side]));
                Marshal.WriteInt32(collisionObject, 0x90, unchecked((int)inputs["hull-value"][side]));
            }

            for (int step = 0; step < IvpPairWatcherReplay.StepCount; step++)
            {
                _objects.Events.Clear();
                _objects.Place(inputs, step);

                for (int side = 0; side < 2; side++)
                {
                    Marshal.WriteInt32(_objects.Core(side), 0x1dc, unchecked((int)inputs["linear"][(step * 2) + side]));
                    Marshal.WriteInt32(_objects.Core(side), 0x254, unchecked((int)inputs["surface"][(step * 2) + side]));
                }

                if (step == 0)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        _fileNode(_nodes[side], _objects.Object(side) + 0x80, BitConverter.Int64BitsToDouble(inputs["node-gap"][side]));
                    }

                    watcher = Slot<CreateCall>(_creator, 5)(_creator, _objects.Object(0), _objects.Object(1));
                }
                else
                {
                    int passed = (int)inputs["passed"][step];
                    nint record = watcher + (passed == 0 ? 0x28 : 0x48);

                    Slot<PassedCall>(record, 1)(record, _objects.Object(passed) + 0x80, 0f);
                }

                List<int> names = [];
                nint elements = Marshal.ReadIntPtr(watcher, 0x70);

                for (int index = 0; index < (ushort)Marshal.ReadInt16(watcher, 0x6a); index++)
                {
                    names.Add(_objects.Event(0, Marshal.ReadIntPtr(elements, index * 8)));
                }

                IvpPairMindistsReplay.Record(outputs, "pair", step, names);
                IvpPairMindistsReplay.Record(outputs, "event", step, _objects.Events);
                outputs["live"][step] = Marshal.ReadInt32(environment, 0xb0);
                outputs["created"][step] = Marshal.ReadInt32(environment, 0xb4);
                outputs["deleted"][step] = Marshal.ReadInt32(environment, 0xb8);
                outputs["refreshes"][step] = Marshal.ReadInt32(environment, 0xbc);

                for (int side = 0; side < 2; side++)
                {
                    int at = (step * 2) + side;
                    int slot = Marshal.ReadInt32(watcher, side == 0 ? 0x30 : 0x50);
                    float key = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(Marshal.ReadIntPtr(_objects.Object(side), 0xa8), (slot * 0x18) + 8));

                    outputs["record-slot"][at] = slot;
                    outputs["record-key"][at] = IvpImpactReplay.Lane(key);
                    outputs["node-watchers"][at] = (ushort)Marshal.ReadInt16(_nodes[side], 0x42);
                    outputs["watcher-index"][at] = Marshal.ReadInt32(watcher, 0x18 + (side * 4));
                }
            }

            int ending = (int)inputs["ending"][0];

            if (ending == IvpPairWatcherReplay.EndDeleted)
            {
                Slot<DeletingDestructor>(watcher, 0)(watcher, 1);
            }
            else
            {
                Slot<RemovedCall>(_creator, 4)(_creator, _objects.Object(ending - IvpPairWatcherReplay.EndFirstRemoved));
            }

            outputs["end-live"][0] = Marshal.ReadInt32(environment, 0xb0);
            outputs["end-deleted"][0] = Marshal.ReadInt32(environment, 0xb8);

            for (int side = 0; side < 2; side++)
            {
                nint collisionObject = _objects.Object(side);

                outputs["end-watchers"][side] = (ushort)Marshal.ReadInt16(_nodes[side], 0x42);
                outputs["end-hull-count"][side] = Marshal.ReadInt32(collisionObject, 0xa0 + 0x1c);
                _remove(collisionObject + 0xa0, (ushort)Marshal.ReadInt16(_nodes[side], 0x8));
                Marshal.WriteIntPtr(_nodes[side], 0x18, 0);

                if (outputs["end-watchers"][side] != 0 || Marshal.ReadInt32(collisionObject, 0xa0 + 0x1c) != 0)
                {
                    throw new InvalidOperationException("A pair watcher case left a watcher on a node or a record in a min-list.");
                }
            }

            _objects.Unload();
            return outputs;
        }

        public void Dispose() => _objects.Dispose();

        private static T Slot<T>(nint instance, int slot)
            where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * 8));
    }
}
