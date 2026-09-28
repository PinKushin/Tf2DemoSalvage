---
name: the-interpolation-pair-is-found-by-changetime
description: "GetInterpolationInfo selects its pair by comparing CHANGETIMES to the target, so the pair always brackets it. An arrival-adjacent pair does not, and that was the jitter."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T19:00:15.438Z
---

The engine's interpolation walks history comparing each entry's CHANGETIME against a target, so its
pair always brackets the target on that clock — whatever order entries arrived in.

**The fault:** this project binary-searched the ARRIVAL tick for a pair, then read that pair's
changetimes separately. Whenever the clocks disagree, those are different pairs. Measured: 26,064 of
27,478 keyframes apply away from their arrival tick, and 1,631 apply EARLIER than the keyframe before
them — stamps are NOT monotonic, contrary to an assumption written elsewhere. Result: an average step
of 0.89 units carrying single steps of 32.3, and 207 units of wander on a 60-unit journey — the
owner's *"kinda jittery and its not a FPS thing"*.

**How to apply:**
- Search on the CLOCK the fraction is computed from, never by adjacency in another ordering.
- A degenerate span means fraction ZERO, not "abandon the sample" — the engine guards only the
  division and returns true; every other clock still answers.
- A bounded walk is faithful — the engine prunes to the interpolation window; entering near the
  target and capping the walk matches that.
- Measure wander (path length minus net displacement), not just smoothness — only wander separates
  "stutters" from "moves wrong way and comes back".

Related: [[key-a-lookup-on-the-question]], [[read-the-encoder-not-the-decoder]],
[[a-loop-is-state-not-an-event]].

---

## `the-prune-keeps-two-stale-entries` — the history is the window PLUS two, and they can be ancient

**The engine's history is not trimmed to the interpolation window — it keeps the window plus two,
and those two can be arbitrarily old.** The list is newest-first; on finding the first stale entry, it
truncates keeping that entry and the two beyond it (needed for hermite blending).

**Two beliefs this refutes, both written into this repo as fact:**
- "The three spline samples are always recent" — false; the oldest can be seconds old, producing a
  respaced sample that's an extrapolation far outside the real range — worked through for a door: the
  respaced sample dips FIVE UNITS below shut, in the engine itself.
- "An age bound on the older neighbour is Valve's" — it is not; the pruning bounds COUNT, never age.

**It also refuses two entries at ONE changetime** — an update flushes from the head while at-or-after
the new changetime, so "appends unconditionally" is half a reading; an update moving TIME backwards
discards everything newer (Valve's stated case: the server corrected the clock). Measured cost: three
steps under one applied time, keeping all three made drawn height jump 9 units in a tenth of a tick
instead of the correct 0.45.

**What the engine DOES refuse: a sample it hasn't RECEIVED** — that's the only bound worth copying. A
reader holding a whole recording needs an explicit arrival-tick bound per entry.

**It overshoots UPWARD too, for the opposite reason: respacing preserves VELOCITY on purpose**, so a
restated sample computed from real speed carries that speed into a dead stop and overshoots past the
stated maximum — TF2's doors are slightly springy, and removing that isn't more correct.

**A held value's restatements can't change what's drawn** — while nothing newer has arrived, the pair
is degenerate (fraction 0, value holds); every restatement is invisible except in the narrow window
between the next update's arrival and arrival-plus-interpolation-delay. Two conformance sweeps missed
this window entirely by adding the delay to it instead of recognising the delay AS the window.

Related: [[an-unused-method-may-be-the-engines]], [[name-the-trade-before-fixing-valve]],
[[parity-is-the-search-not-the-defence]].

---

## `a-fraction-of-zero-is-an-oracle` — test a curve where it collapses to an identity

**A duration cannot test a spline.** Three duration-based metrics failed to settle a jitter bug
because a hermite eases out of a held position, taking longer between two heights than a straight
line WITH NOTHING WRONG — indistinguishable from the reported symptom.

**The assertion that works needs no model of the curve:** when the drawn target lands exactly ON a
history entry's changetime, the interpolation fraction is mathematically zero, and any spline at
fraction zero returns that entry's own value — regardless of tangents or respacing. So: **the drawn
value at (changetime + interpolation delay) must equal that entry's own value, exactly, for every
live entry.** No speed, no easing, no curve shape enters it. Measured across 7,068 entries, found 3-5%
wrong, worst by 111 units — where duration metrics had reported the same doors as merely "80%
correct" and, before their own bugs were fixed, as FASTER than stated.

**How to apply:** for anything interpolated, find inputs where the interpolation collapses to an
identity and assert there first (fraction 0/1, a degenerate pair, a zero-length segment). Those points
are oracles — the right answer is already in the data. The fixture for such a test must be extreme
enough to reproduce the fault (a sabotage reddening nothing here meant the fixture's clock corruption
was too mild). See [[most-of-a-decoder-is-untested]].

Related: [[port-the-engines-bottom-layer-first]], [[a-picture-is-assertable]].
