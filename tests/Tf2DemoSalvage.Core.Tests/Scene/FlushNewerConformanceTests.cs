using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entry FLUSHES every entry at or after its own changetime (B384).
/// </summary>
/// <remarks>
/// **`NoteChanged` passes <c>bFlushNewer = true</c>, and that branch deletes**
/// (<c>interpolatedvar.h:649</c> into <c>:700</c>):
///
/// <code>
/// AddToHead( changetime, m_pValue, true );
/// …
/// if ( bFlushNewer )
/// {
///     // Get rid of anything that has a timestamp after this sample. The server might have
///     // corrected our clock and moved us back, so our current changeTime is less than a
///     // changeTime we added samples during previously.
///     while ( m_VarHistory.Count() )
///     {
///         if ( (m_VarHistory[0].changetime+0.0001f) &gt; changeTime )
///             m_VarHistory.RemoveAtHead();
///         else
///             break;
///     }
///     newslot = m_VarHistory.AddToHead();
/// }
/// </code>
///
/// The epsilon makes it an at-or-after test, so on a tick axis it is <c>changetime &gt;= changeTime</c>.
/// **Two consequences, and this project had neither.**
///
/// 1. **A history cannot hold two entries at one changetime.** The newest wins and the earlier ones at
///    that changetime are gone.
/// 2. **An update whose changetime moves BACKWARDS discards everything newer**, which is what the
///    comment is about — a server clock correction.
///
/// **B382 said "`AddToHead` is unconditional" and built its no-collapse argument on it.** That was half
/// the function: it is unconditional about identical VALUES, and it flushes on TIME. The restatement
/// argument survives, because a restatement arrives with a LATER changetime and is kept — only same-or-
/// newer changetimes are flushed.
///
/// **Measured on `20130518_0313_cp_granary_blu_blu`, entity 328 at tick 63630**, a granary shutter
/// closing at its stated 4.5 units a tick:
///
/// <code>
/// tick   applied  Z
/// 63665  63665    -294.5
/// 63667  63667    -299.0     three entries, one changetime
/// 63667  63667    -303.5
/// 63667  63667    -308.0
/// 63670  63670    -312.5
/// </code>
///
/// The engine keeps only −308.0 at 63667, so it draws −294.5 to −308.0 over two ticks. Keeping all
/// three makes the pair for a target of 63666 end at −299.0, reach it at a fraction of one, and then
/// switch to −308.0 — **a jump of 9.15 units in a tenth of a tick**, which the `jitter` probe measured
/// as 9.019. The smooth stretch covers less ground per tick, so the door reads SLOW while the jumps make
/// the distance up invisibly. That is the owner's *"animating too slow"*.
///
/// **The flush is a REACH bound here, not a deletion.** The engine can delete because it flushes at the
/// moment of receipt and never looks back; a viewer scrubbed to before the flush must still see what the
/// client had then. So each entry records the tick it was flushed at and the search ignores it from that
/// tick on — the same licensed difference as the prune and the reset.
/// </remarks>
public sealed class FlushNewerConformanceTests
{
    /// <summary>The track's own render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <summary>Granary's shutter, closing at the 4.5 units a tick its `func_door speed` states.</summary>
    /// <remarks>
    /// The three entries sharing changetime 63667 are the measured shape, reduced to the ticks that
    /// matter and rebased to zero so the arithmetic reads.
    /// </remarks>
    private static ScenePropTrack Shutter()
    {
        ScenePropTrack track = new(entityIndex: 328, "*50");

        track.Add(0, Height(0f), appliedAt: 0);
        track.Add(2, Height(-9f), appliedAt: 2);

        // Three updates carrying one applied time, as the demo really contains.
        track.Add(4, Height(-13.5f), appliedAt: 4);
        track.Add(4, Height(-18f), appliedAt: 4);
        track.Add(4, Height(-22.5f), appliedAt: 4);

        track.Add(7, Height(-27f), appliedAt: 7);

        return track;
    }

    private static ScenePose Height(float z) => new() { Z = z };

    [Test]
    public void Add_ThreeEntriesAtOneChangetime_KeepsOnlyTheLast()
    {
        // **Sampled just BEFORE changetime 4, which is the only place the duplicates change the answer.**
        // Asking exactly AT 4 cannot fail: the walk takes the highest index at or before the target, so it
        // lands on the last duplicate either way. A tenth of a tick earlier, the pair is 2-to-4 and its far
        // end is what differs — the engine's only entry at 4 is -22.5, where three entries make it -13.5.
        //
        //   flushed:  -9 to -22.5 over ticks 2 to 4, at a fraction of 0.95  ->  -21.84
        //   kept:     -9 to -13.5 over the same span                        ->  -13.28
        ScenePropTrack track = Shutter();

        track.At(3.9d + Delay).ShouldNotBeNull().Z.ShouldBe(
            -21.84f,
            0.05f,
            "the two earlier entries at changetime 4 were flushed, so the span ends at the last one");
    }

    [Test]
    public void At_AcrossAFlushedChangetime_DoesNotJump()
    {
        // **The symptom, and it is what the owner reported.** Sampled either side of the moment the
        // duplicates used to switch, the drawn height must move by about the door's speed and not by
        // several units at once. Four and a half units a tick is 0.45 over a tenth of a tick; a tenth of
        // that band is generous and still an order of magnitude under the 9.15 the duplicates produced.
        ScenePropTrack track = Shutter();

        float worst = 0f;
        float previous = float.NaN;

        for (int step = 0; step <= 100; step++)
        {
            double at = Delay + (step / 10d);

            if (track.At(at) is not { } pose)
            {
                continue;
            }

            if (!float.IsNaN(previous))
            {
                worst = Math.Max(worst, Math.Abs(pose.Z - previous));
            }

            previous = pose.Z;
        }

        worst.ShouldBeLessThan(
            1f,
            "a door moving 4.5 units a tick cannot move a unit in a tenth of one; the duplicates at " +
            "changetime 4 made it jump 9.15");
    }

    [Test]
    public void Add_AnEntryWhoseChangetimeMovesBackwards_FlushesEverythingNewer()
    {
        // **Valve's own stated reason for the flush**: "The server might have corrected our clock and
        // moved us back, so our current changeTime is less than a changeTime we added samples during
        // previously." Measured on the same demo, 26 of that track's 362 keyframes apply EARLIER than the
        // keyframe before them, so this is not a hypothetical shape.
        ScenePropTrack track = new(entityIndex: 328, "*50");

        track.Add(0, Height(0f), appliedAt: 0);
        track.Add(10, Height(-45f), appliedAt: 10);

        // A correction: this update says the entity is at -9 and that it applied at tick 2, which is
        // BEFORE the -45 already stored. Everything at or after tick 2 goes.
        track.Add(11, Height(-9f), appliedAt: 2);

        // So the newest thing the history holds is -9 at changetime 2, and a moment past it holds there
        // rather than interpolating back toward a -45 the client has discarded.
        track.At(12d + Delay).ShouldNotBeNull().Z.ShouldBe(
            -9f, 0.01f, "the -45 entry was flushed by a correction that moved the clock back");
    }

    /// <remarks>
    /// **The strongest assertion available for a drawn curve, because it assumes nothing about the
    /// curve** (B370). When the drawn target lands ON an entry's changetime, `GetInterpolationInfo` gives
    /// `frac = (targettime - older) / (newer - older) = 0` with that entry as `older`
    /// (<c>interpolatedvar.h:845</c>), and `Lerp_Hermite` at a fraction of zero returns <c>p1</c> whatever
    /// its tangents are. So the drawn height at <c>changetime + delay</c> is that entry's own value —
    /// exactly, for every entry, with no model of speed or easing involved.
    ///
    /// **What it caught.** `Bracket` binary-searches `_changeTimes`, which still holds FLUSHED entries,
    /// and their changetimes are not monotonic with the live ones. The engine never has this problem: its
    /// flush deletes, so its list is monotonic and its newest-first walk is exact. Ours can land BELOW the
    /// true position, and the downward walk then stops at the first live entry it meets — an older one. So
    /// the door was drawn where it had been at an earlier moment: a PHASE error, which is what "very very
    /// close to the door by the time it opens" is, and which no duration measurement can separate from a
    /// slow rate.
    ///
    /// Measured on `20130518_0313_cp_granary_blu_blu` across 7,068 history entries: **3 to 5 per cent of
    /// entries were not drawn at their own changetime, the worst by 111 units — a whole door travel.**
    /// After the climb, three entries in total.
    /// </remarks>
    [Test]
    public void At_EveryLiveEntry_IsDrawnAtItsOwnChangetime()
    {
        // **A LARGE backwards correction, and the size is the point.** A small one does not reproduce the
        // fault: the flushed entries have to carry changetimes well ABOVE the live ones that follow, so a
        // binary search over the raw array lands below the right entry AND the "still at or before the
        // target" clause cannot climb past them either. A first version of this fixture corrected by one
        // tick, and sabotaging the flushed-entry clause reddened nothing at all.
        ScenePropTrack track = new(entityIndex: 328, "*50");

        track.Add(0, Height(0f), appliedAt: 0);
        track.Add(1, Height(-4.5f), appliedAt: 2);

        // Two entries far ahead on the server's clock, which the correction below discards.
        track.Add(2, Height(-90f), appliedAt: 20);
        track.Add(3, Height(-94.5f), appliedAt: 21);

        // The correction: applied at 4, so both entries at or after 4 are flushed. The array now reads
        // changetimes 0, 2, 20(flushed), 21(flushed), 4, 6 — not monotonic, which is what the engine's
        // deleting flush never leaves behind.
        track.Add(4, Height(-9f), appliedAt: 4);
        track.Add(5, Height(-13.5f), appliedAt: 6);

        InterpolatedHistory history = track.Simulation;
        int off = 0;

        for (int index = 0; index < history.Count; index++)
        {
            (int received, int flushedAt) = history.ArrivalAt(index);
            double at = history.ChangeTimeAt(index) + Delay;

            if (at < received || flushedAt <= at)
            {
                continue;
            }

            if (track.At(at) is { } pose &&
                Math.Abs(pose.Z - history.ValuesAt(index)[2]) > 0.01f)
            {
                off++;
            }
        }

        off.ShouldBe(
            0,
            "at a fraction of zero the curve returns its own sample, so every live entry must be drawn " +
            "at its own changetime whatever the flushed entries around it do to a binary search");
    }

    [Test]
    public void At_ScrubbedBeforeAFlush_StillSeesWhatTheClientHeldThen()
    {
        // **The licensed difference, and the control for the test above.** The engine deletes because it
        // flushes at the moment of receipt and never seeks backwards. A viewer does, so the entries stay
        // and the flush bounds the SEARCH from its own tick on. Asking about a moment before the
        // correction arrived must give the answer the client had then.
        ScenePropTrack track = new(entityIndex: 328, "*50");

        track.Add(0, Height(0f), appliedAt: 0);
        track.Add(10, Height(-45f), appliedAt: 10);
        track.Add(11, Height(-9f), appliedAt: 2);

        // Drawn at tick 10, before the tick-11 correction has arrived: the -45 entry is still live and the
        // delayed target is inside the 0-to-10 span.
        float shown = track.At(10d).ShouldNotBeNull().Z;

        shown.ShouldBeLessThan(
            -0.01f, "before the correction arrives the -45 entry is still what the client holds");
    }
}
