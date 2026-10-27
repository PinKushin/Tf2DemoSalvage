---
name: a-split-carries-its-first-use-builds
description: "A timer split of a cached lookup includes its first-use builds; time the miss path separately before calling the lookup slow."
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-27
---

A throwaway split of `baked` (2026-10-27, `docs/measurements/2026-10-27-static-prop-lighting.md`) read the static-state
lookup (`LevelLighting.StaticPropLightingAt`, a dictionary hit after the first call) at 0.15-0.21 ms a frame for ~51
props — about 4 µs a hit, which no dictionary costs. Timing the miss path on its own showed **0.146 ms of it was 174
first-use builds** (rays per prop) spread over the run's frames; the hits were ~0.006 ms.

**Rule:** when a split puts time on something cached, time the miss path apart (ticks and count) before believing the
hit is slow. A per-frame average over the whole process also carries the opening seek's builds.
**Why:** the first reading pointed at the wrong fix (the lookup) while the real per-frame cost sat next to it (a walk
over 32 dead dlight slots per prop). Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[count-the-changes-before-caching-on-unchanged]].
