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

/// <summary>A `Reset()` a history did not store (B878): the tick it ran at, the entries before it, and the value it seeded.</summary>
/// <param name="Time">Its `curtime` — the arrival of the update that reset.</param>
/// <param name="Seed">The newest entry received by then; every entry after it arrived after the reset.</param>
/// <param name="Values">
/// `m_pValue` when it ran — the update's NETWORKED value, which is not always an entry: a cycle latches only when the
/// animation time changed (`c_baseentity.cpp:2583`), and `Reset()` seeds whatever was just unpacked
/// (`interpolatedvar.h:745`).
/// </param>
public readonly record struct HistoryAnchor(int Time, int Seed, ReadOnlyMemory<float> Values);

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
///
/// **After a delay rise the prune is visible, so it is answered too** (B860): the delayed target can fall below what the
/// earlier frames' cuts left, and <see cref="PrunedFloor"/> answers the oldest entry the engine would still hold.
/// </remarks>
public sealed class InterpolatedHistory
{
    private int _width;

    private readonly List<int> _changeTimes = [];

    private readonly List<int> _received = [];

    private readonly List<float> _values = [];

    /// <summary>Where each entry's components START in <see cref="_values"/>.</summary>
    /// <remarks>
    /// **Carried rather than computed as <c>index * _width</c>, because the list is a MIXED stride**
    /// (B386). <see cref="SetMaxCount"/> moves the width and <see cref="Reset"/> deletes nothing, so a
    /// history that outlives a model change holds entries of two widths at once — which the engine
    /// never does, because its `ClearHistory()` throws the old ones away.
    ///
    /// A single stride was therefore right until the first width change and wrong for every entry
    /// after it, in one of two ways: past the end when the width GREW, which threw
    /// <c>ArgumentOutOfRangeException</c> out of <c>Slice</c> and killed a headless capture; and
    /// inside a neighbour's components when it SHRANK, which returned the wrong floats and said
    /// nothing. The second is why the fix is the offset and not a bounds check — a clamp turns the
    /// loud half into the silent one.
    ///
    /// An entry's STORED width is the distance to the next offset, so it needs no second list; the width it
    /// was appended UNDER is <see cref="_widths"/>, since an entry may store fewer (B870).
    /// </remarks>
    private readonly List<int> _offsets = [];

    /// <summary>For each entry, the history's width when it was appended — the engine's <c>m_nMaxCount</c> then (B870).</summary>
    /// <remarks>
    /// One generation shares it, because a width change is a wipe, so this is the width an interpolation over the entry
    /// runs: components past it are not interpolated, and the stored ones short of it read as zero.
    /// </remarks>
    private readonly List<byte> _widths = [];

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

    /// <summary>The furthest any entry's changetime has run past its arrival, never below zero.</summary>
    /// <remarks>
    /// Kept across generations and trims and never lowered, so it can only widen a walk that stops by it: one outlier makes
    /// the walk longer, never wrong (B860 review 11, Sonnet).
    /// </remarks>
    private int _maxLead;

    /// <summary>A history whose entries carry a fixed number of components.</summary>
    /// <param name="width">How many floats an entry holds — the engine's <c>m_nMaxCount</c>.</param>
    /// <remarks>
    /// **The width is per variable, and the engine's widths are the variables' own**: three for
    /// `m_iv_vecOrigin`, three for `m_iv_angRotation`, one for `m_iv_flCycle`, and for
    /// `m_iv_flPoseParameter` <c>MAXSTUDIOPOSEPARAM</c> until a model is set and **the model's own parameter count**
    /// after — `m_iv_flPoseParameter.SetMaxCount( hdr->GetNumPoseParameters() )` (<c>c_baseanimating.cpp:1124</c>), with
    /// entries storing a prefix (B870).
    /// Where this project merges two of them into one history it is because they share a latch group and
    /// therefore a changetime, and the risk entry carries the equivalence argument and what would falsify
    /// it.
    /// </remarks>
    /// <param name="storesPrefix">
    /// Whether an entry may store fewer components than the width, the rest reading as +0 — the pose-parameter history's
    /// shape (B870). Every other history keeps the exact-width check.
    /// </param>
    public InterpolatedHistory(int width, bool storesPrefix = false)
    {
        _width = Math.Max(1, width);
        _looping = new bool[_width];
        _storesPrefix = storesPrefix;
    }

    private readonly bool _storesPrefix;

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

    /// <summary>
    /// Drops entries received before <paramref name="cut"/>, keeping <paramref name="tail"/> of them (D217 step 3).
    /// </summary>
    /// <param name="cut">The earliest tick a later question can be asked at, less its lookback.</param>
    /// <param name="tail">How many entries before the cut to keep, for the spline's older samples.</param>
    /// <remarks>
    /// **Indices here are relative and that is safe**: <see cref="Bracketing"/> is produced and consumed within one call
    /// on the same history, never held across a trim. A generation that began before the cut starts at the first kept
    /// entry, which every answer at or after the cut already reaches no lower than.
    /// </remarks>
    internal void DropReceivedBefore(int cut, int tail)
    {
        int drop = Math.Min(LastReceivedAtOrBefore(cut - 1) + 1 - tail, _changeTimes.Count - 1);

        if (drop <= 0)
        {
            return;
        }

        int values = _offsets[drop];

        // The shift and the clear together under the memo's lock, so no memoized read can rebuild from the old indices
        // between them (B860 review 18, Sonnet); other readers are kept off by the build order, not by this (review 19).
        lock (_memoLock)
        {
            _changeTimes.RemoveRange(0, drop);
            _received.RemoveRange(0, drop);
            _flushedAt.RemoveRange(0, drop);
            _offsets.RemoveRange(0, drop);
            _widths.RemoveRange(0, drop);
            _generationStarts.RemoveRange(0, drop);
            _values.RemoveRange(0, values);

            for (int index = 0; index < _offsets.Count; index++)
            {
                _offsets[index] -= values;
                _generationStarts[index] = Math.Max(0, _generationStarts[index] - drop);
            }

            _memos = null;
        }

        // A trim runs before every checkpoint, and a copy copies a list's whole capacity, not its count (D217 step 4).
        _changeTimes.TrimIfSparse();
        _received.TrimIfSparse();
        _flushedAt.TrimIfSparse();
        _offsets.TrimIfSparse();
        _widths.TrimIfSparse();
        _generationStarts.TrimIfSparse();
        _values.TrimIfSparse();
    }

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

    /// <summary>One entry's changetime, the anchor's reset entries reading as its arrival (B878).</summary>
    /// <param name="index">An entry, or <see cref="AnchorSlot"/>.</param>
    /// <param name="anchor">The anchor <see cref="BracketAnchored"/> was asked with.</param>
    /// <returns>The changetime the engine's entry carries.</returns>
    public int ChangeTimeAt(int index, HistoryAnchor? anchor) =>
        index == AnchorSlot && anchor is { } reset ? reset.Time : _changeTimes[index];

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
    /// <returns>Exactly the width the history had when this entry was added.</returns>
    /// <remarks>
    /// **Addressed by where it was WRITTEN, not by the current width** (B386) — see
    /// <see cref="_offsets"/> for what a fixed stride cost and why the width is per entry.
    /// </remarks>
    public ReadOnlySpan<float> ValuesAt(int index) =>
        CollectionsMarshal.AsSpan(_values).Slice(_offsets[index], WidthAt(index));

    /// <summary>One entry's components, the anchor's reset entries reading as the anchor's own (B878).</summary>
    /// <param name="index">An entry, or <see cref="AnchorSlot"/>.</param>
    /// <param name="anchor">The anchor <see cref="BracketAnchored"/> was asked with.</param>
    /// <returns>The components the engine's entry carries.</returns>
    public ReadOnlySpan<float> ValuesAt(int index, HistoryAnchor? anchor) =>
        index == AnchorSlot && anchor is { } reset ? reset.Values.Span : ValuesAt(index);

    /// <summary>The history's width when an entry was appended, which its generation interpolates over (B870).</summary>
    /// <param name="index">Which entry, oldest first.</param>
    /// <returns>The engine's <c>m_nMaxCount</c> for that entry.</returns>
    public int WidthOf(int index) => _widths[index];

    /// <summary>How many components one entry carries, which is its own width and not the history's.</summary>
    /// <param name="index">Which entry, oldest first.</param>
    /// <returns>The distance to the next entry's offset, or to the end of the values for the last.</returns>
    private int WidthAt(int index) =>
        (index + 1 < _offsets.Count ? _offsets[index + 1] : _values.Count) - _offsets[index];

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
    /// <param name="values">The components: the history's width, or up to it where the history stores a prefix (B870).</param>
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
        // **Shorter only where the history stores a prefix, and it reads back as zeros** (B870): a pose-parameter entry
        // stores what the wire stated, and a slot it never sent is zero in the engine's fixed array too.
        if (values.Length > _width || (!_storesPrefix && values.Length != _width))
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
        _maxLead = Math.Max(_maxLead, changeTime - received);

        // **Before the components, so it records where they land** (B386). This is the entry's
        // address; `index * _width` was a guess about it that a width change falsifies.
        _offsets.Add(_values.Count);
        _widths.Add(checked((byte)_width));

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
    /// <see cref="TimeFixup(Bracketing, Span{float}, ReadOnlySpan{bool})"/>, because it is about the curve rather than
    /// about the neighbours.
    ///
    /// **No age bound on the older neighbour, and that is deliberate.** One was written here and removed:
    /// `RemoveEntriesPreviousTo` keeps `Truncate( i + 3 )`, which RETAINS the first entry past the cutoff
    /// plus two more, so the engine will interpolate from an arbitrarily old entry when that is the only
    /// older one it has. Refusing it would be this project's own rule rather than Valve's — the pruning
    /// bounds how MANY entries are kept, never how old the bracketing pair may be.
    /// </remarks>
    public Bracketing? Bracket(double target, int arrivedBy) => BracketVariable(target, arrivedBy, 0, -1);

    /// <summary>
    /// <see cref="Bracket"/> for one engine variable of several this history holds (B860 review 2, Fable):
    /// `bNoMoreChanges` compares that variable's own entries (`COMPARE_HISTORY`, <c>interpolatedvar.h:865</c>), so origin
    /// can settle while the angles sharing its entries still turn.
    /// </summary>
    /// <param name="target">The moment being drawn.</param>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count; negative for the whole entry.</param>
    /// <returns>As <see cref="Bracket"/>, settled by those components alone.</returns>
    public Bracketing? BracketVariable(double target, int arrivedBy, int first, int count)
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

            return new Bracketing(index, newer, oldest, Settled(index, newer, oldest, floor, arrivedBy, first, count));
        }

        // The target precedes every entry this run holds: the oldest LIVE one is all a client would have had — "Didn't find
        // any", `older = newer`, the last walked (`interpolatedvar.h:877-882`). Not the run's first entry, which a later latch
        // stamped at or before it may have flushed (B878 review 1): the walk's last `newer` is the oldest still held.
        // **NOT settled**: time is rising toward entries this answer has not reached yet.
        int held = newer >= 0 ? newer : floor;

        return new Bracketing(held, held, held, NoMoreChanges: false);
    }

    /// <summary>The index <see cref="BracketAnchored"/> answers for the anchor's reset entries, which are not stored.</summary>
    public const int AnchorSlot = -1;

    /// <summary>The anchor for a reset at a tick: that tick, the newest entry received by it, and the value it seeded.</summary>
    /// <param name="time">The arrival the reset ran at — `Reset()`'s `curtime`.</param>
    /// <param name="values">The networked value the reset seeded, the history's width.</param>
    /// <returns>The anchor, or null when nothing had arrived by then.</returns>
    public HistoryAnchor? AnchorAt(int time, ReadOnlyMemory<float> values) =>
        LastReceivedAtOrBefore(time) is var seed && seed >= 0 ? new HistoryAnchor(time, seed, values) : null;

    /// <summary>
    /// <see cref="BracketVariable"/> over the history the engine holds after a `Reset()` this history did not store (B878) —
    /// an entity whose bones were not set up across a parity change.
    /// </summary>
    /// <param name="target">The moment being drawn.</param>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <param name="anchor">The reset: its tick, the newest entry received by then, and the value it seeded.</param>
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count; negative for the whole entry.</param>
    /// <returns>
    /// The pair, with <see cref="AnchorSlot"/> for the reset's three entries (read through
    /// <see cref="ChangeTimeAt(int, HistoryAnchor?)"/> and <see cref="ValuesAt(int, HistoryAnchor?)"/>); the plain answer
    /// when a real reset after the anchor opened a newer generation, or when the pair and its third sample arrived after it.
    /// </returns>
    /// <remarks>
    /// **The engine's list, built and walked as the engine does.** `Reset()` clears and adds three entries at its time
    /// holding the value just latched (`interpolatedvar.h:740-751`); each later latch is `AddToHead` with the flush, removing
    /// newer-or-equal entries from the head first (`:649`), which can remove the reset's three. Then
    /// `GetInterpolationInfo` (`:815-888`), newest first: a target past every entry holds the newest and is settled; one
    /// before every entry holds the OLDEST walked ("Didn't find any", `:877-882`) — the reset's, unless a flush took them.
    /// **Only near the anchor**: once the pair's third sample arrived after it, every entry the walk reads is the plain
    /// history's, so that answer is returned without building anything.
    /// </remarks>
    public Bracketing? BracketAnchored(double target, int arrivedBy, HistoryAnchor anchor, int first = 0, int count = -1)
    {
        Bracketing? plain = BracketVariable(target, arrivedBy, first, count);
        int available = LastReceivedAtOrBefore(arrivedBy);

        if (plain is not { } pair || anchor.Seed > available || _received[_generationStarts[available]] > anchor.Time ||
            (pair.Oldest != pair.Older && _received[pair.Oldest] > anchor.Time))
        {
            return plain;
        }

        int capacity = available - anchor.Seed + ResetEntries;
        Span<int> list = capacity <= 64 ? stackalloc int[64] : new int[capacity];
        int length = 0;

        for (; length < ResetEntries; length++)
        {
            list[length] = AnchorSlot;
        }

        for (int index = anchor.Seed + 1; index <= available; index++)
        {
            int changed = _changeTimes[index];

            while (length > 0 && ChangeTimeAt(list[length - 1], anchor) >= changed)
            {
                length--;
            }

            list[length++] = index;
        }

        int newer = -1;

        for (int position = length - 1; position >= 0; position--)
        {
            int older = list[position];
            int olderTime = ChangeTimeAt(older, anchor);

            if (target < olderTime)
            {
                newer = position;
                continue;
            }

            if (newer < 0)
            {
                return new Bracketing(older, older, older, NoMoreChanges: true);
            }

            int newerEntry = list[newer];

            if (ChangeTimeAt(newerEntry, anchor) - olderTime <= 0)
            {
                return new Bracketing(older, newerEntry, older, NoMoreChanges: false);
            }

            bool hasOldest = position > 0;
            int oldest = hasOldest ? list[position - 1] : older;
            bool spline = hasOldest && olderTime - ChangeTimeAt(oldest, anchor) > 0;
            bool settled = newer == length - 1 && SameAt(newerEntry, older, anchor, first, count) &&
                (!spline || SameAt(newerEntry, oldest, anchor, first, count));

            return new Bracketing(older, newerEntry, oldest, settled);
        }

        int held = list[newer < 0 ? 0 : newer];

        return new Bracketing(held, held, held, NoMoreChanges: false);
    }

    /// <summary>Whether two entries — either may be the anchor's — hold the same bits over one variable.</summary>
    private bool SameAt(int left, int right, HistoryAnchor anchor, int first, int count)
    {
        ReadOnlySpan<float> a = ValuesAt(left, anchor);
        ReadOnlySpan<float> b = ValuesAt(right, anchor);
        int end = count < 0 ? Math.Max(a.Length, b.Length) : first + count;

        return SameBits(Within(a, first, end), Within(b, first, end));
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
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count; negative for the whole entry.</param>
    private bool Settled(int older, int newer, int oldest, int floor, int arrivedBy, int first, int count)
    {
        // **The SAME floor the walk used, and passing a different one was a defect.** `Head` searched the
        // whole array while `Bracket`'s walk stops at the generation boundary, so on granary it reported an
        // entry the search could not reach — head `ct 13921` against a pair pinned at `ct 13920` — and the
        // answer was called settled against a comparison the sampler never makes. Two functions that had to
        // agree, with nothing making them: the track then parked past the moment it started moving, drawing
        // 12.47 units behind.
        if (newer != Head(floor, arrivedBy) || !Same(newer, older, first, count))
        {
            return false;
        }

        // `!pInfo->m_bHermite` — with no third sample in play the pair alone decides. The engine reaches this only inside
        // `if ( dt > 0.0001f )` (`interpolatedvar.h:844`), which always holds here: `Bracket` picks `newer` with a
        // changetime past the target and `older` at or before it, so the pair is a tick apart at least (B860 review 31,
        // Sonnet — a `longer <= 0` term that called such a pair settled was dead, and is gone).
        int shorter = _changeTimes[older] - _changeTimes[oldest];

        return shorter <= 0 || Same(newer, oldest, first, count);
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

    /// <summary>The tick the newest entry received by a tick arrived on — the last latch (B860).</summary>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <returns>Its arrival tick, or null when nothing has arrived.</returns>
    public int? LatestArrival(int arrivedBy) =>
        LastReceivedAtOrBefore(arrivedBy) is var last && last >= 0 ? _received[last] : null;

    /// <summary>
    /// The tick the newest entry received by a tick that CHANGED some of the components arrived on — the last latch that
    /// re-armed that variable (B860 review 2, Fable).
    /// </summary>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <param name="first">The variable's first component in an entry.</param>
    /// <param name="count">How many components the variable has; negative for the whole entry.</param>
    /// <returns>Its arrival tick, or null when nothing has arrived.</returns>
    /// <remarks>
    /// `NoteChanged` adds the entry whatever it holds, but returns true only when the value differs from the history's head
    /// (`memcmp` against `m_VarHistory[0]`, <c>interpolatedvar.h:633-639</c>), and only then does the latch set the
    /// variable's `m_bNeedsToInterpolate` (<c>c_baseentity.cpp:2828-2829</c>). One history here holds several engine
    /// variables (origin and angles share an entry), so the caller names which components are the one variable. An entry
    /// that starts a generation counts as a change, as the first entry of an empty history does. **That is not the
    /// engine's**: `Reset()` is `ClearHistory` and three `AddToHead`s with no `NoteChanged` (<c>interpolatedvar.h:740-752</c>),
    /// so it arms nothing, and a restart at the head's value leaves the variable settled. Counted here, it is listed until
    /// its target passes the reset; every entry it can bracket then holds that one value, so no drawn value differs —
    /// extra listed frames only (*reasoned*, B860 review 16, Fable).
    /// </remarks>
    public int? LatestChange(int arrivedBy, int first, int count)
    {
        // Under the memo's lock with the index it reads, so a trim cannot move the entry between (B860 merge review 2).
        lock (_memoLock)
        {
            return LatestChangeIndex(arrivedBy, first, count) is var index && index >= 0 ? _received[index] : null;
        }
    }

    /// <summary>The changetime of the entry <see cref="LatestChange"/> names: the value a held variable keeps (B860 review 3).</summary>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">How many components; negative for the whole entry.</param>
    /// <returns>Its changetime, or null when nothing has arrived.</returns>
    /// <remarks>
    /// The entry that changed it may since have been flushed by a later same-valued latch (B860 review 7, Fable): the first
    /// entry from it on that is still held carries the same value — nothing changed the variable since — and a live
    /// changetime, so sampling there is a fraction of zero on a real entry.
    /// </remarks>
    public int? LatestChangeTime(int arrivedBy, int first, int count)
    {
        lock (_memoLock)
        {
            int last = LastReceivedAtOrBefore(arrivedBy);

            for (int index = LatestChangeIndex(arrivedBy, first, count); index >= 0 && index <= last; index++)
            {
                if (_flushedAt[index] > arrivedBy)
                {
                    return _changeTimes[index];
                }
            }

            return null;
        }
    }

    /// <summary>The newest entry received by a tick that changed the components, or −1.</summary>
    /// <remarks>
    /// Compared with the entry appended before it, which is the head it met: `NoteChanged` `memcmp`s against
    /// `m_VarHistory[0]` BEFORE its `AddToHead` flushes the newer entries (<c>interpolatedvar.h:633-649</c>), so an entry
    /// this latch flushes is still the head it compares with; and an entry appended earlier cannot have been flushed before
    /// this one arrived, since only a later append flushes (B860 review 6, Sonnet).
    ///
    /// **Memoized per variable as the stretches of entries already examined, each starting at a change, with the changes in
    /// them** (B860 merge reviews 1-11). A player standing still latches the same origin every tick, so the walk back to his
    /// last real change ran the whole rest on every call. Every bounded single-cache design that followed lost one way:
    /// filling from the first entry scanned the whole history; one run thrashed, kept a gap or walked every time; two runs
    /// overlapped and evicted the one in use. A call inside a stretch is a binary search; any other walks back from its tick
    /// only to the first change or to the end of the stretch below, records what it found, and joins touching stretches —
    /// so each entry is examined at most once per walk that completes (a throw records nothing, and a retry walks again),
    /// and nothing is evicted. A trim clears it. The memo is asked
    /// only inside rise windows, so what it holds is the changes in the stretches those windows reached (*reasoned*).
    /// </remarks>
    private int LatestChangeIndex(int arrivedBy, int first, int count)
    {
        // `last` inside the lock too, so a trim cannot shorten the lists between reading it and examining to it (review 19).
        lock (_memoLock)
        {
            int last = LastReceivedAtOrBefore(arrivedBy);

            if (last < 0)
            {
                return -1;
            }

            Memos memos = _memos ??= new Memos();
            List<Stretch> stretches = memos.ChangeOf.TryGetValue((first, count), out List<Stretch>? known) ? known : [];
            int at = StretchAtOrBefore(stretches, last);

            if (at >= 0 && last <= stretches[at].End)
            {
                return stretches[at].NewestAtOrBefore(last);
            }

            // Nothing examined at `last`: walk back from it to the first change, or to the stretch below. The walk records
            // nothing in the memo (only the examined count), so a throw from `Same` leaves the memo as it was.
            Stretch? below = at >= 0 ? stretches[at] : null;
            Stretch? above = at + 1 < stretches.Count ? stretches[at + 1] : null;
            int change = WalkBackToChange(last, below?.End ?? -1, first, count);
            int answer = below is not null && change <= below.End ? below.Newest : change;

            // A step forward appends to the stretch below and a step back prepends to the one above, so neither allocates
            // nor copies (merge reviews 3-5 and review 24 had made both so; B860 merge review 12, lead and cavecrew).
            if (below is not null && change <= below.End + 1)
            {
                if (change > below.End)
                {
                    below.Later.Add(change);
                }

                below.End = last;
                JoinAbove(stretches, at);
            }
            else if (above is not null && last + 1 == above.Start)
            {
                above.Earlier.Add(change);
            }
            else
            {
                stretches.Insert(at + 1, new Stretch(change, last));
            }

            memos.ChangeOf[(first, count)] = stretches;

            return answer;
        }
    }

    /// <summary>The last stretch starting at or before <paramref name="index"/>, or -1.</summary>
    private static int StretchAtOrBefore(List<Stretch> stretches, int index)
    {
        int low = 0;
        int high = stretches.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = (low + high) / 2;

            if (stretches[middle].Start <= index)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// Joins the stretch above into one when they touch, moving the smaller one's changes into the larger, so filling gaps
    /// one at a time costs what it moves at most log-many times per change rather than the whole memo each time.
    /// </summary>
    private static void JoinAbove(List<Stretch> stretches, int at)
    {
        if (at + 1 >= stretches.Count || stretches[at].End + 1 != stretches[at + 1].Start)
        {
            return;
        }

        Stretch lower = stretches[at];
        Stretch upper = stretches[at + 1];

        if (lower.Count >= upper.Count)
        {
            for (int i = upper.Earlier.Count - 1; i >= 0; i--)
            {
                lower.Later.Add(upper.Earlier[i]);
            }

            lower.Later.AddRange(upper.Later);
            lower.End = upper.End;
            stretches.RemoveAt(at + 1);
        }
        else
        {
            for (int i = lower.Later.Count - 1; i >= 0; i--)
            {
                upper.Earlier.Add(lower.Later[i]);
            }

            upper.Earlier.AddRange(lower.Earlier);
            stretches.RemoveAt(at);
        }
    }

    /// <summary>
    /// Walking back from <paramref name="from"/>: the first change above <paramref name="floor"/>, or the floor when none
    /// lies above it. With a floor of -1 the walk always ends on a change, since a generation start is one.
    /// </summary>
    private int WalkBackToChange(int from, int floor, int first, int count)
    {
        int change = from;

        while (change > floor)
        {
            _entriesExamined++;

            if (IsChange(change, first, count))
            {
                break;
            }

            change--;
        }

        return change;
    }

    /// <summary>How many entries the latest-change walks have examined, for a test of the walk's floor.</summary>
    internal long EntriesExamined
    {
        get
        {
            lock (_memoLock)
            {
                return _entriesExamined;
            }
        }
    }

    /// <summary>How many changes a variable's latest-change memo holds, for a test to see what a walk kept.</summary>
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count.</param>
    /// <returns>The count, or zero when the variable has no memo.</returns>
    internal int ChangesHeld(int first, int count)
    {
        lock (_memoLock)
        {
            int held = 0;

            if (_memos is { } memos && memos.ChangeOf.TryGetValue((first, count), out List<Stretch>? stretches))
            {
                foreach (Stretch stretch in stretches)
                {
                    held += stretch.Count;
                }
            }

            return held;
        }
    }

    /// <summary>Whether an entry changed a variable from the one before it, or starts a generation.</summary>
    private bool IsChange(int index, int first, int count) =>
        index == _generationStarts[index] || !Same(index, index - 1, first, count);

    /// <summary>
    /// A stretch of entries the latest-change memo examined, from its first change through <see cref="End"/>, and every
    /// change in it: <see cref="Earlier"/> newest first, then <see cref="Later"/> ascending, never empty.
    /// </summary>
    private sealed class Stretch
    {
        /// <summary>A stretch holding one change, examined through <paramref name="end"/>.</summary>
        /// <param name="change">The change, its first entry.</param>
        /// <param name="end">The last entry examined.</param>
        public Stretch(int change, int end)
        {
            Later.Add(change);
            End = end;
        }

        /// <summary>Changes below <see cref="Later"/>, newest first: a step back adds here.</summary>
        public List<int> Earlier { get; } = [];

        /// <summary>Changes ascending: a step forward adds here.</summary>
        public List<int> Later { get; } = [];

        /// <summary>The last entry examined.</summary>
        public int End { get; set; }

        /// <summary>The first entry, always a change.</summary>
        public int Start => Earlier.Count > 0 ? Earlier[^1] : Later[0];

        /// <summary>The newest change.</summary>
        public int Newest => Later[^1];

        /// <summary>How many changes it holds.</summary>
        public int Count => Earlier.Count + Later.Count;

        /// <summary>The newest change at or before <paramref name="index"/>, which is at or past <see cref="Start"/>.</summary>
        /// <param name="index">An entry in the stretch.</param>
        /// <returns>The change.</returns>
        public int NewestAtOrBefore(int index)
        {
            if (Later[0] <= index)
            {
                int at = Later.BinarySearch(index);

                return Later[at >= 0 ? at : ~at - 1];
            }

            // Earlier descends, so the first at or before `index` is the newest; Start guarantees there is one.
            int low = 0;
            int high = Earlier.Count - 1;

            while (low < high)
            {
                int middle = (low + high) / 2;

                if (Earlier[middle] <= index)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return Earlier[low];
        }
    }

    /// <summary>The memos, made by the first read that needs one: most histories never enter a rise window (review 18).</summary>
    private Memos? _memos;

    /// <summary>Guards the memos, which a read extends: samplers read one history from more than one thread.</summary>
    /// <remarks>
    /// A trim shifts the lists and clears the memos under it too, which protects the MEMO reads only: `BracketVariable`,
    /// the floors and `ValuesAt` read the lists unlocked, and a trim computes its drop before the lock and reallocates after
    /// it. That is safe because trims run before any sampler exists (the timeline's build refuses samples while it trims),
    /// not because of this lock (B860 review 19, Fable). Nor does `Add` clear the settle cursors: they rely on the same
    /// build-before-sample order, which the model reset keeps since B870 moved it into the build.
    /// </remarks>
    private readonly object _memoLock = new();

    /// <summary>Entries the walks examined, under <see cref="_memoLock"/>; read by <see cref="EntriesExamined"/>.</summary>
    private long _entriesExamined;

    /// <summary>What <see cref="LatestChangeIndex"/> and <see cref="SettleFrame{TState}"/> remember.</summary>
    private sealed class Memos
    {
        /// <summary>For each variable, the stretches of entries examined, ascending and disjoint.</summary>
        public Dictionary<(int First, int Count), List<Stretch>> ChangeOf { get; } = [];

        /// <summary>Scan cursors, keyed by what the scan depends on.</summary>
        /// <remarks>
        /// One per schedule, variable and re-arming tick, freed only by a trim: for a variable that changes every tick, one
        /// per frame a rise window examined — bounded by the windows' frames (*reasoned*, review 27, Fable).
        /// </remarks>
        public Dictionary<(object Schedule, int First, int Count, int Changed), (int Through, int? Settled)> Settles { get; } = [];
    }

    /// <summary>
    /// The first frame from <paramref name="changed"/> through <paramref name="last"/> at which
    /// <paramref name="settles"/> holds, memoized (B860 merge review).
    /// </summary>
    /// <param name="schedule">The schedule the frames' targets come from: part of the key, since another gives other frames.</param>
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count.</param>
    /// <param name="changed">The re-arming latch's tick, where the scan starts.</param>
    /// <param name="last">The last frame to ask.</param>
    /// <param name="state">What <paramref name="settles"/> needs, passed so the callback captures nothing (review 18).</param>
    /// <param name="settles">Whether a frame settled the variable: a function of the frame alone for one key.</param>
    /// <typeparam name="TState">The callback's state.</typeparam>
    /// <returns>That frame, or null when none through <paramref name="last"/> did.</returns>
    /// <remarks>
    /// A variable that never settles — a standing player re-latched every tick never has its newest entry as `newer` —
    /// scanned every frame since its last change on every call. The scan's answer at a frame does not depend on when it
    /// is asked, so the cursor carries on from where the last call stopped; a trim clears it. The first scan for a key
    /// still walks from the change, once (*not measured* on a populated demo).
    /// **Valid only once the history is built through the frames asked**: an entry appended later at a tick a cursor has
    /// passed would change that frame's answer. The timeline builds every track before any sample (review 19, Sonnet);
    /// an incremental build would have to clear the memos on such an append.
    /// </remarks>
    internal int? SettleFrame<TState>(
        object schedule, int first, int count, int changed, int last, TState state, Func<int, TState, bool> settles)
    {
        ArgumentNullException.ThrowIfNull(settles);

        lock (_memoLock)
        {
            Memos memos = _memos ??= new Memos();
            (object, int, int, int) key = (schedule, first, count, changed);
            (int through, int? settled) = memos.Settles.TryGetValue(key, out (int Through, int? Settled) known)
                ? known
                : (changed - 1, null);

            if (settled is { } found)
            {
                return found <= last ? found : null;
            }

            for (int frame = through + 1; frame <= last; frame++)
            {
                if (settles(frame, state))
                {
                    memos.Settles[key] = (frame, frame);
                    return frame;
                }
            }

            if (last > through)
            {
                memos.Settles[key] = (last, null);
            }

            return null;
        }
    }

    /// <summary>
    /// The changetime of the oldest entry a client still held after pruning before a threshold (B860).
    /// </summary>
    /// <param name="threshold">The prune cut: an earlier frame's target less the extra history kept.</param>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <returns>That changetime, or null when no live entry is older than the cut.</returns>
    /// <remarks>
    /// `RemoveEntriesPreviousTo` walks newest first, and at the first entry older than the cut keeps it and two more
    /// (`m_VarHistory.Truncate( i + 3 )`, <c>interpolatedvar.h:782-795</c>): the third is the oldest the client holds.
    /// The engine deletes; this keeps every entry for a scrub and answers the bound instead, skipping an entry an earlier
    /// frame's cut had already deleted (<paramref name="cutEarlier"/>, B860 review 10, Fable).
    /// </remarks>
    /// <param name="cutEarlier">Whether an entry, by index, was already gone from the engine's history.</param>
    private int? KeptFloor(double threshold, int arrivedBy, Func<int, bool> cutEarlier)
    {
        int last = LastReceivedAtOrBefore(arrivedBy);

        if (last < 0)
        {
            return null;
        }

        int floor = _generationStarts[last];

        for (int index = last; index >= floor; index--)
        {
            if (_flushedAt[index] <= arrivedBy || _changeTimes[index] >= threshold || cutEarlier(index))
            {
                continue;
            }

            int kept = index;

            for (int more = 0; more < 2; more++)
            {
                int older = Older(kept, floor, arrivedBy);

                if (older == kept || cutEarlier(older))
                {
                    break;
                }

                kept = older;
            }

            return _changeTimes[kept];
        }

        return null;
    }

    /// <summary>
    /// Whether a frame before <paramref name="cutAt"/> had already cut an entry out of the engine's history (B860 review
    /// 10, Fable).
    /// </summary>
    /// <param name="index">The entry.</param>
    /// <param name="cutAt">The frame whose cut is being taken; only frames before it are asked.</param>
    /// <param name="cutOf">A frame's cut: its target less the extra history kept.</param>
    /// <param name="prunes">Whether a frame pruned this variable at all: listed, or reading a latch.</param>
    /// <returns>True when some such frame from the entry's arrival on held three live entries newer than it below its cut.</returns>
    /// <remarks>
    /// `Truncate( i + 3 )` keeps the newest entry below the cut and two more (<c>interpolatedvar.h:782-795</c>), so an entry
    /// is deleted exactly when three live entries newer than it are below the cut. An entry that arrived before this one
    /// with a later changetime was flushed by it (`AddToHead`, <c>:649</c>), so every newer entry counted arrived after it,
    /// and none of them can have been cut before it without cutting it too: counting the unflushed ones is enough.
    /// </remarks>
    private bool CutBefore(int index, int cutAt, Func<int, double> cutOf, Func<int, bool> prunes)
    {
        int changeTime = _changeTimes[index];
        int generation = _generationStarts[index];

        // **No frame before the third newer entry arrived held three** (B860 review 11, Sonnet): the walk starts there, so
        // an entry at rest for a long time costs the frames since its third newer arrival, not every frame since its own.
        int? third = null;

        for (int other = index + 1, newer = 0; other < _received.Count && _received[other] < cutAt; other++)
        {
            if (_generationStarts[other] == generation && _changeTimes[other] > changeTime && ++newer == 3)
            {
                third = _received[other];
                break;
            }
        }

        if (third is not { } earliest)
        {
            return false;
        }

        for (int frame = cutAt - 1; frame >= earliest; frame--)
        {
            double cut = cutOf(frame);

            if (cut <= changeTime)
            {
                continue;
            }

            int newer = 0;
            int last = LastReceivedAtOrBefore(frame);

            for (int other = index + 1; other <= last && newer < 3; other++)
            {
                if (_generationStarts[other] == generation && _flushedAt[other] > frame &&
                    _changeTimes[other] > changeTime && _changeTimes[other] < cut)
                {
                    newer++;
                }
            }

            if (newer == 3 && prunes(frame))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The changetime of the oldest entry a client holds now, after a frame cut the history and later updates arrived
    /// (B860 review 9, Fable).
    /// </summary>
    /// <param name="threshold">The cut.</param>
    /// <param name="cutAt">The frame that made it: the three it kept are chosen from what had arrived by then.</param>
    /// <param name="arrivedBy">The tick being played.</param>
    /// <param name="cutOf">A frame's cut: its target less the extra history kept.</param>
    /// <param name="prunes">Whether a frame pruned this variable at all: listed, or reading a latch.</param>
    /// <returns>That changetime, or null when the cut dropped nothing.</returns>
    /// <remarks>
    /// After the cut the history holds the three kept entries and whatever arrived since, less what a later update's
    /// `AddToHead` flushed (every entry at or after its changetime, <c>interpolatedvar.h:649</c>): a late update stamped
    /// below the kept three deletes them and is itself the oldest held. The three are chosen from what earlier frames'
    /// cuts left (B860 review 10, Fable): a late update arriving BEFORE the cutting frame flushes the entries after it, and
    /// the two older than it may already be gone.
    /// </remarks>
    /// <remarks>
    /// **Not exact for entries arriving after the cut** (B860 review 11, Fable): frames after <paramref name="cutAt"/> prune
    /// too, at their lower cuts, and can delete an old late arrival this still counts as held. With one rise no frame draws
    /// the difference, because a frame's cut that reaches such an entry sits below that frame's own target, which is then
    /// above it; a second rise inside the window can drop a later target below it again (review 12, Fable), which needs a
    /// late update tens of ticks back as well (*reasoned*, not measured).
    /// </remarks>
    public int? PrunedFloor(
        double threshold, int cutAt, int arrivedBy, Func<int, double> cutOf, Func<int, bool> prunes)
    {
        if (KeptFloor(threshold, cutAt, index => CutBefore(index, cutAt, cutOf, prunes)) is not { } kept)
        {
            return null;
        }

        int last = LastReceivedAtOrBefore(arrivedBy);
        int? oldest = null;

        // An entry received well before the kept changetime was stamped before it too, so it is neither kept nor late: the
        // walk stops there. "Well before" is by the furthest any entry's changetime has led its arrival (B860 review 10,
        // Sonnet): a simulation time carried onto the demo's axis can sit after the packet that carried it.
        for (int index = last; index >= 0 && index >= _generationStarts[last] && _received[index] + _maxLead >= kept; index--)
        {
            bool held = _flushedAt[index] > arrivedBy &&
                (_received[index] > cutAt || _changeTimes[index] >= kept);

            if (held && (oldest is not { } so || _changeTimes[index] < so))
            {
                oldest = _changeTimes[index];
            }
        }

        return oldest;
    }

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
    /// <param name="first">The variable's first component.</param>
    /// <param name="count">Its component count; negative for the whole entry.</param>
    /// <remarks>
    /// **Entries of two widths are not the same; a span neither holds fails loudly** (B860 reviews 18-21 and merge review
    /// 2). Two entries of one generation share a width (`SetMaxCount` resets on a change), and since merge review 2 the
    /// change memo walks back only to the latest change, which a generation start always is, so a call at or after a
    /// generation's start never examines an older, narrower generation; only a call asked at a tick inside the older one
    /// does (review 22, Fable). A span that does not fit a same-width pair is
    /// a caller asking the wrong history, and B386's slice exception is the right answer to that, not a silent "changed"
    /// on every entry (`LatestChange_AcrossAWidthChange_StopsAtTheReset`).
    ///
    /// **A history that stores a prefix compares the padded arrays** (B870): the engine's entries are all `m_nMaxCount`
    /// wide, and a stored entry shorter than its neighbour holds the slots the wire had not sent, which are +0 — so the
    /// span is cut from each stored prefix and the part either lacks must be +0 in the other.
    /// </remarks>
    private bool Same(int left, int right, int first, int count)
    {
        ReadOnlySpan<float> a = ValuesAt(left);
        ReadOnlySpan<float> b = ValuesAt(right);

        if (!_storesPrefix)
        {
            return count < 0 || a.Length != b.Length
                ? SameBits(a, b)
                : SameBits(a.Slice(first, count), b.Slice(first, count));
        }

        int end = count < 0 ? Math.Max(a.Length, b.Length) : first + count;
        ReadOnlySpan<float> x = Within(a, first, end);
        ReadOnlySpan<float> y = Within(b, first, end);
        ReadOnlySpan<float> shorter = x.Length <= y.Length ? x : y;
        ReadOnlySpan<float> longer = x.Length <= y.Length ? y : x;

        return SameBits(shorter, longer[..shorter.Length]) && ZeroBits(longer[shorter.Length..]);
    }

    /// <summary>The stored part of components <paramref name="first"/> up to <paramref name="end"/>.</summary>
    private static ReadOnlySpan<float> Within(ReadOnlySpan<float> stored, int first, int end) =>
        stored[Math.Min(first, stored.Length)..Math.Min(end, stored.Length)];

    /// <summary>Whether every component is +0, bit for bit, which is what an unsent slot holds.</summary>
    private static bool ZeroBits(ReadOnlySpan<float> values) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(values).IndexOfAnyExcept((byte)0) < 0;

    /// <summary>
    /// Bit for bit, as the engine's <c>memcmp</c> is (<c>interpolatedvar.h:22-23</c>, <c>:636</c>): +0 and −0 differ, and
    /// a NaN matches only its own bits (B860 review 28, Fable).
    /// </summary>
    private static bool SameBits(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(a).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(b));

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
    public bool TimeFixup(Bracketing pair, Span<float> into) => TimeFixup(pair, into, _looping);

    /// <summary>The third spline sample, respaced, with the looping flags the caller holds for this moment.</summary>
    /// <param name="pair">The bracketing indices and the oldest, as <see cref="Bracket"/> returned them.</param>
    /// <param name="into">Where to write the components, which must be the history's width.</param>
    /// <param name="looping">Whether each component wraps, the history's width.</param>
    /// <returns><c>false</c> when the older interval is empty and the curve must be linear.</returns>
    /// <remarks>
    /// **For a flag the engine sets per FRAME rather than per model** (B726): `m_iv_flCycle.SetLooping( IsSequenceLooping(
    /// GetSequence() ) )` (<c>c_baseanimating.cpp:4471-4472</c>). This history answers every frame and every seek, so it
    /// cannot hold that flag; the caller passes it with the question.
    /// </remarks>
    public bool TimeFixup(Bracketing pair, Span<float> into, ReadOnlySpan<bool> looping) =>
        TimeFixup(pair, into, looping, null);

    /// <summary>The third spline sample of a <see cref="BracketAnchored"/> answer (B878).</summary>
    /// <param name="pair">The bracketing indices, any of which may be <see cref="AnchorSlot"/>.</param>
    /// <param name="into">Where to write the components, which must be the history's width.</param>
    /// <param name="looping">Whether each component wraps, the history's width.</param>
    /// <param name="anchor">The anchor the pair was asked with.</param>
    /// <returns><c>false</c> when the older interval is empty and the curve must be linear.</returns>
    public bool TimeFixup(Bracketing pair, Span<float> into, ReadOnlySpan<bool> looping, HistoryAnchor? anchor)
    {
        int longer = ChangeTimeAt(pair.Newer, anchor) - ChangeTimeAt(pair.Older, anchor);
        int older = ChangeTimeAt(pair.Older, anchor) - ChangeTimeAt(pair.Oldest, anchor);

        if (older <= 0)
        {
            return false;
        }

        ReadOnlySpan<float> previous = ValuesAt(pair.Oldest, anchor);
        ReadOnlySpan<float> start = ValuesAt(pair.Older, anchor);

        // Over the pair's own width, `m_nMaxCount` when it was appended; a stored prefix reads zeros after it (B870).
        int width = pair.Newer == AnchorSlot && anchor is { } reset ? reset.Values.Length : WidthOf(pair.Newer);

        // `fabs( dt1 - dt2 ) > 0.0001f`, on integer ticks: the engine's epsilon is there because its
        // changetimes are seconds, and a tick axis makes the same test exact.
        //
        // `Lerp( 1-frac, prev, start )` with `frac = dt1 / dt2`, which places the sample exactly
        // `dt1` before `start` rather than wherever the packet happened to land. Already evenly spaced, the stored entry
        // is the one the spline wants.
        float toward = 1f - ((float)longer / older);

        for (int component = 0; component < width; component++)
        {
            float before = component < previous.Length ? previous[component] : 0f;
            float after = component < start.Length ? start[component] : 0f;

            if (longer == older)
            {
                into[component] = before;
                continue;
            }

            // `Lerp` is `A + (B - A) * flPercent` (`mathlib.h:629-635`); `float.Lerp` rounds differently.
            into[component] = looping[component]
                ? ScenePropTrack.LoopingLerp(before, after, toward)
                : before + ((after - before) * toward);
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
    /// which is why the reach bound is a binary search and the neighbour choice is a walk. Except after a view-time
    /// model reset, which appends out of arrival order: B870.
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
