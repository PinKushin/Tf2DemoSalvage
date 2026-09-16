using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>IVP's own simulation, assembled — <c>FUN_18008a020</c> down to <c>FUN_180099a00</c> (B369, D172).</summary>
/// <remarks>
/// **A drop with no collision**, which is what the ported stages add up to so far: the PSI event fires, the unit's PSI flushes and
/// runs gravity, and the per-core step moves the body by the PREVIOUS step's velocity. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpSimulationTests
{
    [Test]
    public void Advance_ABodyUnderGravity_MovesOnlyOnTheSecondStep()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody core);
        simulation.Start();

        simulation.Advance(0.75d);

        // Three PSIs at 0, 0.5 and 1.0 land inside the target only for the first two. Gravity adds 5 per step, and a body
        // moves by the PREVIOUS step's velocity, so the first step moves nothing and the second moves 0.5 × −5.
        core.Velocity.Z.ShouldBe(-10f, 1e-4f);
        core.Position.Z.ShouldBe(-2.5d, 1e-4d);
    }

    [Test]
    public void Advance_ABodyUnderGravity_KeepsItsClockWithTheEnvironment()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody core);
        simulation.Start();

        int fired = simulation.Advance(1.2d);

        fired.ShouldBe(3);
        simulation.Now.ShouldBe(1.2d, "the clock is snapped to the target after the last PSI");
        core.LastStepped.ShouldBe(1d, "the last PSI ran at one second");
    }

    [Test]
    public void Add_ABody_PutsItInItsOwnAwakeUnitDrivenByGravity()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody core);

        IvpSimulationUnit unit = simulation.Add(new IvpRigidBody());

        simulation.AwakeUnits.ShouldBe(2);
        unit.Entries.Count.ShouldBe(1, "gravity alone, until the constraint and friction controllers are wired in");
        unit.Entries[0].Controller.Priority.ShouldBe(1000);
        core.Controllers.Count.ShouldBe(1);
    }

    [Test]
    public void Advance_ARestingBody_FallsAsleepAtTheRestCheck()
    {
        IvpSimulation simulation = Simulation(out IvpRigidBody core, gravity: (0f, 0f, 0f));
        core.RestAnchorOrientation = (0f, 0f, 0f, 1f);
        core.SettleAnchorOrientation = (0f, 0f, 0f, 1f);
        simulation.Start();

        simulation.Advance(12d);

        simulation.AwakeUnits.ShouldBe(0, "a body that never moves sleeps at the first rest check");
        core.UnitState.ShouldBe((int)IvpCoreMotion.Resting);
    }

    private static IvpSimulation Simulation(out IvpRigidBody core, (float X, float Y, float Z)? gravity = null)
    {
        core = new IvpRigidBody
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            Radius = 1f,
            InverseMass = 1f,

            // Gravity's controller damps before it accelerates, so a drop that is about gravity alone asks for no damping.
            Damping = 0f,
            RotationDamping = 0f,
        };

        IvpSimulation simulation = new(Environment(), gravity ?? (0f, 0f, -10f), () => 0f);
        simulation.Add(core);

        return simulation;
    }

    private static IvpImpactEnvironment Environment() =>
        new()
        {
            InverseStep = 2d,
            Step = 0.5d,
            Limits = new IvpAnomalyLimits(1000f, 0, 1000f, 250, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            RestDelay = 1f,
            RestCheckCountdown = 5,
        };
}
