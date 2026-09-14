using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s material manager and surface lookups, called in process on fabricated surfaces, records and
/// objects — the oracle for <see cref="VphysicsSurfaceProps"/> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** The manager is written with the image's own table (`1800ec560`) and each surface entry with
/// the image's material table (`1800ec528`); `SetWorldMaterialIndexTable` (`FUN_180019220`) fills the world table and
/// `GetIVPMaterial` (`FUN_1800181d0`) is the image's own, reached through a fabricated props table whose only other live slot is
/// `GetSurfaceIndex`, answered by a callback with the case's `default` index. Any other slot reached lands on a trap and is
/// counted.
///
/// **Modes.** A number sweeps that many cases (default 200,000); `list` prints the fixed rows the conformance suite pins.
/// </remarks>
public sealed class VphysicsMaterialProbe : IProbe
{
    private const long GetMaterialAddress = 0x1800181d0;
    private const long SetWorldTableAddress = 0x180019220;
    private const long MaterialAtAddress = 0x180019370;
    private const long FrictionAddress = 0x1800192e0;
    private const long ElasticityAddress = 0x1800192b0;
    private const long ManagerTableAddress = 0x1800ec560;
    private const long MaterialTableAddress = 0x1800ec528;
    private const int DefaultCases = 200_000;
    private const ulong Seed = 20260914;
    private const long PropsPointerAddress = 0x180120b30;
    private const long PropsTableAddress = 0x1800ec598;
    private const string Manifest = "scripts/surfaceproperties_manifest.txt";

    /// <summary>
    /// Texts parsed after the game's own, each for a path its files do not take: a comment and a number with trailing letters, a
    /// bare-word block with <c>base</c> after a key and a block comment, a redefinition, break characters, the shadow surface by
    /// name, a pair before a block, a text that ends inside its block, and a byte past <c>0x7f</c> outside quotes.
    /// </summary>
    private static readonly string[] EdgeTexts =
    [
        "\"Edge_One\" { \"friction\" \"0.25\" // a comment\n \"elasticity\" \"2.5e-1xyz\" }",
        "edge_two { friction .5 base edge_one density 7 /* a block */ dampening -3 }",
        "\"edge_one\" { \"thickness\" \"4\" }",
        "edge_three { friction(0.3) elasticity: 0.6 }",
        "\"$material_index_shadow\" { \"elasticity\" \"0.75\" }",
        "unwanted value edge_four { \"friction\" \"inf\" \"elasticity\" \"nan\" \"density\" \"+1e3\" }",
        "edge_five { \"friction\" \"0.9\"",
        "café_six { friction 0.4 } \"edge_seven\" { friction 0.45 }",
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParseFunction(nint props, nint fileName, nint text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountFunction(nint props);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NameFunction(nint props, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ParametersFunction(nint props, int index, nint parameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetMaterialFunction(nint props, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetWorldTableFunction(nint props, nint map, int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint MaterialAtFunction(nint manager, nint collisionObject, nint position, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double PairFunction(nint manager, nint record);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SurfaceIndexFunction(nint props, nint name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint TrapFunction(nint self);

    /// <inheritdoc />
    public string Name => "vphysics-materials";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's material manager, surface lookups and world material table called in process and compared with " +
        "VphysicsSurfaceProps; 'list' prints the rows the conformance suite pins: vphysics-materials [n | list]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        if (arguments.Count >= 1 && arguments[0] == "parse")
        {
            Parse(output, module, edgesOnly: arguments.Count >= 2 && arguments[1] == "edges");
            return;
        }

        using Native native = new(module);

        MaterialCase control = MaterialCase.Fixed([0.5f, 0.5f], [0.5f, 0.5f]);
        Answers answers = native.Evaluate(control);

        output.WriteLine(
            $"controls: 0.5 · 0.5 friction is 0.25 {answers.Friction == BitConverter.DoubleToInt64Bits(0.25d)}; " +
            $"manager slot 1 is FUN_180019370 {native.SlotIs(ManagerTableAddress, 1, MaterialAtAddress)}; " +
            $"material slot 1 is FUN_180019360 {native.SlotIs(MaterialTableAddress, 1, 0x180019360)}");

        if (arguments.Count >= 1 && arguments[0] == "list")
        {
            List(output, native);
            return;
        }

        int count = arguments.Count >= 1 ? int.Parse(arguments[0], CultureInfo.InvariantCulture) : DefaultCases;
        Draws draws = new(Seed);
        int[] differ = new int[4];
        int overridden = 0;

        for (int index = 0; index < count; index++)
        {
            MaterialCase drawn = MaterialCase.Draw(draws);
            Answers binary = native.Evaluate(drawn);
            Answers port = Port(drawn);

            differ[0] += binary.Lookups.SequenceEqual(port.Lookups) ? 0 : 1;
            differ[1] += binary.At.SequenceEqual(port.At) ? 0 : 1;
            differ[2] += binary.Friction == port.Friction ? 0 : 1;
            differ[3] += binary.Elasticity == port.Elasticity ? 0 : 1;
            overridden += port.Overridden ? 1 : 0;

            if (binary != port && differ.Sum() <= 4)
            {
                output.WriteLine($"case {index}: binary {binary}, port {port}");
            }
        }

        output.WriteLine(
            $"materials: {count} compared; GetIVPMaterial {differ[0]} differ, MaterialAt {differ[1]}, friction {differ[2]}, " +
            $"elasticity {differ[3]}; {overridden} with the friction override; {native.Traps} calls reached a trap");
    }

    /// <summary>
    /// The library's own surface-props object — the global behind <c>180120b30</c>, built when the library loaded — handed the
    /// game's surface files in its manifest's order and then <see cref="EdgeTexts"/>, beside <see cref="VphysicsSurfaceProps.ParseSurfaceData"/>
    /// on the same texts; after each, every surface's name and parameters and the shadow index are compared.
    /// </summary>
    private static void Parse(TextWriter output, nint module, bool edgesOnly)
    {
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed, so its surface files cannot be read.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);
        List<(string Name, byte[] Text)> texts = [];

        if (!edgesOnly && archives.Read(Manifest) is { } manifest)
        {
            KeyValuesReader.Read(manifest, (key, value, _) =>
            {
                if (value is not null && string.Equals(key, "file", StringComparison.OrdinalIgnoreCase) && archives.Read(value) is { } text)
                {
                    texts.Add((value, text));
                }

                return true;
            });
        }

        output.WriteLine($"{Manifest} lists {texts.Count} readable files: {string.Join(", ", texts.Select(text => text.Name))}");
        texts.AddRange(EdgeTexts.Select((edge, index) => ($"edge text {index}", Encoding.Latin1.GetBytes(edge))));

        nint props = Marshal.ReadIntPtr(VphysicsLibrary.Address(module, PropsPointerAddress));
        nint table = Marshal.ReadIntPtr(props);
        ParseFunction parse = Marshal.GetDelegateForFunctionPointer<ParseFunction>(Marshal.ReadIntPtr(table, 1 * 8));
        CountFunction count = Marshal.GetDelegateForFunctionPointer<CountFunction>(Marshal.ReadIntPtr(table, 2 * 8));
        NameFunction name = Marshal.GetDelegateForFunctionPointer<NameFunction>(Marshal.ReadIntPtr(table, 7 * 8));
        ParametersFunction parameters = Marshal.GetDelegateForFunctionPointer<ParametersFunction>(Marshal.ReadIntPtr(table, 9 * 8));

        output.WriteLine(
            $"controls: the library's props object uses table 1800ec598 {table == VphysicsLibrary.Address(module, PropsTableAddress)}; " +
            $"it holds {count(props)} surfaces before any parse");

        VphysicsSurfaceProps port = new([]);
        nint read = Marshal.AllocHGlobal(20);

        try
        {
            foreach ((string file, byte[] text) in texts)
            {
                nint fileName = Marshal.StringToHGlobalAnsi(file);
                nint buffer = Marshal.AllocHGlobal(text.Length + 1);

                try
                {
                    Marshal.Copy(text, 0, buffer, text.Length);
                    Marshal.WriteByte(buffer, text.Length, 0);
                    parse(props, fileName, buffer);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                    Marshal.FreeHGlobal(fileName);
                }

                port.ParseSurfaceData(text);

                int surfaces = count(props);
                int shadow = Marshal.ReadInt32(props, 0x1cc);
                List<string> differences = [];

                for (int index = 0; index < Math.Max(surfaces, port.Surfaces.Count); index++)
                {
                    string binaryName = index < surfaces ? Marshal.PtrToStringAnsi(name(props, index)) ?? "(null)" : "(none)";
                    string portName = index < port.Surfaces.Count ? port.Surfaces[index].Name : "(none)";
                    long[] binaryBits = [];

                    if (index < surfaces)
                    {
                        parameters(props, index, read);
                        binaryBits = [.. Enumerable.Range(0, 5).Select(lane => (long)(uint)Marshal.ReadInt32(read, lane * 4))];
                    }

                    long[] portBits = index < port.Surfaces.Count ? Bits(port.Surfaces[index].Physics) : [];

                    if (binaryName != portName || !binaryBits.SequenceEqual(portBits))
                    {
                        differences.Add(
                            $"  [{index}] binary '{binaryName}' {string.Join(" ", binaryBits.Select(bits => bits.ToString("x8", CultureInfo.InvariantCulture)))}, " +
                            $"port '{portName}' {string.Join(" ", portBits.Select(bits => bits.ToString("x8", CultureInfo.InvariantCulture)))}");
                    }
                }

                output.WriteLine(
                    $"{file}: binary {surfaces} surfaces, shadow {shadow}; port {port.Surfaces.Count}, shadow {port.ShadowSurface}; " +
                    $"{differences.Count} differ");

                foreach (string difference in differences.Take(5))
                {
                    output.WriteLine(difference);
                }
            }

            if (!edgesOnly)
            {
                foreach (string surface in (string[])["default", "flesh", "concrete", "ice", "metal"])
                {
                    SurfacePhysicsParams? physics = port.ObjectMaterial(surface)?.Physics;
                    List<string> blocks = [];

                    foreach ((string file, byte[] text) in texts.Where(text => !text.Name.StartsWith("edge", StringComparison.Ordinal)))
                    {
                        string? block = null;

                        KeyValuesReader.Read(text, (key, value, depth) =>
                        {
                            if (depth == 0)
                            {
                                block = value is null ? key : null;
                            }
                            else if (string.Equals(block, surface, StringComparison.OrdinalIgnoreCase) &&
                                     key.ToUpperInvariant() is "FRICTION" or "ELASTICITY" or "BASE")
                            {
                                blocks.Add($"{Path.GetFileName(file)} {key}={value}");
                            }

                            return true;
                        });
                    }

                    output.WriteLine(
                        $"{surface}: friction {physics?.Friction.ToString("R", CultureInfo.InvariantCulture) ?? "none"}, " +
                        $"elasticity {physics?.Elasticity.ToString("R", CultureInfo.InvariantCulture) ?? "none"}; " +
                        $"the files say [{string.Join("; ", blocks)}]");
                }
            }

            if (edgesOnly)
            {
                for (int index = 0; index < count(props); index++)
                {
                    parameters(props, index, read);
                    output.WriteLine(
                        $"  [{index}] '{Marshal.PtrToStringAnsi(name(props, index))}' " +
                        string.Join(" ", Enumerable.Range(0, 5).Select(lane => ((uint)Marshal.ReadInt32(read, lane * 4)).ToString("x8", CultureInfo.InvariantCulture))));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(read);
        }
    }

    private static long[] Bits(SurfacePhysicsParams physics) =>
    [
        (uint)BitConverter.SingleToInt32Bits(physics.Friction), (uint)BitConverter.SingleToInt32Bits(physics.Elasticity),
        (uint)BitConverter.SingleToInt32Bits(physics.Density), (uint)BitConverter.SingleToInt32Bits(physics.Thickness),
        (uint)BitConverter.SingleToInt32Bits(physics.Dampening),
    ];

    private static void List(TextWriter output, Native native)
    {
        float nan = float.NaN;

        (string Label, MaterialCase Case)[] rows =
        [
            ("product", MaterialCase.Fixed([0.3f, 0.7f], [0.3f, 0.7f])),
            ("over-one", MaterialCase.Fixed([1.5f, 0.9f], [2f, 0.9f])),
            ("negative", MaterialCase.Fixed([-0.5f, 0.5f], [-1f, 0.5f])),
            ("nan", MaterialCase.Fixed([nan, 0.5f], [nan, 0.5f])),
            ("override", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with { Offset58 = [true, false], Flag = [true, false], Normal = (1f, 0f, 0f) }),
            ("across-axis", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with { Offset58 = [true, false], Flag = [true, false], Normal = (0f, 1f, 0f) }),
            ("tiny-normal", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with { Offset58 = [true, false], Flag = [true, false], Normal = (0.005f, 0f, 0f) }),
            ("second-flagged", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with { Offset58 = [false, true], Flag = [false, true], Normal = (0.3f, 0.953939f, 0f) }),
            ("unflagged", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with { Offset58 = [true, true], Flag = [false, false], Normal = (1f, 0f, 0f) }),
            ("both-flagged", MaterialCase.Fixed([0.8f, 0.8f], [0.8f, 0.8f]) with
            {
                Offset58 = [true, true],
                Flag = [true, true],
                Normal = (1f, 0f, 0f),
                Matrices =
                [
                    IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
                    IvpMatrix.FromRotation((0f, 0f, 0.70710677f, 0.70710677f), (0d, 0d, 0d)),
                ],
            }),
        ];

        foreach ((string label, MaterialCase row) in rows)
        {
            Answers binary = native.Evaluate(row);

            output.WriteLine(
                $"{label}: friction 0x{binary.Friction:x16} ({BitConverter.Int64BitsToDouble(binary.Friction):R}), " +
                $"elasticity 0x{binary.Elasticity:x16} ({BitConverter.Int64BitsToDouble(binary.Elasticity):R}); port agrees {binary == Port(row)}");
        }

        MaterialCase lookups = MaterialCase.Fixed([0.1f, 0.2f, 0.3f], [0.1f, 0.2f, 0.3f]) with
        {
            DefaultIndex = 1,
            Shadow = 2,
            Map = [2, 0xf000, -1, 7],
            Lookups = [-1, 0, 2, 3, 0x80, 0xf000],
            At = [0, 1, 2, 3, 4, 5, 0x80, 0xf000],
        };

        Answers found = native.Evaluate(lookups);

        output.WriteLine(
            $"lookups over 3 surfaces, shadow 2, map [2, 0xf000, -1, 7], default 1: GetIVPMaterial [-1, 0, 2, 3, 0x80, 0xf000] -> " +
            $"[{string.Join(", ", found.Lookups)}]; MaterialAt [0, 1, 2, 3, 4, 5, 0x80, 0xf000] -> [{string.Join(", ", found.At)}]; " +
            $"port agrees {found == Port(lookups)}");
    }

    private static Answers Port(MaterialCase drawn)
    {
        List<VphysicsSurface> surfaces = [];

        for (int index = 0; index < drawn.Friction.Length; index++)
        {
            surfaces.Add(new VphysicsSurface(
                index == drawn.DefaultIndex ? "default" : $"surface{index}",
                new SurfacePhysicsParams(drawn.Friction[index], drawn.Elasticity[index], 0f, 0f, 0f),
                hasSecondFriction: false));
        }

        VphysicsSurfaceProps props = new(surfaces) { ShadowSurface = drawn.Shadow };

        props.SetWorldMaterialIndexTable(drawn.Map);

        IvpCollisionObject[] objects = [Object(drawn, 0), Object(drawn, 1)];
        IvpContactRecord record = new()
        {
            Normal = drawn.Normal,
            FirstObject = objects[0],
            SecondObject = objects[1],
            FirstMaterial = surfaces[drawn.First],
            SecondMaterial = surfaces[drawn.Second],
        };

        return new Answers(
            [.. drawn.Lookups.Select(index => Slot(props, props.GetIVPMaterial(index)))],
            [.. drawn.At.Select(index => Slot(props, props.MaterialAt(objects[0], index)))],
            BitConverter.DoubleToInt64Bits(props.FrictionFactor(record)),
            BitConverter.DoubleToInt64Bits(props.Elasticity(record)),
            VphysicsSurfaceProps.FrictionIsOverridden(record));
    }

    private static IvpCollisionObject Object(MaterialCase drawn, int side) =>
        new()
        {
            Core = new IvpRigidBody { HasOffset58 = drawn.Offset58[side], CoreMatrix = drawn.Matrices[side] },
            PhysicsFlag48Bit6 = drawn.Flag[side],
        };

    private static int Slot(VphysicsSurfaceProps props, IIvpMaterial? material) =>
        material is VphysicsSurface surface ? props.Surfaces.ToList().IndexOf(surface) : -1;

    /// <summary>What one case asked, and what came back: surface slots, or −1 for none, and the two doubles' bits.</summary>
    private sealed record Answers(int[] Lookups, int[] At, long Friction, long Elasticity, bool Overridden)
    {
        public bool Equals(Answers? other) =>
            other is not null && Lookups.SequenceEqual(other.Lookups) && At.SequenceEqual(other.At) &&
            Friction == other.Friction && Elasticity == other.Elasticity;

        public override int GetHashCode() => HashCode.Combine(Friction, Elasticity);

        public override string ToString() =>
            $"lookups [{string.Join(", ", Lookups)}], at [{string.Join(", ", At)}], friction 0x{Friction:x16}, elasticity 0x{Elasticity:x16}";
    }

    /// <summary>Surfaces, a world table, the indices asked, and a record between two objects.</summary>
    private sealed record MaterialCase(
        float[] Friction,
        float[] Elasticity,
        int DefaultIndex,
        int Shadow,
        int[] Map,
        int[] Lookups,
        int[] At,
        int First,
        int Second,
        (float X, float Y, float Z) Normal,
        bool[] Offset58,
        bool[] Flag,
        bool[] Physics,
        IvpMatrix[] Matrices)
    {
        private static readonly IvpMatrix Identity = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

        public static MaterialCase Fixed(float[] friction, float[] elasticity) =>
            new(friction, elasticity, 0, 0, [], [], [0], 0, 1, (0f, 0f, 1f), [false, false], [false, false], [true, true], [Identity, Identity]);

        public static MaterialCase Draw(Draws draws)
        {
            int count = 1 + draws.Below(6);
            float[] friction = [.. Enumerable.Range(0, count).Select(_ => Value(draws))];
            float[] elasticity = [.. Enumerable.Range(0, count).Select(_ => Value(draws))];
            int defaultIndex = draws.Chance(0.8) ? draws.Below(count) : -1;
            int[] map = [.. Enumerable.Range(0, draws.Chance(0.5) ? 0 : 1 + draws.Below(128))
                .Select(_ => draws.Chance(0.05) ? 0xf000 : draws.Below(count + 3) - 1)];
            int[] lookups = [.. Enumerable.Range(0, 6).Select(_ => Index(draws, count))];
            int[] at = defaultIndex < 0
                ? []
                : [.. Enumerable.Range(0, 6).Select(_ => draws.Chance(0.9) ? draws.Below(128) : Index(draws, count) & 0xffff)];
            (float X, float Y, float Z) normal = draws.Direction();

            if (draws.Chance(0.1))
            {
                normal = (normal.X * 0.005f, normal.Y * 0.005f, normal.Z * 0.005f);
            }
            else if (draws.Chance(0.03))
            {
                normal = (float.NaN, normal.Y, normal.Z);
            }

            bool[] flag = [draws.Chance(0.4), draws.Chance(0.4)];

            return new MaterialCase(
                friction,
                elasticity,
                defaultIndex,
                draws.Below(count + 2) - 1,
                map,
                lookups,
                at,
                draws.Below(count),
                draws.Below(count),
                normal,
                [draws.Chance(0.3), draws.Chance(0.3)],
                flag,
                [flag[0] || draws.Chance(0.5), flag[1] || draws.Chance(0.5)],
                [draws.Rotation(), draws.Rotation()]);
        }

        private static float Value(Draws draws) =>
            draws.Unit() switch
            {
                < 0.08 => float.NaN,
                < 0.18 => (float)-draws.Unit(),
                < 0.33 => (float)(1d + (2d * draws.Unit())),
                < 0.38 => 0f,
                _ => (float)draws.Unit(),
            };

        private static int Index(Draws draws, int count) =>
            draws.Below(4) switch
            {
                0 => draws.Below(count + 2) - 1,
                1 => 0x80 + draws.Below(16),
                2 => 0xf000,
                _ => draws.Below(128),
            };
    }

    /// <summary>A SplitMix64 stream, so a sweep is the same sweep every run.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public bool Chance(double probability) => Unit() < probability;

        public int Below(int bound) => (int)(Unit() * bound);

        public (float X, float Y, float Z) Direction()
        {
            double z = (2d * Unit()) - 1d;
            double angle = 2d * Math.PI * Unit();
            double radius = Math.Sqrt(1d - (z * z));

            return ((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)), (float)z);
        }

        public IvpMatrix Rotation()
        {
            (float x, float y, float z) = Direction();
            double half = Math.PI * Unit();
            float sine = (float)Math.Sin(half);

            return IvpMatrix.FromRotation((x * sine, y * sine, z * sine, (float)Math.Cos(half)), (0d, 0d, 0d));
        }
    }

    /// <summary>Props, the manager inside it, the surfaces, a record, two objects with their cores and physics objects.</summary>
    private sealed class Native : IDisposable
    {
        private const int PropsSize = 0x200;
        private const int ManagerOffset = 0xb0;
        private const int EntrySize = 0x78;
        private const int MostSurfaces = 8;
        private const int CoreSize = 0x270;
        private const int PropsSlots = 12;
        private const int SurfaceIndexSlot = 3;
        private const int GetMaterialSlot = 10;

        private readonly List<nint> _blocks = [];
        private readonly nint _module;
        private readonly GetMaterialFunction _getMaterial;
        private readonly SetWorldTableFunction _setWorldTable;
        private readonly MaterialAtFunction _materialAt;
        private readonly PairFunction _friction;
        private readonly PairFunction _elasticity;
        private readonly SurfaceIndexFunction _surfaceIndex;
        private readonly TrapFunction _trap;
        private readonly nint _props;
        private readonly nint _entries;
        private readonly nint _map;
        private readonly nint _record;
        private readonly nint[] _objects = new nint[2];
        private readonly nint[] _cores = new nint[2];
        private readonly nint[] _physics = new nint[2];

        private int _defaultIndex;

        public Native(nint module)
        {
            _module = module;
            _getMaterial = VphysicsLibrary.Function<GetMaterialFunction>(module, GetMaterialAddress);
            _setWorldTable = VphysicsLibrary.Function<SetWorldTableFunction>(module, SetWorldTableAddress);
            _materialAt = VphysicsLibrary.Function<MaterialAtFunction>(module, MaterialAtAddress);
            _friction = VphysicsLibrary.Function<PairFunction>(module, FrictionAddress);
            _elasticity = VphysicsLibrary.Function<PairFunction>(module, ElasticityAddress);
            _surfaceIndex = (_, _) => _defaultIndex;
            _trap = _ =>
            {
                Traps++;
                return 0;
            };

            _props = Allocate(PropsSize);
            _entries = Allocate(MostSurfaces * EntrySize);
            _map = Allocate(128 * 4);
            _record = Allocate(0x110);

            nint propsTable = Allocate(PropsSlots * 8);

            for (int slot = 0; slot < PropsSlots; slot++)
            {
                Marshal.WriteIntPtr(propsTable, slot * 8, Marshal.GetFunctionPointerForDelegate(_trap));
            }

            Marshal.WriteIntPtr(propsTable, SurfaceIndexSlot * 8, Marshal.GetFunctionPointerForDelegate(_surfaceIndex));
            Marshal.WriteIntPtr(propsTable, GetMaterialSlot * 8, VphysicsLibrary.Address(module, GetMaterialAddress));
            PropsTable = propsTable;

            for (int side = 0; side < 2; side++)
            {
                _objects[side] = Allocate(0x108);
                _cores[side] = Allocate(CoreSize);
                _physics[side] = Allocate(0x50);
            }
        }

        public int Traps { get; private set; }

        private nint PropsTable { get; }

        public bool SlotIs(long table, int slot, long function) =>
            Marshal.ReadIntPtr(VphysicsLibrary.Address(_module, table), slot * 8) == VphysicsLibrary.Address(_module, function);

        public Answers Evaluate(MaterialCase drawn)
        {
            Zero(_props, PropsSize);
            Marshal.WriteIntPtr(_props, 0, PropsTable);
            Marshal.WriteIntPtr(_props, 0x70, _entries);
            Marshal.WriteInt32(_props, 0x80, drawn.Friction.Length);
            Marshal.WriteInt32(_props, 0x1cc, drawn.Shadow);
            Marshal.WriteIntPtr(_props, ManagerOffset, VphysicsLibrary.Address(_module, ManagerTableAddress));
            Marshal.WriteIntPtr(_props, ManagerOffset + 0x10, _props);

            for (int index = 0; index < 128; index++)
            {
                Marshal.WriteInt16(_props, ManagerOffset + 0x18 + (index * 2), (short)index);
            }

            for (int index = 0; index < drawn.Map.Length; index++)
            {
                Marshal.WriteInt32(_map, index * 4, drawn.Map[index]);
            }

            _setWorldTable(_props, _map, drawn.Map.Length);
            _defaultIndex = drawn.DefaultIndex;

            for (int index = 0; index < drawn.Friction.Length; index++)
            {
                nint entry = _entries + (index * EntrySize);

                Zero(entry, EntrySize);
                Marshal.WriteIntPtr(entry, 0, VphysicsLibrary.Address(_module, MaterialTableAddress));
                Marshal.WriteInt32(entry, 0x14, BitConverter.SingleToInt32Bits(drawn.Friction[index]));
                Marshal.WriteInt32(entry, 0x18, BitConverter.SingleToInt32Bits(drawn.Elasticity[index]));
            }

            for (int side = 0; side < 2; side++)
            {
                Zero(_objects[side], 0x108);
                Zero(_cores[side], CoreSize);
                Zero(_physics[side], 0x50);
                Marshal.WriteIntPtr(_objects[side], 0xe8, _cores[side]);
                Marshal.WriteIntPtr(_objects[side], 0x100, drawn.Physics[side] ? _physics[side] : 0);
                Marshal.WriteByte(_physics[side], 0x48, (byte)(drawn.Flag[side] ? 0x40 : 0xbf));
                Marshal.WriteIntPtr(_cores[side], 0x58, drawn.Offset58[side] ? _cores[side] : 0);

                IvpMatrix matrix = drawn.Matrices[side];
                double[] terms = [matrix.M0, matrix.M1, matrix.M2, matrix.M4, matrix.M5, matrix.M6, matrix.M8, matrix.M9, matrix.M10];

                for (int term = 0; term < terms.Length; term++)
                {
                    Marshal.WriteInt64(_cores[side], 0x90 + ((term / 3) * 0x20) + ((term % 3) * 8), BitConverter.DoubleToInt64Bits(terms[term]));
                }
            }

            Zero(_record, 0x110);
            Marshal.WriteInt32(_record, 0x20, BitConverter.SingleToInt32Bits(drawn.Normal.X));
            Marshal.WriteInt32(_record, 0x24, BitConverter.SingleToInt32Bits(drawn.Normal.Y));
            Marshal.WriteInt32(_record, 0x28, BitConverter.SingleToInt32Bits(drawn.Normal.Z));
            Marshal.WriteIntPtr(_record, 0x40, _objects[0]);
            Marshal.WriteIntPtr(_record, 0x48, _objects[1]);
            Marshal.WriteIntPtr(_record, 0x60, _entries + (drawn.First * EntrySize));
            Marshal.WriteIntPtr(_record, 0x68, _entries + (drawn.Second * EntrySize));

            nint manager = _props + ManagerOffset;

            return new Answers(
                [.. drawn.Lookups.Select(index => SurfaceSlot(_getMaterial(_props, index)))],
                [.. drawn.At.Select(index => SurfaceSlot(_materialAt(manager, _objects[0], 0, index)))],
                BitConverter.DoubleToInt64Bits(_friction(manager, _record)),
                BitConverter.DoubleToInt64Bits(_elasticity(manager, _record)),
                Overridden: false);
        }

        public void Dispose()
        {
            // The props table points at these delegates until the blocks are freed.
            GC.KeepAlive(_surfaceIndex);
            GC.KeepAlive(_trap);

            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();
        }

        private int SurfaceSlot(nint material) => material == 0 ? -1 : (int)((material - _entries) / EntrySize);

        private nint Allocate(int size)
        {
            nint block = Marshal.AllocHGlobal(size);
            _blocks.Add(block);
            Zero(block, size);
            return block;
        }

        private static void Zero(nint block, int size)
        {
            for (int offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(block, offset, 0);
            }
        }
    }
}
