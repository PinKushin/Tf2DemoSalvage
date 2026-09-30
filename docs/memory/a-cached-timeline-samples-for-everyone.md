---
name: a-cached-timeline-samples-for-everyone
description: DemoTimeline.PropsAt keeps a sample between calls; it is a cache, so a stepped sample must answer exactly what a cold one does, and it is locked so a shared timeline is safe (B438).
metadata:
  type: project
---

`PropsAt` keeps state between calls — the wake queue, the lerp list, `_sampledTo`, each track's `Live`
and `Lerping` (B259 stage C) — so it pays only for what changed. **That state is a cache: every answer
must equal what a timeline built cold answers at the same tick, and it is taken under a lock**, so one
timeline can be shared (`TimelineCache`) and asked any ticks in any order, from any thread.

**Why:** 2026-09-30 (B105 `model_world`), four parallel `[TestCase]`s on the cached z1800: three found no
sapper where each alone did. First filed as a test race and worked around with a timeline per test. B438
found it was two faults: no lock, AND a stepped sample that was not the cold one — `Motion` parked a
track once its ORIGIN settled, where `Interp_Interpolate` ANDs every var (`c_baseentity.cpp:861-893`).
`sample-history` measured it: sentries' cycle and aim stepped at the packet rate on 52-94% of samples
while playing, and a sticky with a corrected clock drew 352 units off; a scrub showed the right pose.

**How to apply:**
- A wake schedule is a restatement of `At`'s branches; every one must be a wake or keep the track on the
  lerp list — all three histories, the first-pose regime (`born + delay`), a move child's keyframes, and
  every caller-supplied input (tick, team, pause, view entity) as a resync trigger.
- Check with `sample-history <demo>` (stepped against cold, with controls) and
  `DemoTimelineSampleOrderTests`; any difference is a missing boundary, never noise.
- An absence that appears only under parallel runs is shared state, not the demo. See
  [[instrument-bugs-outnumber-decoder-bugs]], [[the-interpolation-pair-is-found-by-changetime]].
