# Measurement 2026-10-22: what a model draw pays when a dynamic light is alive

## Observation

Read in our code, not measured: `LevelLighting.WithDynamicLights` copies the whole world-light list
(`[.. _worldLights]`) and re-ranks it, allocating a new `LocalLight[]`. It does this for every model draw that sees
at least one dynamic light (B425). Muzzle flashes and rockets make dynamic lights, so f12's fights should reach it.

## Question

On f12 tick 30000, first person, `--measure 20`: how many model draws see a dynamic light, how many re-rank, and how
many ms a frame do those draws spend in `WithDynamicLights`?

## Method

A carried counter in `LevelLighting.DynamicMerges`. It counts draws that saw a dynamic light (`Asked`) and draws that
re-ranked (`Merged`), and sums the stopwatch ticks taken inside the method for those draws. The census line prints all
three, cumulative since the map loaded. The per-frame cost is the difference between the first and last steady census
lines, divided by the frames between them (from the per-second lines). One run, D1, with the command of
`2026-10-22-f12-camera-breakdown.md`, through `run-exclusive.ps1`, with load recorded. Raw output in
`2026-10-22-dynamic-light-merge/`.

## Control

- The counter must read non-zero at some point in the 20 s, because the same window's census shows rocket and grenade
  muzzle effects (`muzzle_grenadelauncher`, `rocketjump_smoke`). A zero means the instrument is not reached or no
  dynamic light is made, and the next step is to ask `DynamicLights` how many it holds.
- `Merged <= Asked` always.

## Environment

## Result

## Verdict
