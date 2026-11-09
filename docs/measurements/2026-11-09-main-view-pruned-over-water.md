# The main view's pruned list over expensive water: frame time (B711, D251)

## Observation

`eec53820`, `160074f9` and `cba72e58` make the main view collate over a pruned world list when it sees expensive water,
as `PruneWorldListInfo` does (viewrender.cpp:3539-3582). The view above the water drops every leaf below the water
plane, and models fully under the water are no longer set up or drawn by the main view. The owner asked for this as
an fps gain (D251) and then asked for the A/B.

The same three commits also add work to each water view: water particles, reflection detail sprites, and the
monitor's own collation. So this A/B measures the whole B711 batch, not the pruning alone.

No corpus demo is on a map whose water is expensive in play except `demostf-pass_coastal_rc8-1491292.dem` (lcor), a
real match. Its ocean (`canaveral/water_ocean`, 343 upward faces at z -96 around the island, from the `faces-near`
probe) reflects in a headless shot from the camera below, so the water views run there.

## Question

From a fixed free camera above the ocean, looking at the island, does the frame mean fall after the batch?

## Prediction

Little world lies below z -96 in this view: the sea floor and the dock footings. The batch also adds per-water-view
work. So B's frame mean is between 0% and 10% below A's.

Falsified if B is slower than A in both pairs by more than the pair's A-to-A spread.

## Method

Two Release builds alternated under the desktop lock, A B A B:
- A at `3a213f47` (the parent of `eec53820`), in a temporary worktree on `D:` (`D:/measure-f12/b711/a`);
- B at `cba72e58`.

Each:

```bash
TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="3800 -400 400 12 180" TF2VIEW_AUTOPLAY=1 \
  pwsh run-exclusive.ps1 <build>/tf2demoview.exe demostf-pass_coastal_rc8-1491292.dem --tick 20000 --measure 20 +fps_max 0
```

Free camera, not `--first-person`, so the camera stays over the water for the whole window. Read the summary line's
frame mean and its `gc` line from stdout.

## Control

- Every run ends at the same tick (within a few) with about 1,330 ticks advanced.
- A's two runs agree within 5% on the frame mean, or the pair says nothing.
- The camera sees reflecting water: a `--shot` from B at tick 20000 shows the dock reflected in the ocean (taken
  before the run and deleted).

## Result

Run 2026-11-09 at `7ba918f6` (B is `cba72e58` code; the later commit is this file only). Both builds were built in
the same command as the run: 0 warnings, 0 errors. CPU load after the four runs: 13%.

```
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 2271 frames, mean 8.81 ms
  gc: paused 0.21 ms a frame; collections gen0 220, gen1 12, gen2 0; allocated 0.61 MB a frame; render thread 419,054 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21334; 2319 frames, mean 8.63 ms
  gc: paused 0.20 ms a frame; collections gen0 216, gen1 10, gen2 0; allocated 0.58 MB a frame; render thread 398,110 bytes a frame
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 2398 frames, mean 8.34 ms
  gc: paused 0.19 ms a frame; collections gen0 221, gen1 11, gen2 0; allocated 0.58 MB a frame; render thread 392,090 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21341; 2367 frames, mean 8.45 ms
  gc: paused 0.20 ms a frame; collections gen0 225, gen1 10, gen2 0; allocated 0.59 MB a frame; render thread 407,366 bytes a frame
```

Means over each run's per-second `moment cost` lines (`posed N of M`, and `pose`):

| | A1 | B1 | A2 | B2 |
|---|---|---|---|---|
| frame mean | 8.81 ms | 8.63 ms | 8.34 ms | 8.45 ms |
| props posed | 123.4 | 113.5 | 113.7 | 116.4 |
| pose | 5.68 ms | 5.43 ms | 5.21 ms | 5.32 ms |

Control:
- every run ends at tick 21334 to 21341;
- A's two runs differ by 0.47 ms, 5.5% of their mean, just outside the 5% the control allows.

B against A: −0.18 ms in the first pair, +0.11 ms in the second. The posed count does not fall in B. The frame is
mostly pose (about 5.4 of 8.5 ms), and the pruning does not touch it in this view.

## Verdict

**Inconclusive.** A's control spread (5.5%) is just over its bound, and B sits inside it: −2.0% and +1.3%. The batch
neither shows the predicted gain nor is slower in both pairs, so the prediction is not falsified either. On this camera
the frame is bound by pose work, which the pruning cannot reach: the models are on the island, above the water, and the
pruned leaves (sea floor, dock footings) are cheap to draw. A view where the pruning could show needs much world or
many models below expensive water. No demo in the corpus has that: snakewater's only water is a hidden room, and
upward's is cheap water.
