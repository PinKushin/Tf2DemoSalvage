using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>One core's whole step as the island tail runs it — <c>FUN_180099a00</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the decompiler** (headless `DecompAt` of <c>180099a00</c>, 2026-09-15): the limits asked unless the core is exempt,
/// the linear speed kept, the step taken, and each object's hull manager advanced and pushed when due. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpIntegratorStepCoreTests
{
    [Test]
    public void StepCore_AMovingCore_KeepsItsLinearSpeedAndStampsItsStep()
    {
        IvpRigidBody core = Core(velocity: (3f, 4f, 0f));

        IvpIntegrator.StepCore(core, Environment(new Recording()), now: 2d, step: 0.5f, []);

        core.LinearSpeed.ShouldBe(5f);
        core.LastStepped.ShouldBe(2d);
    }

    [Test]
    public void StepCore_OverTheVelocityLimit_AsksTheAnomalyManager()
    {
        Recording anomalies = new();
        IvpRigidBody core = Core(velocity: (30f, 0f, 0f));

        IvpIntegrator.StepCore(core, Environment(anomalies), now: 2d, step: 0.5f, []);

        anomalies.Velocity.ShouldBe(1);
    }

    [Test]
    public void StepCore_ACoreExemptByItsOffsets_IsNotChecked()
    {
        Recording anomalies = new();
        IvpRigidBody core = Core(velocity: (30f, 0f, 0f));
        core.HasOffset58 = true;
        core.Offset08 = 0f;

        IvpIntegrator.StepCore(core, Environment(anomalies), now: 2d, step: 0.5f, []);

        anomalies.Velocity.ShouldBe(0);
    }

    [Test]
    public void StepCore_AnObjectWhoseHullIsPassed_PushesItsManager()
    {
        IvpCollisionObject body = new();
        IvpRigidBody core = new(body) { Velocity = (3f, 4f, 0f), Orientation = (0d, 0d, 0d, 1d), WorkingOrientation = (0d, 0d, 0d, 1d) };
        body.Hull.Install(new Listener(), now: 0d, allowance: 0d);
        List<IvpHullManager> pushed = [];

        IvpIntegrator.StepCore(core, Environment(new Recording()), now: 2d, step: 0.5f, pushed);

        pushed.ShouldBe([body.Hull]);
        body.Hull.Time.ShouldBe(2d);
        body.Hull.Gradient.ShouldBe(IvpHullManager.GradientFor(core.SurfaceSpeedBound, 5f));
    }

    [Test]
    public void StepCore_AnObjectWithNothingFiled_IsNotPushed()
    {
        IvpCollisionObject body = new();
        IvpRigidBody core = new(body) { Velocity = (3f, 4f, 0f), Orientation = (0d, 0d, 0d, 1d), WorkingOrientation = (0d, 0d, 0d, 1d) };
        List<IvpHullManager> pushed = [];

        IvpIntegrator.StepCore(core, Environment(new Recording()), now: 2d, step: 0.5f, pushed);

        pushed.ShouldBeEmpty();
        body.Hull.Time.ShouldBe(2d, "the manager is still advanced");
    }

    private static IvpRigidBody Core((float X, float Y, float Z) velocity) =>
        new() { Velocity = velocity, Orientation = (0d, 0d, 0d, 1d), WorkingOrientation = (0d, 0d, 0d, 1d) };

    private static IvpImpactEnvironment Environment(IIvpAnomalyManager anomalies) =>
        new()
        {
            InverseStep = 2d,
            Step = 0.5d,
            Limits = new IvpAnomalyLimits(10f, 0, 1000f, 250, 0f, 0f),
            Anomalies = anomalies,
            Materials = Tf2DemoSalvage.Animation.Tests.IvpFrictionSystemRevalidatePairTests.Materials.Instance,
        };

    private sealed class Recording : IIvpAnomalyManager
    {
        public int Velocity { get; private set; }

        public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity) =>
            Velocity++;

        public void MaximumAngularVelocityExceeded(
            IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin)
        {
        }

        public bool MaximumContactsExceeded(IReadOnlyList<IvpRigidBody> cores) => false;

        public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core) => false;
    }

    private sealed class Listener : IIvpHullSynapse
    {
        public int? HullSlot { get; set; }

        public void HullPassed(IvpHullManager manager, float overshoot)
        {
        }

        public void Rebased(float valueShift, float centerShift)
        {
        }
    }
}
