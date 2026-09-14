using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's default range manager as lanes of bits — its slot 2 (<c>FUN_1800a04e0</c>) for each of two cores and its slot 1
/// (<c>FUN_1800a0560</c>) for the pair (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-range` probe from the shipped `vphysics.dll` and
/// replayed by `IvpRangeConformanceTests`. A core is three lanes: its radius (<c>+0x4</c>), linear speed (<c>+0x1dc</c>) and
/// surface speed bound (<c>+0x254</c>).
/// </remarks>
public static class IvpRangeReplay
{
    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("step", IvpReplayKind.Real64, 1),
        new("first", IvpReplayKind.Real32, 3),
        new("second", IvpReplayKind.Real32, 3),
    ];

    /// <summary>What the calls leave.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("object-first", IvpReplayKind.Real64, 1),
        new("object-second", IvpReplayKind.Real64, 1),
        new("pair", IvpReplayKind.Real64, 2),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        double step = IvpImpactReplay.Real64(inputs, "step", 0);
        IvpCoreBounds first = Core(inputs, "first");
        IvpCoreBounds second = Core(inputs, "second");
        (double pairFirst, double pairSecond) = IvpRangeManager.PairRange(first, second, step);

        return new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["object-first"] = [IvpImpactReplay.Lane(IvpRangeManager.ObjectRange(first, step))],
            ["object-second"] = [IvpImpactReplay.Lane(IvpRangeManager.ObjectRange(second, step))],
            ["pair"] = [IvpImpactReplay.Lane(pairFirst), IvpImpactReplay.Lane(pairSecond)],
        };
    }

    /// <summary>Every lane where two readings of the same case differ, named.</summary>
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

    private static IvpCoreBounds Core(IReadOnlyDictionary<string, long[]> inputs, string name) => new(
        IvpImpactReplay.Real32(inputs, name, 0), 0f, 0f, IvpImpactReplay.Real32(inputs, name, 1), IvpImpactReplay.Real32(inputs, name, 2));
}
