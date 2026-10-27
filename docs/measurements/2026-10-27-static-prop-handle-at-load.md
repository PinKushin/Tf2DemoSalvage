# Measurement 2026-10-27: static-prop lighting handles built at load, bit 4 cached (B607)

**Status: planned.** This plan is committed before any timed run.

## Observation

`2026-10-27-static-prop-lighting.md` left `baked` at ~0.05 ms a frame on f12 gummo first person. Its split
(`2026-10-27-static-prop-lighting/split.txt`) counted **174 static states built during play**, 0.146 ms a frame averaged
over every frame of the process (opening seek included), and the colour-mesh misses beside them. The engine builds all of
it when the level loads (`CStaticPropMgr::PrecacheLighting`, `engine.dll` `0x180205b20` → `FUN_1801b8350`).

## What changed (branch `perf/static-prop-handle-at-load`)

- Once the level's static props are packed, `EntityModelSet.PrecacheStaticLighting` builds each one's colour mesh, its
  handle's styled list and, for a bump-mapped model, its static state, at the lighting origin the draw uses.
- Bit 4's ranking is cached per prop and rebuilt when a listed style is stamped (`LevelLighting.StylesChanged(styles)`).
  f12 lists no styled light at any drawn prop, so this half is not exercised by these runs; the conformance tests carry it.

## Question

- A. f12 gummo first person: the frame and `baked`, before (B, `06ae76c2`) against after (A, the branch).
- B. First uses inside the window: `baked built` in both; `static states` in A (B's binary has no such counter).
- C. What moved to load: `before play` `models packed … ms`, `baked built … ms` and (A) `static states … ms`.
- D. Is the drawn result unchanged: a `--shot` at tick 30600, B twice (control) and A.

## Prediction

- A: `baked` and frame equal within the spread. The window opens after the opening seek, by when most first uses are
  done; the 0.146 ms was spread over the whole process. Falsified if A's frame is more than 0.1 ms slower.
- B: A's in-window `baked built` and `static states` are 0; B's `baked built` is whatever few props first come into view
  in the window (≥ 0).
- C: A's before-play time rises by the cost of every static prop's state and mesh: hundreds of milliseconds (174 states
  were ~0.4 s over a run; f12's map has more props than one view sees).
- D: A's shot equals B's wherever B's two shots agree.

## Method

Release builds to `D:/measure-f12/hal-before/` (B, `06ae76c2`) and `D:/measure-f12/hal-after/` (A). One viewer at a
time under `run-exclusive.ps1`, each behind the quiet gate of `2026-10-27-static-prop-lighting/run.ps1` (reused): 20 s
each, f12 tick 30000, `--spectate gummo --first-person`, `+fps_max 0 +r_threaded_particles 1 +cl_threaded_bone_setup 0`,
1280x720. Order ABBA BAAB (`grid.ps1`); then S1-B, S2-B, S3-A shots. Table by `../2026-10-27-static-prop-lighting/read.ps1`;
pixels by its `compare.ps1`; first uses from each run's `first uses in play` line.

## Control

- **Same work:** `posed`/`built` within 3% across a pair, or the pair is void.
- **Machine:** `sound` and `between` within 10% across runs, or the outliers are named and set aside.
- **Shot control:** B against B.
