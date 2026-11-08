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

The owner's desktop machine, Windows 11, 6 cores and 12 logical, viewport 1280x720. A: Release build of `5a55aab7` in a
temporary worktree; B: Release build of this branch with the change, uncommitted on top of `e9ffa630`. Alternated A B A B
under the desktop lock, no other viewer or agent running, TF2 closed. Load before and after was not recorded.

## Result

The control holds: every run ends at tick 21340, with nonzero gen0, and A's two runs agree within 4% (425 and 441).

```
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3097 frames, mean 6.46 ms
  gc: paused 0.39 ms a frame; collections gen0 425, gen1 70, gen2 0; allocated 0.96 MB a frame; render thread 411,583 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3158 frames, mean 6.33 ms
  gc: paused 0.36 ms a frame; collections gen0 382, gen1 67, gen2 0; allocated 0.85 MB a frame; render thread 409,325 bytes a frame
== A
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3188 frames, mean 6.27 ms
  gc: paused 0.38 ms a frame; collections gen0 441, gen1 72, gen2 0; allocated 0.96 MB a frame; render thread 410,758 bytes a frame
== B
measured 20 seconds of playback, 19 samples (19 rebuild reports), ending at tick 21340; 3092 frames, mean 6.47 ms
  gc: paused 0.35 ms a frame; collections gen0 374, gen1 64, gen2 0; allocated 0.85 MB a frame; render thread 409,884 bytes a frame
```

| | A1 | B1 | A2 | B2 |
|---|---|---|---|---|
| gen0 over 20 s | 425 | 382 | 441 | 374 |
| allocated a frame | 0.96 MB | 0.85 MB | 0.96 MB | 0.85 MB |
| GC paused a frame | 0.39 ms | 0.36 ms | 0.38 ms | 0.35 ms |
| frame mean | 6.46 ms | 6.33 ms | 6.27 ms | 6.47 ms |

## Verdict

**Confirmed, at the low edge:** gen0 collections fell 10.1% and 15.2% in the two pairs (predicted 10 to 25%), allocation
fell 11.5% a frame (0.96 to 0.85 MB), and the GC pause by 0.03 ms a frame. The frame mean did not move beyond noise, as
predicted. The rest of the corpse physics' garbage (contact records, the solver's arrays, ledge sides, rigid-body arrays)
remains, and the next targets are there.
