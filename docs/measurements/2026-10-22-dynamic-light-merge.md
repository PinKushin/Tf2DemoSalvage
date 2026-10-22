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

Debug viewer, RTX 3060, i5-10600K. D1 at `12f8c617`, load 7/6/3 / 6/7/11%. D2 at the same commit plus a control count
(`Checked`), load 4/8/8 / 8/5/11%.

## Result

- **D1:** every census line from tick 30075 to 31281 reads `dynamic light draws so far 0, re-ranked 0, 0 ms`. **The
  control failed:** a zero could not be told from an instrument that is never reached. So, before D2, the count of draws
  that ASK for dynamic lights (`Checked`, at the method's first line) was added.
- **D2:** at tick 30080, `0 of 6065`. At tick 31288, `0 of 331621`, re-ranked 0, 0 ms. The method was asked 331,621
  times in the window and no dynamic light reached a model in any of them.

## Verdict

**The copy is real code but costs nothing on this scene.** Across the 20 s window no dynamic light that lights models is
alive, so `WithDynamicLights` returns at its count check every time, and the world-light copy never runs. No fix is made.
There is nothing to measure it against, and a change here could not be checked on f12. **Open, and not a performance
question:** whether TF2 itself makes a model-lighting dynamic light anywhere in this window (rocket and grenade muzzle
effects are in the census) is a parity question for `DynamicLights`. It is not answered here.
