# Sound first plays off the frame (B568, B570, finding 90)

## Observation

Finding 87's after-review run (f12 tick 30000, first person, `--measure 60`) left four sounds that miss the precache and are
read and decoded on the render thread at first play: `player/invulnerable_on.wav`, `_off.wav`, `vo/null.mp3` and a
`>weapons/fx/nearmiss/bulletLtoR*.wav`. Each cost 0.3-0.8 ms on a quiet machine and up to ~395 ms on a busy one (B568).

## Question

1. How many sound reads and decodes does the render thread pay during play on f12 before and after the change?
2. After it, are those first plays loaded somewhere else, and which sounds are they?

## Prediction

- Before: the end-of-run `sound cache:` line says `4 decodes in play` (finding 87's count; 3-5 tolerated, the set depends
  on what plays in 60 s).
- After: `0 decodes in play`, and the same line's new `loaded off the frame` count equals the before count, each one named
  by a `loaded off the frame` line.
- Falsified if the after run reports any decode in play, or reports fewer off-frame loads than the before run's in-play
  decodes (that would mean sounds went missing rather than moving).

## Method

- `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview <f12> --tick 30000 --first-person --measure 60`, Debug, once on the
  branch base `e1ec318a` (before) and once on the fix commit (after), each built in the same locked command.
- f12 is `tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem` in the main checkout.
- Read: the `sound cache:` summary line (its `decodes in play` field is `SoundCacheHeld.Misses`, counted where the read
  happens in `SoundCache.Sample`), every `STALL reading` line, and after the change every `loaded off the frame` line.

## Control

- The same `sound cache:` line's sound count must be non-zero and near finding 87's (~1,372 sounds precached), so a silent
  cache is distinguishable from a quiet one.
- After the change, the off-frame count is the control for the in-play zero: a zero there with no off-frame loads would
  mean the sounds never played, not that they moved.

## Environment

## Result

## Verdict
