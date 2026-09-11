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

Related: [[a-vector-keys-its-halves-differently]], [[key-a-lookup-on-the-question]],
[[read-the-encoder-not-the-decoder]], [[a-loop-is-state-not-an-event]].
