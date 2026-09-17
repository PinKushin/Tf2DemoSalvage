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

        IvpPhysicsPipeline.Psi(environment, units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, _ => { }, () => 0f, now: 2d);

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
            Environment(countdown: 1), units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, _ => { }, () => 0f, now: 100d);

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
            _ => order.Add("recheckInvalid"),
            _ => order.Add("examine"),
            () => 0f,
            now: 2d);

        order.ShouldBe(["minimize", "examine"]);
    }

    /// <remarks>
    /// <c>FUN_180074240</c> (<see cref="IvpCollisionObject.RecheckInvalid"/>) calls
    /// <c>IvpMindistMinimize::MinimizeWithoutBudget</c> (<c>FUN_180095ad0</c>) on an invalid pair, never the budgeted
    /// <c>FUN_180095cb0</c> phase 0/3 already use — the two are the same routine but for one stack constant (a step budget of
    /// 20 versus none), so wiring the wrong one in means an invalid pair is minimized with a budget it should never get.
    /// </remarks>
    [Test]
    public void Psi_AnInvalidMindist_IsRecheckedWithTheNoBudgetMinimizeNotThePhaseThreeOne()
    {
        (IvpUnitManager units, _, IvpRigidBody core) = Units();
        IvpMindistManager mindists = new();
        IvpCollisionObject first = new() { Core = core };
        IvpCollisionObject second = new() { Core = new IvpRigidBody() };
        core.Objects.Add(first);
        IvpMinList<IvpMindist> queue = new();
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f);
        mindists.LinkExact(mindist, first, second);
        mindists.Invalidate(mindist, first, second, queue);
        List<string> order = [];

        IvpPhysicsPipeline.Psi(
            Environment(countdown: 5),
            units,
            mindists,
            queue,

            // The budgeted minimize must never reach an invalid pair through RecheckInvalid; if it did, this would run before
            // the pair is revalidated back onto the exact list and the assertion below would see it in `order`.
            _ => order.Add("budgeted"),

            // Leaves the pair's flags exactly at 0x4000 — parked — so RecheckInvalid's own condition
            // (`(flags & 0xC000) != 0x4000`) does not revalidate it, and phase 3 never gets a chance to touch it either way.
            mindist => { order.Add("noBudget"); mindist.Flags = (mindist.Flags & ~0xC000) | 0x4000; },
            _ => order.Add("examine"),
            () => 0f,
            now: 2d);

        order.ShouldBe(["noBudget"], "only the no-budget minimize reaches an invalid pair, never phase 3's budgeted one");
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
