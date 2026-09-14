using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's event loop — the time manager's <c>FUN_18008a110</c>, and the environment's clock set <c>FUN_180082460</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Found: the event loop*, read again for the port). While the queue's
/// minimum is under `(float)(target − base)`, or either is NaN, the head is unlinked and marked unqueued, both clocks are
/// set from the captured minimum, the event fires, and the stop flag is checked; afterwards the environment's clock is
/// snapped to the target. The queue's base is `10` throughout.
/// </remarks>
public sealed class IvpTimeManagerConformanceTests
{
    private const double Base = 10d;

    /// <remarks>
    /// **Events under the limit fire in order, each after the clock is set to its time**: at a target of `10.25`, events at
    /// `0.1`, `0.2` fire and `0.3` waits. The environment's clock goes `10 + (double)0.1f`, `10 + (double)0.2f`, then the
    /// target, and the fired events are unqueued.
    /// </remarks>
    [Test]
    public void Run_EventsUnderTheTarget_FireInOrderAfterTheClockIsSetToEach()
    {
        Fixture fixture = new();
        TestEvent late = fixture.Queue("late", 0.3f);
        TestEvent first = fixture.Queue("first", 0.1f);
        TestEvent second = fixture.Queue("second", 0.2f);

        fixture.Run(10.25d);

        fixture.Fired.ShouldBe(["first@10.100000001490116", "second@10.200000002980232"]);
        fixture.Clock.ShouldBe([Base + 0.1f, Base + 0.2f, 10.25d]);
        fixture.Manager.Clock.ShouldBe((double)0.2f);
        first.QueueSlot.ShouldBeNull();
        second.QueueSlot.ShouldBeNull();
        late.QueueSlot.ShouldNotBeNull();
    }

    /// <remarks>**An event at the limit does not fire** — `COMISS` then `JNC` leaves the loop when the minimum is not under it.</remarks>
    [Test]
    public void Run_AnEventAtTheTarget_DoesNotFire()
    {
        Fixture fixture = new();
        fixture.Queue("at the target", 0.25f);

        fixture.Run(10.25d);

        fixture.Fired.ShouldBeEmpty();
        fixture.Clock.ShouldBe([10.25d]);
    }

    /// <remarks>
    /// **The stop flag is read after each fire**: an event that raises it ends the run with the next due event unfired, and
    /// the clock is still snapped to the target.
    /// </remarks>
    [Test]
    public void Run_AnEventThatRaisesTheStopFlag_EndsTheRunAndStillSnapsTheClock()
    {
        Fixture fixture = new() { StopOn = "first" };
        fixture.Queue("first", 0.1f);
        fixture.Queue("second", 0.2f);

        fixture.Run(10.25d);

        fixture.Fired.ShouldBe(["first@10.100000001490116"]);
        fixture.Clock[^1].ShouldBe(10.25d);
    }

    /// <remarks>**The queue is read again after each fire**: an event queued by a firing event fires in the same run when it is due.</remarks>
    [Test]
    public void Run_AnEventQueuedWhileFiring_FiresInTheSameRunWhenDue()
    {
        Fixture fixture = new() { QueueOn = ("first", "follower", 0.15f) };
        fixture.Queue("first", 0.1f);

        fixture.Run(10.25d);

        fixture.Fired.ShouldBe(["first@10.100000001490116", "follower@10.150000005960464"]);
    }

    /// <remarks>
    /// **An empty queue whose limit is over its `1e10f` minimum would be read from index `0xffff`** in the engine: refused.
    /// </remarks>
    [Test]
    public void Run_AnEmptyQueueWithALimitOverTenBillion_IsRefused() =>
        Should.Throw<InvalidOperationException>(() => new Fixture().Run(Base + 2e10d));

    private sealed class TestEvent(string name) : IIvpTimeEvent
    {
        public string Name => name;

        public int? QueueSlot { get; set; }
    }

    private sealed class Fixture
    {
        public IvpTimeManager<TestEvent> Manager { get; } = new() { Base = Base };

        public List<string> Fired { get; } = [];

        public List<double> Clock { get; } = [];

        public string? StopOn { get; init; }

        public (string After, string Name, float Time)? QueueOn { get; init; }

        public TestEvent Queue(string name, float time)
        {
            TestEvent queued = new(name);
            queued.QueueSlot = Manager.Queue.Add(queued, time);
            return queued;
        }

        public void Run(double target) =>
            Manager.Run(
                target,
                Clock.Add,
                fired =>
                {
                    Fired.Add($"{fired.Name}@{Clock[^1]:R}");

                    if (fired.Name == StopOn)
                    {
                        Manager.Stopping = true;
                    }

                    if (QueueOn is { } follow && fired.Name == follow.After)
                    {
                        Queue(follow.Name, follow.Time);
                    }
                });
    }
}
