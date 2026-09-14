using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s impact solver, <c>FUN_18008e290</c>, called in process on fabricated cores — the oracle for
/// <see cref="IvpImpactSolver"/> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** The solver, both cores, the environment, its limits and the anomaly manager are written at
/// the offsets `docs/findings/51` reads; the manager's table is the loaded image's own (`1800ebf90`), so its slots are Valve's
/// code; the two virtual calls those slots make outside the library — the object's `GetShadowController` and the game's
/// `ShouldFreezeObject` — are callbacks here, answering no shadow controller and the case's freeze answer. Any other slot the
/// binary reached would land on a trap and be counted.
///
/// **Controls first**: the image's anomaly table must hold `FUN_180017010` in slot 0, a core flagged `0x10` must present a
/// virtual mass of exactly one, and a point on a core with no spin must move at exactly the velocity given.
///
/// **Modes.** With no mode it compares the four helpers the solver calls and then sweeps whole impacts; `sweep n` sweeps `n`;
/// `fixture path` writes the replay cases `IvpImpactSolverConformanceTests` reads, drawn from a fixed seed.
///
/// **The entry modes** run a contact point through `FUN_1800908d0`, `FUN_18008db40`, `FUN_18008fca0` and `FUN_18008ed60` on a
/// fabricated contact point, record, two objects, their triangles and frames, and the environment: `entry [n]` sweeps and
/// `entry-fixture path` writes the cases `IvpImpactEntryConformanceTests` reads. The material manager's slots 1–3 and each
/// material's slots 1–2 are callbacks answering the case's fixed values, as <see cref="IvpEntryReplay"/> answers them.
/// </remarks>
public sealed class VphysicsImpactProbe : IProbe
{
    private const long SolveAddress = 0x18008e290;
    private const long PointVelocityAddress = 0x180077fa0;
    private const long UnitPushAddress = 0x180078f50;
    private const long VirtualMassAddress = 0x1800770f0;
    private const long TurnAddress = 0x180070620;
    private const long AnomalyTableAddress = 0x1800ebf90;
    private const long MaximumVelocityExceededAddress = 0x180017010;
    private const long DeriveBlockAddress = 0x180098fd0;
    private const long ToleranceBlockAddress = 0x18012d540;
    private const int TwiceToleranceOffset = 0x128;
    private const long SetMaterialsAddress = 0x1800908d0;
    private const long EstimateAddress = 0x18008db40;
    private const long PushOutAddress = 0x18008fca0;
    private const long EnterAddress = 0x18008ed60;
    private const long MaterialAxesAddress = 0x18008fe70;

    private const int DefaultSweep = 20_000;
    private const int HelperSweep = 200_000;
    private const int FixtureCases = 96;
    private const ulong FixtureSeed = 18008;
    private const ulong SweepSeed = 20260913;
    private const int SearchAttempts = 2_000_000;

    private static readonly string[] Sides = ["first-", "second-"];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SolveFunction(nint solver, nint cores, int mayHoldBack, int impacts, float pushOut);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeriveFunction(nint block, double tolerance, double gravity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PointVelocityFunction(nint core, nint arm, nint velocity, nint spin, nint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UnitPushFunction(nint core, nint arm, nint local, nint world, nint velocity, nint spin);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double VirtualMassFunction(nint core, nint arm, nint local, nint world);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TurnFunction(nint matrix, nint vector, nint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ShadowControllerFunction(nint self);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ShouldFreezeFunction(nint self, nint physicsObject);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint TrapFunction(nint self);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetMaterialsFunction(nint point, nint record);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EstimateFunction(nint point);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float PushOutFunction(nint point, nint environment);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnterFunction(nint record, nint cores, float pushOut, nint point);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MaterialAxesFunction(nint solver, nint point);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint MaterialAtFunction(nint self, nint collisionObject, nint position, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double PairFunction(nint self, nint record);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double MaterialValueFunction(nint self);

    /// <inheritdoc />
    public string Name => "vphysics-impact";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's impact solver FUN_18008e290 called in process on fabricated cores and compared with IvpImpactSolver " +
        "lane by lane, and the collision entry above it; 'fixture' and 'entry-fixture' write the conformance suites' cases: " +
        "vphysics-impact [sweep n | fixture path | entry [n] | entry-fixture path]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        SettleToleranceBlock(module);

        using NativeImpact native = new(module);

        if (!Controls(output, module, native))
        {
            return;
        }

        if (arguments.Count >= 1 && arguments[0] == "entry")
        {
            EntrySweep(output, native, arguments.Count >= 2 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep);
            return;
        }

        if (arguments.Count >= 2 && arguments[0] == "entry-fixture")
        {
            EntryFixture(output, native, arguments[1]);
            return;
        }

        if (arguments.Count >= 2 && arguments[0] == "fixture")
        {
            Fixture(output, native, arguments[1]);
            return;
        }

        int count = arguments.Count >= 2 && arguments[0] == "sweep"
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultSweep;

        Helpers(output, native);
        Sweep(output, native, count);
    }

    private static bool Controls(TextWriter output, nint module, NativeImpact native)
    {
        nint slot = Marshal.ReadIntPtr(VphysicsLibrary.Address(module, AnomalyTableAddress));
        bool table = slot == VphysicsLibrary.Address(module, MaximumVelocityExceededAddress);

        Draws draws = new(1);
        Dictionary<string, long[]> inputs = RandomCase(draws);
        inputs["first-flags"] = [0x10];

        IvpRigidBody pinned = IvpImpactReplay.Core(inputs, "first-");
        long one = BitConverter.DoubleToInt64Bits(1d);
        bool unit =
            BitConverter.DoubleToInt64Bits(native.VirtualMass(inputs, (0.25f, 0f, 0f), (0f, 1f, 0f), (0f, 1f, 0f))) == one &&
            BitConverter.DoubleToInt64Bits(pinned.VirtualMass((0.25f, 0f, 0f), (0f, 1f, 0f), (0f, 1f, 0f))) == one;

        inputs["first-spin"] = [0, 0, 0];
        (float X, float Y, float Z) given = (1.5f, -2.25f, 3.125f);
        (float X, float Y, float Z) moved = native.PointVelocity(inputs, (0.5f, 0.5f, 0.5f), given, (0f, 0f, 0f));
        bool still = moved == given;

        int settled = Marshal.ReadInt32(VphysicsLibrary.Address(module, ToleranceBlockAddress), TwiceToleranceOffset);
        bool block = settled == BitConverter.SingleToInt32Bits(IvpCollisionTolerance.TwiceToleranceMetres);

        output.WriteLine($"controls: anomaly slot 0 is FUN_180017010 {table}; a 0x10 core's virtual mass is 1 {unit}; " +
                         $"a point with no spin moves at the velocity {still}; block[0x4a] is the port's 0x{settled:x8} {block}");

        if (table && unit && still && block)
        {
            return true;
        }

        output.WriteLine("A control failed: these addresses or offsets do not fit this build, so nothing else is reported.");
        return false;
    }

    /// <summary>
    /// Leaves the image's tolerance block as a running environment reads it: <c>FUN_180098fd0</c> run as the environment
    /// constructor runs it, then again on its own margin as <c>SetGravity</c> runs it.
    /// </summary>
    /// <remarks>
    /// **The loaded library holds the load-time block** (`FUN_180002540`, `d = 0.01`) until an environment is built, and none is
    /// built here. The first sweep differed on every impact by exactly `1.2 · (0.02 − block[0x4a])` in the separating speed for
    /// that reason, with the four helpers already agreeing on 200,000 calls each. The constructor passes `9.81f` widened; the
    /// client's `SetGravity` passes the length of `800 · 0.0254f`, which the solver never reads.
    /// </remarks>
    private static void SettleToleranceBlock(nint module)
    {
        DeriveFunction derive = VphysicsLibrary.Function<DeriveFunction>(module, DeriveBlockAddress);
        nint block = VphysicsLibrary.Address(module, ToleranceBlockAddress);

        derive(block, IvpCollisionTolerance.Metres, 9.81f);

        float margin = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, 4));

        derive(block, margin, IvpTransform.MetresPerInch * 800f);
    }

    /// <summary>The four routines the solver calls most, compared alone, so a whole-impact difference can be placed.</summary>
    private static void Helpers(TextWriter output, NativeImpact native)
    {
        Draws draws = new(SweepSeed);
        int[] differ = new int[4];

        for (int index = 0; index < HelperSweep; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            IvpRigidBody core = IvpImpactReplay.Core(inputs, "first-");
            (float X, float Y, float Z) arm = Arm(draws);
            (float X, float Y, float Z) velocity = Cube(draws, 12f);
            (float X, float Y, float Z) spin = Cube(draws, 15f);
            (float X, float Y, float Z) world = draws.Direction();
            (float X, float Y, float Z) local = draws.Direction();

            if (native.PointVelocity(inputs, arm, velocity, spin) != core.PointVelocity(arm, velocity, spin))
            {
                differ[0]++;
            }

            if (native.UnitPush(inputs, arm, local, world) != core.UnitPush(arm, local, world))
            {
                differ[1]++;
            }

            if (BitConverter.DoubleToInt64Bits(native.VirtualMass(inputs, arm, local, world)) !=
                BitConverter.DoubleToInt64Bits(core.VirtualMass(arm, local, world)))
            {
                differ[2]++;
            }

            if (native.Turn(inputs, world) != core.CoreMatrix.RotateInverseNarrowed(world))
            {
                differ[3]++;
            }
        }

        output.WriteLine(
            $"helpers over {HelperSweep}: FUN_180077fa0 {differ[0]} differ, FUN_180078f50 {differ[1]}, " +
            $"FUN_1800770f0 {differ[2]}, FUN_180070620 {differ[3]}");
    }

    private static void Sweep(TextWriter output, NativeImpact native, int count)
    {
        Draws draws = new(SweepSeed);
        int differing = 0;
        int heldBack = 0;
        int frozen = 0;
        int approaching = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            Dictionary<string, long[]> binary = native.Solve(inputs);
            IReadOnlyList<string> differences = IvpImpactReplay.Differences(binary, IvpImpactReplay.Run(inputs));

            heldBack += binary["counters"][1] > 0 ? 1 : 0;
            frozen += binary["counters"][2] > 0 ? 1 : 0;
            approaching += Approaching(inputs, binary) ? 1 : 0;

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            if (differing <= 4)
            {
                Print(output, index, differences);
            }
        }

        output.WriteLine(
            $"impacts: {count} compared, {differing} differ; {approaching} approaching, {heldBack} held back, {frozen} frozen; " +
            $"{native.Traps} calls reached a trap");

        int nanDiffering = 0;

        for (int index = 0; index < count / 10; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            inputs[index % 2 == 0 ? "first-velocity" : "second-spin"][index % 3] = IvpImpactReplay.Lane(float.NaN);
            nanDiffering += IvpImpactReplay.Differences(native.Solve(inputs), IvpImpactReplay.Run(inputs)).Count > 0 ? 1 : 0;
        }

        output.WriteLine($"with one NaN velocity or spin lane: {count / 10} compared, {nanDiffering} differ");
    }

    /// <summary>
    /// Cases the draws miss, found through the port's instruments: the push loop at its cap, a heavier core closing between
    /// the hold-back share and <c>−0.8</c>, a response small enough that the stiffness term sets the impulse, and a NaN.
    /// </summary>
    private static List<(string Label, Dictionary<string, long[]> Inputs)> Targeted(TextWriter output)
    {
        Draws draws = new(FixtureSeed + 1);
        List<(string, Dictionary<string, long[]>)> found = [];

        Dictionary<string, long[]>? cap = Search(draws, Stress, solver => solver.Pushes == 100);
        Dictionary<string, long[]>? held = Search(draws, RandomCase, solver =>
            solver.HoldBackSpeed >= solver.SeparationSpeed * -0.8333333f && solver.HoldBackSpeed < solver.SeparationSpeed * -0.8f);

        output.WriteLine($"targeted: push cap found {cap is not null}; hold-back band found {held is not null}");

        if (cap is not null)
        {
            found.Add(("cap", cap));
        }

        if (held is not null)
        {
            found.Add(("holdback", held));
        }

        // At rest, so the pair separates and the separating push divides by the response plus the stiffness term.
        Dictionary<string, long[]> stiff = RandomCase(draws);
        stiff["p5"] = [IvpImpactReplay.Lane(1f)];

        foreach (string side in Sides)
        {
            stiff[side + "velocity"] = Lanes(default);
            stiff[side + "spin"] = Lanes(default);
            stiff[side + "pending-velocity"] = Lanes(default);
            stiff[side + "pending-spin"] = Lanes(default);
            stiff[side + "flags"] = [0];
            stiff[side + "inverse-mass"] = [IvpImpactReplay.Lane(1e-15f)];
            stiff[side + "inverse-inertia"] = Lanes((1e-15f, 1e-15f, 1e-15f));
        }

        Dictionary<string, long[]> nan = RandomCase(draws);
        nan["first-velocity"][0] = IvpImpactReplay.Lane(float.NaN);

        found.Add(("stiffness", stiff));
        found.Add(("nan", nan));

        return found;
    }

    private static Dictionary<string, long[]>? Search(
        Draws draws, Func<Draws, Dictionary<string, long[]>> draw, Func<IvpImpactSolver, bool> wanted)
    {
        for (int attempt = 0; attempt < SearchAttempts; attempt++)
        {
            Dictionary<string, long[]> inputs = draw(draws);

            if (wanted(IvpImpactReplay.Solve(inputs).Solver))
            {
                return inputs;
            }
        }

        return null;
    }

    /// <summary>A draw skewed to long arms, light cores and a wide cone, where a push mostly turns a core instead of parting it.</summary>
    private static Dictionary<string, long[]> Stress(Draws draws)
    {
        Dictionary<string, long[]> inputs = RandomCase(draws);
        (float cosine, float tangent) = Cone(draws.Between(0f, 1f), draws.Between(1f, 3f));

        inputs["cone"] = [IvpImpactReplay.Lane(cosine), IvpImpactReplay.Lane(tangent)];

        foreach (string side in Sides)
        {
            inputs[side + "arm"] = Lanes(Cube(draws, 5f));
            inputs[side + "inverse-mass"] = [IvpImpactReplay.Lane(draws.Between(0.001f, 0.05f))];
            inputs[side + "inverse-inertia"] = Lanes((draws.Between(10f, 200f), draws.Between(10f, 200f), draws.Between(10f, 200f)));
        }

        return inputs;
    }

    private static void Fixture(TextWriter output, NativeImpact native, string path)
    {
        Draws draws = new(FixtureSeed);
        List<IvpReplayCase> cases = [];
        int differing = 0;

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            Dictionary<string, long[]> binary = native.Solve(inputs);

            differing += IvpImpactReplay.Differences(binary, IvpImpactReplay.Run(inputs)).Count > 0 ? 1 : 0;
            cases.Add(new IvpReplayCase(index.ToString("d3", CultureInfo.InvariantCulture), inputs, binary));
        }

        foreach ((string label, Dictionary<string, long[]> inputs) in Targeted(output))
        {
            Dictionary<string, long[]> binary = native.Solve(inputs);

            differing += IvpImpactReplay.Differences(binary, IvpImpactReplay.Run(inputs)).Count > 0 ? 1 : 0;
            cases.Add(new IvpReplayCase(label, inputs, binary));
        }

        using (StreamWriter writer = File.CreateText(path))
        {
            writer.WriteLine("# FUN_18008e290 in the game's x64 vphysics.dll, called in process by the vphysics-impact probe.");
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"# {cases.Count} cases, {FixtureCases} drawn from seed {FixtureSeed} and the rest targeted, written {DateTime.UtcNow:yyyy-MM-dd}; every lane is the bits the binary left."));

            foreach (IvpReplayCase replay in cases)
            {
                IvpImpactReplay.Write(writer, replay);
            }
        }

        output.WriteLine($"wrote {cases.Count} cases to {path}; the port differs on {differing}; {native.Traps} calls reached a trap");
    }

    private static void Print(TextWriter output, int index, IReadOnlyList<string> differences)
    {
        output.WriteLine($"case {index} differs on {differences.Count} lanes:");

        for (int line = 0; line < Math.Min(differences.Count, 12); line++)
        {
            output.WriteLine($"  {differences[line]}");
        }
    }

    private static void EntrySweep(TextWriter output, NativeImpact native, int count)
    {
        Draws draws = new(SweepSeed);
        long noEstimate = IvpImpactReplay.Lane(1e20f);
        int differing = 0;
        int estimated = 0;
        int staticSecond = 0;
        int axes = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomEntry(draws);
            Dictionary<string, long[]> binary = native.Entry(inputs);
            IReadOnlyList<string> differences = IvpEntryReplay.Differences(binary, IvpEntryReplay.Run(inputs));

            estimated += binary["estimate"][1] != noEstimate ? 1 : 0;
            staticSecond += inputs["movable"][1] == 0 ? 1 : 0;
            axes += inputs["uses-material-axes"][0] != 0 ? 1 : 0;

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            if (differing <= 4)
            {
                Print(output, index, differences);
            }
        }

        output.WriteLine(
            $"entries: {count} compared, {differing} differ; {estimated} estimated, {staticSecond} with a static second core, " +
            $"{axes} asking for the materials' axes; {native.Traps} calls reached a trap");
    }

    private static void EntryFixture(TextWriter output, NativeImpact native, string path)
    {
        Draws draws = new(FixtureSeed);
        List<(string Label, Dictionary<string, long[]> Inputs)> drawn = [];

        for (int index = 0; index < FixtureCases; index++)
        {
            drawn.Add((index.ToString("d3", CultureInfo.InvariantCulture), RandomEntry(draws)));
        }

        // A material axis along the normal: the axis is exactly zero, so both blocks skip on their length.
        Dictionary<string, long[]> along = RandomEntry(draws);

        along["uses-material-axes"] = [1];
        along["material-has-second"] = [1, 1, 1];
        along["first-offset58"] = [0];
        along["second-offset58"] = [0];
        along["normal"] = Lanes((0f, 0f, 1f));

        foreach (string side in Sides)
        {
            along[side + "frame"] = MatrixLanes(new IvpMatrix(0d, 1d, 0d, 0d, 0d, 1d, 1d, 0d, 0d, (0d, 0d, 0d)));
        }

        drawn.Add(("axis-along-normal", along));

        // Cones along a material's axis whose fourth-order term rounds apart when its product is regrouped — `(x²·(1/24f))·x²`
        // against `(x²·x²)·(1/24f)` — as far as FUN_18008fe70's tangent, which no drawn case reached. The axis is the frame's x
        // across a z normal, so its length is exactly one and only the first material's block runs; the search only selects.
        int regrouped = 0;

        for (int attempt = 0; attempt < SearchAttempts && regrouped < 4; attempt++)
        {
            Dictionary<string, long[]> candidate = RandomEntry(draws);

            candidate["normal"] = Lanes((0f, 0f, 1f));
            candidate["first-frame"] = MatrixLanes(IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)));
            candidate["material-indices"] = [0, 0];
            candidate["material-has-second"] = [1, 0, 0];

            double axisFriction = BitConverter.Int64BitsToDouble(candidate["material-friction"][1]) *
                                  BitConverter.Int64BitsToDouble(candidate["material-second-friction"][0]);
            double friction = (float)BitConverter.Int64BitsToDouble(candidate["manager"][0]);
            float elasticity = (float)BitConverter.Int64BitsToDouble(candidate["manager"][1]);
            double tangent = (Math.Sqrt(elasticity) + 1d) * (float)(friction - (friction - axisFriction));
            float angle = (float)IvpMath.Atan(tangent);
            float squared = angle * angle;
            float grouped = (1f - (squared * 0.5f)) + (squared * (1f / 24f) * squared);
            float regroupedCosine = (1f - (squared * 0.5f)) + (squared * squared * (1f / 24f));

            if (BitConverter.SingleToInt32Bits((float)(grouped * tangent)) ==
                BitConverter.SingleToInt32Bits((float)(regroupedCosine * tangent)))
            {
                continue;
            }

            drawn.Add(($"regrouped-cone-{regrouped}", candidate));
            regrouped++;
        }

        List<IvpReplayCase> cases = [];
        int differing = 0;

        foreach ((string label, Dictionary<string, long[]> inputs) in drawn)
        {
            Dictionary<string, long[]> binary = native.Entry(inputs);

            differing += IvpEntryReplay.Differences(binary, IvpEntryReplay.Run(inputs)).Count > 0 ? 1 : 0;
            cases.Add(new IvpReplayCase(label, inputs, binary));
        }

        using (StreamWriter writer = File.CreateText(path))
        {
            writer.WriteLine(
                "# FUN_1800908d0, FUN_18008db40, FUN_18008fca0 and FUN_18008ed60 in the game's x64 vphysics.dll, called in process by the vphysics-impact probe.");
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"# {cases.Count} cases, {FixtureCases} drawn from seed {FixtureSeed} and the rest targeted, written {DateTime.UtcNow:yyyy-MM-dd}; every output lane is the bits the binary left."));

            foreach (IvpReplayCase replay in cases)
            {
                IvpEntryReplay.Write(writer, replay);
            }
        }

        output.WriteLine($"wrote {cases.Count} cases to {path}; the port differs on {differing}; {native.Traps} calls reached a trap");
    }

    /// <summary>An impact's lanes plus a contact point, its record, three materials and each object's radius and frame.</summary>
    private static Dictionary<string, long[]> RandomEntry(Draws draws)
    {
        Dictionary<string, long[]> inputs = RandomCase(draws);
        bool firstMovable = !draws.Chance(0.2);
        bool secondMovable = !firstMovable || !draws.Chance(0.25);

        inputs["step"] = [IvpImpactReplay.Lane(draws.Chance(0.5) ? 0.015d : 1d / 66d)];
        inputs["gap"] = [IvpImpactReplay.Lane(draws.Chance(0.05) ? float.NaN : draws.Between(0f, 0.2f))];
        inputs["kinds"] = [draws.Below(4), draws.Below(4)];
        inputs["uses-material-axes"] = [draws.Chance(0.5) ? 1 : 0];
        inputs["material-indices"] = [draws.Chance(0.7) ? 0 : 1 + draws.Below(127), draws.Chance(0.7) ? 0 : 1 + draws.Below(127)];
        inputs["turns"] = [.. Lanes(Cube(draws, 0.6f)), .. Lanes(Cube(draws, 0.6f))];
        inputs["movable"] = [firstMovable ? 1 : 0, secondMovable ? 1 : 0];
        inputs["impacts"] = [draws.Below(16)];
        inputs["material-friction"] = [Factor(draws, 1.5f), Factor(draws, 1.5f), Factor(draws, 1.5f)];
        inputs["material-second-friction"] = [Factor(draws, 1.5f), Factor(draws, 1.5f), Factor(draws, 1.5f)];
        inputs["material-has-second"] = [draws.Chance(0.5) ? 1 : 0, draws.Chance(0.5) ? 1 : 0, draws.Chance(0.5) ? 1 : 0];
        inputs["manager"] = [Factor(draws, 1.5f), Factor(draws, 1f)];

        foreach (string side in Sides)
        {
            inputs[side + "radius"] = [IvpImpactReplay.Lane(draws.Between(0.01f, 1f))];
            inputs[side + "frame"] = MatrixLanes(IvpMatrix.FromRotation(draws.Rotation(), (0d, 0d, 0d)));
        }

        return inputs;
    }

    private static long Factor(Draws draws, float most) => IvpImpactReplay.Lane((double)draws.Between(0f, most));

    private static bool Approaching(Dictionary<string, long[]> inputs, Dictionary<string, long[]> outputs)
    {
        long[] normal = inputs["normal"];
        long[] push = outputs["push"];

        return push[0] == (normal[0] ^ 0x80000000L) && push[1] == (normal[1] ^ 0x80000000L) && push[2] == (normal[2] ^ 0x80000000L);
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal)
        {
            ["p3"] = [draws.Chance(0.85) ? 1 : 0],
            ["p4"] = [draws.Below(16)],
            ["p5"] = [IvpImpactReplay.Lane(draws.Chance(0.2) ? 0f : draws.Between(0f, 2f))],
            ["max-velocity"] = [IvpImpactReplay.Lane(draws.Chance(0.7) ? IvpTransform.MetresPerInch * 2000f : draws.Between(0.5f, 60f))],
            ["max-collisions"] = [draws.Chance(0.6) ? 6 : draws.Below(10)],
            ["max-spin"] = [IvpImpactReplay.Lane(draws.Chance(0.6) ? 3600f * 0.017453292f * (float)(1d / 66d) : draws.Between(0.05f, 2f))],
            ["inverse-step"] = [IvpImpactReplay.Lane(draws.Chance(0.5) ? 1d / 0.015 : 66d)],
            ["freezes"] = [draws.Chance(0.5) ? 1 : 0],
        };

        float elasticity = draws.Between(0f, 1f);
        (float cosine, float tangent) = draws.Chance(0.1) ? (1f, 0f) : Cone(elasticity, draws.Between(0f, 1.5f));
        bool axis = draws.Chance(0.25);

        inputs["elasticity"] = [IvpImpactReplay.Lane(elasticity)];
        inputs["cone"] = [IvpImpactReplay.Lane(cosine), IvpImpactReplay.Lane(tangent)];
        inputs["uses-axis"] = [axis ? 1 : 0];
        inputs["axis-tangent"] = [IvpImpactReplay.Lane(axis ? draws.Between(0f, 1f) : 0f)];
        inputs["axis"] = Lanes(axis ? draws.Direction() : default);
        inputs["normal"] = Lanes(draws.Direction());
        inputs["first-arm"] = Lanes(Arm(draws));
        inputs["second-arm"] = Lanes(Arm(draws));

        bool bothStatic = draws.Chance(0.02);

        RandomCore(inputs, "first-", draws, bothStatic || draws.Chance(0.15));
        RandomCore(inputs, "second-", draws, bothStatic || draws.Chance(0.1));

        return inputs;
    }

    /// <summary>A cone as the entry builds one: <c>t = (√e + 1)·friction</c>, and the fourth-order cosine of its arctangent.</summary>
    private static (float Cosine, float Tangent) Cone(float elasticity, float friction)
    {
        double tangent = (Math.Sqrt(elasticity) + 1d) * friction;
        float angle = (float)IvpMath.Atan(tangent);
        float squared = angle * angle;
        float cosine = (1f - (squared * 0.5f)) + (squared * (1f / 24f) * squared);

        return (cosine, (float)(cosine * tangent));
    }

    private static void RandomCore(Dictionary<string, long[]> inputs, string side, Draws draws, bool immovable)
    {
        int flags = (immovable ? 0x2 : 0) | (draws.Chance(0.06) ? 0x10 : 0) | (draws.Chance(0.1) ? draws.Below(4) << 6 : 0);
        float speed = draws.Chance(0.15) ? 90f : 12f;
        float turn = draws.Chance(0.15) ? 150f : 15f;

        inputs[side + "flags"] = [flags];
        inputs[side + "collisions"] = [draws.Below(9)];
        inputs[side + "offset08"] = [IvpImpactReplay.Lane(draws.Chance(0.5) ? 0f : draws.Between(-1f, 1f))];
        inputs[side + "offset58"] = [draws.Chance(0.1) ? 1 : 0];
        inputs[side + "inverse-inertia"] = Lanes((draws.Between(0.05f, 12f), draws.Between(0.05f, 12f), draws.Between(0.05f, 12f)));
        inputs[side + "inverse-mass"] = [IvpImpactReplay.Lane(draws.Between(0.01f, 1.5f))];
        inputs[side + "velocity"] = Lanes(Cube(draws, speed));
        inputs[side + "spin"] = Lanes(Cube(draws, turn));
        inputs[side + "pending-velocity"] = Lanes(draws.Chance(0.3) ? Cube(draws, 2f) : default);
        inputs[side + "pending-spin"] = Lanes(draws.Chance(0.3) ? Cube(draws, 2f) : default);

        inputs[side + "matrix"] = MatrixLanes(IvpMatrix.FromRotation(draws.Rotation(), (0d, 0d, 0d)));
    }

    private static long[] MatrixLanes(IvpMatrix matrix) =>
    [
        IvpImpactReplay.Lane(matrix.M0), IvpImpactReplay.Lane(matrix.M1), IvpImpactReplay.Lane(matrix.M2),
        IvpImpactReplay.Lane(matrix.M4), IvpImpactReplay.Lane(matrix.M5), IvpImpactReplay.Lane(matrix.M6),
        IvpImpactReplay.Lane(matrix.M8), IvpImpactReplay.Lane(matrix.M9), IvpImpactReplay.Lane(matrix.M10),
    ];

    private static (float X, float Y, float Z) Arm(Draws draws) =>
        (draws.Between(-0.6f, 0.6f), draws.Between(-0.6f, 0.6f), draws.Between(-0.6f, 0.6f));

    private static (float X, float Y, float Z) Cube(Draws draws, float reach) =>
        (draws.Between(-reach, reach), draws.Between(-reach, reach), draws.Between(-reach, reach));

    private static long[] Lanes((float X, float Y, float Z) vector) =>
        [IvpImpactReplay.Lane(vector.X), IvpImpactReplay.Lane(vector.Y), IvpImpactReplay.Lane(vector.Z)];

    /// <summary>A reproducible stream of draws.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Next() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public bool Chance(double probability) => Next() < probability;

        public int Below(int bound) => (int)(Next() * bound);

        public float Between(float low, float high) => (float)(low + ((high - low) * Next()));

        public (float X, float Y, float Z) Direction()
        {
            while (true)
            {
                (float x, float y, float z) = (Between(-1f, 1f), Between(-1f, 1f), Between(-1f, 1f));
                float squared = (x * x) + (y * y) + (z * z);

                if (squared > 1e-4f && squared <= 1f)
                {
                    float length = MathF.Sqrt(squared);
                    return (x / length, y / length, z / length);
                }
            }
        }

        public (float X, float Y, float Z, float W) Rotation()
        {
            (float x, float y, float z, float w) = (Between(-1f, 1f), Between(-1f, 1f), Between(-1f, 1f), Between(-1f, 1f));
            float length = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

            return length > 1e-3f ? (x / length, y / length, z / length, w / length) : (0f, 0f, 0f, 1f);
        }
    }

    /// <summary>The solver, two cores, the environment and the anomaly manager, in unmanaged memory at the engine's offsets.</summary>
    private sealed class NativeImpact : IDisposable
    {
        private const int SolverSize = 0x150;
        private const int CoreSize = 0x270;
        private const int EnvironmentSize = 0x200;
        private const int LimitsSize = 0x40;
        private const int VectorSize = 12;
        private const int PhysicsSlots = 80;
        private const int GameSlots = 5;
        private const int ShadowControllerSlot = 70;
        private const int ShouldFreezeObjectSlot = 2;
        private const int PointSize = 0xd0;
        private const int RecordSize = 0x110;
        private const int ObjectSize = 0x100;
        private const int MaterialSize = 0x10;
        private const int TriangleSize = 16;
        private const int SynapseSize = 0x28;

        private readonly List<nint> _blocks = [];
        private readonly ShadowControllerFunction _shadow;
        private readonly ShouldFreezeFunction _freeze;
        private readonly TrapFunction _trap;
        private readonly SolveFunction _solve;
        private readonly PointVelocityFunction _pointVelocity;
        private readonly UnitPushFunction _unitPush;
        private readonly VirtualMassFunction _virtualMass;
        private readonly TurnFunction _turn;

        private readonly nint _solver;
        private readonly nint _first;
        private readonly nint _second;
        private readonly nint _cores;
        private readonly nint _environment;
        private readonly nint _limits;
        private readonly nint _normal;
        private readonly nint _firstArm;
        private readonly nint _secondArm;
        private readonly nint _output;
        private readonly nint _scratchA;
        private readonly nint _scratchB;
        private readonly nint _scratchC;
        private readonly nint _scratchD;
        private readonly nint _scratchE;

        private readonly SetMaterialsFunction _setMaterials;
        private readonly EstimateFunction _estimate;
        private readonly PushOutFunction _pushOut;
        private readonly EnterFunction _enter;
        private readonly MaterialAxesFunction _materialAxes;
        private readonly MaterialAtFunction _materialAt;
        private readonly PairFunction _pairFriction;
        private readonly PairFunction _pairElasticity;
        private readonly MaterialValueFunction _materialFriction;
        private readonly MaterialValueFunction _materialSecondFriction;

        private readonly nint _point;
        private readonly nint _record;
        private readonly nint _materialManager;
        private readonly nint[] _objects = new nint[2];
        private readonly nint[] _frames = new nint[2];
        private readonly nint[] _triangles = new nint[2];
        private readonly nint[] _materials = new nint[3];
        private readonly double[] _materialFrictions = new double[3];
        private readonly double[] _materialSecondFrictions = new double[3];

        private bool _freezes;
        private double _managerFriction;
        private double _managerElasticity;

        public NativeImpact(nint module)
        {
            _shadow = _ => 0;
            _freeze = (_, _) => _freezes ? (byte)1 : (byte)0;
            _trap = _ =>
            {
                Traps++;
                return 0;
            };

            _solve = VphysicsLibrary.Function<SolveFunction>(module, SolveAddress);
            _pointVelocity = VphysicsLibrary.Function<PointVelocityFunction>(module, PointVelocityAddress);
            _unitPush = VphysicsLibrary.Function<UnitPushFunction>(module, UnitPushAddress);
            _virtualMass = VphysicsLibrary.Function<VirtualMassFunction>(module, VirtualMassAddress);
            _turn = VphysicsLibrary.Function<TurnFunction>(module, TurnAddress);

            _solver = Allocate(SolverSize);
            _first = Allocate(CoreSize);
            _second = Allocate(CoreSize);
            _cores = Allocate(16);
            _environment = Allocate(EnvironmentSize);
            _limits = Allocate(LimitsSize);
            _normal = Allocate(VectorSize);
            _firstArm = Allocate(VectorSize);
            _secondArm = Allocate(VectorSize);
            _output = Allocate(VectorSize);
            _scratchA = Allocate(VectorSize);
            _scratchB = Allocate(VectorSize);
            _scratchC = Allocate(VectorSize);
            _scratchD = Allocate(VectorSize);
            _scratchE = Allocate(VectorSize);

            nint manager = Allocate(0x20);
            nint gameSolver = Allocate(8);
            nint gameTable = Allocate(GameSlots * 8);
            nint physicsObject = Allocate(8);
            nint physicsTable = Allocate(PhysicsSlots * 8);
            nint objectBlock = Allocate(0x108);
            nint objects = Allocate(8);
            nint trap = Marshal.GetFunctionPointerForDelegate(_trap);

            Marshal.WriteIntPtr(manager, 0, VphysicsLibrary.Address(module, AnomalyTableAddress));
            Marshal.WriteIntPtr(manager, 0x10, gameSolver);
            Marshal.WriteIntPtr(gameSolver, 0, gameTable);

            for (int slot = 0; slot < GameSlots; slot++)
            {
                Marshal.WriteIntPtr(gameTable, slot * 8, trap);
            }

            Marshal.WriteIntPtr(gameTable, ShouldFreezeObjectSlot * 8, Marshal.GetFunctionPointerForDelegate(_freeze));
            Marshal.WriteIntPtr(physicsObject, 0, physicsTable);

            for (int slot = 0; slot < PhysicsSlots; slot++)
            {
                Marshal.WriteIntPtr(physicsTable, slot * 8, trap);
            }

            Marshal.WriteIntPtr(physicsTable, ShadowControllerSlot * 8, Marshal.GetFunctionPointerForDelegate(_shadow));
            Marshal.WriteIntPtr(objectBlock, 0x100, physicsObject);
            Marshal.WriteIntPtr(objects, 0, objectBlock);

            Manager = manager;
            Objects = objects;

            _setMaterials = VphysicsLibrary.Function<SetMaterialsFunction>(module, SetMaterialsAddress);
            _estimate = VphysicsLibrary.Function<EstimateFunction>(module, EstimateAddress);
            _pushOut = VphysicsLibrary.Function<PushOutFunction>(module, PushOutAddress);
            _enter = VphysicsLibrary.Function<EnterFunction>(module, EnterAddress);
            _materialAxes = VphysicsLibrary.Function<MaterialAxesFunction>(module, MaterialAxesAddress);
            _materialAt = (_, _, _, _) => _materials[2];
            _pairFriction = (_, _) => _managerFriction;
            _pairElasticity = (_, _) => _managerElasticity;
            _materialFriction = self => _materialFrictions[Array.IndexOf(_materials, self)];
            _materialSecondFriction = self => _materialSecondFrictions[Array.IndexOf(_materials, self)];

            _point = Allocate(PointSize);
            _record = Allocate(RecordSize);
            _materialManager = Allocate(8);

            nint managerTable = Allocate(4 * 8);
            nint materialTable = Allocate(3 * 8);

            Marshal.WriteIntPtr(_materialManager, 0, managerTable);
            Marshal.WriteIntPtr(managerTable, 0, trap);
            Marshal.WriteIntPtr(managerTable, 0x8, Marshal.GetFunctionPointerForDelegate(_materialAt));
            Marshal.WriteIntPtr(managerTable, 0x10, Marshal.GetFunctionPointerForDelegate(_pairFriction));
            Marshal.WriteIntPtr(managerTable, 0x18, Marshal.GetFunctionPointerForDelegate(_pairElasticity));
            Marshal.WriteIntPtr(materialTable, 0, trap);
            Marshal.WriteIntPtr(materialTable, 0x8, Marshal.GetFunctionPointerForDelegate(_materialFriction));
            Marshal.WriteIntPtr(materialTable, 0x10, Marshal.GetFunctionPointerForDelegate(_materialSecondFriction));

            for (int index = 0; index < _materials.Length; index++)
            {
                _materials[index] = Allocate(MaterialSize);
                Marshal.WriteIntPtr(_materials[index], 0, materialTable);
            }

            for (int side = 0; side < _objects.Length; side++)
            {
                _objects[side] = Allocate(ObjectSize);
                _frames[side] = Allocate(CoreSize);
                _triangles[side] = Allocate(TriangleSize);
            }
        }

        public int Traps { get; private set; }

        private nint Manager { get; }

        private nint Objects { get; }

        public Dictionary<string, long[]> Solve(Dictionary<string, long[]> inputs)
        {
            Zero(_solver, SolverSize);
            Zero(_cores, 16);
            Zero(_environment, EnvironmentSize);
            Zero(_limits, LimitsSize);
            Zero(_output, VectorSize);

            WriteCore(_first, inputs, "first-");
            WriteCore(_second, inputs, "second-");

            WriteEnvironment(inputs);

            WriteLanes(_normal, 0, inputs["normal"]);
            WriteLanes(_firstArm, 0, inputs["first-arm"]);
            WriteLanes(_secondArm, 0, inputs["second-arm"]);

            Marshal.WriteIntPtr(_solver, 0x110, _first);
            Marshal.WriteIntPtr(_solver, 0x118, _second);
            Marshal.WriteIntPtr(_solver, 0x120, _firstArm);
            Marshal.WriteIntPtr(_solver, 0x128, _secondArm);
            Marshal.WriteIntPtr(_solver, 0x140, _normal);
            Marshal.WriteIntPtr(_solver, 0x148, _output);
            Marshal.WriteInt32(_solver, 0x130, (int)inputs["elasticity"][0]);
            WriteLanes(_solver, 0x134, inputs["cone"]);
            Marshal.WriteInt32(_solver, 0xf0, (int)inputs["uses-axis"][0]);
            Marshal.WriteInt32(_solver, 0xf4, (int)inputs["axis-tangent"][0]);
            WriteLanes(_solver, 0x100, inputs["axis"]);

            _solve(
                _solver,
                _cores,
                (int)inputs["p3"][0],
                (int)inputs["p4"][0],
                BitConverter.Int32BitsToSingle((int)inputs["p5"][0]));

            Dictionary<string, long[]> outputs = new(StringComparer.Ordinal)
            {
                ["separation"] = [ReadSingle(_solver, 0)],
                ["virtual-mass"] = [Marshal.ReadInt64(_solver, 0x8), Marshal.ReadInt64(_solver, 0x10)],
                ["may-hold-back"] = [Marshal.ReadInt32(_solver, 0x18)],
                ["working-velocity"] = [.. ReadVector(_solver, 0x60), .. ReadVector(_solver, 0x70)],
                ["working-spin"] = [.. ReadVector(_solver, 0x40), .. ReadVector(_solver, 0x50)],
                ["velocity-change"] = [.. ReadVector(_solver, 0xa0), .. ReadVector(_solver, 0xb0)],
                ["spin-change"] = [.. ReadVector(_solver, 0x80), .. ReadVector(_solver, 0x90)],
                ["relative"] = ReadVector(_solver, 0xc0),
                ["push"] = ReadVector(_solver, 0xd0),
                ["fallback"] = ReadVector(_solver, 0xe0),
                ["record-relative"] = ReadVector(_output, 0),
                ["counters"] =
                [
                    Marshal.ReadInt32(_environment, 0x94), Marshal.ReadInt32(_environment, 0xa4), Marshal.ReadInt32(_environment, 0xac),
                ],
                ["cores"] = [Slot(Marshal.ReadIntPtr(_cores, 0)), Slot(Marshal.ReadIntPtr(_cores, 8))],
            };

            ReadCore(outputs, _first, "first-");
            ReadCore(outputs, _second, "second-");

            return outputs;
        }

        public Dictionary<string, long[]> Entry(Dictionary<string, long[]> inputs)
        {
            Zero(_cores, 16);
            Zero(_environment, EnvironmentSize);
            Zero(_limits, LimitsSize);
            Zero(_point, PointSize);
            Zero(_record, RecordSize);

            nint[] cores = [_first, _second];

            WriteCore(_first, inputs, Sides[0]);
            WriteCore(_second, inputs, Sides[1]);
            WriteEnvironment(inputs);
            Marshal.WriteIntPtr(_environment, 0xe8, _materialManager);
            Marshal.WriteInt64(_environment, 0x108, inputs["step"][0]);

            for (int index = 0; index < _materials.Length; index++)
            {
                _materialFrictions[index] = BitConverter.Int64BitsToDouble(inputs["material-friction"][index]);
                _materialSecondFrictions[index] = BitConverter.Int64BitsToDouble(inputs["material-second-friction"][index]);
                Marshal.WriteInt32(_materials[index], 0xc, (int)inputs["material-has-second"][index]);
            }

            _managerFriction = BitConverter.Int64BitsToDouble(inputs["manager"][0]);
            _managerElasticity = BitConverter.Int64BitsToDouble(inputs["manager"][1]);

            for (int side = 0; side < _objects.Length; side++)
            {
                int synapse = 0x10 + (side * SynapseSize);

                Marshal.WriteInt32(cores[side], 4, (int)inputs[Sides[side] + "radius"][0]);
                Zero(_objects[side], ObjectSize);
                Marshal.WriteIntPtr(_objects[side], 0x30, _environment);
                Marshal.WriteIntPtr(_objects[side], 0xd0, _materials[side]);
                Marshal.WriteIntPtr(_objects[side], 0xe8, cores[side]);
                Marshal.WriteIntPtr(_objects[side], 0xf0, _frames[side]);
                WriteMatrix(_frames[side], inputs[Sides[side] + "frame"]);
                Marshal.WriteInt32(_triangles[side], 0, (int)inputs["material-indices"][side] << 24);

                Marshal.WriteIntPtr(_point, synapse + 0x10, _objects[side]);
                Marshal.WriteInt16(_point, synapse + 0x1a, (short)inputs["kinds"][side]);
                Marshal.WriteIntPtr(_point, synapse + 0x20, _triangles[side] + 4);
            }

            Marshal.WriteByte(_point, 0x64, (byte)inputs["uses-material-axes"][0]);
            Marshal.WriteIntPtr(_point, 0x70, _record);
            Marshal.WriteInt32(_point, 0x8c, (int)inputs["gap"][0]);

            long[] movable = inputs["movable"];
            long[] turns = inputs["turns"];

            WriteLanes(_record, 0x20, inputs["normal"]);
            Marshal.WriteInt16(_record, 0x72, (short)inputs["impacts"][0]);
            Marshal.WriteIntPtr(_record, 0x98, movable[0] != 0 ? _first : 0);
            Marshal.WriteIntPtr(_record, 0xa0, movable[1] != 0 ? _second : 0);
            WriteLanes(_record, 0xd0, inputs["first-arm"]);
            WriteLanes(_record, 0xe0, inputs["second-arm"]);
            WriteLanes(_record, 0xf0, turns[..3]);
            WriteLanes(_record, 0x100, turns[3..]);

            _setMaterials(_point, _record);
            _estimate(_point);

            long estimated = (ushort)Marshal.ReadInt16(_record, 0x74);
            long[] estimate = [ReadSingle(_record, 0x78), ReadSingle(_record, 0x7c)];
            float pushOut = _pushOut(_point, _environment);
            long[] pushed = [IvpImpactReplay.Lane(pushOut), ReadSingle(_record, 0x78)];

            // FUN_18008fe70 alone, on a zeroed solver holding only the record's elasticity at +0x130.
            Zero(_solver, SolverSize);
            Marshal.WriteInt32(_solver, 0x130, Marshal.ReadInt32(_record, 0x80));
            _materialAxes(_solver, _point);

            long axisUses = Marshal.ReadInt32(_solver, 0xf0);
            long[] axisCone = [ReadSingle(_solver, 0xf4), .. ReadVector(_solver, 0x100)];

            _enter(_record, _cores, pushOut, _point);

            Dictionary<string, long[]> outputs = new(StringComparer.Ordinal)
            {
                ["objects"] = [ObjectSlot(Marshal.ReadIntPtr(_record, 0x40)), ObjectSlot(Marshal.ReadIntPtr(_record, 0x48))],
                ["materials"] = [MaterialSlot(Marshal.ReadIntPtr(_record, 0x60)), MaterialSlot(Marshal.ReadIntPtr(_record, 0x68))],
                ["elasticity"] = [ReadSingle(_record, 0x80)],
                ["friction"] = [ReadSingle(_point, 0x78)],
                ["estimated"] = [estimated],
                ["estimate"] = estimate,
                ["push-out"] = pushed,
                ["axis-uses"] = [axisUses],
                ["axis-cone"] = axisCone,
                ["record-relative"] = ReadVector(_record, 0x30),
                ["counters"] =
                [
                    Marshal.ReadInt32(_environment, 0x94), Marshal.ReadInt32(_environment, 0xa4), Marshal.ReadInt32(_environment, 0xac),
                ],
                ["cores"] = [Slot(Marshal.ReadIntPtr(_cores, 0)), Slot(Marshal.ReadIntPtr(_cores, 8))],
            };

            ReadCore(outputs, _first, Sides[0]);
            ReadCore(outputs, _second, Sides[1]);

            return outputs;
        }

        public (float X, float Y, float Z) PointVelocity(
            Dictionary<string, long[]> inputs,
            (float X, float Y, float Z) arm,
            (float X, float Y, float Z) velocity,
            (float X, float Y, float Z) spin)
        {
            WriteCore(_first, inputs, "first-");
            WriteVector(_scratchA, arm);
            WriteVector(_scratchB, velocity);
            WriteVector(_scratchC, spin);

            _pointVelocity(_first, _scratchA, _scratchB, _scratchC, _scratchD);

            return VectorAt(_scratchD);
        }

        public ((float X, float Y, float Z) Velocity, (float X, float Y, float Z) Spin) UnitPush(
            Dictionary<string, long[]> inputs,
            (float X, float Y, float Z) arm,
            (float X, float Y, float Z) local,
            (float X, float Y, float Z) world)
        {
            WriteCore(_first, inputs, "first-");
            WriteVector(_scratchA, arm);
            WriteVector(_scratchB, local);
            WriteVector(_scratchC, world);

            _unitPush(_first, _scratchA, _scratchB, _scratchC, _scratchD, _scratchE);

            return (VectorAt(_scratchD), VectorAt(_scratchE));
        }

        public double VirtualMass(
            Dictionary<string, long[]> inputs,
            (float X, float Y, float Z) arm,
            (float X, float Y, float Z) local,
            (float X, float Y, float Z) world)
        {
            WriteCore(_first, inputs, "first-");
            WriteVector(_scratchA, arm);
            WriteVector(_scratchB, local);
            WriteVector(_scratchC, world);

            return _virtualMass(_first, _scratchA, _scratchB, _scratchC);
        }

        public (float X, float Y, float Z) Turn(Dictionary<string, long[]> inputs, (float X, float Y, float Z) vector)
        {
            WriteCore(_first, inputs, "first-");
            WriteVector(_scratchA, vector);

            _turn(_first + 0x90, _scratchA, _scratchB);

            return VectorAt(_scratchB);
        }

        public void Dispose()
        {
            // The tables in the blocks point at these delegates until the blocks are freed.
            GC.KeepAlive(_shadow);
            GC.KeepAlive(_freeze);
            GC.KeepAlive(_trap);
            GC.KeepAlive(_materialAt);
            GC.KeepAlive(_pairFriction);
            GC.KeepAlive(_pairElasticity);
            GC.KeepAlive(_materialFriction);
            GC.KeepAlive(_materialSecondFriction);

            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();
        }

        private void WriteCore(nint core, Dictionary<string, long[]> inputs, string side)
        {
            Zero(core, CoreSize);

            Marshal.WriteInt16(core, 0, (short)inputs[side + "flags"][0]);
            Marshal.WriteInt16(core, 2, (short)inputs[side + "collisions"][0]);
            Marshal.WriteInt32(core, 0x8, (int)inputs[side + "offset08"][0]);
            Marshal.WriteIntPtr(core, 0x10, _environment);
            WriteLanes(core, 0x40, inputs[side + "inverse-inertia"]);
            Marshal.WriteInt32(core, 0x4c, (int)inputs[side + "inverse-mass"][0]);
            Marshal.WriteIntPtr(core, 0x58, inputs[side + "offset58"][0] != 0 ? core : 0);
            Marshal.WriteIntPtr(core, 0x70, Objects);

            WriteMatrix(core, inputs[side + "matrix"]);

            WriteLanes(core, 0x110, inputs[side + "pending-spin"]);
            WriteLanes(core, 0x120, inputs[side + "pending-velocity"]);
            WriteLanes(core, 0x130, inputs[side + "spin"]);
            WriteLanes(core, 0x140, inputs[side + "velocity"]);
        }

        private static void ReadCore(Dictionary<string, long[]> outputs, nint core, string side)
        {
            outputs[side + "flags-after"] = [(ushort)Marshal.ReadInt16(core, 0)];
            outputs[side + "collisions-after"] = [Marshal.ReadInt16(core, 2)];
            outputs[side + "velocity-after"] = ReadVector(core, 0x140);
            outputs[side + "spin-after"] = ReadVector(core, 0x130);
            outputs[side + "pending-velocity-after"] = ReadVector(core, 0x120);
            outputs[side + "pending-spin-after"] = ReadVector(core, 0x110);
        }

        private static void WriteMatrix(nint core, long[] matrix)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    Marshal.WriteInt64(core, 0x90 + (row * 0x20) + (column * 8), matrix[(row * 3) + column]);
                }
            }
        }

        private void WriteEnvironment(Dictionary<string, long[]> inputs)
        {
            Marshal.WriteIntPtr(_environment, 0x40, Manager);
            Marshal.WriteIntPtr(_environment, 0x48, _limits);
            Marshal.WriteInt64(_environment, 0x110, inputs["inverse-step"][0]);
            Marshal.WriteInt32(_limits, 0xc, (int)inputs["max-velocity"][0]);
            Marshal.WriteInt32(_limits, 0x10, (int)inputs["max-collisions"][0]);
            Marshal.WriteInt32(_limits, 0x14, (int)inputs["max-spin"][0]);
            _freezes = inputs["freezes"][0] != 0;
        }

        private int ObjectSlot(nint candidate) => Array.IndexOf(_objects, candidate) + 1;

        private int MaterialSlot(nint candidate) => Array.IndexOf(_materials, candidate) + 1;

        private int Slot(nint core)
        {
            if (core == 0)
            {
                return 0;
            }

            if (core == _first)
            {
                return 1;
            }

            return core == _second ? 2 : 9;
        }

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

        private static void WriteLanes(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[lane]));
            }
        }

        private static void WriteVector(nint block, (float X, float Y, float Z) vector)
        {
            Marshal.WriteInt32(block, 0, BitConverter.SingleToInt32Bits(vector.X));
            Marshal.WriteInt32(block, 4, BitConverter.SingleToInt32Bits(vector.Y));
            Marshal.WriteInt32(block, 8, BitConverter.SingleToInt32Bits(vector.Z));
        }

        private static long ReadSingle(nint block, int offset) => (uint)Marshal.ReadInt32(block, offset);

        private static long[] ReadVector(nint block, int offset) =>
            [ReadSingle(block, offset), ReadSingle(block, offset + 4), ReadSingle(block, offset + 8)];

        private static (float X, float Y, float Z) VectorAt(nint block) =>
            (BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, 0)),
             BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, 4)),
             BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, 8)));
    }
}
