using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The PSI event — <c>FUN_18008a020</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): the base and the next PSI are set, the queue rebased, the pipeline run,
/// and the event requeued one step later. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpPsiEventTests
{
    [Test]
    public void RunPsi_APsi_SetsTheBaseAndTheNextPsiAndRequeuesItself()
    {
        IvpImpactEnvironment environment = Environment();
        PhysicsTimeManager time = new();
        IvpUnitManager units = new();

        IvpPsiEvent.RunPsi(
            environment, time, units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, _ => { }, () => 0f, now: 4d);

        environment.RebaseBase.ShouldBe(4d);
        environment.PsiEnd.ShouldBe(4.5d, "the step is added as a float");
        time.Base.ShouldBe(4d);
        time.Now.ShouldBe(0d);
        time.Count.ShouldBe(1, "the event requeued itself");
    }

    [Test]
    public void Start_ThenDraining_RunsOnePsiPerStep()
    {
        IvpImpactEnvironment environment = Environment();
        PhysicsTimeManager time = new();
        IvpUnitManager units = new();
        IvpSimulationUnit unit = new();
        IvpRigidBody core = Core();
        unit.Cores.Add(core);
        units.Active.Add(unit);

        IvpPsiEvent.Start(
            environment, time, units, new IvpMindistManager(), new IvpMinList<IvpMindist>(), _ => { }, _ => { }, _ => { }, () => 0f);
        int fired = time.DrainUntil(1.2d, _ => { });

        fired.ShouldBe(3, "one at zero, then one per half-second step");
        core.LastStepped.ShouldBe(1d, "the last PSI ran at one second");
    }

    /// <remarks>**The manager's own clock goes back to the base**, which only shows after an event has moved it.</remarks>
    [Test]
    public void RunPsi_AfterAnEarlierEventMovedTheClock_PutsItBackToZero()
    {
        IvpImpactEnvironment environment = Environment();
        PhysicsTimeManager time = new();
        time.Add(new PhysicsEvent(0.25f, _ => { }));
        time.DrainUntil(0.3d, _ => { });
        time.Now.ShouldNotBe(0d, "the control: the drained event moved the clock");

        IvpPsiEvent.RunPsi(
            environment, time, new IvpUnitManager(), new IvpMindistManager(), new IvpMinList<IvpMindist>(),
            _ => { }, _ => { }, _ => { }, () => 0f, now: 2d);

        time.Now.ShouldBe(0d);
    }

    [Test]
    public void RunPsi_QueuedEvents_AreRebasedOntoTheNewBase()
    {
        IvpImpactEnvironment environment = Environment();
        PhysicsTimeManager time = new();
        time.Add(new PhysicsEvent(3f, _ => { }));

        IvpPsiEvent.RunPsi(
            environment, time, new IvpUnitManager(), new IvpMindistManager(), new IvpMinList<IvpMindist>(),
            _ => { }, _ => { }, _ => { }, () => 0f, now: 2d);

        time.Base.ShouldBe(2d, "every queued time is now measured from here");
        time.Count.ShouldBe(2);
    }

    private static IvpRigidBody Core() =>
        new()
        {
            Orientation = (0d, 0d, 0d, 1d),
            WorkingOrientation = (0d, 0d, 0d, 1d),
            Radius = 1f,
            RestAnchorOrientation = (0f, 0f, 0f, 1f),
            SettleAnchorOrientation = (0f, 0f, 0f, 1f),
        };

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
