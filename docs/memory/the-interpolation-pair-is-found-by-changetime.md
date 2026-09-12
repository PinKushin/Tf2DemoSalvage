---
name: the-interpolation-pair-is-found-by-changetime
description: "GetInterpolationInfo selects its pair by comparing CHANGETIMES to the target, so the pair always brackets it. An arrival-adjacent pair does not, and that was the jitter."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T19:00:15.438Z
---

`GetInterpolationInfo` (`interpolatedvar.h:815`) walks the history newest-first and compares each
entry's **changetime** against `targettime = currentTime - interpolation_amount`. It keeps going while
an entry is later than the target and stops at the first one at or before it, so `older` and `newer`
**always bracket the target on the interpolation's own clock** — whatever order the entries arrived in.

**The fault:** this project binary-searched the ARRIVAL tick for a pair, then read that pair's
changetimes out of `_appliedAt`. Whenever the clocks disagree those are different pairs. Measured on
`tf2-2026-pub-pov-clean` entity 9: 26,064 of 27,478 keyframes apply away from their arrival tick, and
**1,631 apply EARLIER than the keyframe before them** — so the stamps are NOT monotonic, contrary to a
comment that assumed they were.

Two shapes broke it: two arrival-adjacent keyframes sharing one changetime (span zero, so the sampler
held the older pose — a stall then a jump) and a later arrival carrying an earlier changetime (span
negative). Drawn result: an average step of 0.89 units carrying single steps of 32.3, and 207 units of
back-and-forth wander on a 60-unit journey. That was the owner's *"kinda jittery and its not a FPS
thing"*.

**How to apply:**

- **Search on the clock the fraction is computed from.** If a fraction uses `_appliedAt`, the pair must
  be found by comparing `_appliedAt` to the target — never by adjacency in another ordering.
- **A degenerate span means fraction ZERO, not "abandon the sample".** The engine guards only the
  division (`if ( dt > 0.0001f )`) and returns true; every other clock still answers for itself.
  Bailing out discarded the animation clock's own interpolation and
  `At_WhenTheTwoClocksDisagree_EachFieldFollowsItsOwn` caught it.
- **A bounded walk is faithful.** `CInterpolatedVar` prunes its history to the interpolation window, so
  the engine never walks far. Holding a whole recording, enter the walk near the target and cap it.
- **Measure wander, not just smoothness.** Path length minus net displacement separates "stutters" from
  "moves the wrong way and comes back", and only the second explains a 2.5× path length.

Related: [[key-a-lookup-on-the-question]],
[[read-the-encoder-not-the-decoder]], [[a-loop-is-state-not-an-event]].

---

## `the-prune-keeps-two-stale-entries` — the history is the window PLUS two, and they can be ancient

**`CInterpolatedVar` does not keep its history "trimmed to the interpolation window".** It keeps the
window plus two, and those two can be arbitrarily old (`interpolatedvar.h:782`):

```cpp
for ( int i = 0; i < m_VarHistory.Count(); i++ )
    if ( m_VarHistory[i].changetime < flTime )
    {
        // We need to preserve this sample (ie: the one right before this timestamp)
        // and the sample right before it (for hermite blending), and we can get rid
        // of everything else.
        m_VarHistory.Truncate( i + 3 );
        break;
    }
```

The list is newest-first, so `i` is the first STALE entry and `Truncate(i+3)` keeps it and the two beyond
it. The live call is at the END of `Interpolate()` (`:1057`); the one at `:667` is inside `#if 0`.

**Two beliefs this refutes, both of which had been written into this repository as fact.**

- *"The engine's three spline samples are always recent, so a long span may be refused."* No. Whatever
  the packet spacing, `oldest` can be seconds old, `TimeFixup2_Hermite` will compute
  `frac = dt1/dt2` in the hundreds, and the respaced sample is an extrapolation far outside the range of
  the real ones. Worked through for a closing door: `Lerp( 1-200, 600, 584 ) = 3784`, then
  `Lerp_Hermite( 0.96, 3784, 584, 584 ) = 579.085` — **five units below shut, in the engine.** A door
  really can dip through its own frame.
- *"An age bound on the older neighbour is Valve's."* It is not, and one was written and removed from
  `InterpolatedHistory.Bracket` for that reason. The pruning bounds how MANY entries are kept, never how
  old the bracketing pair may be.

**It also refuses two entries at ONE changetime, and that is the half of `AddToHead` most easily missed.**
`NoteChanged` passes `bFlushNewer = true` (`interpolatedvar.h:649`), and that branch removes from the head
while `(changetime + 0.0001f) > changeTime` — at-or-after, so `>=` on a tick axis. So `AddToHead` is
unconditional about identical VALUES and flushes on TIME, and a claim that it "appends unconditionally"
is half a reading. An update whose changetime moves BACKWARDS discards everything newer, which is Valve's
stated case: *"The server might have corrected our clock and moved us back."*

Measured cost: a granary shutter sends three 4.5-unit steps under one applied time, and keeping all three
made the drawn height reach the FIRST of them and then switch to the last — 9.019 units in a tenth of a
tick, against 0.45 for a door at its stated speed. Flushing them took the worst step to 3.290.

**What the engine DOES refuse is a sample it has not RECEIVED**, and that is the only bound worth
copying: its history contains arrived entries only. A reader that holds the whole recording has to make
that explicit — a stored arrival tick per entry, and a search bounded by it. Skipping it is B94: a door
sliding toward an update that had not been sent.

**It overshoots UPWARD for the same reason, and there the respacing is the cause rather than the cure.**
A door rising 4.625 units/tick with 4-tick updates, restated 36 ticks after it stops: `frac = 36/4 = 9`,
the synthetic sample is `Lerp( 1-9, 92.5, 111 ) = -55.5` at changetime 88, and its slope to the older
sample is `(111 - -55.5)/36 = 4.625` — the door's REAL speed. Respacing preserves velocity; that is its
purpose. So the curve carries a real velocity into a dead stop and reaches 117.4 against a stated maximum
of 111. **TF2's doors are slightly springy**, and removing that is not more correct.

Two assertions in a row were written on "the curve stays inside the range of its samples", and both times
it looked obviously true. It is false in both directions.

**A held value's restatements cannot change what is drawn, and that is where a conformance sweep goes
wrong.** While nothing newer has ARRIVED the pair is `Older == Newer`, the fraction is 0 and the value
holds — every restatement is invisible. They matter for exactly the window between the next moving
update's arrival and that arrival plus the interpolation delay: four ticks, for a door restated to 300 and
moving at 304. Two sweeps in a row missed it, one over the hold and one starting at `300 + delay`. **When
a mechanism only shows at an ARRIVAL, do not add the delay to the window — the delay is the window.**

`INTERPOLATE_LINEAR_ONLY` would prevent the overshoot and is set on exactly ONE variable in the whole
client — `m_viewtarget` (`c_baseflex.cpp:133`). Not the origin, not the cycle.

Related: [[an-unused-method-may-be-the-engines]],
[[name-the-trade-before-fixing-valve]], [[parity-is-the-search-not-the-defence]].

---

## `a-fraction-of-zero-is-an-oracle` — test a curve where it collapses to an identity

**A duration cannot test a spline.** Three metrics in a row failed to settle B370 because they compared a
drawn motion against a constant-speed ramp, and a hermite is not a ramp: it eases out of a held position
whenever the third sample equals the second, so its time between two heights exceeds a straight line's
**with nothing wrong**. Worse, the ease-in is indistinguishable from the reported symptom — a door that
"opens late" and a door easing off a rest look identical in a duration.

**The assertion that works needs no model of the curve at all.** When the drawn target lands ON a history
entry's changetime, `GetInterpolationInfo` computes
`frac = (targettime - older_change_time) / (newer_change_time - older_change_time) = 0`
(`interpolatedvar.h:845`), and `Lerp_Hermite` at a fraction of zero returns `p1` — whatever its tangents,
whatever the respacing did, whatever the sample either side holds. So:

> **The drawn value at `changetime + interpolationDelay` must equal that entry's own value, exactly, for
> every live entry.**

No speed, no easing, no curve shape enters it. Measured across 7,068 entries on one demo it found 3–5% of
them wrong, the worst by 111 units — a whole door travel — where the duration metrics had reported the
doors as merely "80% of correct" and had also reported them, before their own bugs were fixed, as *faster*
than stated.

**How to apply.** When testing anything interpolated, look for the inputs where the interpolation collapses
to an identity and assert there first:

- fraction 0 or 1 on a lerp or a spline returns an endpoint;
- a degenerate pair (`Older == Newer`) holds a value;
- a reset's three same-changetime entries make `dt2` zero, so the curve is linear.

Those points are oracles: the right answer is a number already in the data, not something a model has to
predict. Everything else about the curve is a second question, and mixing the two produces a measurement
that cannot fail for the fault it names.

**And the fixture for such a test has to be extreme enough to reproduce the fault.** The first one here
corrected a clock by one tick; sabotaging the guard reddened nothing. The fault needed flushed changetimes
far ABOVE the live ones following them, so a binary search lands low and no cheaper clause can climb past.
See [[most-of-a-decoder-is-untested]] — a sabotage that reddens nothing names the missing input.

Related: [[port-the-engines-bottom-layer-first]], [[a-picture-is-assertable]].
