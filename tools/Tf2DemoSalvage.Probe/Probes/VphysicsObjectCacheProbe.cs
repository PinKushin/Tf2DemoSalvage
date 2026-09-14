using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s object cache refresh, <c>FUN_180080a60</c>, called in process on a fabricated cache, object,
/// core and environment — the oracle for <c>IvpObjectCache</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Only the fields the refresh reads are fabricated**: the cache's object (`+0xc8`); the object's environment (`+0x30`), core
/// (`+0xe8`), rotation pointer (`+0x58`), offset (`+0x60`) and flags (`+0x78`, bit `0x800` when it has no offset); the
/// environment's time (`+0x188`) and PSI (`+0x1a0`); and the core's position, previous velocity, both orientations, stamp and
/// inverse step. **A twentieth of the cases carry a NaN of one of two payloads** in one or two lanes, so the destinations the
/// refresh, the interpolation and the matrix fill `FUN_180071330` keep are all in the lanes.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpObjectCacheConformanceTests` reads.
/// </remarks>
public sealed class VphysicsObjectCacheProbe : IProbe
{
    private const long RefreshAddress = 0x180080a60;

    private const int DefaultSweep = 100_000;
    private const int FixtureCases = 400;
    private const ulong FixtureSeed = 180080;
    private const ulong SweepSeed = 20260916;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RefreshFunction(nint cache);

    /// <inheritdoc />
    public string Name => "vphysics-object-cache";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's object cache refresh (FUN_180080a60) called in process and compared with the port; 'fixture' writes the " +
        "conformance suite's cases: vphysics-object-cache [sweep n | fixture path]";

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

            writer.WriteLine("# Written by the vphysics-object-cache probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws, poison: true);

                IvpObjectCacheReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            int searched = 0;

            foreach ((string label, Dictionary<string, long[]> inputs) in Killers(draws))
            {
                IvpObjectCacheReplay.Write(writer, new IvpReplayCase(label, inputs, native.Run(inputs)));
                searched++;
            }

            output.WriteLine($"{FixtureCases} cases and {searched} searched cases written to {arguments[1]}");
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep" ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        Draws sweep = new(SweepSeed);
        int differing = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(sweep, poison: true);
            IReadOnlyList<string> differences = IvpObjectCacheReplay.Differences(native.Run(inputs), IvpObjectCacheReplay.Run(inputs));

            if (differences.Count > 0 && ++differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} object cache cases, {differing} differing");
    }

    /// <summary>Cases searched for the orders a sabotage round found random cases could not see.</summary>
    /// <remarks>
    /// **A NaN time**, which copies the core rather than interpolating it; **two NaNs of different payloads** in one position lane's
    /// velocity and position, in the object rotation's `w` and the orientation's `y`, and in its `x` and the orientation's `z` —
    /// the operand pairs whose destinations the lanes would otherwise never show; and **offsets on cores at the origin**, where no
    /// translation swallows the rounding of the offset's three terms.
    /// </remarks>
    private static IEnumerable<(string Label, Dictionary<string, long[]> Inputs)> Killers(Draws draws)
    {
        const long FirstNan = unchecked((long)0xfff8000000000001UL);
        const long SecondNan = 0x7ff8000000000002L;

        Dictionary<string, long[]> elapsed = RandomCase(draws, poison: false);
        elapsed["now"][0] = FirstNan;
        yield return ("elapsed-nan", elapsed);

        Dictionary<string, long[]> sum = Still(draws);
        sum["now"][0] = IvpImpactReplay.Lane(BitConverter.Int64BitsToDouble(sum["stepped"][0]) + (1d / 132d));
        sum["velocity"][0] = 0xffc00001L;
        sum["position"][0] = SecondNan;
        yield return ("position-sum-payloads", sum);

        Dictionary<string, long[]> product = Still(draws);
        product["has-rotation"][0] = 1;
        product["rotation"][3] = FirstNan;
        product["orientation"][1] = SecondNan;
        yield return ("compose-product-payloads", product);

        Dictionary<string, long[]> order = Still(draws);
        order["has-rotation"][0] = 1;
        order["rotation"][0] = FirstNan;
        order["orientation"][2] = SecondNan;
        yield return ("compose-order-payloads", order);

        for (int index = 0; index < 16; index++)
        {
            Dictionary<string, long[]> origin = Still(draws);

            origin["has-offset"][0] = 1;
            Array.Fill(origin["position"], IvpImpactReplay.Lane(0d));
            yield return ($"offset-at-origin-{index}", origin);
        }
    }

    /// <summary>A case with no NaN, no time elapsed, and neither an offset nor a rotation.</summary>
    private static Dictionary<string, long[]> Still(Draws draws)
    {
        Dictionary<string, long[]> inputs = RandomCase(draws, poison: false);

        inputs["now"][0] = inputs["stepped"][0];
        inputs["has-offset"][0] = 0;
        inputs["has-rotation"][0] = 0;
        return inputs;
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws, bool poison)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpObjectCacheReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        (double X, double Y, double Z, double W) orientation = draws.Rotation();
        (double X, double Y, double Z, double W) working = draws.Unit() < 0.1 ? draws.Rotation() : draws.Near(orientation);
        double stepped = draws.Unit() * 1000d;
        double roll = draws.Unit();
        double elapsed = 0d;

        if (roll >= 0.15)
        {
            elapsed = roll < 0.85 ? draws.Unit() / 66d : draws.Unit() * 0.1;
        }

        for (int lane = 0; lane < 3; lane++)
        {
            inputs["position"][lane] = IvpImpactReplay.Lane(draws.Signed() * 100d);
            inputs["velocity"][lane] = IvpImpactReplay.Lane((float)(draws.Signed() * 20d));
            inputs["offset"][lane] = IvpImpactReplay.Lane((float)(draws.Signed() * 0.5d));
        }

        Store(inputs["orientation"], orientation);
        Store(inputs["working"], working);
        Store(inputs["rotation"], draws.Rotation());

        if (poison)
        {
            draws.Poison(inputs);
        }
        inputs["stepped"][0] = IvpImpactReplay.Lane(stepped);
        inputs["inverse-step"][0] = IvpImpactReplay.Lane(draws.Unit() < 0.7 ? 66f : (float)(1d / (0.001d + (draws.Unit() * 0.1d))));
        inputs["now"][0] = IvpImpactReplay.Lane(stepped + elapsed);
        inputs["psi"][0] = (int)(draws.Unit() * 100_000);
        inputs["has-offset"][0] = draws.Unit() < 0.5 ? 1 : 0;
        inputs["has-rotation"][0] = draws.Unit() < 0.2 ? 1 : 0;

        return inputs;
    }

    private static void Store(long[] lanes, (double X, double Y, double Z, double W) rotation)
    {
        lanes[0] = IvpImpactReplay.Lane(rotation.X);
        lanes[1] = IvpImpactReplay.Lane(rotation.Y);
        lanes[2] = IvpImpactReplay.Lane(rotation.Z);
        lanes[3] = IvpImpactReplay.Lane(rotation.W);
    }

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public double Signed() => (Unit() * 2d) - 1d;

        public (double X, double Y, double Z, double W) Rotation() => Normalised((Signed(), Signed(), Signed(), Signed() + 1e-3));

        /// <summary>A twentieth of the cases get a NaN — one of two payloads — in one or two lanes of the core's or the object's fields.</summary>
        public void Poison(Dictionary<string, long[]> inputs)
        {
            if (Unit() >= 0.05)
            {
                return;
            }

            string[] doubles = ["position", "orientation", "working", "rotation"];
            string[] singles = ["velocity", "offset", "inverse-step"];
            int poisons = 1 + (int)(Unit() * 2);

            for (int poison = 0; poison < poisons; poison++)
            {
                bool payload = Unit() < 0.5;

                if (Unit() < 0.6)
                {
                    long[] lanes = inputs[doubles[(int)(Unit() * doubles.Length)]];

                    lanes[(int)(Unit() * lanes.Length)] = payload ? unchecked((long)0xfff8000000000001UL) : unchecked((long)0x7ff8000000000002UL);
                }
                else
                {
                    long[] lanes = inputs[singles[(int)(Unit() * singles.Length)]];

                    lanes[(int)(Unit() * lanes.Length)] = payload ? 0xffc00001L : 0x7fc00002L;
                }
            }
        }

        public (double X, double Y, double Z, double W) Near((double X, double Y, double Z, double W) rotation) =>
            Normalised((rotation.X + (Signed() * 0.05d), rotation.Y + (Signed() * 0.05d), rotation.Z + (Signed() * 0.05d), rotation.W + (Signed() * 0.05d)));

        private static (double X, double Y, double Z, double W) Normalised((double X, double Y, double Z, double W) q)
        {
            double length = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));

            return (q.X / length, q.Y / length, q.Z / length, q.W / length);
        }
    }

    /// <summary>The binary's cache, object, core, environment and object rotation, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int CacheSize = 0xd0;
        private const int ObjectSize = 0x100;
        private const int CoreSize = 0x300;
        private const int EnvironmentSize = 0x200;
        private const int RotationSize = 0x20;

        private readonly RefreshFunction _refresh;
        private readonly nint _cache = Marshal.AllocHGlobal(CacheSize);
        private readonly nint _object = Marshal.AllocHGlobal(ObjectSize);
        private readonly nint _core = Marshal.AllocHGlobal(CoreSize);
        private readonly nint _environment = Marshal.AllocHGlobal(EnvironmentSize);
        private readonly nint _rotation = Marshal.AllocHGlobal(RotationSize);

        public Native(nint module)
        {
            _refresh = VphysicsLibrary.Function<RefreshFunction>(module, RefreshAddress);

            foreach ((nint block, int size) in new[] { (_cache, CacheSize), (_object, ObjectSize), (_core, CoreSize), (_environment, EnvironmentSize), (_rotation, RotationSize) })
            {
                for (int offset = 0; offset < size; offset++)
                {
                    Marshal.WriteByte(block, offset, 0);
                }
            }

            Marshal.WriteIntPtr(_cache, 0xc8, _object);
            Marshal.WriteIntPtr(_object, 0x30, _environment);
            Marshal.WriteIntPtr(_object, 0xe8, _core);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Doubles(_core, 0x150, inputs["position"]);
            Singles(_core, 0x170, inputs["velocity"]);
            Doubles(_core, 0x180, inputs["orientation"]);
            Doubles(_core, 0x1a0, inputs["working"]);
            Marshal.WriteInt64(_core, 0x1d0, inputs["stepped"][0]);
            Marshal.WriteInt32(_core, 0x1d8, unchecked((int)inputs["inverse-step"][0]));
            Marshal.WriteInt64(_environment, 0x188, inputs["now"][0]);
            Marshal.WriteInt32(_environment, 0x1a0, (int)inputs["psi"][0]);
            Marshal.WriteInt32(_object, 0x78, inputs["has-offset"][0] != 0 ? 0 : 0x800);
            Singles(_object, 0x60, inputs["offset"]);
            Doubles(_rotation, 0, inputs["rotation"]);
            Marshal.WriteIntPtr(_object, 0x58, inputs["has-rotation"][0] != 0 ? _rotation : 0);

            _refresh(_cache);

            return new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["core-position"] = Read(0x0, 0x8, 0x10),
                ["cached-rotation"] = Read(0x20, 0x28, 0x30, 0x38),
                ["matrix"] = Read(0x40, 0x48, 0x50, 0x60, 0x68, 0x70, 0x80, 0x88, 0x90),
                ["translation"] = Read(0xa0, 0xa8, 0xb0),
                ["refreshed-at"] = [Marshal.ReadInt32(_cache, 0xc0)],
            };
        }

        public void Dispose()
        {
            foreach (nint block in new[] { _cache, _object, _core, _environment, _rotation })
            {
                Marshal.FreeHGlobal(block);
            }
        }

        private static void Doubles(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt64(block, offset + (lane * 8), lanes[lane]);
            }
        }

        private static void Singles(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[lane]));
            }
        }

        private long[] Read(params int[] offsets) => Array.ConvertAll(offsets, offset => Marshal.ReadInt64(_cache, offset));
    }
}
