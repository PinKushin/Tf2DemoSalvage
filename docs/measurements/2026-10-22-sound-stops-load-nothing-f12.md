# A stop loads nothing on f12 (finding 90, B568)

## Observation

`2026-10-21-uber-sounds-path-f12.md` saw the two Uber sounds asked for only by a stop at tick 30097, and each was then
read and decoded on the pool (368-402 ms of reads) although nothing plays them. Review: a stop must not load, and must
cancel a sound still waiting for its data.

## Question

On f12 after `fd31d8d2`: do the stop-only sounds still load, and do the sounds that start still start late?

## Prediction

- No `loaded off the frame` line for `player/invulnerable_on.wav` or `_off.wav`.
- The starts are unchanged: `vo/null.mp3` and the near-miss whizzes log `waiting for ... started`, `loaded off the frame`
  and `late start`; 0 decodes in play.
- No `stopped while loading` line (no start on f12 is followed by its stop inside its read).
- Falsified by an Uber load, a missing late start, or a decode in play.

## Method

- `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview <f12> --tick 30000 --first-person --measure 60`, Debug, built in the
  locked command, on `fd31d8d2`; f12 is `tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem` in the main
  checkout. CPU load by `Get-CimInstance Win32_Processor` before and after.
- Read the `sound cache:` line and every `loaded off the frame`, `waiting for`, `late ...`, `stopped while loading` and
  `invulnerable` line in the run's log.

## Control

- The late starts of `vo/null.mp3` and a whiz must still appear; without them, an absent Uber load could mean nothing
  loads at all.

## Environment

## Result

## Verdict
