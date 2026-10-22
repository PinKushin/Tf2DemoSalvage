# Which path asked for the Uber sounds on f12 (finding 90, B568)

## Observation

In `2026-10-21-sound-late-start-f12.md`, `player/invulnerable_on.wav` and `_off.wav` were loaded off the frame just after
the seek to tick 30000 but logged no late start. Why was inferred, not observed.

## Question

For those two sounds on f12: which path asked for them (a start, a stop, a re-established one-shot, or something
outside `SoundPresenter`), at which ticks, and what did that path do with them?

## Prediction

Inferred last time: a stop, or a re-established one-shot. Falsified if they are asked for by a start that never starts.

## Method

- Same run as the earlier file: `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview <f12> --tick 30000 --first-person
  --measure 60`, Debug, built in the locked command, on `29f21e61`.
- The instrument (`29f21e61`): `SoundPresenter.Start` records its outcome at the branch that decides it; the log says
  `waiting for '<name>' (tick, started|re-established)` the first time a start meets a loading sound, `stop of '<name>'
  ... while it loads: nothing to stop` for a stop, and `late start` / `late one-shot not re-established` / `late sound
  ... not started` when the retry ends, from that carried outcome.
- Read every line naming `invulnerable`, and every `waiting for`, `late ...` and `stop of` line.

## Control

- `vo/null.mp3` and the near-miss whizzes started late in the earlier run; they must log `waiting for ... started` and
  then `late start` here. If they do not, the instrument is broken.
- If the Uber sounds log no `SoundPresenter` line at all, the request came from outside it, which is an answer too, but
  only once the control has fired.

## Environment

## Result

## Verdict
