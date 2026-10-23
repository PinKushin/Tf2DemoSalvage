# Measurement 2026-10-23: blend inputs at bone setup — the empty view and gummo, before and after (f12)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

On the quiet repeat (`2026-10-23-empty-view-floor-quiet.md`) the empty view's `pose` is 0.7 ms with nothing posed, 0.5 of it
`simulate`. A probe timer (not committed) around the layers/autoplay/IK block inside `SimulateEach` read 0.3-0.4 ms of that
in the empty view. The engine builds those inside `SetupBones` (`StandardBlendingRules`, `c_baseanimating.cpp:1953`).
Commit `f61c7602` moves them to the first bone request.

## Question

1. Does the empty view's `simulate` drop by the probe's 0.3-0.4 ms, and the frame with it?
2. Is the first-person picture unchanged?

## Prediction

1. Empty view `simulate` 0.5 → 0.1-0.2 ms; frame mean down ~0.3 ms, which is inside the whole-frame run-to-run spread seen
   today (5.0-9.2 ms), so only the stage timer can show it. gummo `simulate` down by less (its in-view players still blend).
2. Byte-identical shots at ticks 30000 and 30400.

## Method

Builds: `base` = `origin/main` `273d7c57`, `fix1` = `f61c7602`; Debug, copied out of `bin`. Under one `run-exclusive.ps1`
hold, interleaved: empty, gummo, empty, gummo, empty for each build in turn (base, fix1, base, fix1 …) — the command and
camera of the quiet record. CPU load sampled every 2 s during each run (`Get-CimInstance Win32_Processor`), mean and max
recorded. Then `--shot` for each build at ticks 30000 and 30400, `--spectate gummo --first-person`, compared by SHA-256.

## Control

- Ticks advanced ~1,333 per window.
- Stage timers are compared run against run by build; a frame-mean difference is quoted only if it exceeds the spread of
  the same build's own repeats.
- Shot control: the two ticks' shots must differ from each other (the hash can see a change).

## Result
