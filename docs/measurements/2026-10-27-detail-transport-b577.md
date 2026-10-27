# Measurement 2026-10-27: detail sprite build, transport bar repaint, B577 one-shots

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

From the read-only ranking:

1. `camera setcamera detail` averages 0.44 ms on f12 gummo first person (5% of the frame), but it reads 0.00-0.01 ms for
   about 20 s from tick 30000 and then 0.6-1.0 ms. B580's record (`2026-10-23-b580-detail-reach.md`) measured 20 s windows
   from tick 30000, so it never saw gummo inside detail range.
2. `between` holds 0.32-0.36 ms of transport bar repaint on every run (B572). `ShowTick` wrote the tick label's text and
   accessible name twice per frame, and once even when the tick had not moved.
3. B577's remainder: `dispatches` plus `muzzles` cost 0.30 ms, the same on the empty view.

## Changes measured (branch `perf/detail-transport-b577`)

- **D** (detail): `5faf3cfa` HeapSorts each leaf lane separately and reads through spans. `ae9791ca` makes
  `DetailSpriteLeaves.Add` count per lane.
- **T** (transport): `38e543e8`, which writes the label only when the tick moved, and only once.
- **B** (B577): `73f02aad`, amended by the following commit. On played frames, the one-shots `ParticleEffects` has let go
  of are re-offered from cache instead of rebuilt.

**Identity, shown before any timing run** (no viewer was needed):

- D: the `detail-hash` probe on cp_process_f12, 2000 views, 5,437,195 quads. The per-lane set hash is
  `747bd2e36488e1b3` before and after. Order-including hash: `293530ac9f0717a4` → `9be0ad88d4b0ef3c`. With ties broken by
  lump index in both builds the two order-including hashes match (`dc27b769f0b8dc78`), so the only reorder is among the
  79 exact ties, which now take Valve's HeapSort order.
- T: unit tests on the label.
- B: a per-frame state-equivalence test against offer-everything. It checks batch bytes in order, whizzes, offered keys
  in order, census and step counters. The B577/B592 fingerprints are unchanged. Gone keys are re-offered from cache
  (coordinator: do not change drawn order).

## Viewer-path identity (detail), if the lead wants it beyond the probe

The B580 `cull` hash (`TF2VIEW_FRAME_HASH=1`) will differ between A and B on frames that hold an exact tie, so a raw A/B
comparison proves nothing. Instead, build A′ and B′ with lump-index tie-breaks, as the probe did (its hashes match), and
compare their `cull` hashes tick by tick, with A′ vs A′ and B′ vs B′ as the floor. The probe is the identity proof
offered here. This section runs only on request, and after GO.

## UI check (transport), after GO

The worktree's gcor demos are LFS pointers. Copy them in first (`docs/memory/a-worktree-copies-gcor-demos-never-lfs-pull.md`),
then rerun the two `LoadDemo*` tests in `Viewer3D.Tests` that fail here without the demos. Then run `TransportUiTests` alone
through `run-exclusive.ps1`, because it takes the desktop. `Transport_JumpToEnd_MovesTheScrubBar` reads the tick label's
UIA name.

## Question

How much of each named part do the three changes remove, in ms and as a % of the frame, on f12 gummo first person and on
the empty view?

## Prediction

- `setcamera detail` falls on the second half of a 60 s window, where gummo is in detail range, from 0.6-1.0 ms to under
  half that. Falsified if the change is smaller than the same-build spread.
- `between` falls by about 0.1-0.3 ms on both subjects. Most frames repeat a tick, so the label write is skipped
  entirely. The TrackBar repaint on a moved tick stays.
- `camera particles/bursts/dispatches` plus `/muzzles` falls from about 0.30 ms by 0.05-0.2 ms on both subjects.
  Followed points are still read every frame, as the engine reads them.

## Method

Two Release builds of the viewer, copied out so both exist at once:

- **A (before)**: `d75ab179` (origin/main at branch point). `D:/measure-f12/dtb-before/`.
- **B (after)**: the branch head with D, T and B. `D:/measure-f12/dtb-after/`.

**Timing.** Each run is one viewer through `run-exclusive.ps1` (exclusive), and only one timed run happens at a time.
ABBA order, then repeated reversed (BAAB), per subject, which gives 4 runs per build per subject:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 60 +fps_max 0`. **Only the per-second lines of the second 30 s count** for `detail`, because
  gummo is inside detail range there. The first half is reported separately as the out-of-range control.
- empty view: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`.

**Quiet gate before each run:** three consecutive `(Get-CimInstance Win32_Processor).LoadPercentage` samples, all under 25%,
and no build running; otherwise wait and repeat. Loads before and after each run are recorded. A run whose after-load
exceeds 25% is reported and marked, not silently dropped. Discarded runs are listed with their reason.

**Reported per run:** frames, frame mean, `camera`, `setcamera detail` (unweighted mean of the per-second lines in the window
named above), `between`, and `camera particles/bursts/dispatches` and `/muzzles`. Each figure is given in ms and as a % of
that run's frame mean, with the range across a build's runs.

## Control

- **Instrument:** ticks advance about 4,000 per 60 s window. The tick reached at the end is stated for every run, and runs
  of one build must agree within the spread.
- **In range:** the gummo `detail` reading in the second 30 s must be well above zero on A (0.6-1.0 ms is expected). If it
  is not, the window did not reach detail range and the detail result is void.
- **Spread:** same-build spread is stated beside every A-B difference. A difference inside the spread is reported as no
  measured change.

## Environment

This machine, 1280x720 windowed, shared with other agents. One timed agent (bones/HUD, finding 105) is running when this is
written, so **no timed run starts before the lead's GO**.

## Result

Run 2026-10-27, from 04:17 to 05:31, after the coordinator's GO. Raw stdout of every run, the run script and the
summariser were kept in the session scratch directory and are not committed. The tables below are the summariser's
output. Builds: A is `d75ab179` (built from a `git archive` export at `D:/measure-f12/dtb-before-src`). B is `4a3e1b3b`.
Both are Release builds with 0 warnings.

**Deviations, stated.**
1. The first launch passed the order as `A,B,B,...`. `pwsh -File` bound that to one string, so it matched no build and
   ran no viewer, and nothing from it is used. The script now takes one string such as `ABBABAAB`. This is B580's
   deviation (1) again.
2. The gate waited 2 and 4 times before runs 1 and 2. The machine was busy then (samples up to 100%); every run started
   under 25% on all three samples. No run was discarded.
3. `between` and the frame mean come from the frame-weighted `phases` line of the whole window. `detail`, `dispatches`
   and `muzzles` are unweighted means of the per-second `parts:` lines. There are 58 such lines on gummo, and the second
   half is lines 30-58. The empty view has 18.

**gummo, first person, 60 s** (every run ended at tick 33990-34014):

| run | build | load before / after | frames | mean ms | between | detail 1st half | detail 2nd half | dispatches | muzzles |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | A | 24 16 22 / 14 11 13 | 6473 | 9.27 | 0.34 | 0.156 | 0.753 | 0.201 | 0.146 |
| 2 | B | 15 6 13 / 10 13 9 | 6804 | 8.82 | 0.32 | 0.084 | 0.458 | 0.192 | 0.141 |
| 3 | B | 13 13 12 / 9 13 12 | 6887 | 8.71 | 0.32 | 0.090 | 0.456 | 0.188 | 0.142 |
| 4 | A | 11 6 15 / 13 6 12 | 6598 | 9.09 | 0.33 | 0.157 | 0.749 | 0.198 | 0.143 |
| 5 | B | 10 15 10 / 13 10 10 | 6795 | 8.83 | 0.32 | 0.092 | 0.459 | 0.195 | 0.141 |
| 6 | A | 12 20 12 / 7 18 7 | 6674 | 8.99 | 0.33 | 0.151 | 0.729 | 0.198 | 0.145 |
| 7 | A | 8 14 12 / 18 14 11 | 6719 | 8.93 | 0.33 | 0.149 | 0.733 | 0.193 | 0.138 |
| 8 | B | 18 18 17 / 18 13 10 | 6818 | 8.80 | 0.32 | 0.094 | 0.454 | 0.189 | 0.139 |

**Empty view, 20 s** (every run ended at tick 31347):

| run | build | load before / after | frames | mean ms | between | detail | dispatches | muzzles |
|---|---|---|---:|---:|---:|---:|---:|---:|
| 1 | A | 21 13 19 / 15 10 11 | 4867 | 4.11 | 0.25 | 0.000 | 0.214 | 0.227 |
| 2 | B | 13 16 19 / 19 14 13 | 4996 | 4.00 | 0.25 | 0.000 | 0.201 | 0.221 |
| 3 | B | 19 13 8 / 23 10 21 | 4731 | 4.23 | 0.27 | 0.000 | 0.196 | 0.226 |
| 4 | A | 10 14 21 / 11 18 11 | 4995 | 4.00 | 0.26 | 0.000 | 0.191 | 0.214 |
| 5 | B | 13 9 21 / 16 19 10 | 5019 | 3.99 | 0.25 | 0.000 | 0.192 | 0.217 |
| 6 | A | 11 12 7 / 17 13 16 | 4988 | 4.01 | 0.24 | 0.000 | 0.193 | 0.211 |
| 7 | A | 12 10 9 / 18 18 14 | 4875 | 4.10 | 0.25 | 0.000 | 0.207 | 0.224 |
| 8 | B | 11 18 15 / 9 5 15 | 5022 | 3.98 | 0.25 | 0.000 | 0.196 | 0.220 |

**By change, A range → B range, with % of that build's frame mean:**

- **Detail (D), gummo, second half:** A 0.729-0.753 ms (8.1-8.2%) → B 0.454-0.459 ms (5.2%). The spread is 0.024 in A
  and 0.005 in B, and the drop is 0.27-0.30 ms, about 38%. In the first half A reads 0.149-0.157 and B 0.084-0.094; over
  the whole window A is 0.440-0.454 and B 0.271-0.275. The in-range control passes: A's second half is well above zero.
  **Prediction ("under half") falsified on the high side:** B is 61-63% of A, not under 50%.
- **Frame mean, gummo:** A 8.93-9.27 → B 8.71-8.83 ms. The ranges do not overlap. The difference of the build means
  (A 9.07, B 8.79) is 0.28 ms, about the size of the detail drop.
- **Transport (T), `between`:** gummo A 0.33-0.34 → B 0.32 ms (3.6-3.7% of the frame both ways). Empty A 0.24-0.26 → B
  0.25-0.27 (6.0-6.5% both ways). **No measured change** on either subject: the ranges overlap and the resolution is
  0.01 ms. The prediction of 0.1-0.3 ms is falsified. The repeated label write was not where `between`'s cost lives;
  the TrackBar repaint on a moved tick, and whatever else `between` holds, was not split here.
- **B577 (B), dispatches + muzzles:** gummo A 0.332-0.348 (3.7-3.8%) → B 0.328-0.336 (3.7-3.8%). Empty A 0.404-0.441
  (10.1-10.7%) → B 0.408-0.422 (10.0-10.6%). **No measured change**: the ranges overlap on both subjects. The prediction
  of 0.05-0.2 ms is falsified. Re-offering finished one-shots from cache saved nothing measurable, so the cost is not in
  the finished ones. It is in what is still live: the owned dispatches' per-frame offers (attachment queries on followed
  points, `AddParticleEffect`), and the window searches. This is the split B577's "Left" already asked for, still not
  taken.

## Verdict

**Detail: faster, 0.27-0.30 ms a frame on gummo in detail range (8.1% → 5.2% of the frame), with the frame mean down
0.28 ms.** Identity is from the probe, with only the exact ties reordered into HeapSort order (above). **Transport and
B577: correct by their tests, but no frame time measured as removed on either subject.** Neither is a perf win as
built; both are kept as parity and allocation tidying. B577's real cost needs the split of `dispatches` into its live
owned offers before another fix.
