# 2026-11-14 — B734, particle effects in the translucent walk: what it costs

**Question.** Does drawing each particle effect as its own translucent renderable (B734) cost frame time? Before, the
effects were merged into one batch per material.

**Command.** Each run used this command, through `run-exclusive.ps1`, in A/B/A order: B734, main `a13a421a0`, B734.

```
TF2VIEW_AUTOPLAY=1 TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_VIEWPORT_SIZE=1280x720 \
  tf2demoview D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2
```

## Round 1: B734 `2d9e31b3b`

| | B734 | main | B734 |
|---|---|---|---|
| frames, mean | 2591, 7.72 ms | 2714, 7.37 ms | 2656, 7.53 ms |
| render thread draw | 2.19 | 2.06 | 2.14 |
| Translucent, cpu / draws / maps | 0.209 / 81.5 / 156.4 | 0.075 / 32.8 / 58.9 | 0.208 / 81.2 / 155.7 |
| Particles (main only), cpu / draws / maps | — | 0.070 / 17.0 / 34.0 | — |

## Round 2: B734 `f09929c23`

This round adds the camera-constant cache and reuses the effect box from collation.

| | B734 | main | B734 |
|---|---|---|---|
| frames, mean | 2677, 7.47 ms | 2557, 7.82 ms | 2222, 9.00 ms (a 40.7 ms camera spike: noise) |
| render thread draw | 2.10 | 2.15 | 2.48 |
| Translucent, cpu / draws / maps | 0.193 / 82.0 / 118.8 | 0.076 / 33.1 / 59.5 | 0.230 / 81.4 / 117.9 |
| Particles (main only), cpu / draws / maps | — | 0.073 / 17.0 / 34.1 | — |

The per-sample `camera particles/build/systems` figures from round 2, run 1 against main, are in the order the samples
were taken: 0.40/0.30, 0.38/0.29, 0.49/0.36, 0.30/0.25, 0.35/0.28. That is about 0.08 ms higher.

## Reading

- In main, the particle cost was split between the `Translucent` and `Particles` passes. Comparing like with like,
  render-thread CPU goes from 0.149 ms to 0.193 ms, which is +0.044 ms.
- The draw count rises from 50 to 82: one draw per effect per material. That is the engine's shape
  (`clientleafsystem.cpp:651`).
- Frame means swing by ±0.3 ms between runs of one binary, so they do not resolve this difference. The pass ledger does.
- The phases don't add up to the frame mean (round 1, main: 7.37 ms frame against 2.06 ms render thread). The rest is
  the game thread's `camera` phase, 5.79 ms.
