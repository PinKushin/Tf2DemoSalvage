using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Turning elapsed real time into demo ticks.
/// </summary>
/// <remarks>
/// **The rate comes from the demo and is never assumed.** TF2's usual interval is 0.015 — 66 ticks
/// a second — but it is a server setting: a box left at its default runs 33. That is a property of
/// how the server was set up, not of when the demo was made, so it cannot be inferred from the
/// protocol or the date.
///
/// Pure arithmetic, deliberately: a clock driven by a UI timer can only be tested by waiting, and
/// waiting in a test is the thing this project bans. Feed it elapsed seconds and it says which tick
/// to show.
/// </remarks>
public sealed class PlaybackClockTests
{
    [Test]
    public void Advance_AtOneSecond_MovesOneSecondOfTicks()
    {
        // 0.015 seconds a tick is 66.67 a second, so a second of real time is 66 whole ticks and a
        // fraction that must not be thrown away - see the accumulation test below.
        PlaybackClock clock = new(intervalPerTick: 0.015f, lastTick: 100_000);

        clock.Advance(1.0);

        clock.Tick.ShouldBe(66);
    }

    [Test]
    public void Advance_AtThirtyThreeTick_MovesHalfAsFar()
    {
        // **The case that a hardcoded 66.67 gets wrong**, and it is not hypothetical: a server left
        // at its default runs 33, which is a setup difference rather than an era one. Replayed at
        // 66 such a demo plays at double speed, and that looks like a fast server rather than a
        // bug.
        PlaybackClock clock = new(intervalPerTick: 0.030f, lastTick: 100_000);

        clock.Advance(1.0);

        clock.Tick.ShouldBe(33);
    }

    [Test]
    public void Advance_InSmallSteps_DoesNotLoseTheRemainder()
    {
        // **A frame is not a tick, and the leftover is where drift comes from.** At 60 frames a
        // second each frame is 0.0167 seconds, which is 1.11 ticks; truncating each frame loses
        // 0.11 of a tick sixty times a second and the demo runs ten per cent slow. The clock keeps
        // a fractional position for exactly this.
        PlaybackClock clock = new(intervalPerTick: 0.015f, lastTick: 100_000);

        for (int frame = 0; frame < 60; frame++)
        {
            clock.Advance(1.0 / 60.0);
        }

        clock.Tick.ShouldBe(66, "one second of frames is one second of ticks");
    }

    [Test]
    public void Advance_AtHalfSpeed_MovesHalfAsFar()
    {
        PlaybackClock clock = new(intervalPerTick: 0.015f, lastTick: 100_000) { TimeScale = 0.5 };

        clock.Advance(1.0);

        clock.Tick.ShouldBe(33);
    }

    [Test]
    public void Advance_StopsAtTheEndRatherThanRunningPast()
    {
        // The scrub bar's maximum is the last tick, and a clock that walked past it would ask the
        // timeline for frames that do not exist - which answers with the last one anyway, so the
        // demo would appear to freeze while still claiming to play.
        PlaybackClock clock = new(intervalPerTick: 0.015f, lastTick: 100);

        clock.Advance(10.0);

        clock.Tick.ShouldBe(100);
        clock.AtEnd.ShouldBeTrue();
    }

    [Test]
    public void Seek_MovesWithoutCarryingTheOldRemainder()
    {
        // Scrubbing sets the position outright. A leftover fraction from before the seek would make
        // the first tick after it arrive early, which is a subtle wrongness that only shows up as
        // playback that does not line up with the scrub bar.
        PlaybackClock clock = new(intervalPerTick: 0.015f, lastTick: 100_000);

        clock.Advance(0.014);
        clock.Seek(5_000);

        clock.Tick.ShouldBe(5_000);

        clock.Advance(0.014);

        clock.Tick.ShouldBe(5_000, "less than one tick of time has passed since the seek");
    }

    [Test]
    public void AnIntervalOfZero_FallsBackToTheEngineDefault()
    {
        // **A demo with no svc_ServerInfo still has to play.** DEFAULT_TICK_INTERVAL from Valve's
        // const.h is 0.015, and choosing it here rather than inside the timeline keeps the reader
        // honest about what the demo actually said.
        PlaybackClock clock = new(intervalPerTick: 0f, lastTick: 100_000);

        clock.Advance(1.0);

        clock.Tick.ShouldBe(66);
    }
}
