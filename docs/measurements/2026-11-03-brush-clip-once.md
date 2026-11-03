# Measurement 2026-11-03: does clipping each brush once a trace cut the light cache's per-frame cost?

## Observation

The lead's and B667's own account: `docs/RISKS.md` B667 leaves the brush tree untouched, "two thirds of the remaining trace
cost", and `measurements/2026-11-02-b666-light-trace-load.md` has the pose's `lighting` column at about 0.71 ms mean on f12.
Reading `BspLeafTree.Descend`/`ClipToLeafBrushes` on 2026-11-03: the whole ray is clipped against every brush of every leaf
the walk visits, and nothing remembers a brush already clipped. Quake 2's `CM_TraceToLeaf` (`qcommon/cmodel.c`, the ancestor
this walk is read from) skips one: `if (b->checkcount == checkcount) continue;`. A brush spanning several leaves is therefore
clipped once per leaf here. How often is not known. An unquotable exploratory sampled trace and counters ranked the column
first among the frame's discrete items, and put traces at most of an entry build.

## Question

Over the rays the light visibility trace really casts, how many brush clips does a ray make, how many of them are of a brush
already clipped in that ray, and what does clipping each once save in the brush tree's replay and in the viewer's `lighting`
column, with every trace's answer unchanged?

## Hypothesis

Repeats are common: a brush large enough to hold up a map spans many leaves, and a long light ray crosses many leaves.

## Prediction

- Census: clips a ray with the skip are at most 0.8 of clips a ray without it (at least one clip in five is a repeat), on both
  maps. Below that margin the change is dropped and this verdict says so.
- Replay: the `brush tree` figure falls by at least 15% on both maps.
- Both checksums stay exactly `5fe80ad4d5e01f8e` (koth_toxic) and `76d663f449d20c7c` (cp_process_f12). Any difference
  falsifies the change, not the checksum.

## Method

A = this branch with the counting seam and the probe's census line, no skip. B = the same with the skip. Each is built once and
its output copied aside (`D:/nfw-scratch/{A,B}`), so an ABBA order compares fixed binaries.

**Instrument 1, the replay:** `Tf2DemoSalvage.Probe.exe trace-cost koth_toxic` and `trace-cost cp_process_f12`, order A B B A for
each map. It records the rays of every static state through `LevelLighting.From`'s own trace and replays them. Read: `replay`
(`brush tree`, best of 3, `TraceBrushOnly`), `brush clips a ray`, and the checksum.

**Instrument 2, the frame:** `TF2VIEW_AUTOPLAY=1 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000 --first-person --measure 20
+fps_max 0`, under `run-exclusive.ps1`, order A B B A. Read: the summary's frames and mean ms, the per-interval `lighting` column
and frame count. Reported: the frame-weighted `lighting` mean over all intervals and over the intervals after the first four,
which are warm-up. Both ms and percent of the frame.

## Control

- Replay, a count that must be present: A's `brush clips a ray` must be above zero and B's above zero, or the seam is not on the
  path. The probe's own three controls (wrapped total, split against whole, composed fraction, 0 differing) still print.
- Replay, the checksum over all thirteen `BspTrace` values of every ray, A against B: the equivalence instrument, proved able to
  move in B667.
- Frame: ticks advanced about 66.7 x 20 (about 1,334), and the `lighting` column non-zero in the window, or no entry was lit and
  the window cannot see the change.
- A run that overlapped a build or a game on the desktop is void. The desktop is contended by other agents: the lock serialises
  the runs, and what held it is noted.

## Environment

RTX 3060, i5-10600K; Debug build (optimized, D176), the build the owner runs. Commits and load are filled in with the result.

## Result

Run 2026-11-03 04:57-05:04, A and B built from `7bd2440c` plus the seam (and, for B, the skip); neither was committed. The
lock showed other agents' runs near these: a holder before koth_toxic's first B, and short shared probes (`vmt`, `pak`,
`prop-shade`) finishing as three of the f12 runs began. The timings carry that noise, and no other load was recorded. The columns are each probe output's `replay` and `brush clips` lines.

| map | run | `brush clips a ray` | `brush tree`, best of 3 | `TraceBrushOnly` | checksum |
|---|---|---:|---:|---:|---|
| koth_toxic, 267,527 rays | A | 14.6 | 387.0 ms | 591.8 ms | `5fe80ad4d5e01f8e` |
| | B | 13.5 | 354.1 ms | 497.1 ms | `5fe80ad4d5e01f8e` |
| | B | 13.5 | 332.3 ms | 528.3 ms | `5fe80ad4d5e01f8e` |
| | A | 14.6 | 341.1 ms | 533.3 ms | `5fe80ad4d5e01f8e` |
| cp_process_f12, 47,923 rays | A | 10.4 | 60.9 ms | 132.6 ms | `76d663f449d20c7c` |
| | B | 9.2 | 59.9 ms | 129.8 ms | `76d663f449d20c7c` |
| | B | 9.2 | 59.4 ms | 148.3 ms | `76d663f449d20c7c` |
| | A | 10.4 | 59.9 ms | 125.6 ms | `76d663f449d20c7c` |

The probe's three controls held on every run: the composed fraction differed on 0 rays, and both timing controls agreed. The
count's controls held: A and B both read above zero, and B below A.

Instrument 2 was not run: the census decided it first.

## Verdict

**Falsified.** Clips with the skip are 0.92 of those without on koth_toxic (13.5/14.6) and 0.88 on f12 (9.2/10.4), against a
predicted 0.8 or less, and the brush tree moved about 1 ms of 60 on f12. Only a repeat in a ray that reaches a second leaf can be
skipped: a ray that hits in the near half never walks the far leaf. Every checksum held, so the arithmetic that a repeat changes
no answer stands, but the change is dropped, as the prediction said it would be: no code from this measurement is on the branch.
The dropped test file is kept outside the repo, `D:/nfw-scratch/dropped/BrushClipOnceConformanceTests.cs`.
