using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// What <c>GetInterpolationInfo</c> answers: the pair, the spline's third sample, and whether the answer
/// is settled (B370).
/// </summary>
/// <param name="Older">The entry at or before the target.</param>
/// <param name="Newer">Its later neighbour, or the same entry when the value holds.</param>
/// <param name="Oldest">The entry before <paramref name="Older"/>, for the spline.</param>
/// <param name="NoMoreChanges">
/// The engine's <c>pNoMoreChanges</c> — that a rising current time cannot change this answer, so the
/// entity leaves the interpolation list until an update re-adds it (<c>interpolatedvar.h:832</c> and
/// <c>:865</c>).
///
/// **This is the whole of the engine's scheduling, and it is why this project's was a divergence.** The
/// engine never predicts WHEN an answer will change. It interpolates every frame for everything on
/// <c>g_InterpolationList</c> and drops an entity when its own history says nothing more is coming —
/// `if ( bNoMoreChanges ) RemoveFromInterpolationList()` (<c>c_baseanimating.cpp:4478</c>) — and a later
/// update puts it back through `PostDataUpdate`. Predicting boundary ticks instead is a second mechanism
/// that has to agree with the sampler, and measured on granary it disagreed on 2.9% of ticks, by up to
/// 12.47 units.
/// </param>
public readonly record struct Bracketing(int Older, int Newer, int Oldest, bool NoMoreChanges);

/// <summary>
/// One registered variable's interpolation history, appended and searched as the engine's is (B382).
/// </summary>
/// <remarks>
/// **This is `CInterpolatedVarArrayBase`, and the shape is the point.** The engine keeps one of these per
/// registered variable — origin, angles, cycle, pose parameters, every overlay layer — and
/// `OnLatchInterpolatedVariables` appends to each whose latch group fired
/// (<c>c_baseentity.cpp:2814</c>). This project kept ONE list of whole poses and tried to recover which
/// clock each entry belonged to, which is B382.
///
/// **An entry is <c>m_nMaxCount</c> floats and knows nothing about what they mean.** The engine stores
/// `Type m_pValue[m_nMaxCount]` and loops `for ( int i = 0; i &lt; m_nMaxCount; i++ )` in every operation;
/// naming the components here would need one type per variable group and make a third group cost a new
/// type rather than a new width. The values are held flat rather than as an array per entry because a
/// long recording appends tens of thousands of them.
///
/// **Entries are never collapsed.** `NoteChanged` calls `AddToHead` unconditionally
/// (<c>interpolatedvar.h:649</c>); its "differs/identical" result is a hint that lets the caller skip
/// interpolation work, never a reason to omit the entry. A door held open therefore leaves several
/// entries carrying the same position at different changetimes — which is exactly what gives the
/// Hermite spline a third sample equal to its second, so the curve leaves that position with no
/// incoming velocity.
///
/// **Nothing is pruned, and that is the one licensed difference.** The engine deletes with
/// `RemoveEntriesPreviousTo( currentTime - interpolation_amount - EXTRA_INTERPOLATION_HISTORY_STORED )`
/// at the end of `Interpolate()` (<c>interpolatedvar.h:1057</c>), keeping `Truncate( i + 3 )` — it can,
/// because a live client never seeks backwards. A viewer does, so the entries stay and the SEARCH is
/// bounded instead, by <c>arrivedBy</c> alone. The owner set that requirement: *"we
/// should be able to get valve parity there and still scrub and rewind the demo, we just have to make it
/// work in both directions."*
/// </remarks>
public sealed class InterpolatedHistory
{
    private int _width;

    private readonly List<int> _changeTimes = [];

    private readonly List<int> _received = [];

    private readonly List<float> _values = [];

    private readonly List<int> _generationStarts = [];

    /// <summary>For each entry, the tick a later append FLUSHED it, or <see cref="Never"/>.</summary>
    /// <remarks>
    /// **`bFlushNewer`, held as a bound rather than performed as a deletion** — see <see cref="Add"/>.
    /// </remarks>
    private readonly List<int> _flushedAt = [];

    /// <summary>The flush tick of an entry nothing has flushed.</summary>
    private const int Never = int.MaxValue;

    private bool[] _looping;

    private int _generation;

    private int _lastGeneration = -1;

    /// <summary>A history whose entries carry a fixed number of components.</summary>
    /// <param name="width">How many floats an entry holds — the engine's <c>m_nMaxCount</c>.</param>
    /// <remarks>
    /// **The width is per variable, and the engine's widths are the variables' own**: three for
    /// `m_iv_vecOrigin`, three for `m_iv_angRotation`, one for `m_iv_flCycle`, and for
    /// `m_iv_flPoseParameter` **the model's own parameter count, not <c>MAXSTUDIOPOSEPARAM</c>** —
    /// `m_iv_flPoseParameter.SetMaxCount( hdr->GetNumPoseParameters() )` (<c>c_baseanimating.cpp:1124</c>).
    /// Where this project merges two of them into one history it is because they share a latch group and
    /// therefore a changetime, and the risk entry carries the equivalence argument and what would falsify
    /// it.
    /// </remarks>
    public InterpolatedHistory(int width)
    {
        _width = Math.Max(1, width);
        _looping = new bool[_width];
    }

    /// <summary>Resizes an entry, wiping the history when the width actually changes.</summary>
    /// <param name="newmax">The new component count.</param>
    /// <param name="at">The tick a wipe's replacement entries are stamped with.</param>
    /// <param name="values">The current value, which a wipe seeds the history with.</param>
    /// <remarks>
    /// **`SetMaxCount`, and the wipe is the engine's** (<c>interpolatedvar.h:1272</c>):
    ///
    /// <code>
    /// bool changed = ( newmax != m_nMaxCount ) ? true : false;
    /// newmax = MAX(1,newmax);        // BUGBUG: Support 0 length properly?
    /// m_nMaxCount = newmax;
    /// if ( changed )                 // Wipe everything any time this changes!!!
    /// {
    ///     … m_bLooping = new byte[m_nMaxCount]; memset( m_bLooping, 0, … );
    ///     Reset();
    /// }
    /// </code>
    ///
    /// **Called from `OnNewModel` and sized by the MODEL**, which is why a two-parameter sentry's history
    /// is two floats an entry and not twenty-four. Getting that wrong is not only wasteful, it is
    /// measurable: a width of <c>MAXSTUDIOPOSEPARAM</c> for every track put a corpus test host at 11 GB.
    ///
    /// **The looping flags are wiped with it**, which is the engine's order too — `SetLooping` is called
    /// per parameter immediately after, out of `Pose.loop != 0.0f` (<c>c_baseanimating.cpp:1130</c>), so
    /// anything set before a resize is deliberately discarded.
    ///
    /// **Note the engine's own bug, preserved.** `changed` is computed BEFORE the `MAX(1, …)` clamp, so
    /// asking for 0 when the width is already 1 reports a change and wipes the history. Kept, because a
    /// model with no pose parameters really does re-seed here.
    /// </remarks>
    public void SetMaxCount(int newmax, int at, ReadOnlySpan<float> values)
    {
        bool changed = newmax != _width;

        _width = Math.Max(1, newmax);

        if (!changed)
        {
            return;
        }

        _looping = new bool[_width];

        Reset(at, values);
    }

    /// <summary>Starts the variable's life over, holding at the value it has now.</summary>
    /// <param name="at">The tick to stamp the replacement entries with — the engine's <c>curtime</c>.</param>
    /// <param name="values">The current value, which every replacement entry carries.</param>
    /// <remarks>
    /// **`Reset()`, and the interesting part is that it adds THREE** (<c>interpolatedvar.h:740</c>):
    ///
    /// <code>
    /// ClearHistory();
    /// if ( m_pValue )
    /// {
    ///     AddToHead( gpGlobals->curtime, m_pValue, false );
    ///     AddToHead( gpGlobals->curtime, m_pValue, false );
    ///     AddToHead( gpGlobals->curtime, m_pValue, false );
    ///     memcpy( m_LastNetworkedValue, m_pValue, m_nMaxCount * sizeof( Type ) );
    /// }
    /// </code>
    ///
    /// **Three, so that `oldest` is VALID but degenerate.** `GetInterpolationInfo` takes
    /// `oldestindex = i+1` and sets `m_bHermite` only on `dt2 > 0.0001f` (<c>:851</c>); three entries
    /// sharing one changetime make `dt2` zero, so the first blend after a reset is LINEAR. Two entries
    /// would give an invalid `oldest` and the same linear result; one would not bracket at all. The count
    /// is the mechanism, not a margin.
    ///
    /// **Nothing is deleted, because a viewer can scrub backwards and a client cannot** — the one licensed
    /// difference, applied here as it is to the prune. `ClearHistory` becomes a GENERATION boundary that
    /// <see cref="Bracket"/> will not walk back across, so a lookup answers exactly what the client's
    /// freshly-reset history would have held, and a scrub to before the reset still finds the old entries.
    /// </remarks>
    public void Reset(int at, ReadOnlySpan<float> values)
    {
        _generation++;

        // **Three entries at one changetime, and they do NOT flush each other.** `Reset` passes
        // `bFlushNewer = false` where `NoteChanged` passes true (<c>interpolatedvar.h:745</c> against
        // <c>:649</c>) — which is the whole point: three entries sharing one changetime is exactly what a
        // flush would collapse, and it is what makes `dt2` zero and the first blend after a reset linear.
        // Routing these through the flushing path left ONE entry and destroyed the mechanism.
        for (int entry = 0; entry < ResetEntries; entry++)
        {
            Add(at, at, values, flushNewer: false);
        }
    }

    /// <summary>How many entries <see cref="Reset"/> seeds, which is Valve's three.</summary>
    private const int ResetEntries = 3;

    /// <summary>How many entries the history holds.</summary>
    public int Count => _changeTimes.Count;

    /// <summary>How many components an entry carries — the engine's <c>m_nMaxCount</c>.</summary>
    /// <remarks>
    /// Read by the caller filling an entry, so the copy is the width the history actually has rather than
    /// the width the caller assumed. <see cref="SetMaxCount"/> is what moves it.
    /// </remarks>
    public int Width => _width;

    /// <summary>The tick one entry's value applied.</summary>
    /// <param name="index">Which entry, oldest first.</param>
    /// <returns>
    /// Its changetime, which is the clock its latch group stamps: <c>GetSimulationTime()</c> for a
    /// simulation-latched variable, <c>GetAnimTime()</c> for an animation-latched one
    /// (<c>c_baseentity.cpp:2808</c>).
    /// </returns>
    public int ChangeTimeAt(int index) => _changeTimes[index];

    /// <summary>The tick an entry arrived, and the tick a later append flushed it.</summary>
    /// <param name="index">Which entry, oldest first.</param>
    /// <returns>Its arrival, and its flush tick or <see cref="int.MaxValue"/> when nothing flushed it.</returns>
    /// <remarks>
    /// **For a diagnostic to report the history the sampler USED, carried rather than recomputed** (B243).
    /// A probe that rebuilt liveness from the keyframes would be checking its own arithmetic; these are the
    /// numbers <see cref="Bracket"/> reads.
    /// </remarks>
    public (int Received, int FlushedAt) ArrivalAt(int index) => (_received[index], _flushedAt[index]);

    /// <summary>One entry's components, in the order they were appended.</summary>
    /// <param name="index">Which entry, oldest first.</param>
    /// <returns>Exactly the width the history was built with.</returns>
    public ReadOnlySpan<float> ValuesAt(int index) =>
        CollectionsMarshal.AsSpan(_values).Slice(index * _width, _width);

    /// <summary>Marks one component as wrapping, so blends take the short way round.</summary>
    /// <param name="looping">Whether it wraps.</param>
    /// <param name="index">Which component.</param>
    /// <remarks>
    /// **`SetLooping( bool looping, int iArrayIndex = 0 )`** (<c>interpolatedvar.h:490</c>), and the
    /// engine sets it from the model rather than once: `m_iv_flCycle.SetLooping( IsSequenceLooping(
    /// GetSequence() ) )` (<c>c_baseanimating.cpp:4472</c>), `m_iv_flPoseParameter.SetLooping(
    /// Pose.loop != 0.0f, i )` (<c>:1130</c>). It decides which of two blends every loop in the class
    /// uses — `LoopingLerp` against `Lerp`, and `LoopingLerp_Hermite` against `Lerp_Hermite`.
    /// </remarks>
    public void SetLooping(bool looping, int index = 0) => _looping[index] = looping;

    /// <summary>Appends an entry, unconditionally.</summary>
    /// <param name="changeTime">The tick the value applied.</param>
    /// <param name="received">The tick the packet carrying it arrived.</param>
    /// <param name="values">The components, which must be the history's width.</param>
    /// <param name="flushNewer">
    /// Whether this append discards the entries at or after its own changetime — `AddToHead`'s third
    /// argument. **`NoteChanged` passes true and `Reset` passes false**
    /// (<c>interpolatedvar.h:649</c> against <c>:745</c>), and the difference is load-bearing rather than
    /// incidental: `Reset` seeds THREE entries sharing one changetime, which is precisely what a flush
    /// collapses. Defaulting to true because a latch is the ordinary caller.
    /// </param>
    /// <remarks>
    /// **No comparison against the head, deliberately.** `AddToHead` is unconditional
    /// (<c>interpolatedvar.h:649</c>), and the identical entries that produces are load-bearing: they are
    /// what makes a restated pose's third spline sample equal to its second.
    ///
    /// **Oldest first, where the engine's list is newest first.** The engine walks from the head because
    /// its list is short and freshly pruned; ours is a whole recording and is searched, so ascending
    /// order is what a binary search needs. The ORDER of the walk is preserved in <see cref="Bracket"/>,
    /// which is what has to match.
    ///
    /// **The generation is which run of the variable's life this entry belongs to**, bumped by
    /// <see cref="Reset"/> and never by anything else. It is <c>ClearHistory</c> expressed as a boundary
    /// rather than a deletion, so a scrub backwards still has the older entries to answer with.
    ///
    /// **And it FLUSHES every live entry at or after its own changetime** (B384). `NoteChanged` passes
    /// <c>bFlushNewer = true</c> (<c>interpolatedvar.h:649</c>), and that branch of `AddToHead` deletes:
    ///
    /// <code>
    /// // Get rid of anything that has a timestamp after this sample. The server might have
    /// // corrected our clock and moved us back, so our current changeTime is less than a
    /// // changeTime we added samples during previously.
    /// while ( m_VarHistory.Count() )
    /// {
    ///     if ( (m_VarHistory[0].changetime+0.0001f) &gt; changeTime )
    ///         m_VarHistory.RemoveAtHead();
    ///     else
    ///         break;
    /// }
    /// </code>
    ///
    /// The epsilon makes it at-or-after, so on a tick axis it is <c>changetime &gt;= changeTime</c>, and
    /// **a history therefore cannot hold two entries at one changetime.** B382's claim that "`AddToHead`
    /// is unconditional" was half this function: unconditional about identical VALUES, and flushing on
    /// TIME. The restatement argument survives, because a restatement carries a LATER changetime.
    ///
    /// **Measured cost of getting it wrong:** on `20130518_0313_cp_granary_blu_blu` a closing shutter
    /// sends three 4.5-unit steps stamped with one applied time, so the pair ended at the FIRST of them
    /// and then switched to the last — a 9.15-unit jump in a tenth of a tick, and a door that reads slow
    /// because its smooth stretch covers less ground while the jumps make the distance up.
    ///
    /// **Recorded rather than deleted, for the same reason as the prune and the reset.** The engine
    /// flushes at the moment of receipt and never seeks backwards; a viewer does, so the entry stays and
    /// <see cref="Bracket"/> ignores it from the tick it was flushed at.
    /// </remarks>
    public void Add(int changeTime, int received, ReadOnlySpan<float> values, bool flushNewer = true)
    {
        if (values.Length != _width)
        {
            throw new ArgumentException(
                $"a {_width}-component history was given {values.Length} values", nameof(values));
        }

        // Walk the LIVE entries newest-first and stop at the first that predates this one, which is where
        // the engine's `break` is. An already-flushed entry is not in the engine's list at all, so it is
        // skipped rather than treated as the head.
        for (int index = _changeTimes.Count - 1; flushNewer && index >= 0; index--)
        {
            if (_flushedAt[index] != Never)
            {
                continue;
            }

            if (_changeTimes[index] < changeTime)
            {
                break;
            }

            _flushedAt[index] = received;
        }

        // Where this generation's run began, carried forward rather than searched for: the boundary has to
        // be found on every lookup, and walking back to it would make one sample O(run).
        _generationStarts.Add(
            _changeTimes.Count == 0 || _generation != _lastGeneration
                ? _changeTimes.Count
                : _generationStarts[^1]);

        _lastGeneration = _generation;

        _changeTimes.Add(changeTime);
        _received.Add(received);
        _flushedAt.Add(Never);

        foreach (float value in values)
        {
            _values.Add(value);
        }
    }

    /// <summary>The two entries whose changetimes bracket a moment, as the engine picks them.</summary>
    /// <param name="target">The moment being drawn, already an interpolation delay behind.</param>
    /// <param name="arrivedBy">
    /// The tick being played, so nothing that arrived after it is considered. This is the REACH bound,
    /// and it is the whole of the licensed difference from the engine: entries are all retained so a
    /// scrub backwards still has data, and the search sees only what the client's own history would
    /// have held at that moment. Cheap because the list is in arrival order, so the unavailable
    /// entries are always a suffix of it.
    /// </param>
    /// <returns>
    /// The older and newer indices and the older's own predecessor for the spline, or null when the
    /// history cannot bracket the moment at all. <c>Older == Newer</c> means the value holds.
    /// </returns>
    /// <remarks>
    /// **`GetInterpolationInfo`, transcribed** (<c>interpolatedvar.h:815</c>): walk newest-first, keep
    /// going while an entry's changetime is later than the target, stop at the first at or before it. So
    /// the pair always brackets the target ON THE CHANGETIME, whatever order the entries arrived in —
    /// which is B377, and why a search on arrival was the jitter.
    ///
    /// <code>
    /// if ( targettime &lt; older_change_time ) { pInfo-&gt;newer = pInfo-&gt;older; continue; }
    /// …
    /// if ( pInfo-&gt;newer == varHistory.InvalidIndex() ) { pInfo-&gt;newer = pInfo-&gt;older; return true; }
    /// </code>
    ///
    /// **`Oldest` is the entry before `Older`, and the engine takes it unconditionally** —
    /// `int oldestindex = i+1;` — gating only whether HERMITE applies on
    /// <c>dt2 = older_change_time - oldest_change_time &gt; 0.0001f</c>. That gate lives in
    /// <see cref="TimeFixup"/>, because it is about the curve rather than about the neighbours.
    ///
    /// **No age bound on the older neighbour, and that is deliberate.** One was written here and removed:
    /// `RemoveEntriesPreviousTo` keeps `Truncate( i + 3 )`, which RETAINS the first entry past the cutoff
    /// plus two more, so the engine will interpolate from an arbitrarily old entry when that is the only
    /// older one it has. Refusing it would be this project's own rule rather than Valve's — the pruning
    /// bounds how MANY entries are kept, never how old the bracketing pair may be.
    /// </remarks>
    public Bracketing? Bracket(double target, int arrivedBy)
    {
        // **The history the client would have held**, which is every entry received by now and no others.
        // Nothing is deleted to achieve it, so playing the same moment again after a scrub gives the same
        // answer.
        int available = LastReceivedAtOrBefore(arrivedBy);

        if (available < 0)
        {
            return null;
        }

        // **And back only as far as the last `Reset()`**, which cleared the history in the engine and is a
        // boundary here so a scrub backwards keeps its data. Crossing it would interpolate from a run of
        // the variable's life the client had already thrown away.
        int floor = _generationStarts[available];

        // Enter the walk near the target rather than at the newest entry of a whole recording: the
        // engine's history is pruned to the window, so it never walks far, and ours would be O(match).
        int start = Math.Clamp(IndexAtOrBefore(target), floor, available);

        // **The binary search ran over an array that still holds FLUSHED entries, and their changetimes
        // are not monotonic with the live ones** (B370). The engine never has this problem: its flush
        // deletes, so its list contains only live entries and their changetimes ascend, which is what
        // makes its newest-first walk exact. Ours keeps them so a scrub can still reach them, so
        // `IndexAtOrBefore` can land BELOW the true position — and the downward walk then returns at the
        // first live entry it meets, which is older than the right one. Measured on granary before this
        // loop existed: 3 to 5 per cent of history entries were not drawn at their own changetime, the
        // worst by 111 units, a whole door travel.
        //
        // So climb until the next entry is neither flushed nor still at or before the target. From a
        // correct start that is zero steps; it is bounded by the run of flushed entries beside the target,
        // which is a handful — duplicates from one changetime, or one clock correction.
        int entry = Math.Min(start + 1, available);

        // **Climb until the CURRENT entry could serve as `newer`, which is live and past the target.** Three
        // earlier versions tested the NEXT entry and each stopped one short: an entry with a changetime past
        // the target is exactly the `newer` candidate, so halting in front of it left the downward walk
        // starting below it, never seeing it, and reporting `newer` as unset — which reads as "the target is
        // past everything" and sets `NoMoreChanges`. On granary that parked entity 328 with a pair pinned at
        // changetime 13920 while the history's head was 13921, and drew it 12.47 units behind.
        //
        // Testing the current entry subsumes all three clauses the earlier versions needed separately: a
        // flushed entry cannot be `newer`, nor can one at or before the target, and a run sharing one
        // changetime is walked through because every member but the last is flushed.
        while (entry < available &&
               (_flushedAt[entry] <= arrivedBy || _changeTimes[entry] <= target))
        {
            entry++;
        }

        int newer = -1;

        for (int index = entry; index >= floor; index--)
        {
            // **An entry a later update FLUSHED is not in the client's history at all** (B384), so the walk
            // steps over it exactly as the engine's walk never sees it. Bounded by `arrivedBy` and not by
            // "was it ever flushed", because a scrub to before the flush must still find it.
            if (_flushedAt[index] <= arrivedBy)
            {
                continue;
            }

            if (_changeTimes[index] > target)
            {
                newer = index;
                continue;
            }

            int oldest = Older(index, floor, arrivedBy);

            if (newer < 0)
            {
                // `if ( pInfo->newer == varHistory.InvalidIndex() ) { pInfo->newer = pInfo->older;
                //   if ( pNoMoreChanges ) *pNoMoreChanges = 1; return true; }` (`interpolatedvar.h:832`)
                // — the target is past every entry, so the pair is the same entry, the value holds, and a
                // rising current time cannot change that until an update arrives.
                return new Bracketing(index, index, oldest, NoMoreChanges: true);
            }

            return new Bracketing(index, newer, oldest, Settled(index, newer, oldest, floor, arrivedBy));
        }

        // The target precedes every entry this run holds: the oldest is all a client would have had.
        // **NOT settled**: time is rising toward entries this answer has not reached yet.
        return new Bracketing(floor, floor, floor, NoMoreChanges: false);
    }

    /// <summary>Whether a rising current time can still change this pair's answer.</summary>
    /// <param name="older">The entry at or before the target.</param>
    /// <param name="newer">Its later neighbour.</param>
    /// <param name="oldest">The spline's third sample.</param>
    /// <param name="arrivedBy">The tick being played, which decides what the head is.</param>
    /// <returns><c>true</c> when the answer is settled until an update arrives.</returns>
    /// <remarks>
    /// **The second of the engine's two <c>pNoMoreChanges</c> sites, transcribed**
    /// (<c>interpolatedvar.h:865</c>):
    ///
    /// <code>
    /// // If pInfo->newer is the most recent entry we have, and all 2 or 3 other
    /// // entries are identical, then we're always going to return the same value
    /// // if currentTime increases.
    /// if ( pNoMoreChanges &amp;&amp; pInfo-&gt;newer == m_VarHistory.Head() )
    /// {
    ///      if ( COMPARE_HISTORY( pInfo-&gt;newer, pInfo-&gt;older ) )
    ///      {
    ///         if ( !pInfo-&gt;m_bHermite || COMPARE_HISTORY( pInfo-&gt;newer, pInfo-&gt;oldest ) )
    ///             *pNoMoreChanges = 1;
    ///      }
    /// }
    /// </code>
    ///
    /// **`Head()` is the NEWEST entry**, which in this oldest-first list is the last one live at
    /// <paramref name="arrivedBy"/>. **`COMPARE_HISTORY` compares VALUES**, not changetimes: the answer is
    /// settled when the newest entry is the one being interpolated toward and every sample in play already
    /// holds the same value, so advancing the clock lands on that value however long it runs.
    ///
    /// **The hermite clause is not optional.** With a spline, a third sample that differs still bends the
    /// curve between two identical endpoints, so the answer keeps moving even though the pair does not.
    /// </remarks>
    /// <param name="floor">The generation boundary the walk stopped at, so the head is the same one.</param>
    private bool Settled(int older, int newer, int oldest, int floor, int arrivedBy)
    {
        // **The SAME floor the walk used, and passing a different one was a defect.** `Head` searched the
        // whole array while `Bracket`'s walk stops at the generation boundary, so on granary it reported an
        // entry the search could not reach — head `ct 13921` against a pair pinned at `ct 13920` — and the
        // answer was called settled against a comparison the sampler never makes. Two functions that had to
        // agree, with nothing making them: the track then parked past the moment it started moving, drawing
        // 12.47 units behind.
        if (newer != Head(floor, arrivedBy) || !Same(newer, older))
        {
            return false;
        }

        // `!pInfo->m_bHermite` — with no third sample in play the pair alone decides.
        int longer = _changeTimes[newer] - _changeTimes[older];
        int shorter = _changeTimes[older] - _changeTimes[oldest];

        return longer <= 0 || shorter <= 0 || Same(newer, oldest);
    }

    /// <summary>The changetime of the newest entry held, or null when nothing is live.</summary>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <returns>Its changetime — the last moment this history can interpolate toward.</returns>
    /// <remarks>
    /// **For a scheduler that skips frames, which the engine never does** (B370). An answer reported as
    /// settled is settled for a RISING TARGET, and the target runs an interpolation delay behind the tick
    /// — so the moment it reaches this changetime is the moment the newest entry starts being interpolated
    /// toward. That is `changetime + delay` in tick terms, and it is not the entry's ARRIVAL: measured on
    /// granary, scheduling only arrivals left four ticks drawing a door up to 12.47 units behind, because
    /// the wake fired eight ticks before the pair actually moved.
    /// </remarks>
    public int? HeadChangeTime(int arrivedBy) =>
        LastReceivedAtOrBefore(arrivedBy) is var last && last >= 0 &&
        Head(_generationStarts[last], arrivedBy) is var head && head >= 0
            ? _changeTimes[head]
            : null;

    /// <summary>The newest entry the client still holds — the engine's <c>m_VarHistory.Head()</c>.</summary>
    /// <param name="floor">The generation boundary, so this is the same head the walk can reach.</param>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <returns>Its index, or −1 when the history holds nothing live.</returns>
    /// <remarks>
    /// **The newest LIVE entry, not merely the newest received.** A flushed entry is not in the engine's
    /// list at all, so it cannot be its head — and using the newest received would report an answer as
    /// settled while an entry beyond it was still waiting to be interpolated toward.
    /// </remarks>
    private int Head(int floor, int arrivedBy)
    {
        for (int index = LastReceivedAtOrBefore(arrivedBy); index >= floor; index--)
        {
            if (_flushedAt[index] > arrivedBy)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether two entries hold the same components — the engine's <c>COMPARE_HISTORY</c>.</summary>
    /// <param name="left">One entry.</param>
    /// <param name="right">The other.</param>
    /// <returns><c>true</c> when every component matches.</returns>
    private bool Same(int left, int right) => ValuesAt(left).SequenceEqual(ValuesAt(right));

    /// <summary>The live entry before one, which is the engine's <c>oldestindex = i+1</c>.</summary>
    /// <param name="index">The older of the bracketing pair.</param>
    /// <param name="floor">The generation boundary the search may not cross.</param>
    /// <param name="arrivedBy">The tick being played, which decides what counts as flushed.</param>
    /// <returns>Its live predecessor, or <paramref name="index"/> itself when it has none.</returns>
    /// <remarks>
    /// **`i+1` in a newest-first list is the next entry the engine still HOLDS**, so a flushed one is not
    /// it. Returning `index` when there is no predecessor gives the spline a degenerate third sample,
    /// which is what `dt2 > 0.0001f` then rejects — the same outcome as the engine's
    /// `varHistory.IsIdxValid(oldestindex)` coming back false (<c>interpolatedvar.h:851</c>).
    /// </remarks>
    private int Older(int index, int floor, int arrivedBy)
    {
        for (int candidate = index - 1; candidate >= floor; candidate--)
        {
            if (_flushedAt[candidate] > arrivedBy)
            {
                return candidate;
            }
        }

        return index;
    }

    /// <summary>The third spline sample, respaced to match the interval the curve runs over.</summary>
    /// <param name="pair">The bracketing indices and the oldest, as <see cref="Bracket"/> returned them.</param>
    /// <param name="into">Where to write the components, which must be the history's width.</param>
    /// <returns><c>false</c> when the older interval is empty and the curve must be linear.</returns>
    /// <remarks>
    /// **`TimeFixup2_Hermite`, and it is not an optimisation — it is what makes the spline usable on
    /// real data** (<c>interpolatedvar.h:1372</c>):
    ///
    /// <code>
    /// float dt2 = start-&gt;changetime - prev-&gt;changetime;
    /// if ( fabs( dt1 - dt2 ) &gt; 0.0001f &amp;&amp; dt2 &gt; 0.0001f )
    /// {
    ///     float frac = dt1 / dt2;
    ///     fixup.changetime = start-&gt;changetime - dt1;
    ///     for ( int i = 0; i &lt; m_nMaxCount; i++ )
    ///         fixup.GetValue()[i] = m_bLooping[i]
    ///             ? LoopingLerp( 1-frac, prev-&gt;GetValue()[i], start-&gt;GetValue()[i] )
    ///             : Lerp( 1-frac, prev-&gt;GetValue()[i], start-&gt;GetValue()[i] );
    ///     prev = &amp;fixup;
    /// }
    /// </code>
    ///
    /// A hermite curve assumes its three samples are evenly spaced and a demo's are not: the server
    /// sends when it sends, and a packet arriving late leaves a gap of a different size from the one
    /// before it. Skipping this does not produce a slightly different curve; it produces one that
    /// overshoots whenever the packet spacing wobbles, which on a real recording is most of the time.
    /// Measured on a fixture before it existed: 74.22 units where the engine gives 77.5.
    ///
    /// **<c>dt1</c> is the bracketing interval and it comes from `TimeFixup_Hermite`'s one line** —
    /// `TimeFixup2_Hermite( fixup, prev, start, end->changetime - start->changetime )`
    /// (<c>interpolatedvar.h:1416</c>) — so it is measured between the SAME two entries the fraction
    /// was, on the same clock. That is B278: this arithmetic ran on arrival ticks while everything
    /// around it had moved to applied times, and two packets carrying one applied time gave a
    /// positive arrival gap over a zero-length interval.
    ///
    /// **False means linear rather than a held sample.** The engine reaches this only when
    /// `GetInterpolationInfo` already set `m_bHermite` on `dt2 &gt; 0.0001f`
    /// (<c>interpolatedvar.h:851</c>), so an empty older interval never splines at all — the guard
    /// inside the fixup is the second of two, and here it is the only one.
    /// </remarks>
    public bool TimeFixup(Bracketing pair, Span<float> into)
    {
        int longer = _changeTimes[pair.Newer] - _changeTimes[pair.Older];
        int older = _changeTimes[pair.Older] - _changeTimes[pair.Oldest];

        if (older <= 0)
        {
            return false;
        }

        ReadOnlySpan<float> previous = ValuesAt(pair.Oldest);

        // `fabs( dt1 - dt2 ) > 0.0001f`, on integer ticks: the engine's epsilon is there because its
        // changetimes are seconds, and a tick axis makes the same test exact.
        if (longer == older)
        {
            // Already evenly spaced, so the stored entry is the one the spline wants.
            previous.CopyTo(into);

            return true;
        }

        ReadOnlySpan<float> start = ValuesAt(pair.Older);

        // `Lerp( 1-frac, prev, start )` with `frac = dt1 / dt2`, which places the sample exactly
        // `dt1` before `start` rather than wherever the packet happened to land.
        float toward = 1f - ((float)longer / older);

        for (int component = 0; component < _width; component++)
        {
            into[component] = _looping[component]
                ? ScenePropTrack.LoopingLerp(previous[component], start[component], toward)
                : float.Lerp(previous[component], start[component], toward);
        }

        return true;
    }

    /// <summary>When the next entry after a tick arrives, so a sampler can wake for it.</summary>
    /// <param name="tick">The moment being played.</param>
    /// <returns>The arrival tick of the first entry received after it, or <c>null</c> when there is none.</returns>
    /// <remarks>
    /// **A new entry changes what <see cref="Bracket"/> answers, so it is a wake.** A live client is
    /// told by <c>NoteChanged</c> at the moment the packet lands; a demo's arrivals are on disk, which
    /// is what lets the wake be scheduled rather than discovered.
    /// </remarks>
    public int? ArrivesAfter(int tick)
    {
        int available = LastReceivedAtOrBefore(tick);

        return available + 1 < _received.Count ? _received[available + 1] : null;
    }

    /// <summary>The newest entry the client would have received by a tick, or −1 when none had.</summary>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <returns>Its index, and everything above it is an entry from the future.</returns>
    /// <remarks>
    /// **Exact rather than a starting point, because arrival IS monotonic.** Entries are appended as
    /// packets are read, so the received tick ascends down the list where the changetime does not —
    /// which is why the reach bound is a binary search and the neighbour choice is a walk.
    /// </remarks>
    private int LastReceivedAtOrBefore(int arrivedBy)
    {
        if (_received.Count == 0 || _received[0] > arrivedBy)
        {
            return -1;
        }

        int low = 0;
        int high = _received.Count - 1;

        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);

            if (_received[middle] <= arrivedBy)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The last entry at or before a changetime, by binary search.</summary>
    /// <remarks>
    /// **Only a starting point for <see cref="Bracket"/>'s walk, not the answer.** Changetimes are not
    /// monotonic — measured, 1,631 of one track's 27,478 updates apply earlier than the update before
    /// them — so a binary search over them can land one either side of the target, and the walk is what
    /// makes the result the engine's.
    /// </remarks>
    private int IndexAtOrBefore(double target)
    {
        int low = 0;
        int high = _changeTimes.Count - 1;

        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);

            if (_changeTimes[middle] <= target)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
