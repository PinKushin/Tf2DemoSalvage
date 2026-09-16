using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Runs the same falling body through the invented <see cref="IvpEnvironment"/> and through <see cref="IvpSimulation"/>, the
/// ported engine step, and prints where they part company (B369, D172).
/// </summary>
/// <remarks>
/// **A measurement, not a test** (D38): it asserts nothing, because the answer is a number — how far the two solvers have drifted
/// after N steps. The engine's own step is the reference; a difference is <see cref="IvpEnvironment"/>'s.
///
/// **No collision on either side.** The new simulation has no narrow phase wired in yet, so the comparison is a free fall with
/// damping — which is exactly the part both solvers claim to do the same way.
/// </remarks>
public sealed class IvpStepCompareProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "ivp-step-compare";

    /// <inheritdoc/>
    public string Summary =>
        "free-falls one body through IvpEnvironment and through the ported engine step (IvpSimulation), printing the drift per step";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        int steps = arguments.Count > 0 ? int.Parse(arguments[0], CultureInfo.InvariantCulture) : 8;
        float step = 1f / 66f;

        output.WriteLine($"step = {step.ToString("0.000000", CultureInfo.InvariantCulture)}, {steps} steps each");

        Case(output, "a free fall, no damping", step, steps, damping: 0f, rotationDamping: 0f, spin: (0f, 0f, 0f));
        Case(output, "a fall with TF2's own damping", step, steps, damping: 0.1f, rotationDamping: 4f, spin: (0f, 0f, 0f));
        Case(output, "a fall, damped, spinning", step, steps, damping: 0.1f, rotationDamping: 4f, spin: (3f, 0f, 1f));
    }

    private static void Case(
        TextWriter output,
        string what,
        float step,
        int steps,
        float damping,
        float rotationDamping,
        (float X, float Y, float Z) spin)
    {
        (float X, float Y, float Z) gravity = (0f, 0f, -600f);

        IvpEnvironment invented = new(step, gravity);
        IvpRigidBody inventedBody = Body(damping, rotationDamping, spin);
        invented.Add(inventedBody);

        IvpSimulation ported = new(Environment(step), gravity, () => 0f);
        IvpRigidBody portedBody = Body(damping, rotationDamping, spin);
        ported.Add(portedBody);
        ported.Start();

        output.WriteLine();
        output.WriteLine($"== {what}");
        output.WriteLine(
            "  n   invented z            ported z              dz                    dvz             d|orientation|  ported qx (control)");

        for (int index = 1; index <= steps; index++)
        {
            invented.Simulate();
            ported.Advance(index * (double)step);

            output.WriteLine(
                $"  {index,-3} {Show(inventedBody.Position.Z),-21} {Show(portedBody.Position.Z),-21} " +
                $"{Show(portedBody.Position.Z - inventedBody.Position.Z),-21} " +
                $"{Show(portedBody.Velocity.Z - inventedBody.Velocity.Z),-15} {Show(Turn(inventedBody, portedBody)),-15} " +
                $"{Show(portedBody.Orientation.X)}");
        }

        double drift = Math.Abs(portedBody.Position.Z - inventedBody.Position.Z) + Turn(inventedBody, portedBody);

        output.WriteLine(
            drift < double.Epsilon
                ? "  the two agree to the bit after every step"
                : $"  they differ by {Show(drift)} — the engine's step is the reference, so the drift is the invented one's");
    }

    /// <summary>How far apart the two bodies' visible orientations are, as the sum of the component differences.</summary>
    private static double Turn(IvpRigidBody first, IvpRigidBody second) =>
        Math.Abs(first.Orientation.X - second.Orientation.X) +
        Math.Abs(first.Orientation.Y - second.Orientation.Y) +
        Math.Abs(first.Orientation.Z - second.Orientation.Z) +
        Math.Abs(first.Orientation.W - second.Orientation.W);

    private static string Show(double value) => value.ToString("0.000000000", CultureInfo.InvariantCulture);

    private static IvpRigidBody Body(float damping, float rotationDamping, (float X, float Y, float Z) spin) =>
        new()
        {
            Position = (0d, 0d, 0d),
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            AngularVelocity = spin,
            Radius = 1f,
            InverseMass = 1f,
            InverseInertia = (1f, 1f, 1f),
            Damping = damping,
            RotationDamping = rotationDamping,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
        };

    private static IvpImpactEnvironment Environment(float step) =>
        new()
        {
            Step = step,
            InverseStep = 1d / step,
            Limits = new IvpAnomalyLimits(2000f, 6, 3600f, 250, 1f, 1e30f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            RestDelay = 5f,
            RestCheckCountdown = 15,
        };
}
