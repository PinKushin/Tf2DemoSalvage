using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// The many-contact normal solve's linear algebra as lanes of bits — the scaling (<c>FUN_1800aa2c0</c>), the gathered sub-system
/// (<c>FUN_1800a4d40</c>) and its elimination (<c>FUN_1800a80a0</c>), the row test (<c>FUN_1800a7270</c>) and the constraint
/// solver (<c>FUN_1800a5e60</c>), run in <c>FUN_1800aa5c0</c>'s order on one system (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-contact-solve` probe from the shipped
/// `vphysics.dll` and replayed by `IvpComplementaritySolverConformanceTests`. A system of `size` contacts packs its values row by
/// row into the first `size·size` lanes; `gathered` of the `active` indices form the sub-system; the row test reads the
/// sub-system's answer scattered back at those indices, as `FUN_1800aa9f0` leaves it. The solver's `state` lanes are its
/// `+0x80`, `+0x88`, `+0xa4`, `+0xf4`, `+0x90`, `+0x94`, `+0x98`, `+0x9c` and `+0xa0`, which pin the path as well as the answer.
/// </remarks>
public static class IvpComplementarityReplay
{
    /// <summary>The most contacts a case holds.</summary>
    public const int MostContacts = 10;

    private const int StateLanes = 9;

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("size", IvpReplayKind.Whole32, 1),
        new("warm", IvpReplayKind.Whole32, 1),
        new("gathered", IvpReplayKind.Whole32, 1),
        new("active", IvpReplayKind.Whole32, MostContacts),
        new("matrix", IvpReplayKind.Real64, MostContacts * MostContacts),
        new("rhs", IvpReplayKind.Real64, MostContacts),
    ];

    /// <summary>What a case leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("scale", IvpReplayKind.Real64, 1),
        new("equilibrated", IvpReplayKind.Real64, MostContacts * MostContacts),
        new("equilibrated-rhs", IvpReplayKind.Real64, MostContacts),
        new("gathered-solved", IvpReplayKind.Whole32, 1),
        new("gathered-values", IvpReplayKind.Real64, MostContacts * MostContacts),
        new("gathered-rhs", IvpReplayKind.Real64, MostContacts),
        new("gathered-result", IvpReplayKind.Real64, MostContacts),
        new("holds", IvpReplayKind.Whole32, MostContacts),
        new("solved", IvpReplayKind.Whole32, 1),
        new("result", IvpReplayKind.Real64, MostContacts),
        new("residual", IvpReplayKind.Real64, MostContacts),
        new("order", IvpReplayKind.Whole32, MostContacts),
        new("state", IvpReplayKind.Whole32, StateLanes),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        int size = IvpImpactReplay.Whole32(inputs, "size", 0);
        int warm = IvpImpactReplay.Whole32(inputs, "warm", 0);
        int gathered = IvpImpactReplay.Whole32(inputs, "gathered", 0);
        int[] active = new int[size];

        for (int i = 0; i < gathered; i++)
        {
            active[i] = IvpImpactReplay.Whole32(inputs, "active", i);
        }

        IvpLinearSystem system = new(size, size);

        for (int i = 0; i < size * size; i++)
        {
            system.Values[i] = IvpImpactReplay.Real64(inputs, "matrix", i);
        }

        for (int i = 0; i < size; i++)
        {
            system.RightHandSide[i] = IvpImpactReplay.Real64(inputs, "rhs", i);
        }

        double scale = system.Equilibrate();
        long[] equilibrated = Lanes(system.Values, MostContacts * MostContacts);
        long[] equilibratedRight = Lanes(system.RightHandSide, MostContacts);
        IvpLinearSystem sub = new(size, size) { Rows = gathered, Columns = gathered };

        sub.Gather(system, active, gathered);

        bool gatheredSolved = sub.Solve();

        for (int i = 0; i < gathered; i++)
        {
            system.Result[active[i]] = sub.Result[i];
        }

        long[] holds = new long[MostContacts];

        for (int i = 0; i < size; i++)
        {
            holds[i] = system.Holds(i) ? 1 : 0;
        }

        double[] result = new double[size];
        IvpComplementaritySolver solver = new(system.Values, system.RightHandSide, result, size);
        bool solved = solver.Solve(warm);
        long[] order = new long[MostContacts];

        for (int i = 0; i < size; i++)
        {
            order[i] = solver.Order[i];
        }

        (int activeFirst, int activeSecond, int waitingFirst, int waitingSecond) = solver.Shuffles;

        return new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["scale"] = [IvpImpactReplay.Lane(scale)],
            ["equilibrated"] = equilibrated,
            ["equilibrated-rhs"] = equilibratedRight,
            ["gathered-solved"] = [gatheredSolved ? 1 : 0],
            ["gathered-values"] = Lanes(sub.Values, MostContacts * MostContacts),
            ["gathered-rhs"] = Lanes(sub.RightHandSide, MostContacts),
            ["gathered-result"] = Lanes(sub.Result, MostContacts),
            ["holds"] = holds,
            ["solved"] = [solved ? 1 : 0],
            ["result"] = Lanes(result, MostContacts),
            ["residual"] = Lanes(solver.Residual, MostContacts),
            ["order"] = order,
            ["state"] =
            [
                solver.ActiveCount, solver.Settled, solver.InverseState, solver.InverseSize, solver.Eliminations,
                activeFirst, activeSecond, waitingFirst, waitingSecond,
            ],
        };
    }

    /// <summary>Every lane where two readings of the same case differ, named — a NaN's sign and payload included.</summary>
    /// <param name="expected">What the binary left.</param>
    /// <param name="actual">What the port left.</param>
    /// <returns>One line per differing lane; empty when the two agree.</returns>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, long[]> expected, IReadOnlyDictionary<string, long[]> actual) =>
        IvpImpactReplay.Differences(Outputs, expected, actual);

    /// <summary>Writes one case in the fixture's form.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="replay">The case.</param>
    public static void Write(TextWriter writer, IvpReplayCase replay) => IvpImpactReplay.Write(Inputs, Outputs, writer, replay);

    /// <summary>Reads every case a fixture holds.</summary>
    /// <param name="reader">The fixture.</param>
    /// <returns>The cases, in file order.</returns>
    public static IReadOnlyList<IvpReplayCase> Parse(TextReader reader) => IvpImpactReplay.Parse(Inputs, Outputs, reader);

    /// <summary>Doubles as a field's lanes, zero past the array.</summary>
    internal static long[] Lanes(double[] values, int count)
    {
        long[] lanes = new long[count];

        for (int i = 0; i < Math.Min(values.Length, count); i++)
        {
            lanes[i] = IvpImpactReplay.Lane(values[i]);
        }

        return lanes;
    }
}
