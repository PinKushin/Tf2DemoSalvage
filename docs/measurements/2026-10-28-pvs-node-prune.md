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

## Results

(pending)
