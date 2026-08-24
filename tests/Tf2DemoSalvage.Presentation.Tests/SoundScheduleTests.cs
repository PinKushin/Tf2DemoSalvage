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

    /// <summary>A sound on a named channel, which is what can be started and stopped.</summary>
    private static SceneSound On(
        int tick, int entity, int channel, bool stop = false, string name = "ambient/machine_hum") =>
        new(tick, name, 1, entity, channel, 1f, 75, 100, 0f, 0f, 0f, 0f, IsStop: stop);

    [Test]
    public void LiveAt_ALoopStartedEarlier_IsStillLive()
    {
        // **The defect this exists for.** cp_process starts six `)ambient/machine_hum.wav` at tick
        // 4 and restarts them only at round boundaries. `Advance` is a cursor over EVENTS, so after
        // a seek past tick 4 nothing ever starts them again and the map's machinery is silent for
        // the rest of the demo — which the owner heard as "the pc hum isnt playing at all".
        //
        // A looping ambient is STATE, not an event: it holds until something stops it.
        SoundSchedule schedule = new([On(4, 104, 6)]);

        schedule.LiveAt(50000).Select(sound => sound.EntityIndex).ShouldBe(new[] { 104 });
    }

    [Test]
    public void LiveAt_ALoopStoppedBeforeTheTick_IsNotLive()
    {
        SoundSchedule schedule = new([On(4, 104, 6), On(334, 104, 6, stop: true)]);

        schedule.LiveAt(50000).ShouldBeEmpty("a stopped loop must not be resurrected by a seek");
    }

    [Test]
    public void LiveAt_ALoopRestartedAfterAStop_IsLive()
    {
        // cp_process's exact sequence at tick 334: the round restart stops all six and starts them
        // again in the same tick. Reading only the last event per key gets this right; reading the
        // first would leave the map permanently silent after every round.
        SoundSchedule schedule = new(
            [On(4, 104, 6), On(334, 104, 6, stop: true), On(334, 104, 6)]);

        schedule.LiveAt(50000).Count.ShouldBe(1, "the restart is the later event and wins");
    }

    [Test]
    public void LiveAt_TheSameChannelOnDifferentEntities_KeepsEveryOne()
    {
        // **The control, and the one that catches a key of channel alone.** All six of cp_process's
        // machine hums are CHAN_STATIC — six entities, one channel — so a schedule that tracked
        // channels rather than (entity, channel) pairs would report exactly one hum and sound like
        // a working viewer with five sixths of the map's machinery missing.
        SoundSchedule schedule = new(
            [On(4, 104, 6), On(4, 105, 6), On(4, 106, 6), On(4, 107, 6)]);

        schedule.LiveAt(50000).Count.ShouldBe(4, "each entity holds its own channel");
    }

    [Test]
    public void LiveAt_ALoopStartedAfterTheTick_IsNotLive()
    {
        SoundSchedule schedule = new([On(4, 104, 6), On(18022, 105, 6)]);

        schedule.LiveAt(1000).Select(sound => sound.EntityIndex)
            .ShouldBe(new[] { 104 }, "a seek must not start what has not happened yet");
    }

    [Test]
    public void LiveAt_TheAutoChannel_IsNotTracked()
    {
        // **`CHAN_AUTO` is 0 and means "the engine picks", so such a sound cannot be stopped and is
        // meant to overlap.** Re-establishing every auto-channel sound ever started would replay a
        // match's worth of gunfire into one frame — the exact failure `Advance` avoids by not
        // playing across a seek.
        SoundSchedule schedule = new([On(4, 104, 0)]);

        schedule.LiveAt(50000).ShouldBeEmpty("an auto-channel sound is a one-shot by construction");
    }

    [Test]
    public void LiveAt_AReplacementOnOneChannel_KeepsOnlyTheLater()
    {
        SoundSchedule schedule = new(
            [On(4, 104, 6, name: "first"), On(900, 104, 6, name: "second")]);

        schedule.LiveAt(50000).Single().Name
            .ShouldBe("second", "a channel plays one sound at a time");
    }

    [Test]
    public void Repositioned_TheFirstCallAndASeek_BothAskForTheLoopsBack()
    {
        SoundSchedule schedule = Of(10, 20, 30);

        // **The first call counts.** `Jumped` is deliberately false there — there is nothing in
        // flight to silence — but the loops still have to be established, and using `Jumped` for
        // both is why opening a demo mid-way started none of its ambience.
        schedule.Advance(10);
        schedule.Repositioned.ShouldBeTrue("opening a demo positions the cursor and must re-establish");

        schedule.Advance(11);
        schedule.Repositioned.ShouldBeFalse("ordinary playback establishes nothing");

        schedule.Advance(5000);
        schedule.Repositioned.ShouldBeTrue("a seek must re-establish the loops at its destination");
    }
}
