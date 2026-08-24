using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a real match actually plays, by name and by soundlevel.
/// </summary>
/// <remarks>
/// **Written to answer "why are there no footsteps", and deliberately a probe rather than a test.**
/// It reports a population; there is no prediction it could falsify, and dressing a report up as an
/// assertion is how a number nobody measured ends up defended.
///
/// The question is not idle. The owner listened to a full STV match and reported gunfire and voice
/// lines playing, ambience playing far too loud, and footsteps absent entirely — and "absent from
/// the stream" and "dropped by the viewer" are indistinguishable from the speakers. This says which.
/// </remarks>
[Explicit("Reports the sound population of a demo; run deliberately.")]
public sealed class SoundPopulationProbe
{
    [TestCase("demostf-cp_process_f12-2026-08-08-2207")]
    [TestCase("movement-test-pov-cp_process")]
    public void Sounds_ADemo_AreReportedByNameAndLevel(string name)
    {
        string path = Corpus.Demo(name);

        DemoTimeline timeline = TimelineCache.For(path);

        TestContext.Out.WriteLine(
            $"{Path.GetFileName(path)}: {timeline.Sounds.Count} sounds");

        // **Grouped by stem rather than by full name**, because a footstep is eight files with a
        // digit on the end and counting them separately buries the population that matters.
        IEnumerable<IGrouping<string, SceneSound>> byStem = timeline.Sounds
            .GroupBy(sound => Stem(sound.Name))
            .OrderByDescending(group => group.Count());

        foreach (IGrouping<string, SceneSound> group in byStem.Take(25))
        {
            IEnumerable<int> levels = group.Select(sound => sound.SoundLevel).Distinct().Order();

            TestContext.Out.WriteLine(
                $"  {group.Count().ToString(CultureInfo.InvariantCulture),6}  " +
                $"{group.Key}  sndlvl [{string.Join(", ", levels.Take(4))}]");
        }

        // The specific question, asked directly so the answer is not buried in the list above.
        int footsteps = timeline.Sounds.Count(sound =>
            sound.Name.Contains("footstep", StringComparison.OrdinalIgnoreCase) ||
            sound.Name.Contains("step", StringComparison.OrdinalIgnoreCase));

        TestContext.Out.WriteLine(
            $"  footstep-like names: {footsteps.ToString(CultureInfo.InvariantCulture)}");

        // And the two ways a sound that IS present can still never be heard.
        int stops = timeline.Sounds.Count(sound => sound.IsStop);
        int unnamed = timeline.Sounds.Count(sound => sound.Name.Length == 0);
        int silent = timeline.Sounds.Count(sound => sound.Volume <= 0f);

        TestContext.Out.WriteLine(
            $"  stops {stops.ToString(CultureInfo.InvariantCulture)}, " +
            $"unnamed {unnamed.ToString(CultureInfo.InvariantCulture)}, " +
            $"zero-volume {silent.ToString(CultureInfo.InvariantCulture)}");

        // **Pitch, because "too slow" is a rate and the viewer divides this by 100.** If the wire
        // carries something other than a percentage centred on 100, every sound in the game plays
        // at the wrong speed and the ones with long tails are where it is audible.
        IEnumerable<IGrouping<int, SceneSound>> byPitch = timeline.Sounds
            .GroupBy(sound => sound.Pitch)
            .OrderByDescending(group => group.Count());

        TestContext.Out.WriteLine(
            "  pitch: " +
            string.Join(
                ", ",
                byPitch.Take(6).Select(group =>
                    $"{group.Key.ToString(CultureInfo.InvariantCulture)}" +
                    $" x{group.Count().ToString(CultureInfo.InvariantCulture)}")));

        // **What the STOPS refer to**, since a gate that plays too long is what an unhonoured stop
        // sounds like. Reported by name so it is clear whether the stops are for the sounds that
        // misbehave or for something else entirely.
        IEnumerable<IGrouping<string, SceneSound>> stopped = timeline.Sounds
            .Where(sound => sound.IsStop)
            .GroupBy(sound => Stem(sound.Name))
            .OrderByDescending(group => group.Count());

        TestContext.Out.WriteLine(
            "  stopped: " +
            string.Join(
                ", ",
                stopped.Take(6).Select(group =>
                    $"{group.Key} x{group.Count().ToString(CultureInfo.InvariantCulture)}")));
    }

    /// <summary>A sound's name with any trailing variation digits removed.</summary>
    private static string Stem(string name)
    {
        if (name.Length == 0)
        {
            return "(unnamed)";
        }

        int dot = name.LastIndexOf('.');
        string bare = dot > 0 ? name[..dot] : name;

        return bare.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    }
}
