using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// The object cache's refresh, <c>FUN_180080a60</c>, as lanes of bits: a core, an environment's time and PSI, an object's offset and
/// rotation in its core, and every field the refresh writes (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-object-cache` probe from the shipped `vphysics.dll`
/// and replayed by `IvpObjectCacheConformanceTests`. The matrix is carried as the nine rotation terms the cache keeps at `+0x40`,
/// `+0x48`, `+0x50`, `+0x60` … `+0x90`, and its translation at `+0xa0`.
/// </remarks>
public static class IvpObjectCacheReplay
{
    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("position", IvpReplayKind.Real64, 3),
        new("velocity", IvpReplayKind.Real32, 3),
        new("orientation", IvpReplayKind.Real64, 4),
        new("working", IvpReplayKind.Real64, 4),
        new("stepped", IvpReplayKind.Real64, 1),
        new("inverse-step", IvpReplayKind.Real32, 1),
        new("now", IvpReplayKind.Real64, 1),
        new("psi", IvpReplayKind.Whole32, 1),
        new("has-offset", IvpReplayKind.Whole32, 1),
        new("offset", IvpReplayKind.Real32, 3),
        new("has-rotation", IvpReplayKind.Whole32, 1),
        new("rotation", IvpReplayKind.Real64, 4),
    ];

    /// <summary>What the refresh writes.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("core-position", IvpReplayKind.Real64, 3),
        new("cached-rotation", IvpReplayKind.Real64, 4),
        new("matrix", IvpReplayKind.Real64, 9),
        new("translation", IvpReplayKind.Real64, 3),
        new("refreshed-at", IvpReplayKind.Whole32, 1),
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
            Position = Triple(inputs, "position"),
            PreviousVelocity = IvpImpactReplay.Vector(inputs, "velocity", 0),
            Orientation = Quaternion(inputs, "orientation"),
            WorkingOrientation = Quaternion(inputs, "working"),
            LastStepped = IvpImpactReplay.Real64(inputs, "stepped", 0),
            InverseStep = IvpImpactReplay.Real32(inputs, "inverse-step", 0),
        };
        IvpObjectCache cache = new();

        cache.Refresh(
            core,
            IvpImpactReplay.Real64(inputs, "now", 0),
            IvpImpactReplay.Whole32(inputs, "psi", 0),
            IvpImpactReplay.Whole32(inputs, "has-offset", 0) != 0 ? IvpImpactReplay.Vector(inputs, "offset", 0) : null,
            IvpImpactReplay.Whole32(inputs, "has-rotation", 0) != 0 ? Quaternion(inputs, "rotation") : null);

        IvpMatrix m = cache.Matrix;

        return new Dictionary<string, long[]>(StringComparer.Ordinal)
        {
            ["core-position"] = [IvpImpactReplay.Lane(cache.CorePosition.X), IvpImpactReplay.Lane(cache.CorePosition.Y), IvpImpactReplay.Lane(cache.CorePosition.Z)],
            ["cached-rotation"] =
            [
                IvpImpactReplay.Lane(cache.Rotation.X), IvpImpactReplay.Lane(cache.Rotation.Y),
                IvpImpactReplay.Lane(cache.Rotation.Z), IvpImpactReplay.Lane(cache.Rotation.W),
            ],
            ["matrix"] =
            [
                IvpImpactReplay.Lane(m.M0), IvpImpactReplay.Lane(m.M1), IvpImpactReplay.Lane(m.M2),
                IvpImpactReplay.Lane(m.M4), IvpImpactReplay.Lane(m.M5), IvpImpactReplay.Lane(m.M6),
                IvpImpactReplay.Lane(m.M8), IvpImpactReplay.Lane(m.M9), IvpImpactReplay.Lane(m.M10),
            ],
            ["translation"] = [IvpImpactReplay.Lane(m.Translation.X), IvpImpactReplay.Lane(m.Translation.Y), IvpImpactReplay.Lane(m.Translation.Z)],
            ["refreshed-at"] = [cache.RefreshedAt],
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

    private static (double X, double Y, double Z) Triple(IReadOnlyDictionary<string, long[]> inputs, string name) =>
        (IvpImpactReplay.Real64(inputs, name, 0), IvpImpactReplay.Real64(inputs, name, 1), IvpImpactReplay.Real64(inputs, name, 2));

    private static (double X, double Y, double Z, double W) Quaternion(IReadOnlyDictionary<string, long[]> inputs, string name) =>
        (IvpImpactReplay.Real64(inputs, name, 0), IvpImpactReplay.Real64(inputs, name, 1),
         IvpImpactReplay.Real64(inputs, name, 2), IvpImpactReplay.Real64(inputs, name, 3));
}
