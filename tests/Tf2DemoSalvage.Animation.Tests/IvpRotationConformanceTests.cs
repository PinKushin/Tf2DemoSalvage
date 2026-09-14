using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's rotation routines — the product, the normalization, the interpolation, the two step rotations and a core's rotation
/// step — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-rotation` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, called `FUN_180070d60`, `FUN_180070c60`, `FUN_180071060`, `FUN_180071680`,
/// `FUN_180070f50` and `FUN_180099fc0` on each case — the runtime path flag set to the path the case names — and wrote every
/// lane they left to `Data/ivp-rotation.txt`. <see cref="IvpRotationReplay"/> is the one definition of the lanes.
/// </remarks>
public sealed class IvpRotationConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-rotation.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Rotation_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Rotation_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpRotationReplay.Differences(replay.Outputs, IvpRotationReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the cases must reach both of the interpolation's branches, a normalization that
    /// rescales and one that leaves the bits alone, a rotation step that sub-steps, both second routes, both `sin` paths, and a
    /// NaN.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(replay => Math.Abs(Dot(replay)) >= 0.999f).ShouldBeGreaterThan(0);
        replays.Count(replay => Math.Abs(Dot(replay)) < 0.999f).ShouldBeGreaterThan(0);
        replays.Count(replay => !replay.Outputs["normalised"].SequenceEqual(replay.Inputs["first"])).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["normalised"].SequenceEqual(replay.Inputs["first"])).ShouldBeGreaterThan(0);
        replays.Count(replay => !SecondRoute(replay) && SubSteps(replay) > 1).ShouldBeGreaterThan(0);
        replays.Count(replay => SecondRoute(replay) && OneAxis(replay)).ShouldBeGreaterThan(0);
        replays.Count(replay => SecondRoute(replay) && !OneAxis(replay)).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["fused"][0] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["fused"][0] != 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["spin"].Any(lane => float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)lane)))))
            .ShouldBeGreaterThan(0);
    }

    private static double Dot(IvpReplayCase replay)
    {
        (double X, double Y, double Z, double W) first = IvpRotationReplay.Quaternion(replay.Inputs, "first");
        (double X, double Y, double Z, double W) second = IvpRotationReplay.Quaternion(replay.Inputs, "second");

        return (first.X * second.X) + (first.Y * second.Y) + (first.Z * second.Z) + (first.W * second.W);
    }

    private static bool SecondRoute(IvpReplayCase replay) => (replay.Inputs["flags"][0] & 0x8) != 0 || replay.Inputs["phase"][0] == 5;

    private static bool OneAxis(IvpReplayCase replay)
    {
        long offset = replay.Inputs["offset08"][0];

        // UCOMISS against zero and JNZ: a zero of either sign, or a NaN, takes the one-axis route.
        return replay.Inputs["offset58"][0] != 0 && ((offset & 0x7fffffff) == 0 || float.IsNaN(Real32(offset)));
    }

    private static int SubSteps(IvpReplayCase replay)
    {
        long[] spin = replay.Inputs["spin"];

        return IvpIntegrator.SubSteps(
            (Real32(spin[0]), Real32(spin[1]), Real32(spin[2])), Real32(replay.Inputs["step"][0]));
    }

    private static float Real32(long lane) => BitConverter.Int32BitsToSingle(unchecked((int)lane));

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpRotationReplay.Parse(reader);
    }
}
