# IVP's ring walks without iterator objects: garbage and frame time in playback (B708)

## Observation

`2026-11-08-landing-allocation.md`: in played frames on `z1800.dem` after the seek to 20000, the corpse physics' thread
allocates about 3.5 GB per 20 s, of which `IvpLedgeTopology`'s `<Walk>` and `<EndingAt>` iterator objects are about
0.83 GB. The frame thread allocates about 1.1 GB per 20 s. Every gen0 collection pauses every thread, the frame's included.
The change under test walks a ring with a struct enumerator (`IvpLedgeRing`) that allocates nothing
(`Ring_AndEndingAt_WalkedByForeach_AllocateNothing`).

## Question

In steady playback, how much do the gen0 collections and the GC pause per second fall, and does the frame time move?

## Prediction

The process allocates about 18% less (0.83 of 4.6 GB per 20 s), so gen0 collections per 20 s fall by 10 to 25%. The
frame mean moves by less than run-to-run noise (about 0.5 ms here). Falsified if the gen0 count does not fall by at least
10% in both pairs.

## Method

Two Release builds alternated under the desktop lock, A B A B: A at `5a55aab7` (before), B this change. Each:

```bash
TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh run-exclusive.ps1 <build>/tf2demoview.exe tools/corpus/demos/z1800.dem --tick 20000 --autoplay --measure 20
```

`--measure` counts playback after the opening seek's landing, so the landing is outside the window. Read the summary
line's frame mean and its `gc` part (collections and pause over the window) from stdout.

## Control

Both builds must report the same playback span (the `ending at tick` figure within a few ticks) and a nonzero gen0
count; A's figures must be steady across its two runs (gen0 within 15%), or the pair says nothing.

## Environment

## Result

## Verdict
