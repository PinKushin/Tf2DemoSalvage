using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's rotation routines as lanes of bits — the product <c>FUN_180070d60</c>, the normalization <c>FUN_180070c60</c>, the
/// interpolation <c>FUN_180071060</c>, the step rotations <c>FUN_180071680</c> and <c>FUN_180070f50</c>, and a core's rotation
/// step <c>FUN_180099fc0</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-rotation` probe from the shipped `vphysics.dll` and
/// replayed by `IvpRotationConformanceTests`. **A case carries the runtime path vphysics' `sin` took** (`fused`), and the port
/// runs the same one, so a fixture written on a processor with FMA3 pins both paths.
/// </remarks>
public static class IvpRotationReplay
{
    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("fused", IvpReplayKind.Whole32, 1),
        new("first", IvpReplayKind.Real64, 4),
        new("second", IvpReplayKind.Real64, 4),
        new("fraction", IvpReplayKind.Real64, 1),
        new("spin", IvpReplayKind.Real32, 3),
        new("delta", IvpReplayKind.Real64, 1),
        new("step", IvpReplayKind.Real32, 1),
        new("inertia", IvpReplayKind.Real32, 3),
        new("inverse-inertia", IvpReplayKind.Real32, 3),
        new("flags", IvpReplayKind.Whole32, 1),
        new("phase", IvpReplayKind.Whole32, 1),
        new("offset08", IvpReplayKind.Real32, 1),
        new("offset58", IvpReplayKind.Whole32, 1),
        new("axis", IvpReplayKind.Whole32, 1),
    ];

    /// <summary>What a case leaves: each routine's output, and the core's spin after its rotation step.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("product", IvpReplayKind.Real64, 4),
        new("normalised", IvpReplayKind.Real64, 4),
        new("interpolated", IvpReplayKind.Real64, 4),
        new("delta-turn", IvpReplayKind.Real64, 4),
        new("sine-turn", IvpReplayKind.Real64, 4),
        new("turn", IvpReplayKind.Real64, 4),
        new("turned-spin", IvpReplayKind.Real32, 3),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        bool fused = IvpImpactReplay.Whole32(inputs, "fused", 0) != 0;
        (double X, double Y, double Z, double W) first = Quaternion(inputs, "first");
        (double X, double Y, double Z, double W) second = Quaternion(inputs, "second");
        (float X, float Y, float Z) spin = IvpImpactReplay.Vector(inputs, "spin", 0);
        double delta = IvpImpactReplay.Real64(inputs, "delta", 0);
        IvpRigidBody core = new()
        {
            FlagBit3 = (IvpImpactReplay.Whole32(inputs, "flags", 0) & 0x8) != 0,
            Offset08 = IvpImpactReplay.Real32(inputs, "offset08", 0),
            HasOffset58 = IvpImpactReplay.Whole32(inputs, "offset58", 0) != 0,
            Offset58Axis = IvpImpactReplay.Whole32(inputs, "axis", 0),
            Inertia = IvpImpactReplay.Vector(inputs, "inertia", 0),
            InverseInertia = IvpImpactReplay.Vector(inputs, "inverse-inertia", 0),
            AngularVelocity = spin,
        };

        return new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["product"] = Lanes(IvpQuaternion.Product(first, second)),
            ["normalised"] = Lanes(IvpQuaternion.Normalise(first)),
            ["interpolated"] = Lanes(IvpQuaternion.Interpolate(first, second, IvpImpactReplay.Real64(inputs, "fraction", 0), fused)),
            ["delta-turn"] = Lanes(IvpQuaternion.Delta(spin, delta)),
            ["sine-turn"] = Lanes(IvpQuaternion.SineDelta(spin, delta, fused)),
            ["turn"] = Lanes(IvpIntegrator.Rotate(
                core, IvpImpactReplay.Real32(inputs, "step", 0), IvpImpactReplay.Whole32(inputs, "phase", 0), fused)),
            ["turned-spin"] = IvpImpactReplay.Lanes(core.AngularVelocity),
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

    /// <summary>A quaternion field's four doubles, <c>(x, y, z, w)</c>.</summary>
    internal static (double X, double Y, double Z, double W) Quaternion(IReadOnlyDictionary<string, long[]> inputs, string name) =>
        (IvpImpactReplay.Real64(inputs, name, 0), IvpImpactReplay.Real64(inputs, name, 1),
         IvpImpactReplay.Real64(inputs, name, 2), IvpImpactReplay.Real64(inputs, name, 3));

    private static long[] Lanes((double X, double Y, double Z, double W) rotation) =>
    [
        IvpImpactReplay.Lane(rotation.X), IvpImpactReplay.Lane(rotation.Y), IvpImpactReplay.Lane(rotation.Z), IvpImpactReplay.Lane(rotation.W),
    ];
}
