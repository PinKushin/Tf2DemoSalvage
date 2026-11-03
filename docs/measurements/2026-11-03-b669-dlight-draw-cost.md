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
