using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Following a looping sound so it can be re-attenuated as the listener moves.
/// </summary>
/// <remarks>
/// **The whole of B169's logic, without a device.** A one-shot spatialised once is correct; a loop
/// spatialised once keeps the gain implied by wherever the listener stood when it began, for as long
/// as it plays. The owner heard the consequence as the map's ambience being inaudible — six hums on
/// cp_process, all started at the recording's first tick.
///
/// Device-free on purpose: CI and the measurement boxes have no sound card, and a test that needed
/// one would skip exactly where this matters.
/// </remarks>
public sealed class ActiveLoopsTests
{
    /// <summary>A loop at a position, at Valve's ordinary <c>SNDLVL_NORM</c>.</summary>
    private static SceneSound Hum(float x, int entity = 1, int channel = 6) =>
        new(30, ")ambient/machine_hum.wav", 1, entity, channel, 1f, 75, 100, 0f, x, 0f, 0f);

    [Test]
    public void GainsAt_MovingTowardALoop_RaisesItsGain()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 1000f));

        float far = loops.GainsAt(0f, 0f, 0f).Single().Gain;
        float near = loops.GainsAt(900f, 0f, 0f).Single().Gain;

        // **The whole point, and it is a comparison rather than a pinned value.** Which exact gain
        // Valve's curve gives at 1000 units is SoundGain's business and is tested there; what this
        // type must guarantee is that the answer TRACKS the listener. A gain fixed at start would
        // return the same number twice, which is precisely the bug.
        near.ShouldBeGreaterThan(far, "walking toward a loop should make it louder");
    }

    [Test]
    public void GainsAt_StandingOnALoop_IsFullVolume()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 0f));

        // Inside the reference distance the curve is unattenuated, so the sound's own volume is all
        // that remains. An exact prediction: 1.0, not merely "loud".
        loops.GainsAt(0f, 0f, 0f).Single().Gain.ShouldBe(1f, 0.001d);
    }

    [Test]
    public void GainsAt_ALoopOutOfRange_IsReportedAsSilentRatherThanDropped()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 100_000f));

        (int Entity, int Channel, float Gain)[] gains = [.. loops.GainsAt(0f, 0f, 0f)];

        // **Present and silent, not absent.** The sound is still playing; the sink has to be told it
        // is now inaudible. Dropping the entry would leave the source holding its last audible gain
        // for ever, which is the exact failure this type was written to fix — so "no entry" and
        // "gain zero" are opposite outcomes here, not equivalent ones.
        gains.Length.ShouldBe(1, "an out-of-range loop is still playing and still needs updating");
        gains[0].Gain.ShouldBe(0f);
    }

    [Test]
    public void Track_ASecondLoopOnOneChannel_ReplacesTheFirst()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 0f, entity: 1, channel: 6));
        loops.Track(Hum(x: 5000f, entity: 1, channel: 6));

        loops.Count.ShouldBe(1, "a named channel holds one sound per entity, as the sink does");

        // And it is the SECOND one that survived — a check the count alone cannot make, since
        // keeping the first would also leave exactly one entry.
        loops.GainsAt(0f, 0f, 0f).Single().Gain.ShouldBeLessThan(
            1f, "the surviving loop should be the distant one that replaced it");
    }

    [Test]
    public void Track_LoopsOnDifferentChannels_AreFollowedSeparately()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 0f, entity: 1, channel: 6));
        loops.Track(Hum(x: 0f, entity: 1, channel: 5));
        loops.Track(Hum(x: 0f, entity: 2, channel: 6));

        // The control on the key: entity and channel TOGETHER identify a voice, so three distinct
        // pairs are three loops. A key on either alone would collapse these.
        loops.Count.ShouldBe(3);
    }

    [Test]
    public void Forget_AStoppedLoop_IsNoLongerFollowed()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 0f, entity: 1, channel: 6));

        loops.Forget(1, 6).ShouldBeTrue();
        loops.GainsAt(0f, 0f, 0f).ShouldBeEmpty();

        loops.Forget(1, 6).ShouldBeFalse("forgetting twice should report that nothing was there");
    }

    [Test]
    public void Clear_AfterASeek_ForgetsEverything()
    {
        ActiveLoops loops = new();

        loops.Track(Hum(x: 0f, entity: 1, channel: 6));
        loops.Track(Hum(x: 0f, entity: 2, channel: 6));

        loops.Clear();

        // A seek silences the sink, so these voices no longer exist. Keeping them would
        // re-attenuate sources that are gone and never restart the ones that should now run.
        loops.Count.ShouldBe(0);
    }
}
