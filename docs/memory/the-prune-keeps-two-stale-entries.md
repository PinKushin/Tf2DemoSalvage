---
name: the-prune-keeps-two-stale-entries
description: RemoveEntriesPreviousTo keeps Truncate(i+3), so a client's interpolation history holds two arbitrarily old entries — the engine's spline is NOT bounded to recent samples.
metadata:
  type: reference
---

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

Related: [[the-interpolation-pair-is-found-by-changetime]], [[an-unused-method-may-be-the-engines]],
[[name-the-trade-before-fixing-valve]], [[parity-is-the-search-not-the-defence]].
