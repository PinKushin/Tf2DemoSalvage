# Measurement 2026-10-27: static-prop lighting handles built at load, bit 4 cached (B607)

**Status: measured 2026-10-27.** The plan was committed (`e803a855`) before any timed run; one replacement ABBA (09-12,
`grid2.ps1`) was added after the grid, for the reason in the Environment.

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

## Environment

RTX 3060, i5-10600K; Release builds as in the Method (A from `e761fc6e`, B from `06ae76c2`); 2026-10-27 18:34-19:52. Every
run passed the quiet gate before it started (`grid-log.txt`), but **other agents' work loaded the machine during 01-06**
(load after 01-A 46/43/97, after 05-B 41/100/26; the coordinator confirmed a decompile and an ffmpeg job at 18:41-18:50).
Load after 09-A and 10-B was high too (54/96/87, 54/87/77), but their `sound` and `between` pass the machine control, so
it came after their windows or did not reach them. **Added after the grid, not in the plan:** `grid2.ps1`, one more ABBA (09-A, 10-B, 11-B, 12-A).

## Result

Raw output: `2026-10-27-static-prop-handle-at-load/NN-*.txt`; table by `../2026-10-27-static-prop-lighting/read.ps1`. ms a
frame.

| run | frame | baked | pose | camera pose | sound | between | posed | built |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 07 A | 8.37 | 0.069 | 3.35 | 3.05 | 0.17 | 0.34 | 35.7 | 35.8 |
| 08 B | 8.39 | 0.056 | 3.22 | 2.94 | 0.17 | 0.34 | 36.1 | 36.1 |
| 09 A | 8.38 | 0.063 | 3.16 | 3.01 | 0.18 | 0.34 | 34.8 | 34.9 |
| 10 B | 8.35 | 0.056 | 3.23 | 2.98 | 0.18 | 0.33 | 35.6 | 35.6 |
| *01 A, set aside* | 13.39 | 0.082 | 4.33 | 3.79 | 0.35 | 0.61 | 36.6 | 36.7 |
| *02 B, set aside* | 14.85 | 0.089 | 4.87 | 4.79 | 0.41 | 0.67 | 37.9 | 37.9 |
| *03 B, set aside* | 11.17 | 0.054 | 3.70 | 3.49 | 0.26 | 0.52 | 35.9 | 35.8 |
| *04 A, set aside* | 15.01 | 0.073 | 4.75 | 4.97 | 0.39 | 0.60 | 35.0 | 35.2 |
| *05 B, set aside* | 9.83 | 0.064 | 3.64 | 3.18 | 0.21 | 0.44 | 35.7 | 35.7 |
| *06 A, set aside* | 9.33 | 0.063 | 3.46 | 3.26 | 0.21 | 0.39 | 34.8 | 34.9 |
| *11 B, set aside* | 8.88 | 0.053 | 3.37 | 3.06 | 0.19 | 0.38 | 35.2 | 35.2 |
| *12 A, set aside* | 9.20 | 0.067 | 3.49 | 3.17 | 0.20 | 0.37 | 34.6 | 34.6 |

**Controls, applied as the plan wrote them.** Machine (`sound`/`between` within 10% of the quiet 0.17/0.33-0.34): 07, 08,
09, 10 pass; 01-06 (`between` 0.39-0.67) and 11-12 (0.37-0.38) fail and are set aside. Same work (`posed` within 3%):
07-A/08-B 1.1%, 09-A/10-B 2.2%, 07-A/10-B 0.3% pass; 08-B/09-A 3.6% is void.

**First uses** (each run's `first uses in play` line; identical in every run of a binary):

| | in play (20 s window) | before play |
|---|---|---|
| B | baked built **233**, 10.8-17.3 ms per run | models packed 735 in 444-734 ms; baked built 188, 23.6-37.5 ms |
| A | baked built **0**; static states **0** | models packed 735 in 1020-1330 ms; baked built 959, 64.9-80.0 ms; static states **404, 468-585 ms** |

*Read with care:* A's `models packed` ms in these runs includes the precache, which then ran inside `Add`'s pack clock
(fixed after the runs, review item 1: the precache now runs after it), and its `baked built 959` counts the static props
answered "no colour mesh" too (fixed likewise: only a mesh built counts now). B's counter also counted a refused `.vhv`
as a build. So the before-play rise is **~0.5-0.6 s** (A's 1020-1330 against B's 444-734), of which the static states
are 468-585 ms and the colour meshes most of the rest.

Identity (`compare.ps1`): S2-B against S3-A, **54 pixels, all in (1101,2)-(1119,10)** — the frame-rate readout. S1-B
against S2-B, 36,200 pixels in the band (0,0)-(1279,48); S1-B against S3-A, 36,192 in the same band. So A equals S2-B
outside the readout, and differs from S1-B only where the two B shots disagree with each other.

## Verdict

- **Frame: unchanged.** A 8.37-8.38 ms against B 8.35-8.39 in the pairs passing both controls, −0.02 to +0.03 ms
  (−0.2 to +0.4% of the frame). Prediction A holds.
- **`baked`: +0.007 to +0.013 ms in A** (0.063-0.069 against 0.056), 0.1-0.2% of the frame, inside B's own spread across
  all runs (0.053-0.089). Not explained; not measured further.
- **First uses in play: 233 colour meshes (10.8-17.3 ms a run) → 0; static states → 0.** Prediction B holds. Averaged
  over 20 s at ~2,400 frames this was ~0.005 ms a frame, so it shows as no frame change; it was a stall per prop
  coming into view, B595's subject, rather than a per-frame cost.
- **Load: +0.5-0.6 s before the first frame**, user-visible. Prediction C holds. *For the release notes:* opening a map
  takes about half a second longer on f12 (404 static-prop lighting states and every static prop's colour mesh built up
  front, as TF2 builds them at map load); in exchange no prop's lighting is built while the demo plays.
- **Pixels: unchanged** outside the frame-rate readout and the band S1-B itself disagrees in. Prediction D holds on
  S2-B; the B-B control failed in the top 48 rows, so that band says nothing. This is the output-level check that a
  load-time origin and the draw's agree: a mismatch would rebuild on first draw (`static states` in play would not be 0)
  or draw the wrong state.

**Not drawn or not compared** (D129): skinning runs on the GPU where Valve's is on the processor; muzzle flashes, shell
ejection and `DoAnimationEvents` unchecked since D129. **f12 lists no styled light at any drawn prop**, so bit 4's cache
is not exercised by these runs: it is pinned by the conformance tests, `LightStyleEffectsTests`, and on a real map by
`StaticPropModelsWiringTests.Instances_KothDryfieldDrawnTwiceThenStyleOneSwitchedOff_RanksOnlyOnTheStamp` (a count, not a
shot).
