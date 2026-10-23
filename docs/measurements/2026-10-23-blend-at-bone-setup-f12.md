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

Raw outputs kept in the session scratch directory, not committed. Ticks advanced ~1,340 in every window (passes).

**Load: not quiet.** In-run sampled CPU load averaged 43-56% (peaks 62-100%) on every run. The later runs passed a
pre-run gate (three samples under 25%, no `vbcscompiler`/`MSBuild`), so part of the in-run load is the viewer itself.

| # | view | build | frames | mean ms | `simulate` per-second readings (ms) | in-run load mean/max |
|---|---|---|---:|---:|---|---|
| 1 | empty | base | 4,059 | 4.93 | 0.5-0.6 | 64/100 |
| 2 | empty | fix1 | 4,221 | 4.74 | 0.3-0.4 | 51/100 |
| 3 | gummo | base | 1,536 | 13.03 | 0.5-0.7 | 48/71 |
| 4 | gummo | fix1 | 1,579 | 12.67 | 0.3-0.5 | 43/94 |
| 5 | empty | base | 4,026 | 4.97 | 0.5-0.7 | 45/100 |
| 6 | empty | fix1 | 4,115 | 4.86 | 0.3-0.4 | 45/62 |
| 7 | gummo | base | 1,544 | 12.95 | 0.5-0.7 | 46/65 |
| 8 | gummo | fix1 | 1,568 | 12.76 | 0.3-0.5 | 46/77 |
| 9 | empty | base | — | — | killed from outside at ~84 s, exit -1, no output; discarded | 56/100 |
| 10 | empty | fix1 | 3,986 | 5.02 | 0.3-0.4 | 52/100 |
| 9′ | empty | base (repeat) | 3,982 | 5.02 | — | 49/70 |
| 10′ | empty | fix1 (repeat) | 4,045 | 4.95 | — | 48/71 |

**Shots** (pixel comparison, 1280x720):

| pair | pixels differing | max channel delta |
|---|---:|---:|
| base vs fix1, tick 30000 | 73 | 255 |
| base vs fix1, tick 30400 | 70 | 255 |
| base vs base (second copy), tick 30000 | 41,710 | 255 |
| base vs base, tick 30400 | 63 | 255 |
| control: base 30000 vs base 30400 | 870,568 | 255 |

### Deviations

1. Load was not quiet (above). Run 9 was killed by something outside this session's scripts, which never stop a viewer.
   It was discarded and repeated as 9′, with a fresh fix1 run 10′ beside it.
2. **The shot comparison could not be byte-identity:** base does not reproduce its own shot. A base-vs-base control was
   added after the first comparison differed.

## Verdict

1. **The stage dropped as predicted in kind, by less:** `simulate` 0.5-0.6 → 0.3-0.4 ms in the empty view, 0.5-0.7 →
   0.3-0.5 in gummo (prediction 0.1-0.2 and "less"). Every fix1 run is below every base run on this timer. The empty
   view's frame means overlap (base 4.93-5.02, fix1 4.74-5.02), so **no frame gain is claimed there**. The gummo pairs
   are 0.2-0.4 ms faster each time, but under load.
2. **Equivalence not shown byte-identical.** Fix1's 70-73 differing pixels are inside base's own 63-41,710.
