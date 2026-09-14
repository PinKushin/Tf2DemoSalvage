using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// A core's rest test <c>FUN_180077220</c> and the process-wide generator <c>FUN_18007d5c0</c> as lanes of bits (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-rest` probe from the shipped `vphysics.dll` and
/// replayed by `IvpRestConformanceTests`. The two anchors a core keeps are inputs and outputs both, because the test rewrites
/// them.
/// </remarks>
public static class IvpRestReplay
{
    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("now", IvpReplayKind.Real64, 1),
        new("rest-delay", IvpReplayKind.Real32, 1),
        new("radius", IvpReplayKind.Real32, 1),
        new("position", IvpReplayKind.Real64, 3),
        new("orientation", IvpReplayKind.Real64, 4),
        new("working-orientation", IvpReplayKind.Real64, 4),
        new("spin", IvpReplayKind.Real32, 3),
        new("anchor-time", IvpReplayKind.Real64, 1),
        new("anchor-orientation", IvpReplayKind.Real32, 4),
        new("anchor-position", IvpReplayKind.Real32, 3),
        new("settle-time", IvpReplayKind.Real64, 1),
        new("settle-orientation", IvpReplayKind.Real32, 4),
        new("settle-position", IvpReplayKind.Real32, 3),
        new("seed", IvpReplayKind.Whole32, 1),
    ];

    /// <summary>What a case leaves: the answer, both anchors, and one draw of the generator with the seed it left.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("motion", IvpReplayKind.Whole32, 1),
        new("anchored-time", IvpReplayKind.Real64, 1),
        new("anchored-orientation", IvpReplayKind.Real32, 4),
        new("anchored-position", IvpReplayKind.Real32, 3),
        new("settled-time", IvpReplayKind.Real64, 1),
        new("settled-orientation", IvpReplayKind.Real32, 4),
        new("settled-position", IvpReplayKind.Real32, 3),
        new("draw", IvpReplayKind.Real32, 1),
        new("drawn-seed", IvpReplayKind.Whole32, 1),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        IvpRigidBody core = new()
        {
            Radius = IvpImpactReplay.Real32(inputs, "radius", 0),
            Position = (IvpImpactReplay.Real64(inputs, "position", 0), IvpImpactReplay.Real64(inputs, "position", 1),
                IvpImpactReplay.Real64(inputs, "position", 2)),
            Orientation = IvpRotationReplay.Quaternion(inputs, "orientation"),
            WorkingOrientation = IvpRotationReplay.Quaternion(inputs, "working-orientation"),
            AngularVelocity = IvpImpactReplay.Vector(inputs, "spin", 0),
            RestAnchorTime = IvpImpactReplay.Real64(inputs, "anchor-time", 0),
            RestAnchorOrientation = Rotation(inputs, "anchor-orientation"),
            RestAnchorPosition = IvpImpactReplay.Vector(inputs, "anchor-position", 0),
            SettleAnchorTime = IvpImpactReplay.Real64(inputs, "settle-time", 0),
            SettleAnchorOrientation = Rotation(inputs, "settle-orientation"),
            SettleAnchorPosition = IvpImpactReplay.Vector(inputs, "settle-position", 0),
        };

        IvpCoreMotion motion = core.TestRest(
            IvpImpactReplay.Real64(inputs, "now", 0), IvpImpactReplay.Real32(inputs, "rest-delay", 0));
        IvpRandom random = new() { Seed = IvpImpactReplay.Whole32(inputs, "seed", 0) };
        float draw = random.Next();

        return new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["motion"] = [(int)motion],
            ["anchored-time"] = [IvpImpactReplay.Lane(core.RestAnchorTime)],
            ["anchored-orientation"] = Lanes(core.RestAnchorOrientation),
            ["anchored-position"] = IvpImpactReplay.Lanes(core.RestAnchorPosition),
            ["settled-time"] = [IvpImpactReplay.Lane(core.SettleAnchorTime)],
            ["settled-orientation"] = Lanes(core.SettleAnchorOrientation),
            ["settled-position"] = IvpImpactReplay.Lanes(core.SettleAnchorPosition),
            ["draw"] = [IvpImpactReplay.Lane(draw)],
            ["drawn-seed"] = [random.Seed],
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

    private static (float X, float Y, float Z, float W) Rotation(IReadOnlyDictionary<string, long[]> inputs, string name) =>
        (IvpImpactReplay.Real32(inputs, name, 0), IvpImpactReplay.Real32(inputs, name, 1),
         IvpImpactReplay.Real32(inputs, name, 2), IvpImpactReplay.Real32(inputs, name, 3));

    private static long[] Lanes((float X, float Y, float Z, float W) rotation) =>
    [
        IvpImpactReplay.Lane(rotation.X), IvpImpactReplay.Lane(rotation.Y), IvpImpactReplay.Lane(rotation.Z), IvpImpactReplay.Lane(rotation.W),
    ];
}
