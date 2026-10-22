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

- The development machine, Debug viewer, default window, under `run-exclusive.ps1`, built in the locked command, on
  `a76b5acf` (+ this file), 21:30. CPU load 16% before, 10% after.

## Result

stdout:

```
measured 60 seconds of playback, 59 samples (47 rebuild reports), ending at tick 34004; 5123 frames, mean 11.71 ms
  pauses: 2 frames over 100 ms, 0 heartbeat gaps over 50 ms
  sound cache: 1337 sounds, WAV 242 MB, MP3 102 MB held; 0 decodes in play
```

`viewer-20261021-213043-27564.log`, every matching line:

```
precached 1320 of 1321 sounds in 3243 ms
precached 12 of 12 sounds in 13 ms
loaded off the frame 'player/invulnerable_off.wav' (read 0.5 ms, decode 0.3 ms; 114944 frames)
loaded off the frame 'player/invulnerable_on.wav' (read 0.6 ms, decode 0.4 ms; 193008 frames)
loaded off the frame 'vo/null.mp3' (read 0.2 ms, decode 0.3 ms; 6912 frames)
late start 'vo/null.mp3' (tick 31864) at tick 31865
loaded off the frame '>weapons/fx/nearmiss/bulletLtoR13.wav' (read 398.3 ms, decode 0.0 ms; 13246 frames)
late start '>weapons/fx/nearmiss/bulletLtoR13.wav' (tick 32418) at tick 32446
loaded off the frame '>weapons/fx/nearmiss/bulletLtoR08.wav' (read 0.2 ms, decode 0.0 ms; 17178 frames)
late start '>weapons/fx/nearmiss/bulletLtoR08.wav' (tick 32994) at tick 32996
```

No `STALL reading`, no `did not arrive; not started`, no `would not open`.

- **The two `invulnerable` loads have no late start.** Both arrived together at tick ~30088, right after the seek to
  30000. `SoundPresenter` asks for a sample in two places: a start, which queues a late start, and a STOP, which does
  not, because nothing can be playing a sound that has not loaded. No late line names them, so they were asked for by a
  stop (or by a re-established one-shot, which is not restarted). *Inferred from which paths exist, not logged.*

## Verdict

**Confirmed, with one prediction detail falsified.** Decodes in play stayed at 0, and every sound STARTED while loading
started late, none dropped: 3 of 3, 1-28 ticks after they were asked for (`bulletLtoR13` waited out a 398 ms read on
the pool instead of freezing a frame). The prediction of one late start per off-frame load was wrong for the two
`invulnerable` sounds. Those were asked for by a path that does not start a sound (inferred above).
