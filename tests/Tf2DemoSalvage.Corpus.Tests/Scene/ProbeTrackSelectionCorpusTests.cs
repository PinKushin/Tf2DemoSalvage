using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Probe.Probes;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// That the probes report the entity their argument names, on a demo whose slots were reused.
/// </summary>
/// <remarks>
/// **The synthetic tests in <c>TrackSelectionByTickTests</c> prove the SELECTION; only this can prove
/// the WIRING** (`docs/memory/output-level-assertion-or-it-is-not-done.md`). A probe that went back to
/// `DemoTimeline.TrackFor(entity)` would leave every one of those green while printing a table about
/// a different entity — which is precisely what `cycle` did until B386, and what nobody noticed
/// because the table was complete and the model name was plausible.
///
/// **The specimen is committed, so this runs in CI.** `tf2-2013-build1729296-stv-cp_foundry` puts two
/// occupants in slot 200, `[7..9349]` and `[9349..end]`, both `door_slide_door.mdl` — which is why
/// the assertion is on the WINDOW rather than the model path. Two occupants sharing a model is the
/// harder case and the commoner one: a slot is usually recycled by the round restarting.
///
/// **The index is found rather than typed.** A hardcoded 200 becomes a claim about that file, and the
/// first thing a replaced specimen would do is make this test pass by measuring nothing.
/// </remarks>
public sealed class ProbeTrackSelectionCorpusTests
{
    /// <summary>The committed specimen whose slots are reused within the recording.</summary>
    private const string DemoName = "tf2-2013-build1729296-stv-cp_foundry";

    [Test]
    public void Cycle_AnIndexWhoseSlotWasReused_ReportsTheOccupantAliveAtTheTick()
    {
        string path = Corpus.Demo(DemoName);
        DemoTimeline timeline = TimelineCache.For(path);

        (int entity, ScenePropTrack first) = FirstReusedSlot(timeline);

        // **The control, and it is what makes the assertion below mean anything.** If the index-only
        // lookup happened to name the same track, this test could not tell a probe that consults the
        // tick from one that does not. Stating it as an assertion rather than assuming it also
        // catches the day a specimen changes and the case quietly stops existing.
        timeline.TrackFor(entity).ShouldNotBeSameAs(
            first,
            "the index-only lookup must name a DIFFERENT occupant, or this demo cannot separate "
            + "a tick-aware probe from an index-keyed one");

        // A tick inside the first occupant's life, and well before the second one starts.
        int tick = first.FirstTick + 1;

        StringWriter output = new();

        new CycleProbe().Run(
            output,
            [
                DemoName,
                entity.ToString(CultureInfo.InvariantCulture),
                tick.ToString(CultureInfo.InvariantCulture),
                "1",
            ]);

        string reported = output.ToString();

        reported.ShouldContain(
            string.Create(
                CultureInfo.InvariantCulture,
                $"alive [{first.FirstTick}..{first.EndTick}]"),
            Case.Sensitive,
            reported);
    }

    [Test]
    public void Cycle_ATickNoOccupantOfTheSlotCovers_NamesEveryWindowInstead()
    {
        // **The half that made the doors tractable** (B370). "No track" and "seven tracks, none
        // covering your tick" send the reader to opposite places, and a probe that reports the first
        // when the second is true sends them hunting a decode bug.
        string path = Corpus.Demo(DemoName);
        DemoTimeline timeline = TimelineCache.For(path);

        (int entity, ScenePropTrack first) = FirstReusedSlot(timeline);

        first.FirstTick.ShouldBeGreaterThan(
            0, "a slot occupied from tick zero has no moment before its first occupant to ask about");

        StringWriter output = new();

        new CycleProbe().Run(
            output, [DemoName, entity.ToString(CultureInfo.InvariantCulture), "0", "1"]);

        string reported = output.ToString();

        reported.ShouldContain(
            string.Create(
                CultureInfo.InvariantCulture,
                $"That index owns {timeline.TracksFor(entity).Count} track(s)"),
            Case.Sensitive,
            reported);

        reported.ShouldContain(
            string.Create(CultureInfo.InvariantCulture, $"[{first.FirstTick}..{first.EndTick}]"),
            Case.Sensitive,
            reported);
    }

    /// <summary>The lowest entity index this demo hands to more than one object.</summary>
    /// <param name="timeline">The recording.</param>
    /// <returns>That index, and the first track to occupy it.</returns>
    /// <remarks>
    /// Ordered by index so the answer is the same on every run: a test that picks whichever slot
    /// enumerates first would change subject when an unrelated decode change reorders the props.
    /// </remarks>
    private static (int Entity, ScenePropTrack First) FirstReusedSlot(DemoTimeline timeline)
    {
        foreach (int entity in timeline.Props
            .Select(track => track.EntityIndex)
            .Distinct()
            .OrderBy(index => index))
        {
            IReadOnlyList<ScenePropTrack> owned = timeline.TracksFor(entity);

            if (owned.Count > 1 && owned[0].EndTick != int.MaxValue)
            {
                return (entity, owned[0]);
            }
        }

        throw new InvalidOperationException(
            $"No slot in {DemoName} is occupied twice, so the reuse this test exists for cannot be "
            + "measured on it. Pick a specimen that has one rather than weakening the assertion.");
    }
}
