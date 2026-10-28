# 2026-10-28 — the world walk pruned by the PVS (B259)

What changed: `WorldVisibility` stamps the PVS-visible leaves and their ancestors once per change of the eye's cluster
(the engine's mark pass, read from engine.dll — RISKS B259) and the walk returns from an unstamped node before its box
test, where it used to descend every node the frustum kept and test the PVS per leaf. The listed leaves and reached
surfaces are unchanged on cp_process and cp_badlands (`Surfaces_ForAPvsPrunedWalk_AreTheUnprunedWalksInTheSameOrder`);
node steps fall by about three quarters there (40,848 of 162,512, 49,515 of 187,710 over 175 views).

## Predictions, registered before the first run

- **Identity.** Paused `--shot` pairs at the same tick and camera: 0 world pixels differ (world-only mask,
  `2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`), both in the empty view and first person. Falsified by any
  world pixel.
- **Empty view** (still camera): the cull runs only when the view changes, so `camera setcamera batches` is ~0 in both
  builds and the steady frame does not move beyond the runs' own spread. `posed 0` in every moment line, else void.
  `batches/marks` (B only) 0 in the steady window. Nothing claimed.
- **First person** (the moving view, crossing clusters): `camera setcamera batches` falls by **0 to 0.15 ms** (finding 83
  put the whole of `batches` at about 0.15 ms). Falsified if B's `batches` sits inside A's spread, or rises.
- **The miss path** (`batches/marks`, B only, carried from `MarkLeaves`): a per-frame mean under 0.05 ms in the steady
  window, since a re-mark (one pass over the leaves) happens only on a cluster change. Falsified above 0.05 ms, which
  would mean re-marking is frequent or slow enough to cancel the walk's saving.
- **Frame:** no claim beyond `batches`; a change under the spread of four runs per build is reported as none.

## Method

Two Release builds out of the tree: A = `752d6981` (origin/main, the merge base's code), B = `87d075e7` (this branch,
which adds the `batches/marks` timer), each from a `git archive` at `D:/measure-f12/pvs-{before,after}-src`, copied to
`D:/measure-f12/pvs-{before,after}`. Every run behind the quiet gate (`run.ps1`: no other viewer, `dotnet`, `testhost`
or `MSBuild` process, idle `/nodemode:1` reuse nodes and `VBCSCompiler` excluded; then three `LoadPercentage` samples
under 25%), under `run-exclusive.ps1`, at 1280x720:

- empty: `TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe> D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`
- first person: `<exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0`

Order (`all.ps1`): shots, then ABBA BAAB empty, then ABBA BAAB first person, then a second shot pair. Reading
(`2026-10-27-empty-view-breakdown/read.ps1`): per-second lines after the first five (steady, the hit path),
frame-weighted; the first five apart (warm). % = part / the steady window's own frame mean.

## Control

- Ticks advanced ~1,333 per 20 s window.
- Empty view: `posed 0` and a shot with no model, else void.
- `camera setcamera` parts sum to the parent within 0.1 ms (`batches/marks` is nested in `batches`, not added).
- A run that fails or exits non-zero is void and re-run.

## Environment

RTX 3060, i5-10600K, 2026-10-28 01:40-04:04. Every run passed the quiet gate before it started and exited 0. Load
(3 × 1 s, %) before / after:

| run | before | after | | run | before | after |
|---|---|---|---|---|---|---|
| 01-empty-A | 3 7 4 | 17 56 65 | | 09-fp-A | 2 4 1 | 0 4 12 |
| 02-empty-B | 5 1 14 | 29 18 19 | | 10-fp-B | 4 7 2 | 3 3 4 |
| 03-empty-B | 23 19 20 | 16 9 51 | | 11-fp-B | 2 3 6 | 3 100 13 |
| 04-empty-A | 4 6 7 | 24 46 73 | | 12-fp-A | 5 1 5 | 5 4 3 |
| 05-empty-B | 3 4 6 | 28 23 25 | | 13-fp-B | 2 4 5 | 14 4 6 |
| 06-empty-A | 9 3 3 | 7 10 4 | | 14-fp-A | 4 4 4 | 8 7 5 |
| 07-empty-A | 8 1 3 | 4 6 4 | | 15-fp-A | 6 4 4 | 2 3 4 |
| 08-empty-B | 2 3 5 | 7 6 4 | | 16-fp-B | 3 6 5 | 5 7 1 |

The high after-reads are another agent's `-Shared` work (a corpus test run) starting as a run ended; each such run is
judged against its build's other runs below. Raw output beside this file: `2026-10-28-pvs-node-prune/NN-*.txt`.

## Results

**Identity: held.** World-only pixel diff (`2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`), paused shots at
tick 30000:

| pair | world pixels differ | whole frame |
|---|---:|---|
| empty A / B | 0 | 52, box (1101,2)-(1119,10) |
| empty A / B, second pair | 0 | 19, box (1113,2)-(1119,9) |
| empty A / A (control) | 0 | 16, box (1113,5)-(1119,10) |
| first person A / B | 0 | 36, box (1113,2)-(1119,10) |
| first person A / B, second pair | 0 | 13, box (1113,2)-(1119,7) |
| first person A / A (control) | 0 | 13, box (1113,5)-(1119,10) |

The whole-frame differences are one box in the top bar, the same between two runs of one build, so they are the HUD and
not this change.

**Controls.** Every run ended at tick 31347 (1,347 advanced) except 14-fp-A at 31337, whose warm window holds an 18.9 ms
first-use frame mean; its steady window is reported and marked. Empty view: `posed 0` in every moment line of all eight
runs (of 1,851-1,888 selected).

**First person (the moving view), steady window, ms and % of that run's steady frame:**

| run | frame | `camera setcamera` | `batches` | `sky` | `overlays` | `batches/marks` |
|---|---:|---:|---:|---:|---:|---:|
| 09-A | 6.781 | 0.770 (11.4%) | 0.420 (6.2%) | 0.198 (2.9%) | 0.143 | — |
| 12-A | 6.796 | 0.769 (11.3%) | 0.420 (6.2%) | 0.198 (2.9%) | 0.144 | — |
| 14-A † | 7.100 | 0.781 (11.0%) | 0.426 (6.0%) | 0.204 (2.9%) | 0.145 | — |
| 15-A | 6.753 | 0.761 (11.3%) | 0.415 (6.1%) | 0.198 (2.9%) | 0.140 | — |
| 10-B | 6.636 | 0.406 (6.1%) | 0.212 (3.2%) | 0.052 (0.8%) | 0.138 | 0.000 |
| 11-B | 6.694 | 0.415 (6.2%) | 0.214 (3.2%) | 0.052 (0.8%) | 0.140 | 0.000 |
| 13-B | 6.582 | 0.404 (6.1%) | 0.208 (3.2%) | 0.050 (0.8%) | 0.138 | 0.000 |
| 16-B | 6.704 | 0.413 (6.2%) | 0.214 (3.2%) | 0.051 (0.8%) | 0.139 | 0.000 |

† 14-A: the run with the warm stall (above).

- **`batches` 0.415-0.426 → 0.208-0.214 ms: −0.21 ms**, about 3% of A's frame. The prediction (0 to 0.15) is
  **falsified on the high side**: finding 83's 0.15 ms was a different build and window, and the walk's share was larger
  than that figure said.
- **`sky` 0.198-0.204 → 0.050-0.052 ms: −0.15 ms**, about 2% — not predicted. The sky view runs its own
  `WorldVisibility` from the sky camera every time the view moves (`WorldCulling.SkyRunsFrom`), and that walk used to
  descend the whole map's frustum-kept tree to find the sky room's few leaves; the sky cluster's PVS now prunes the rest.
- **`camera setcamera` 0.761-0.781 → 0.404-0.415 ms: −0.36 ms, 5.3% of A's mean frame.** `overlays` is unchanged
  (0.138-0.145), as it should be: it reads the walk's output, which is identical.
- **The miss path, `batches/marks`: below the rate line's 0.005 ms resolution in every steady second of all four B
  runs**; the first two seconds read 0.01 and 0.04-0.05 ms a frame (the first marks after the seek, at the warm window's
  low frame rate). The prediction (under 0.05 ms a frame) held. *Not measured:* how many cluster changes the steady
  window holds — the line rounds a frame's mean, so a re-mark's own cost is not separable from how rarely it runs.
- **Frame:** A 6.753-7.100 ms (mean 6.858; 6.777 without 14-A), B 6.582-6.704 (mean 6.654). The ranges do not overlap;
  the difference is 0.12-0.20 ms, smaller than `setcamera`'s 0.36, so not all of the saving reaches the frame (the
  camera phase's other parts and the render thread absorb some). Reported as measured, not as a claim of 0.36 ms of
  frame.

**Empty view (still camera), steady:** A 2.658-3.315 ms, B 2.640-3.429 ms (the high ones are 01-A, the session's first run, and
03-B, which started at 19-23% load; without them A 2.658-2.699, B 2.640-2.672); `camera setcamera batches` 0.000 in all eight, `batches/marks` 0.000
in all four B runs, as predicted: a still camera does not re-cull. No difference claimed. Moment (all eight, plain means):
sample 0.30-0.33, drawlist 0.14-0.17, collate 0.20-0.22, unchanged — this change does not touch the entity walks that
remain B259's open floor.

**Against the brief's 8.05-8.06 ms baseline:** that figure is not this build or this form (these are Release builds at
1280x720); the paired A runs above are the baseline here.
