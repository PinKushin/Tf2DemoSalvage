using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s pair mindist refresh, <c>FUN_180096680</c>, called in process over two fabricated objects with
/// synthesized surfaces behind the binary's own polygon manager table — its constructor's tails detoured to recorders — the oracle
/// for <c>IvpPairMindists</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary does the allocating**: each new mindist is its own 0xe0 bytes, built by `FUN_180096680` and `FUN_1800975d0` and freed
/// by its own destructor `FUN_180096250`. **What the probe fabricates** beside <see cref="VphysicsPairObjects"/>: the pair vector, and a
/// delegator table whose slot 0 takes a mindist out by its back-index as `FUN_1800b5fd0` does. Every case ends by deleting the pair's
/// mindists through their own tables.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpPairMindistsConformanceTests` reads.
/// </remarks>
public sealed class VphysicsPairMindistsProbe : IProbe
{
    private const long RefreshAddress = 0x180096680;

    private const int DefaultSweep = 5_000;
    private const int FixtureCases = 200;
    private const ulong FixtureSeed = 180096;
    private const ulong SweepSeed = 20260919;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RefreshCall(
        nint first, nint second, double gap, nint pair, nint firstLedge, nint secondLedge, nint firstRoot, nint secondRoot, nint delegator);

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
            VphysicsPairDraws draws = new(FixtureSeed);

            using StreamWriter writer = File.CreateText(arguments[1]);

            writer.WriteLine("# Written by the vphysics-pair-mindists probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpPairMindistsReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            Dictionary<string, long[]> searched = FloatElapsed();
            Dictionary<string, long[]> left = native.Run(searched);

            IvpPairMindistsReplay.Write(writer, new IvpReplayCase("float-elapsed", searched, left));
            output.WriteLine($"{FixtureCases} cases and 1 searched case written to {arguments[1]}; float-elapsed left {left["pair-count"][0]} mindists");
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

            made += binary["created"][IvpPairMindistsReplay.StepCount - 1];

            IReadOnlyList<string> differences = IvpPairMindistsReplay.Differences(binary, IvpPairMindistsReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} pair mindist cases, {differing} differing; the binary made {made} mindists in all");
    }

    private static Dictionary<string, long[]> RandomCase(VphysicsPairDraws draws)
    {
        Dictionary<string, long[]> inputs = VphysicsPairObjects.NewInputs(IvpPairMindistsReplay.Inputs);
        List<int>[] terminals = VphysicsPairObjects.RandomObjects(draws, inputs);
        double now = 10d + draws.Unit();

        for (int step = 0; step < IvpPairMindistsReplay.StepCount; step++)
        {
            now += draws.Unit() * 0.02d;
            inputs["now"][step] = IvpImpactReplay.Lane(now);
            inputs["gap"][step] = IvpImpactReplay.Lane(draws.Unit() * 0.3d);
            VphysicsPairObjects.RandomPlacement(draws, inputs, step, now);
            inputs["given-first"][step] = terminals[0].Count > 0 && draws.Unit() < 0.15 ? terminals[0][(int)(draws.Unit() * terminals[0].Count)] : -1;
            inputs["given-second"][step] = terminals[1].Count > 0 && draws.Unit() < 0.15 ? terminals[1][(int)(draws.Unit() * terminals[1].Count)] : -1;
        }

        return inputs;
    }

    /// <summary>
    /// The searched case where the extrapolation's elapsed time decides a ledge: <c>FUN_180096680</c> narrows <c>now − stepped</c> to
    /// float, so a core at 64 m/s over 10.5 − 10.4 s lands 9.5e-8 m further out than the double puts it — past a lone ledge's reach of
    /// 6.40000005, where the double's 6.4 is inside it.
    /// </summary>
    private static Dictionary<string, long[]> FloatElapsed()
    {
        Dictionary<string, long[]> inputs = VphysicsPairObjects.NewInputs(IvpPairMindistsReplay.Inputs);

        for (int side = 0; side < 2; side++)
        {
            string suffix = IvpPairMindistsReplay.Suffix(side);

            Array.Fill(inputs["kind" + suffix], IvpLedgeTreeReplay.Unused);
            inputs["kind" + suffix][0] = IvpLedgeTreeReplay.Terminal;
            inputs["radius" + suffix][0] = IvpImpactReplay.Lane(1f);
            inputs["box" + suffix][0] = 0xffffff;
            inputs["cache-rotation"][(side * 4) + 3] = IvpImpactReplay.Lane(1d);
        }

        for (int step = 0; step < IvpPairMindistsReplay.StepCount; step++)
        {
            inputs["now"][step] = IvpImpactReplay.Lane(10.5d);
            inputs["gap"][step] = IvpImpactReplay.Lane(5.40000005d);
            inputs["given-first"][step] = -1;
            inputs["given-second"][step] = -1;

            for (int side = 0; side < 2; side++)
            {
                inputs["stepped"][(step * 2) + side] = IvpImpactReplay.Lane(10.4d);
                inputs["velocity"][((step * 2) + side) * 3] = IvpImpactReplay.Lane(64f);
            }
        }

        return inputs;
    }

    /// <summary>The two objects, the pair vector and the delegator, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int PairCapacity = 0x100;

        private readonly VphysicsPairObjects _objects;
        private readonly RefreshCall _refresh;
        private readonly nint _pair;
        private readonly nint _pairElements;
        private readonly nint _delegator;

        public Native(nint module)
        {
            _objects = new VphysicsPairObjects(module);
            _refresh = VphysicsLibrary.Function<RefreshCall>(module, RefreshAddress);
            _pair = _objects.Block(0x10);
            _pairElements = _objects.Block(PairCapacity * 8);
            Marshal.WriteInt16(_pair, 0, PairCapacity);
            Marshal.WriteIntPtr(_pair, 8, _pairElements);

            nint delegatorTable = _objects.Block(8 * 4);

            Marshal.WriteIntPtr(delegatorTable, 0, Marshal.GetFunctionPointerForDelegate(_objects.Keep(new RemovedCall(Removed))));
            _delegator = _objects.Block(0x10);
            Marshal.WriteIntPtr(_delegator, 0, delegatorTable);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = IvpPairMindistsReplay.NewOutputs();

            _objects.Load(inputs);

            for (int step = 0; step < IvpPairMindistsReplay.StepCount; step++)
            {
                _objects.Events.Clear();
                _objects.Place(inputs, step);

                int givenFirst = (int)inputs["given-first"][step];
                int givenSecond = (int)inputs["given-second"][step];

                _refresh(
                    _objects.Object(0), _objects.Object(1), BitConverter.Int64BitsToDouble(inputs["gap"][step]), _pair,
                    givenFirst < 0 ? 0 : _objects.Ledge(0, givenFirst), givenSecond < 0 ? 0 : _objects.Ledge(1, givenSecond), 0, 0, _delegator);

                List<int> names = [];

                for (int index = 0; index < (ushort)Marshal.ReadInt16(_pair, 2); index++)
                {
                    names.Add(_objects.Event(0, Marshal.ReadIntPtr(_pairElements, index * 8)));
                }

                IvpPairMindistsReplay.Record(outputs, "pair", step, names);
                IvpPairMindistsReplay.Record(outputs, "event", step, _objects.Events);
                outputs["live"][step] = Marshal.ReadInt32(_objects.Environment, 0xb0);
                outputs["created"][step] = Marshal.ReadInt32(_objects.Environment, 0xb4);
                outputs["deleted"][step] = Marshal.ReadInt32(_objects.Environment, 0xb8);
            }

            for (int index = (ushort)Marshal.ReadInt16(_pair, 2) - 1; index >= 0; index--)
            {
                nint mindist = Marshal.ReadIntPtr(_pairElements, index * 8);

                Marshal.GetDelegateForFunctionPointer<DeletingDestructor>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(mindist), 0))(mindist, 1);
            }

            _objects.Unload();
            return outputs;
        }

        public void Dispose() => _objects.Dispose();

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
