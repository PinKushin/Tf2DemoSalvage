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

- The development machine, Debug viewer, under `run-exclusive.ps1`, built in the locked command, on `29f21e61`, 22:16.
  CPU load 26% before, 20% after.
- The title and Control lost their U-umlaut in a later commit (`095e2c66`), only so the gate could compare them; the run
  used the first form (`aaa7c836`).

## Result

stdout: `measured 60 seconds of playback ... 4872 frames, mean 12.32 ms`; `sound cache: 1336 sounds ... 0 decodes in
play`. From `viewer-20261021-221626-29488.log`, every line of the instrument, in order:

```
stop of 'player/invulnerable_on.wav' (tick 30097) while it loads: nothing to stop
stop of 'player/invulnerable_off.wav' (tick 30097) while it loads: nothing to stop
loaded off the frame 'player/invulnerable_off.wav' (read 401.1 ms, decode 0.3 ms; 114944 frames)
loaded off the frame 'player/invulnerable_on.wav' (read 401.5 ms, decode 0.6 ms; 193008 frames)
waiting for 'vo/null.mp3' (tick 31864, started)
loaded off the frame 'vo/null.mp3' (read 0.2 ms, decode 0.5 ms; 6912 frames)
late start 'vo/null.mp3' (tick 31864) at tick 31866
waiting for '>weapons/fx/nearmiss/bulletLtoR08.wav' (tick 32994, started)
loaded off the frame '>weapons/fx/nearmiss/bulletLtoR08.wav' (read 0.3 ms, decode 0.0 ms; 17178 frames)
late start '>weapons/fx/nearmiss/bulletLtoR08.wav' (tick 32994) at tick 32996
```

- **The control fired**: both sounds that start logged `waiting for ... started` and then `late start`.
- **Both Uber sounds were asked for by a STOP**, at tick 30097, and nothing else asked for them in the 60 s. (The grep's
  other `late ...` lines are the render log's unrelated `translucent`/`moment cost` lines.) This run had no
  `bulletLtoR13` miss, so it shows 3 off-frame starts where the earlier run had 5 loads.

## Verdict

**Confirmed, the prediction's first branch, now observed:** the two Uber sounds were stops, not starts. A stop naming
a sound that has no channel stops nothing (B416, `S_AlterChannel` matching none, engine.dll FUN_18002aa20), and nothing
can be playing a sound that has not loaded. So no sound the engine would play was dropped.
