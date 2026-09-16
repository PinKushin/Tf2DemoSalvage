using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The PSI's pipeline — <c>FUN_180082560</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The PSI around the units*): the phase byte walks 0, 2, 3, 4, 5, the awake
/// units run their PSI, every core they collect is stepped, the hull managers are told, and the mindist list is minimized and
/// then examined. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpPhysicsPipelineTests
{
    [Test]
    public void Psi_AnAwakeUnit_StepsItsCoreAndLeavesThePhaseAtFive()
    {
        (IvpUnitManager units, IvpSimulationUnit unit, IvpRigidBody core) = Units();
        IvpImpactEnvironment environment = Environment(countdown: 5);

        IvpPhysicsPipeline.Psi(environment, units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, () => 0f, now: 2d);

        core.LastStepped.ShouldBe(2d, "the core was collected in phase 2 and stepped in phase 3");
        core.InverseStep.ShouldBe(2f, "the step handed down is the environment's own, 0.5");
        environment.Phase.ShouldBe(5);
        units.Active.ShouldBe([unit]);
    }

    [Test]
    public void Psi_AUnitThatFallsAsleep_MovesToTheSleepingListWithItsCoreStillStepped()
    {
        (IvpUnitManager units, IvpSimulationUnit unit, IvpRigidBody core) = Units();

        IvpPhysicsPipeline.Psi(
            Environment(countdown: 1), units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, () => 0f, now: 100d);

        units.Active.ShouldBeEmpty();
        units.Sleeping.ShouldBe([unit]);
        core.LastStepped.ShouldBe(100d, "the cores were collected before the rest check ran");
    }

    [Test]
    public void Psi_AnExactMindist_IsMinimizedThenExamined()
    {
        (IvpUnitManager units, _, _) = Units();
        IvpMindistManager mindists = new();
        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f);
        mindists.LinkExact(mindist, first, second);
        List<string> order = [];

        IvpPhysicsPipeline.Psi(
            Environment(countdown: 5),
            units,
            mindists,
            new IvpMinList<IvpMindist>(),
            _ => order.Add("minimize"),
            _ => order.Add("examine"),
            () => 0f,
            now: 2d);

        order.ShouldBe(["minimize", "examine"]);
    }

    private static (IvpUnitManager, IvpSimulationUnit, IvpRigidBody) Units()
    {
        IvpRigidBody core = new()
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            Radius = 1f,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
        };
        IvpSimulationUnit unit = new();
        unit.Cores.Add(core);
        IvpUnitManager units = new();
        units.Active.Add(unit);

        return (units, unit, core);
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
}
