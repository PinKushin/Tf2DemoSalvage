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

## Verdict
