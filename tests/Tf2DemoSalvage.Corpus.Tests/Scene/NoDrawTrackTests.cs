using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entity told not to draw stops being drawn, and comes back, on a real demo.
/// </summary>
/// <remarks>
/// **Written because its absence let a working feature be filed as a bug.** `EF_NODRAW` was proved
/// synthetically — <c>SyntheticInterpolationTests</c> hides an entity and brings it back — and by
/// reading <c>DemoTimeline.PropsAt</c>, which drops a hidden pose before the renderer ever sees it.
/// Nothing measured it on a recording. So a search of the renderer alone found no consumer of
/// <c>ScenePose.Hidden</c>, that absence was read as a missing feature, and B133 was filed against
/// code that works. The owner's recollection of pickups vanishing in the viewer is what killed it.
///
/// **What makes this the right instrument and not another proxy.** The claim is about the sequence
/// the viewer draws, so the measurement is <c>PropsAt</c> — the query the viewer actually calls,
/// once per frame — rather than the flag it reads on the way. A test asserting the flag would have
/// passed with the flag reaching nothing, which is precisely the state B133 alleged.
///
/// **A pickup is the mechanism's reason for existing.** <c>CTFPowerup::SetDisabled</c> calls
/// <c>AddEffects(EF_NODRAW)</c> and leaves the entity in place, because it respawns; deleting it
/// would lose the respawn. So a demo of anyone walking over a health kit contains the whole cycle,
/// and the era specimens are solo recordings of the owner doing exactly that.
///
/// **Hiding is broader than <c>EF_NODRAW</c> here and that is correct.** <c>IsDrawn</c> is
/// <c>IsVisible &amp;&amp; no EF_NODRAW</c>, and <c>IsVisible</c> is false while an entity has left
/// the PVS — a different fact meaning a different thing, and on a POV recording the common one.
/// 140 of the 2007 granary demo's 239 tracks are hidden at some tick and drawn at another, which is
/// mostly the player turning around rather than mostly pickups. The two share this path because the
/// question a renderer asks is the same for both: draw it now, or not.
/// </remarks>
public sealed class NoDrawTrackTests
{
    [Test]
    public void PropsAt_AnEntityFlaggedNoDraw_LeavesTheDrawnSetAndReturnsToIt()
    {
        int demosWithACycle = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            // A track that is hidden at some point and visible at another: the cycle, not merely an
            // entity that was born hidden or died hidden. Both of those would satisfy a weaker
            // condition while proving nothing about the flag CHANGING.
            List<ScenePropTrack> cycled =
            [
                .. timeline.Props.Where(track =>
                    track.Keyframes.Any(frame => frame.Pose.Hidden) &&
                    track.Keyframes.Any(frame => !frame.Pose.Hidden)),
            ];

            TestContext.Out.WriteLine(
                $"NODRAW {Path.GetFileName(path)}: {cycled.Count} of {timeline.Props.Count} " +
                "tracks are hidden at one tick and drawn at another");

            if (cycled.Count == 0)
            {
                continue;
            }

            // **A slot is reused, so an entity index does not name a track.** A rocket that explodes
            // frees its index for the next one and each occupant gets its own track (B92), so
            // `timeline.Props` can hold several with the same `EntityIndex` — and `SceneProp`
            // carries only the index, which is all the renderer needs. Membership tested by index
            // therefore answers "is SOME occupant of slot 55 drawable", not "is this one".
            //
            // Measured, not guessed: the fourth wrong condition here failed at exactly that, on
            // entity 55 of the 2007 granary demo at tick 271 — this track hidden, a later occupant
            // of the same slot drawable. Restricting the subject to an index held by one track makes
            // the two questions the same question.
            HashSet<int> reused =
            [
                .. timeline.Props.GroupBy(track => track.EntityIndex)
                    .Where(slot => slot.Count() > 1)
                    .Select(slot => slot.Key),
            ];

            if (cycled.FirstOrDefault(track => !reused.Contains(track.EntityIndex)) is not
                { } subject)
            {
                continue;
            }

            demosWithACycle++;

            // **Swept tick by tick, and the two earlier versions of this test show why.** `At`
            // returns the pose one interpolation window BEHIND the tick it is asked for, because
            // that is what a client draws. So asking at the exact tick a hidden keyframe was stated
            // correctly answers with the pose before it — the first version read that as the
            // renderer being handed a hidden entity and accused working code. Probing every stated
            // tick instead was no better: the delay lands the hidden pose BETWEEN this track's own
            // keyframes, so the second version swept a grid that never visited it and reported that
            // the entity was never withheld.
            //
            // A dense sweep needs to know nothing about the delay. Whatever pose `At` settles on at
            // each moment, membership of the drawn set must agree with it.
            List<SceneProp> drawn = [];
            int withheld = 0;
            int handed = 0;

            // **Past the last keyframe, not up to it.** The delay means a pose stated at tick T is
            // what the viewer draws at roughly T + 7, so a track whose final keyframe is the hidden
            // one — an entity that goes away and stays away, which is most of them — is never
            // observed hidden by a sweep that stops where the keyframes stop. That was the third
            // wrong condition here, and the margin is what fixed it.
            const int PastTheEnd = 32;

            for (int tick = subject.FirstTick;
                 tick <= subject.Keyframes[^1].Tick + PastTheEnd;
                 tick++)
            {
                // **The query the viewer calls.** MainForm has one per-frame prop path — PropsAt
                // into _props into _drawn — so this is what both cameras receive.
                timeline.PropsAt(tick, drawn);

                bool present = drawn.Any(prop => prop.EntityIndex == subject.EntityIndex);
                bool drawable = subject.At(tick) is { Hidden: false };

                present.ShouldBe(
                    drawable,
                    $"{path}: entity {subject.EntityIndex} at tick {tick} — the pose says " +
                    $"drawable={drawable} and the drawn set says present={present}");

                if (drawable)
                {
                    handed++;
                }
                else
                {
                    withheld++;
                }
            }

            // **Both outcomes, or the agreement above is agreement about one case.** A track that
            // is drawable at every swept tick would pass every assertion in the loop while never
            // exercising the hiding at all.
            withheld.ShouldBeGreaterThan(
                0, $"{path}: entity {subject.EntityIndex} was never withheld");

            handed.ShouldBeGreaterThan(
                0,
                $"{path}: entity {subject.EntityIndex} was never handed over — hiding that never " +
                "ends is deletion wearing a flag");
        }

        // **The control, and this test is worth nothing without it.** Every assertion above sits
        // inside a loop that a corpus containing no hidden entity would skip entirely, reporting a
        // pass on zero measurements — the same shape as the search that caused B133.
        demosWithACycle.ShouldBeGreaterThan(
            0,
            "no corpus demo contains an entity that is hidden at one tick and drawn at another, " +
            "so nothing above was measured");
    }
}
