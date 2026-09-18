using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The PSI event — <c>FUN_18008a020</c> (B369, D172).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`): the base and the next PSI are set, the ONE queue rebased, the pipeline run,
/// and the event requeued one step later. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpPsiEventTests
{
    [Test]
    public void SimulateTimeEvent_APsi_SetsTheBaseAndTheNextPsiAndRequeuesItself()
    {
        IvpImpactEnvironment environment = Environment();
        IvpTimeManager<IIvpTimeEvent> time = new();
        IvpPsiEvent psi = Unqueued(Start(environment, time, new IvpUnitManager()), time);

        psi.SimulateTimeEvent(4d);

        environment.RebaseBase.ShouldBe(4d);
        environment.PsiEnd.ShouldBe(4.5d, "the step is added as a float");
        time.Base.ShouldBe(4d);
        time.Clock.ShouldBe(0d);
        time.Queue.Count.ShouldBe(1, "the event requeued itself");
        time.Queue.ValueOf(psi.QueueSlot!.Value).ShouldBe(0.5f, "a step from its own base");
    }

    [Test]
    public void Start_ThenRunning_RunsOnePsiPerStep()
    {
        IvpImpactEnvironment environment = Environment();
        IvpTimeManager<IIvpTimeEvent> time = new();
        IvpUnitManager units = new();
        IvpSimulationUnit unit = new();
        IvpRigidBody core = Core();
        unit.Cores.Add(core);
        units.Active.Add(unit);
        Start(environment, time, units);

        int fired = 0;
        double now = 0d;
        time.Run(1.2d, at => now = at, due =>
        {
            ((IvpPsiEvent)due).SimulateTimeEvent(now);
            fired++;
        });

        fired.ShouldBe(3, "one at zero, then one per half-second step");
        core.LastStepped.ShouldBe(1d, "the last PSI ran at one second");
    }

    /// <remarks>**The manager's own clock goes back to the base**, which only shows after an event has moved it.</remarks>
    [Test]
    public void SimulateTimeEvent_AfterAnEarlierEventMovedTheClock_PutsItBackToZero()
    {
        IvpImpactEnvironment environment = Environment();
        IvpTimeManager<IIvpTimeEvent> time = new();
        IvpPsiEvent psi = Unqueued(Start(environment, time, new IvpUnitManager()), time);
        Other earlier = new();
        earlier.QueueSlot = time.Queue.Add(earlier, 0.25f);
        time.Run(0.3d, _ => { }, _ => { });
        time.Clock.ShouldNotBe(0d, "the control: the earlier event moved the clock");

        psi.SimulateTimeEvent(2d);

        time.Clock.ShouldBe(0d);
    }

    /// <remarks>
    /// **Every queued event — a pair's too — loses the absolute time and is measured from the new base**: `FUN_18008a020`
    /// subtracts `(float)now` from each entry. One event at `3` and a PSI at `2` leave it at `1`.
    /// </remarks>
    [Test]
    public void SimulateTimeEvent_QueuedEvents_AreRebasedOntoTheNewBase()
    {
        IvpImpactEnvironment environment = Environment();
        IvpTimeManager<IIvpTimeEvent> time = new();
        IvpPsiEvent psi = Unqueued(Start(environment, time, new IvpUnitManager()), time);
        Other queued = new();
        queued.QueueSlot = time.Queue.Add(queued, 3f);

        psi.SimulateTimeEvent(2d);

        time.Base.ShouldBe(2d, "every queued time is now measured from here");
        time.Queue.Count.ShouldBe(2);
        time.Queue.ValueOf(queued.QueueSlot.Value).ShouldBe(1f);
    }

    /// <summary>Queues the first PSI event with a pipeline that does nothing of its own.</summary>
    private static IvpPsiEvent Start(IvpImpactEnvironment environment, IvpTimeManager<IIvpTimeEvent> time, IvpUnitManager units) =>
        IvpPsiEvent.Start(environment, time, units, new IvpMindistManager(), _ => { }, _ => { }, _ => { }, _ => { }, () => 0f);

    /// <summary>Takes the event out of the queue, as the time manager's loop does before firing it.</summary>
    private static IvpPsiEvent Unqueued(IvpPsiEvent psi, IvpTimeManager<IIvpTimeEvent> time)
    {
        time.Queue.Remove(psi.QueueSlot!.Value);
        psi.QueueSlot = null;
        return psi;
    }

    private sealed class Other : IIvpTimeEvent
    {
        public int? QueueSlot { get; set; }
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
