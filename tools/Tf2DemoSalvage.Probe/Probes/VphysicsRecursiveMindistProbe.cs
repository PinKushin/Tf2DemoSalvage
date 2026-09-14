using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s larger mindist — made by <c>IvpPairMindists::Refresh</c>, frozen and collided through its own slots 7
/// and 8, told its hull passed through a record's slot 1, and deleted through its slot 0 — called in process over two fabricated objects,
/// the oracle for <c>IvpRecursiveMindist</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary builds and frees every mindist itself**, files and unfiles their records in each object's hull min-list
/// (<c>1800aae10</c>), and answers the pair ranges through its own range manager (<c>1800a0420</c>, policy 1). **What the probe
/// fabricates** beside <see cref="VphysicsPairObjects"/>: a mindist manager with its exact list, rechecked vector and invalid list; the
/// pair vector; an outer delegator whose slot 0 takes a mindist out of the pair, slot 2 names each change and slot 3 answers the case's
/// count; and each object's hull fields and state bits. **What is detoured**: the exact tail names the mindist and calls the binary's own
/// <c>IvpMindistManager::Revalidate</c> (<c>180097ae0</c>), linking it exact without a minimize; <c>IvpMindist::Collide</c>
/// (<c>18008ecb0</c>, fifteen bytes of register saves) names it; <c>IvpMindistMinimize::MinimizeWithoutBudget</c> (<c>180095ad0</c>,
/// a two-byte push and a seven-byte stack reservation, then a four-byte load: the patch ends inside that load and nothing returns into the
/// routine) names it, ORs the step's bits into its flags and writes the step's length. Every case must leave both min-lists, the exact list
/// and the invalid list empty once the pair is deleted, or the probe stops. The lanes are <see cref="IvpRecursiveMindistReplay"/>'s.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpRecursiveMindistReplayConformanceTests` reads.
/// </remarks>
public sealed class VphysicsRecursiveMindistProbe : IProbe
{
    private const long RefreshAddress = 0x180096680;
    private const long RevalidateAddress = 0x180097ae0;
    private const long CollideAddress = 0x18008ecb0;
    private const long RecheckAddress = 0x180095ad0;
    private const long RecursiveTable = 0x1800fe960;
    private const long RangeAddress = 0x1800a0420;
    private const long MinListAddress = 0x1800aae10;
    private const long ToleranceBlock = 0x18012d540;
    private const long FillToleranceAddress = 0x180098fd0;
    private const double Gravity = 9.81d;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 300;
    private const ulong FixtureSeed = 0x1800b2700;
    private const ulong SweepSeed = 20260914;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RefreshCall(
        nint first, nint second, double gap, nint pair, nint firstLedge, nint secondLedge, nint firstRoot, nint secondRoot, nint delegator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LinkCall(nint manager, nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RemovedCall(nint delegator, nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AddedCall(nint delegator, int change);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BeneathCall(nint delegator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MindistCall(nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecheckCall(nint mindist);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreezeCall(nint mindist, nint manager);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PassedCall(nint record, nint manager, float overshoot);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint DeletingDestructor(nint self, int flag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructRange(nint manager, nint environment, int policy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructMinList(nint list, int capacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FillToleranceCall(nint block, double tolerance, double gravity);

    /// <inheritdoc />
    public string Name => "vphysics-recursive-mindist";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's larger mindist (IvpRecursiveMindist::Freeze, ::Collide, ::HullPassed, ::Delete) over synthesized hull ledges, " +
        "compared with the port; 'fixture' writes the conformance suite's cases: vphysics-recursive-mindist [sweep n | fixture path]";

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

            writer.WriteLine("# Written by the vphysics-recursive-mindist probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpRecursiveMindistReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        if (arguments.Count >= 2 && arguments[0] == "detail")
        {
            int target = int.Parse(arguments[1], CultureInfo.InvariantCulture);
            VphysicsPairDraws draws = new(SweepSeed);

            for (int index = 0; index < target; index++)
            {
                native.Run(RandomCase(draws));
            }

            Dictionary<string, long[]> inputs = RandomCase(draws);

            foreach (string difference in IvpRecursiveMindistReplay.Differences(native.Run(inputs), IvpRecursiveMindistReplay.Run(inputs)))
            {
                output.WriteLine(difference);
            }

            output.WriteLine($"actions {string.Join(' ', inputs["action"])}; outer answer {inputs["outer-answer"][0]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        VphysicsPairDraws sweep = new(SweepSeed);
        int differing = 0;
        int acted = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep);
            Dictionary<string, long[]> binary = native.Run(inputs);

            for (int step = 1; step < IvpRecursiveMindistReplay.StepCount; step++)
            {
                acted += binary["chosen"][step] >= 0 ? 1 : 0;
            }

            IReadOnlyList<string> differences = IvpRecursiveMindistReplay.Differences(binary, IvpRecursiveMindistReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} larger mindist cases, {differing} differing; the binary acted on a larger mindist {acted} times");
    }

    private static Dictionary<string, long[]> RandomCase(VphysicsPairDraws draws)
    {
        Dictionary<string, long[]> inputs = VphysicsPairObjects.NewInputs(IvpRecursiveMindistReplay.Inputs);

        VphysicsPairObjects.RandomObjects(draws, inputs, hullShare: 0.6d);

        double now = 10d + draws.Unit();

        for (int side = 0; side < 2; side++)
        {
            string suffix = IvpPairMindistsReplay.Suffix(side);
            long[] bits = inputs["virtual" + suffix];
            long[] kinds = inputs["kind" + suffix];

            // Only a hull's triangles and edges are virtual: a leaf ledge is real geometry, and opening one would start a radius query
            // beneath a terminal node, where the binary reads past it.
            for (int lane = 0; lane < bits.Length; lane++)
            {
                bits[lane] = kinds[lane] == IvpLedgeTreeReplay.InnerWithLedge ? (int)(draws.Unit() * 16) : 0;
            }

            inputs["state"][side] = draws.Unit() < 0.5 ? 0 : 1 + (int)(draws.Unit() * 7);
            inputs["hull-time"][side] = IvpImpactReplay.Lane(now - (draws.Unit() * 0.5d));
            inputs["hull-gradient"][side] = IvpImpactReplay.Lane((float)(draws.Unit() * 2d));
            inputs["hull-value"][side] = IvpImpactReplay.Lane((float)(draws.Unit() * 0.5d));
            inputs["next-psi"][side] = IvpImpactReplay.Lane((float)draws.Unit());
        }

        double answer = draws.Unit();

        inputs["outer-answer"][0] = answer switch
        {
            < 0.7 => -1,
            < 0.76 => 0,
            < 0.82 => 5,
            < 0.9 => 1000,
            _ => 1001,
        };
        inputs["step"][0] = IvpImpactReplay.Lane(0.005d + (draws.Unit() * 0.015d));
        inputs["gap"][0] = IvpImpactReplay.Lane(draws.Unit() * 0.3d);

        for (int step = 0; step < IvpRecursiveMindistReplay.StepCount; step++)
        {
            now += draws.Unit() * 0.02d;
            inputs["now"][step] = IvpImpactReplay.Lane(now);
            VphysicsPairObjects.RandomPlacement(draws, inputs, step, now);

            for (int side = 0; side < 2; side++)
            {
                inputs["linear"][(step * 2) + side] = IvpImpactReplay.Lane((float)(draws.Unit() * 2d));
                inputs["surface"][(step * 2) + side] = IvpImpactReplay.Lane((float)draws.Unit());
            }

            double action = draws.Unit();

            inputs["action"][step] = action switch
            {
                < 0.2 => IvpRecursiveMindistReplay.ActionRefresh,
                < 0.5 => IvpRecursiveMindistReplay.ActionFreeze,
                < 0.8 => IvpRecursiveMindistReplay.ActionCollide,
                _ => IvpRecursiveMindistReplay.ActionHullPassed,
            };
            inputs["pick"][step] = (int)(draws.Unit() * 64);
            inputs["kinds"][step] = Kinds(draws);
            inputs["record"][step] = draws.Unit() < 0.5 ? 0 : 1;
            inputs["recheck-bits"][step] = draws.Unit() switch
            {
                < 0.7 => 0,
                < 0.85 => 0x4000,
                _ => 0x8000,
            };
            inputs["recheck-length"][step] = IvpImpactReplay.Lane(Length(draws));
            inputs["refresh-gap"][step] = IvpImpactReplay.Lane(draws.Unit() * 0.3d);
        }

        return inputs;
    }

    /// <summary>Two feature kinds and slots slot 8 does not assert against: never an edge against a triangle.</summary>
    private static int Kinds(VphysicsPairDraws draws)
    {
        int first = (int)(draws.Unit() * 4);
        int second = (int)(draws.Unit() * 4);

        if (first == (int)IvpFeatureKind.Edge && second == (int)IvpFeatureKind.Triangle)
        {
            second = (int)IvpFeatureKind.Edge;
        }

        return first | (second << 8) | ((int)(draws.Unit() * 3) << 16) | ((int)(draws.Unit() * 3) << 24);
    }

    /// <summary>A rechecked length: under the contact gap, over it, or now and then a NaN.</summary>
    private static float Length(VphysicsPairDraws draws)
    {
        double draw = draws.Unit();

        if (draw < 0.05)
        {
            return float.NaN;
        }

        return draw < 0.5 ? (float)(draws.Unit() * 0.02d) : (float)(0.05d + draws.Unit());
    }

    /// <summary>The objects, the manager, the pair vector and the outer delegator, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int PairCapacity = 0x100;
        private const int MinListCapacity = 64;

        private readonly VphysicsPairObjects _objects;
        private readonly VphysicsDetour _collide;
        private readonly VphysicsDetour _recheck;
        private readonly RefreshCall _refresh;
        private readonly ConstructMinList _minList;
        private readonly nint _recursiveTable;
        private readonly nint _manager;
        private readonly nint _pair;
        private readonly nint _pairElements;
        private readonly nint _delegator;
        private int _answer;
        private int _recheckBits;
        private int _recheckLength;

        public Native(nint module)
        {
            LinkCall revalidate = VphysicsLibrary.Function<LinkCall>(module, RevalidateAddress);

            _objects = new VphysicsPairObjects(module, (manager, mindist) => revalidate(manager, mindist));
            _refresh = VphysicsLibrary.Function<RefreshCall>(module, RefreshAddress);
            _recursiveTable = VphysicsLibrary.Address(module, RecursiveTable);
            _collide = new VphysicsDetour(module, CollideAddress, _objects.Keep(new MindistCall(Collided)));
            _recheck = new VphysicsDetour(module, RecheckAddress, _objects.Keep(new RecheckCall(Recheck)));

            // The tolerance block as a running environment leaves it. At load, IvpCollisionTolerance::FillAtLoad fills it with a tolerance of 0.01, so
            // IvpCollisionTolerance::ContactGap reads 0.02; the environment constructor and SetGravity then run the same fill twice,
            // on the preset in metres and on its own margin. The probe runs those two fills through the binary's own routine, and
            // checks the gap it leaves against the port's before any case runs.
            nint block = VphysicsLibrary.Address(module, ToleranceBlock);
            FillToleranceCall fill = VphysicsLibrary.Function<FillToleranceCall>(module, FillToleranceAddress);
            int limit = Marshal.ReadInt32(block, 0x12c);

            fill(block, IvpCollisionTolerance.Metres, Gravity);
            fill(block, BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, 4)), Gravity);

            if (Marshal.ReadInt32(block, 0x10c) != BitConverter.SingleToInt32Bits(IvpCollisionTolerance.ContactGap) ||
                Marshal.ReadInt32(block, 0x12c) != limit || limit != IvpRecursiveMindist.Limit)
            {
                throw new InvalidOperationException("The binary's settled tolerance block does not match the port's contact gap, or its limit moved.");
            }

            _manager = _objects.Block(0x40);
            Marshal.WriteIntPtr(_manager, 8, _objects.Environment);
            Marshal.WriteIntPtr(_objects.Environment, 0x20, _manager);

            nint range = _objects.Block(0x68);

            VphysicsLibrary.Function<ConstructRange>(module, RangeAddress)(range, _objects.Environment, 1);
            Marshal.WriteIntPtr(_objects.Environment, 0x38, range);

            _minList = VphysicsLibrary.Function<ConstructMinList>(module, MinListAddress);
            _pair = _objects.Block(0x10);
            _pairElements = _objects.Block(PairCapacity * 8);
            Marshal.WriteInt16(_pair, 0, PairCapacity);
            Marshal.WriteIntPtr(_pair, 8, _pairElements);

            nint delegatorTable = _objects.Block(8 * 4);

            Marshal.WriteIntPtr(delegatorTable, 0, Marshal.GetFunctionPointerForDelegate(_objects.Keep(new RemovedCall(Removed))));
            Marshal.WriteIntPtr(delegatorTable, 2 * 8, Marshal.GetFunctionPointerForDelegate(_objects.Keep(new AddedCall(Added))));
            Marshal.WriteIntPtr(delegatorTable, 3 * 8, Marshal.GetFunctionPointerForDelegate(_objects.Keep(new BeneathCall(_ => _answer))));
            _delegator = _objects.Block(0x10);
            Marshal.WriteIntPtr(_delegator, 0, delegatorTable);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = IvpRecursiveMindistReplay.NewOutputs();
            nint environment = _objects.Environment;

            _objects.Load(inputs, IvpRecursiveMindistReplay.Surface);
            Marshal.WriteInt32(environment, 0xbc, 0);
            Marshal.WriteInt64(environment, 0x108, inputs["step"][0]);
            _answer = (int)inputs["outer-answer"][0];

            for (int side = 0; side < 2; side++)
            {
                nint collisionObject = _objects.Object(side);

                // A fresh min-list each case: a list a case emptied hands out its freed slots last freed first, which the port's new list
                // for the next case would not. The old list's storage is left behind for the life of the probe.
                _minList(collisionObject + 0xa0, MinListCapacity);
                Marshal.WriteByte(collisionObject, 0x78, (byte)(8 | (int)inputs["state"][side]));
                Marshal.WriteInt64(collisionObject, 0x80, inputs["hull-time"][side]);
                Marshal.WriteInt32(collisionObject, 0x88, unchecked((int)inputs["hull-gradient"][side]));
                Marshal.WriteInt32(collisionObject, 0x8c, 0);
                Marshal.WriteInt32(collisionObject, 0x90, unchecked((int)inputs["hull-value"][side]));
                Marshal.WriteInt32(collisionObject, 0x94, 0);
                Marshal.WriteInt32(collisionObject, 0x98, unchecked((int)inputs["next-psi"][side]));
            }

            for (int step = 0; step < IvpRecursiveMindistReplay.StepCount; step++)
            {
                _objects.Events.Clear();
                _objects.Place(inputs, step);

                for (int side = 0; side < 2; side++)
                {
                    Marshal.WriteInt32(_objects.Core(side), 0x1dc, unchecked((int)inputs["linear"][(step * 2) + side]));
                    Marshal.WriteInt32(_objects.Core(side), 0x254, unchecked((int)inputs["surface"][(step * 2) + side]));
                }

                int action = step == 0 ? IvpRecursiveMindistReplay.ActionRefresh : (int)inputs["action"][step];
                nint chosen = 0;

                if (action == IvpRecursiveMindistReplay.ActionRefresh)
                {
                    _refresh(
                        _objects.Object(0), _objects.Object(1), BitConverter.Int64BitsToDouble(step == 0 ? inputs["gap"][0] : inputs["refresh-gap"][step]),
                        _pair, 0, 0, 0, 0, _delegator);
                }
                else
                {
                    int state = action == IvpRecursiveMindistReplay.ActionHullPassed ? IvpMindistHull.RecursiveState : IvpMindistHull.ExactState;

                    chosen = Choose(state, (int)inputs["pick"][step]);

                    if (chosen != 0)
                    {
                        Act(chosen, action, inputs, step);
                    }
                }

                RecordStep(outputs, step, chosen);
            }

            _objects.Events.Clear();

            for (int index = (ushort)Marshal.ReadInt16(_pair, 2) - 1; index >= 0; index--)
            {
                nint mindist = Marshal.ReadIntPtr(_pairElements, index * 8);

                Slot<DeletingDestructor>(mindist, 0)(mindist, 1);
            }

            IvpPairMindistsReplay.Record(outputs, "end-event", 0, _objects.Events);
            outputs["end-live"][0] = Marshal.ReadInt32(environment, 0xb0);
            outputs["end-deleted"][0] = Marshal.ReadInt32(environment, 0xb8);

            for (int side = 0; side < 2; side++)
            {
                outputs["end-min-count"][side] = Marshal.ReadInt32(_objects.Object(side), 0xa0 + 0x1c);

                if (outputs["end-min-count"][side] != 0)
                {
                    throw new InvalidOperationException("A larger mindist case left a record in a min-list.");
                }
            }

            if (Marshal.ReadIntPtr(_manager, 0x10) != 0 || Marshal.ReadIntPtr(_manager, 0x28) != 0 || Marshal.ReadInt16(_pair, 2) != 0)
            {
                throw new InvalidOperationException("A larger mindist case left a mindist linked.");
            }

            _objects.Unload();
            return outputs;
        }

        public void Dispose()
        {
            _collide.Dispose();
            _recheck.Dispose();
            _objects.Dispose();
        }

        private static T Slot<T>(nint instance, int slot)
            where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * 8));

        private static int ListCount(nint head)
        {
            int count = 0;

            for (nint mindist = head; mindist != 0; mindist = Marshal.ReadIntPtr(mindist, 0xc8))
            {
                count++;
            }

            return count;
        }

        private nint Choose(int state, int pick)
        {
            List<nint> candidates = [];

            for (int index = 0; index < (ushort)Marshal.ReadInt16(_pair, 2); index++)
            {
                nint mindist = Marshal.ReadIntPtr(_pairElements, index * 8);

                if (Marshal.ReadIntPtr(mindist) == _recursiveTable && (Marshal.ReadInt32(mindist, 0x20) & IvpMindistHull.StateMask) == state)
                {
                    candidates.Add(mindist);
                }
            }

            return candidates.Count == 0 ? 0 : candidates[(int)((uint)pick % (uint)candidates.Count)];
        }

        private void Act(nint chosen, int action, Dictionary<string, long[]> inputs, int step)
        {
            switch (action)
            {
                case IvpRecursiveMindistReplay.ActionFreeze:
                    Slot<FreezeCall>(chosen, 7)(chosen, _manager);
                    break;
                case IvpRecursiveMindistReplay.ActionCollide:
                    int kinds = (int)inputs["kinds"][step];

                    Feature(chosen, 0x50, 0x5a, kinds & 0xff, (kinds >> 16) & 0xff);
                    Feature(chosen, 0x88, 0x92, (kinds >> 8) & 0xff, (kinds >> 24) & 0xff);
                    Slot<MindistCall>(chosen, 8)(chosen);
                    break;
                default:
                    int record = (int)inputs["record"][step];
                    nint address = chosen + (record == 0 ? 0x28 : 0x60);

                    _recheckBits = (int)inputs["recheck-bits"][step];
                    _recheckLength = unchecked((int)inputs["recheck-length"][step]);
                    Slot<PassedCall>(address, 1)(address, _objects.Object(record) + 0x80, 0f);
                    break;
            }
        }

        /// <summary>A record's feature moved to a slot of its triangle, and its kind word written.</summary>
        private static void Feature(nint mindist, int featureOffset, int kindOffset, int kind, int slot)
        {
            nint triangle = Marshal.ReadIntPtr(mindist, featureOffset) & ~(nint)0xf;

            Marshal.WriteIntPtr(mindist, featureOffset, triangle + 4 + (slot * 4));
            Marshal.WriteInt16(mindist, kindOffset, (short)kind);
        }

        private void RecordStep(Dictionary<string, long[]> outputs, int step, nint chosen)
        {
            nint environment = _objects.Environment;
            List<int> names = [];

            for (int index = 0; index < (ushort)Marshal.ReadInt16(_pair, 2); index++)
            {
                names.Add(_objects.Event(0, Marshal.ReadIntPtr(_pairElements, index * 8)));
            }

            IvpPairMindistsReplay.Record(outputs, "pair", step, names);
            IvpPairMindistsReplay.Record(outputs, "event", step, _objects.Events);
            outputs["live"][step] = Marshal.ReadInt32(environment, 0xb0);
            outputs["created"][step] = Marshal.ReadInt32(environment, 0xb4);
            outputs["deleted"][step] = Marshal.ReadInt32(environment, 0xb8);
            outputs["refreshes"][step] = Marshal.ReadInt32(environment, 0xbc);
            outputs["exact-count"][step] = ListCount(Marshal.ReadIntPtr(_manager, 0x10));
            outputs["invalid-count"][step] = ListCount(Marshal.ReadIntPtr(_manager, 0x28));
            outputs["chosen"][step] = chosen == 0 ? -1 : _objects.Event(0, chosen);

            List<int> members = [];
            int state = chosen == 0 ? 0 : Marshal.ReadInt32(chosen, 0x20) & IvpMindistHull.StateMask;
            bool filed = chosen != 0 && (state == IvpMindistHull.RecursiveState || state == IvpMindistHull.FiledState);

            if (chosen != 0)
            {
                outputs["chosen-flags"][step] = Marshal.ReadInt32(chosen, 0x20) & IvpRecursiveMindistReplay.ConstructedFlags;
                outputs["chosen-open"][step] = Marshal.ReadInt32(chosen, 0xf8);
                outputs["chosen-total"][step] = Marshal.ReadInt32(chosen, 0xfc);

                nint elements = Marshal.ReadIntPtr(chosen, 0xf0);

                for (int index = 0; index < (ushort)Marshal.ReadInt16(chosen, 0xea); index++)
                {
                    members.Add(_objects.Event(0, Marshal.ReadIntPtr(elements, index * 8)));
                }
            }

            IvpPairMindistsReplay.Record(outputs, "member", step, members);

            for (int side = 0; side < 2; side++)
            {
                int at = (step * 2) + side;
                nint collisionObject = _objects.Object(side);
                int slotOffset = side == 0 ? 0x30 : 0x68;
                int slot = filed ? Marshal.ReadInt32(chosen, slotOffset) : -1;

                outputs["record-slot"][at] = slot;
                outputs["record-key"][at] = filed
                    ? (uint)Marshal.ReadInt32(Marshal.ReadIntPtr(collisionObject, 0xa8), (slot * 0x18) + 8)
                    : 0;
                outputs["min-count"][at] = Marshal.ReadInt32(collisionObject, 0xa0 + 0x1c);
            }
        }

        private void Collided(nint mindist) => _objects.Events.Add(_objects.Event(IvpRecursiveMindistReplay.Collided, mindist));

        private int Recheck(nint mindist)
        {
            _objects.Events.Add(_objects.Event(IvpRecursiveMindistReplay.Rechecked, mindist));
            Marshal.WriteInt32(mindist, 0x20, Marshal.ReadInt32(mindist, 0x20) | _recheckBits);
            Marshal.WriteInt32(mindist, 0xa8, _recheckLength);
            return 1;
        }

        private void Added(nint delegator, int change) => _objects.Events.Add((IvpRecursiveMindistReplay.Added << 16) | (change & 0xffff));

        private void Removed(nint delegator, nint mindist)
        {
            _objects.Events.Add(_objects.Event(IvpPairMindistsReplay.Removed, mindist));

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
    }
}
