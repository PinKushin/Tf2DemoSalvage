using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>One unit's PSI — <c>FUN_180075c80</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`, *The PSI per unit*): each core's matrix and event position rebuilt, its
/// staged velocities flushed, its freeze bits cleared, the unit's controllers run, its cores collected, and the rest check run
/// only when the environment's countdown reaches zero. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpSimulationUnitTests
{
    [Test]
    public void Psi_AnAwakeCore_RebuildsItsEventPositionAndFlushesItsStagedVelocity()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        core.Position = (1d, 0d, 0d);
        core.PreviousVelocity = (2f, 0f, 0f);
        core.LastStepped = 0.5d;
        core.PendingVelocity = (0f, 3f, 0f);
        core.CollisionFreeze = 2;
        core.Collisions = 7;
        List<IvpRigidBody> pushed = [];

        unit.Psi(Environment(countdown: 5), now: 1d, step: 0.5f, pushed, () => 0f);

        core.EventPosition.ShouldBe((2d, 0d, 0d));
        core.Velocity.ShouldBe((0f, 3f, 0f), "the staged velocity is flushed into the live one");
        core.PendingVelocity.ShouldBe((0f, 0f, 0f));
        core.CollisionFreeze.ShouldBe(0);
        core.Collisions.ShouldBe((short)0);
        pushed.ShouldBe([core]);
    }

    [Test]
    public void Psi_ThePsiBeforeTheRestCheck_RunsNoRestTest()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        core.UnitState = 1;

        bool asleep = unit.Psi(Environment(countdown: 5), now: 1d, step: 0.5f, [], () => 0f);

        asleep.ShouldBeFalse();
        core.UnitState.ShouldBe(1, "untouched until the countdown reaches zero");
    }

    [Test]
    public void Psi_TheRestCheckWithEveryCoreResting_SleepsTheUnit()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        IvpImpactEnvironment environment = Environment(countdown: 1);

        bool asleep = unit.Psi(environment, now: 100d, step: 0.5f, [], () => 0f);

        asleep.ShouldBeTrue();
        unit.State.ShouldBe(8);
        core.UnitState.ShouldBe((int)IvpCoreMotion.Resting);
        environment.RestCheckCountdown.ShouldBe((short)15, "0xf less the jitter, which a zero random leaves alone");
    }

    [Test]
    public void Psi_TheRestCheckWithACoreMoving_LeavesTheUnitAwake()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        core.Position = (50d, 0d, 0d);

        bool asleep = unit.Psi(Environment(countdown: 1), now: 100d, step: 0.5f, [], () => 0f);

        asleep.ShouldBeFalse();
        unit.State.ShouldBe(0);
    }

    [Test]
    public void Psi_AFastSpin_SetsTheCarryBitAndResetsNoAnchors()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        core.AngularVelocity = (2f, 0f, 0f);
        core.RestAnchorTime = 1d;

        unit.Psi(Environment(countdown: 5), now: 50d, step: 0.5f, [], () => 0f);

        (unit.Flags & 0x400).ShouldBe(0x400);
        core.RestAnchorTime.ShouldBe(1d, "the anchors are reset a PSI later, when the bit has carried");
    }

    [Test]
    public void Psi_ThePsiAfterAFastSpin_ResetsTheAnchors()
    {
        (IvpSimulationUnit unit, IvpRigidBody core) = Unit();
        core.AngularVelocity = (2f, 0f, 0f);
        unit.Psi(Environment(countdown: 5), now: 50d, step: 0.5f, [], () => 0f);
        core.AngularVelocity = (0f, 0f, 0f);
        core.RestAnchorTime = 1d;

        unit.Psi(Environment(countdown: 5), now: 51d, step: 0.5f, [], () => 0f);

        core.RestAnchorTime.ShouldBe(51d);
        (unit.Flags & 0xc00).ShouldBe(0, "the carry bits are cleared after they fire");
    }

    [Test]
    public void Psi_AUnitWithAController_RunsIt()
    {
        (IvpSimulationUnit unit, _) = Unit();
        Counting controller = new();
        unit.Controllers.Add(controller);

        unit.Psi(Environment(countdown: 5), now: 1d, step: 0.25f, [], () => 0f);

        controller.Steps.ShouldBe([0.25f]);
    }

    private static (IvpSimulationUnit, IvpRigidBody) Unit()
    {
        IvpSimulationUnit unit = new();
        IvpRigidBody core = new()
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            Radius = 1f,

            // The anchors a core that has been sitting still would hold, so the rest test is about the PSI and not the fixture.
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
        };
        unit.Cores.Add(core);

        return (unit, core);
    }

    private static IvpImpactEnvironment Environment(short countdown) =>
        new()
        {
            InverseStep = 2d,
            Step = 0.5d,
            Limits = new IvpAnomalyLimits(1000f, 0, 1000f, 250, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(answer: false)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            RestDelay = 1f,
            RestCheckCountdown = countdown,
        };

    private sealed class Counting : IIvpUnitController
    {
        public List<float> Steps { get; } = [];

        public void Advance(IvpSimulationUnit unit, float psiStep) => Steps.Add(psiStep);
    }
}
