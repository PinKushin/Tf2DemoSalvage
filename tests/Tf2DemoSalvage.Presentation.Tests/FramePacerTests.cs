namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>When the next frame is due, and what to do until it is.</summary>
/// <remarks>
/// **This was `MainForm.FrameIsDue` and `MainForm.WaitForTheNextFrame`** (B208), which between them
/// computed `1d / FrameRateLimit` twice — the same quantity, derived independently in two methods
/// that had to agree.
///
/// **The pacing POLICY is here; the threading primitive stays in the window.** `Thread.Sleep` and
/// `Thread.Yield` belong with the message pump, and keeping them out of this type is what lets the
/// decision be tested without any test ever sleeping.
/// </remarks>
public sealed class FramePacerTests
{
    [Test]
    public void IsDue_WithNoLimitSet_IsAlwaysDue()
    {
        // Zero and below mean "as fast as the machine will go", which is the default and the case
        // that must not accidentally acquire a budget of infinity or a division by zero.
        FramePacer.IsDue(sinceLastFrame: 0d, framesPerSecond: 0).ShouldBeTrue();
        FramePacer.IsDue(sinceLastFrame: 0d, framesPerSecond: -1).ShouldBeTrue();
    }

    [Test]
    public void IsDue_BeforeTheBudgetIsSpent_IsNotDue()
    {
        // At 60 fps the budget is 16.7 ms, so 8 ms in is early.
        FramePacer.IsDue(sinceLastFrame: 0.008d, framesPerSecond: 60).ShouldBeFalse();
    }

    [Test]
    public void IsDue_OnceTheBudgetIsSpent_IsDue()
    {
        // **The control for the case above.** Without a due case, "not due" would be satisfied by a
        // pacer that never let a frame through at all — which is a black window, not a slow one.
        FramePacer.IsDue(sinceLastFrame: 0.020d, framesPerSecond: 60).ShouldBeTrue();
    }

    [Test]
    public void Budget_ForACommonRate_IsItsReciprocal()
    {
        // Stated once so the two callers cannot derive it differently, which is what they did.
        FramePacer.Budget(60).ShouldBe(1d / 60d, 1e-9);
        FramePacer.Budget(144).ShouldBe(1d / 144d, 1e-9);
    }

    [Test]
    public void WaitFor_WithPlentyOfTimeLeft_Sleeps()
    {
        // **A whole scheduler quantum or more remaining means sleeping is safe**, and sleeping is
        // what gives the CPU back. At 30 fps the budget is 33 ms, so 1 ms in leaves 32 — far more
        // than the granularity a sleep can resolve.
        FramePacer.WaitFor(sinceLastFrame: 0.001d, framesPerSecond: 30)
            .ShouldBe(FrameWait.Sleep);
    }

    [Test]
    public void WaitFor_WithLessThanASchedulerQuantumLeft_YieldsInstead()
    {
        // **The reason a threshold exists at all.** `Thread.Sleep(1)` does not return in 1 ms — it
        // returns on the next scheduler tick, about 16 ms away. Sleeping with less than that left
        // overshoots the frame and turns a limiter into a stutter, so the last stretch is spun.
        FramePacer.WaitFor(sinceLastFrame: 0.030d, framesPerSecond: 30)
            .ShouldBe(FrameWait.Yield);
    }

    [Test]
    public void WaitFor_WithNoLimitSet_DoesNotWaitAtAll()
    {
        // Uncapped means uncapped: neither sleeping nor yielding, or the "no limit" setting would
        // quietly cost a scheduler round trip per frame.
        FramePacer.WaitFor(sinceLastFrame: 0d, framesPerSecond: 0).ShouldBe(FrameWait.None);
    }
}
