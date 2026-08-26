namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The two frame clocks, and why they are two.</summary>
/// <remarks>
/// **`_lastFrameAt` and `_flyWatch` were both fields of `MainForm`** (B188, D90), both measuring
/// "time since the previous frame", and **neither one's documentation mentioned the other** — one
/// argued its case against the playback clock, the other against `FramePacer`. Nobody had compared
/// them, which is what made "were the clocks consolidated?" an open question.
///
/// **They are still two here, deliberately, and that is the point of putting them in one file.**
/// They are stamped at different moments: one when a frame is ALLOWED to begin, the other when it
/// starts DRAWING. Merging them would shift the pacing reference by the render duration, which is a
/// change to frame limiting rather than a tidy-up — and B209 has two frame-pacing parity questions
/// still open. What is fixed is that the relationship is now visible in one place instead of
/// implicit across two fields that never referred to each other.
/// </remarks>
public sealed class FrameClockTests
{
    [Test]
    public void IsDue_WithNoLimit_IsAlwaysDue()
    {
        FakeElapsedTime allowed = new();
        FrameClock clock = new(allowed, new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 0).ShouldBeTrue();
        clock.IsDue(framesPerSecond: 0).ShouldBeTrue();
    }

    [Test]
    public void IsDue_WithNoLimit_DoesNotStampThePacingClock()
    {
        // **A faithful move of a quirk, recorded rather than quietly fixed.** `FrameIsDue` returned
        // on its first line when uncapped and never stamped, so the pacing reference goes stale for
        // as long as the cap is off. It is harmless today because the wait path is unreachable while
        // uncapped — and it is exactly the kind of thing that stops being harmless when somebody
        // adds a third reader, which is why it is pinned here rather than left to be rediscovered.
        FakeElapsedTime allowed = new();
        FrameClock clock = new(allowed, new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 0);

        allowed.Restarts.ShouldBe(0);
    }

    [Test]
    public void IsDue_TheVeryFirstCappedFrame_IsDueWithoutWaiting()
    {
        // Nothing has been drawn, so there is no interval to measure and the first frame must not
        // wait out a budget for a frame that never happened.
        FrameClock clock = new(new FakeElapsedTime(), new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 60).ShouldBeTrue();
    }

    [Test]
    public void IsDue_BeforeTheBudgetHasPassed_IsNotDue()
    {
        FakeElapsedTime allowed = new();
        FrameClock clock = new(allowed, new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 60);

        allowed.Seconds = 0.001d;

        clock.IsDue(framesPerSecond: 60).ShouldBeFalse();
    }

    [Test]
    public void IsDue_OnceTheBudgetHasPassed_IsDueAndStampsAgain()
    {
        // **The stamp is the half a due/not-due test cannot see.** A clock that answered correctly
        // and never restarted would report every subsequent frame as due for ever, which is a cap
        // that silently stops capping.
        FakeElapsedTime allowed = new();
        FrameClock clock = new(allowed, new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 60);

        int stamped = allowed.Restarts;
        allowed.Seconds = 1d;

        clock.IsDue(framesPerSecond: 60).ShouldBeTrue();
        allowed.Restarts.ShouldBe(stamped + 1);
    }

    [Test]
    public void Drew_TheFirstFrame_ReportsNoElapsedTime()
    {
        // **Zero rather than "since the program started".** The camera flies by this duration, so a
        // first frame reporting several seconds would fling it across the map — which is what a
        // stopwatch started at construction and read before its first restart would say.
        FrameClock clock = new(new FakeElapsedTime(), new FakeElapsedTime());

        clock.Drew().ShouldBe(0d);
    }

    [Test]
    public void Drew_AfterAFrame_ReportsThatFramesDurationAndRestarts()
    {
        FakeElapsedTime drawing = new();
        FrameClock clock = new(new FakeElapsedTime(), drawing);

        clock.Drew();

        drawing.Seconds = 0.033d;

        clock.Drew().ShouldBe(0.033d);
        clock.LastFrameSeconds.ShouldBe(0.033d);
    }

    [Test]
    public void Drew_AndIsDue_KeepSeparateClocks()
    {
        // **The claim the whole type exists to make.** These measure from different moments, so a
        // consolidation onto one clock would change frame pacing rather than tidy it. If somebody
        // merges them, this is what goes red.
        FakeElapsedTime allowed = new();
        FakeElapsedTime drawing = new();
        FrameClock clock = new(allowed, drawing);

        clock.IsDue(framesPerSecond: 60);

        int stampedAllowed = allowed.Restarts;

        clock.Drew();

        allowed.Restarts.ShouldBe(stampedAllowed, "drawing a frame must not move the pacing mark");
        drawing.Restarts.ShouldBeGreaterThan(0);
    }

    [Test]
    public void WaitFor_WithTimeToSpare_Sleeps()
    {
        // The decision is here; the act — Thread.Sleep or Thread.Yield — stays with the window,
        // which is the split `FramePacer` already had and this move preserves.
        FakeElapsedTime allowed = new();
        FrameClock clock = new(allowed, new FakeElapsedTime());

        clock.IsDue(framesPerSecond: 24);

        clock.WaitFor(framesPerSecond: 24).ShouldBe(FrameWait.Sleep);
    }
}
