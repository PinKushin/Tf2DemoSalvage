# What a model draw pays under a live dlight (B669)

## Observation

Review of `fix/b669-pipes` asked for a frame measurement on an f12 section with live dlights, reading `DynamicMerges`. No such section exists.
The only allocator a demo can drive is the local player's Dragon's Fury fireball (`MomentScene.ThinkDynamicLights`). `entity-census` on f12
lists no `BallOfFire` class, and the same census's control lists `CTFProjectile_Rocket` 1669 times. `2026-10-22-dynamic-light-merge.md` read
`0 of 331621` on tick 30000's window. So a `--measure` run on f12 cannot reach this path at all.

## Question

With f12's map and a full light cache, how many microseconds and bytes does one model draw pay when one dlight reaches it, on each route?
And does the path trace, which is what the review feared?

## Method

The probe `dlight-cost cp_process_f12`, on the production calls `LevelLighting.ModelLightingAt` and `LevelLighting.StaticPlusDynamicLights`.
Each distinct static prop lighting point stands in for a model draw, with `DynamicLights.DragonsFury`'s light 64 units above it, so every draw
sees a light in its PVS: the worst case. Five passes, each dead then live. The merge time and counts come from the production `DynamicMerges`.

```
pwsh run-exclusive.ps1 -Shared dotnet run --project <probe> -c Debug -- dlight-cost cp_process_f12
pwsh run-exclusive.ps1 -Shared dotnet run --project <probe> -c Release -- dlight-cost cp_process_f12
```

## Control

- `re-ranked` must equal the points on every pass, or the path did not run.
- `traces since the fill` must read 0. If it climbs, the path traces.
- The dead pass is the control for the live one: its time is the draw without the path.

## Prediction

No trace, one clone and one `Add` per draw: a few microseconds in Debug, and 0 traces. Falsified by any trace or by a draw above 50 us.

## Environment

Shared desktop; the probe takes no screen. Debug is what the owner runs; Release is printed beside it.

## Result

`cp_process_f12`, 1347 distinct points, 43585 traces filling the caches, branch working tree on 2026-11-03. Passes 3 and 4 of 5 are steady; the first
three carry tiered JIT.

| build | model draw dead | model draw live | in the merge, mean over all draws | bytes a draw | static prop dead / live | bytes a draw |
|---|---|---|---|---|---|---|
| Debug | 16.2-36.6 us | 17.4-36.7 us | 0.62-2.02 us | 1629 | 0.08-0.26 / 0.53-1.25 us | 436-446 |
| Release | 17.0-37.2 us | 17.3-36.5 us | 0.58-1.68 us | 1629 | 0.08-0.19 / 0.56-1.70 us | 436-446 |

Controls: `traces since the fill` read 0 on all ten passes. Asked read 1347 of 1347 live and the dead pass adds none. **`re-ranked` read 836 of 1347, so the
record's first control failed as written.** It assumed every light 64 units above a prop was in the prop's PVS. The probe then counted that gate from
`ClusterAt` and `Visible` directly, and got 836 of 1347, which reconciles exactly: the other 511 are the gate refusing, so the path ran for every draw it should.

Arithmetic, not measured: a window of about 2,860 frames (`20 s` at the 7 ms frame of `2026-11-03-b669-surface-light-cost.md`) with the 331,621 draws of
`2026-10-22-dynamic-light-merge.md` is about 116 model draws a frame. With a light reaching all of them, the merge is about 0.07-0.23 ms a frame and 190 KB of garbage.

**What is not measured:** no frame. The dead draw's own 16-37 us is the probe walking a different point each call, with no memo hit; the frame's `lighting` column
implies about 6 us a draw. It was not explained here and is not this path.

## Verdict

**Prediction confirmed on cost and on the trace:** no trace, about a microsecond in the merge, under the 50 us limit. The record's premise for the first control was wrong and
its reconciliation stands in place of it. The path costs nothing on f12 because it cannot run there, and at most about 0.2 ms a frame while the local player's own fireball
is in the air. Nothing changes because of it.
