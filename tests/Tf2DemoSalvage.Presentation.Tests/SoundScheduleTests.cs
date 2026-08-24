using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Which sounds start as playback moves, and which do not.
/// </summary>
/// <remarks>
/// **The whole of the audio wiring that can be reasoned about without a device**, which is why it
/// is a type rather than a loop inside the form. Every case here is one that would otherwise be
/// found by listening — and three of them are silent failures that sound like a working viewer
/// until you notice what is missing.
/// </remarks>
public sealed class SoundScheduleTests
{
    private static SceneSound At(int tick, string name = "sound") =>
        new(tick, name, 1, 0, 0, 1f, 75, 100, 0f, 0f, 0f, 0f);

    private static SoundSchedule Of(params int[] ticks) =>
        new([.. ticks.Select(tick => At(tick))]);

    [Test]
    public void Advance_TheFirstCall_StartsNothing()
    {
        SoundSchedule schedule = Of(10, 20, 30);

        // **Otherwise opening a demo fires everything before tick one at once**, including the map
        // ambience the signon block put at the recording's first tick. There is no previous
        // position for the first call to have advanced FROM, so it can only position the cursor.
        schedule.Advance(10).ShouldBeEmpty("the first call has nothing to have advanced from");
    }

    [Test]
    public void Advance_PlayingForward_StartsEachSoundExactlyOnce()
    {
        SoundSchedule schedule = Of(10, 20, 20, 30);

        schedule.Advance(5);

        List<SceneSound> played = [];

        for (int tick = 6; tick <= 40; tick++)
        {
            played.AddRange(schedule.Advance(tick));
        }

        // Exact, because the count is knowable: four sounds exist and playback crossed all of them.
        played.Count.ShouldBe(4, "every sound between the start and the end should have played once");
        played.Select(sound => sound.Tick).ShouldBe(new[] { 10, 20, 20, 30 });
    }

    [Test]
    public void Advance_StayingOnOneTick_DoesNotRepeatIt()
    {
        SoundSchedule schedule = Of(10);

        schedule.Advance(9);

        schedule.Advance(10).Count.ShouldBe(1, "the sound on the tick just reached should play");

        // **A paused viewer asks for the same tick every frame.** Repeating on each one is a
        // stutter rather than a sound, and it is the kind of defect that is obvious to hear and
        // invisible in any state a test would otherwise inspect.
        schedule.Advance(10).ShouldBeEmpty("a tick already played must not play again");
        schedule.Advance(10).ShouldBeEmpty();
    }

    [Test]
    public void Advance_ASeekForward_StartsNothingAndSaysItJumped()
    {
        SoundSchedule schedule = Of(10, 20, 30, 4000, 4010);

        schedule.Advance(5);

        // A scrub past thousands of ticks. Playing what was skipped would empty a match's worth of
        // gunfire into one frame, none of it belonging to the moment now on screen.
        IReadOnlyList<SceneSound> started = schedule.Advance(3990);

        started.ShouldBeEmpty("a seek must not replay everything it skipped");
        schedule.Jumped.ShouldBeTrue("the caller has to know to silence what is still playing");

        // **And the cursor must land in the right place**, which is the half a "played nothing"
        // assertion cannot see: a seek that repositioned wrongly would be silent here and then play
        // the wrong sounds, or none, from now on.
        schedule.Advance(4000).Select(sound => sound.Tick).ShouldBe(new[] { 4000 });
    }

    [Test]
    public void Advance_ASeekBackward_StartsNothingAndRepositions()
    {
        SoundSchedule schedule = Of(10, 20, 30);

        schedule.Advance(5);
        schedule.Advance(30);

        schedule.Advance(9).ShouldBeEmpty("rewinding is a seek, not playback");
        schedule.Jumped.ShouldBeTrue();

        // The point of rewinding is that the sounds play again on the way back through.
        List<SceneSound> again = [];

        for (int tick = 10; tick <= 30; tick++)
        {
            again.AddRange(schedule.Advance(tick));
        }

        again.Count.ShouldBe(3, "after rewinding, the sounds should play again as they are reached");
    }

    [Test]
    public void Advance_AStutterInsideTheCatchUpWindow_IsPlaybackNotASeek()
    {
        SoundSchedule schedule = Of(10, 60, 110);

        schedule.Advance(5);

        // **A dropped frame is not a seek.** A stalled frame, a collection, or the first frame
        // after a pause can advance many ticks at once, and treating that as a jump would drop the
        // sounds in the gap silently — which sounds exactly like a viewer that works.
        IReadOnlyList<SceneSound> started = schedule.Advance(5 + SoundSchedule.CatchUpTicks);

        started.Count.ShouldBe(3, "a gap inside the catch-up window should still play its sounds");
        schedule.Jumped.ShouldBeFalse();
    }

    [Test]
    public void Advance_AnEmptyTimeline_IsHarmless()
    {
        SoundSchedule schedule = new([]);

        schedule.Advance(0).ShouldBeEmpty();
        schedule.Advance(1000).ShouldBeEmpty();
    }
}
