# Sounds loaded off the frame start late on f12 (finding 90, B568)

## Observation

`2026-10-21-sound-first-play-off-frame.md` moved f12's five first-play loads off the render thread, but each of those
plays was dropped: the sound answered null and nothing started it again. Review (the coordinator) asked for the engine's
late start instead.

## Question

On f12, after `a76b5acf`, does every sound asked for while loading start once its load arrives, with none dropped, and do
decodes in play stay at zero?

## Prediction

- `0 decodes in play` on the `sound cache:` line.
- One `late start '<name>'` line per sound asked for while loading, naming the same sounds as the `loaded off the frame`
  lines (finding 90's five, give or take what plays in 60 s), and no `did not arrive; not started` line.
- Falsified by any decode in play, any `not started` line, or a `loaded off the frame` sound with no matching late start.

## Method

- `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview <f12> --tick 30000 --first-person --measure 60`, Debug, built in the
  same locked command, on `a76b5acf`. f12 is `tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem` in the main
  checkout.
- Read from the run's log: the `sound cache:` line, every `loaded off the frame`, `late start`, `not started`,
  `STALL reading` and `soundscape loop ... would not open` line. CPU load by `Get-CimInstance Win32_Processor` before and
  after.

## Control

- The `loaded off the frame` lines are the control for the late starts: zero of them would mean nothing was asked for
  while loading, and zero late starts would then prove nothing.
- The cache's held count (~1,337) shows the precache ran.

## Environment

## Result

## Verdict
