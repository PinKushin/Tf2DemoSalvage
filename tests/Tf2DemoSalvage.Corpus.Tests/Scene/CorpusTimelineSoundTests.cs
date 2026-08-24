using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The timeline carries the sounds a demo plays, at the ticks it plays them.
/// </summary>
/// <remarks>
/// **Everything needed for audio existed and none of it was connected (B168).** The container
/// decodes <c>svc_Sounds</c> into <see cref="Tf2DemoSalvage.Core.Net.DecodedSound"/>, the
/// <c>soundprecache</c> table turns a sound number into a name, and `Tf2DemoSalvage.Audio` reads
/// the wave, applies Valve's gain and attenuation and hands back samples — with 102 tests behind
/// it and no caller anywhere. The viewer's project file does not even reference it.
///
/// The missing link is this one: `DemoTimeline` never handled `SoundsMessage`, so the scene a
/// viewer plays from had no sounds in it at all. A sink could not have been wired to anything.
///
/// **This is the first of the three pieces and deliberately the one with no device in it.** Whether
/// a speaker makes a noise is not answerable in a test suite that runs on a measurement box; whether
/// the recording's sounds reached the timeline, with names and ticks, is — and it is the part that
/// would otherwise be assumed. `docs/memory/ask-whether-the-data-arrived.md` is the reason it comes
/// first.
/// </remarks>
public sealed class CorpusTimelineSoundTests
{
    private const string Demo = "movement-test-pov-cp_process";

    [Test]
    public void Sounds_ARealDemo_ArePlacedOnTheTimelineWithNames()
    {
        string path = Corpus.Demo(Demo);

        DemoTimeline timeline = TimelineCache.For(path);

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(path)}: {timeline.Sounds.Count} sounds on the timeline");

        // **Measured, not guessed.** The first version of this predicted "hundreds" on the reasoning
        // that footsteps alone would be one every few ticks, and the real number is 89 across 6,826
        // ticks. The reasoning was wrong rather than the decode: a great deal of Source's audio is
        // predicted on the client and never travels — svc_Sounds carries what the SERVER chose to
        // send, so a solo movement recording is genuinely this quiet.
        //
        // The bound is therefore a floor under the measurement rather than a claim about how noisy
        // a demo should be, and its job is to catch the stream being lost, not to describe the game.
        timeline.Sounds.Count.ShouldBeGreaterThan(
            50,
            "svc_Sounds is being lost: this demo carried 89 when the accumulation was written");

        // **Named, not merely numbered.** A sound number is an index into the demo's own
        // soundprecache table, and an unresolved one is exactly as useless as no sound at all —
        // there is nothing to open. This is the assertion that fails if the table is not applied.
        List<SceneSound> named = [.. timeline.Sounds.Where(sound => sound.Name.Length > 0)];

        TestContext.Out.WriteLine(
            $"  {named.Count} of {timeline.Sounds.Count} resolved to a name");

        TestContext.Out.WriteLine(
            "  ticks in recorded order: " +
            string.Join(
                ", ",
                timeline.Sounds.Take(20).Select(s => s.Tick.ToString(CultureInfo.InvariantCulture))));

        foreach (SceneSound sound in named.Take(6))
        {
            TestContext.Out.WriteLine(
                $"  tick {sound.Tick.ToString(CultureInfo.InvariantCulture)} " +
                $"entity {sound.EntityIndex.ToString(CultureInfo.InvariantCulture)} " +
                $"'{sound.Name}' vol {sound.Volume.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        named.ShouldNotBeEmpty(
            "not one sound resolved to a name, so the soundprecache table never reached the walk");

        // **Ordered by tick, because a player consumes them in tick order and a sort at playback
        // would be per frame.** Also a cheap check that the tick is being recorded at all: a
        // constant would satisfy "ordered" but is caught by the distinct count below.
        timeline.Sounds
            .Select(sound => sound.Tick)
            .ShouldBe(timeline.Sounds.Select(sound => sound.Tick).Order(), "sounds should be in tick order");

        timeline.Sounds
            .Select(sound => sound.Tick)
            .Distinct()
            .Count()
            .ShouldBeGreaterThan(
                10,
                "every sound landed on the same tick, so the tick is not being recorded");
    }
}
