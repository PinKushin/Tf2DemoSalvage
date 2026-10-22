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

- The development machine, Debug viewer, default window, both runs under `run-exclusive.ps1`, each built in the locked
  command. Before: base `e1ec318a` (+ this file), 20:18. After: `60f4606a`, 20:33.
- **Load was not recorded**: the script's `wmic` sampling printed nothing (not installed). The before run was plainly
  busier (68 frames over 100 ms, mean 34.65 ms, against 16 and 16.10 ms after), so frame times are not compared here.

## Result

Before (`e1ec318a`), stdout and the run's log, `viewer-20261021-201802-32144.log`:

```
measured 60 seconds of playback, 58 samples (19 rebuild reports), ending at tick 33824; 1733 frames, mean 34.65 ms
  pauses: 68 frames over 100 ms, 24 heartbeat gaps over 50 ms
  sound cache: 1337 sounds, WAV 242 MB, MP3 102 MB held; 5 decodes in play
precached 1372 of 1373 sounds in 5151 ms
precached 1393 of 1394 sounds in 17 ms
STALL reading 'vo/null.mp3' took 171 ms (read 170.6 ms, decode 0.5 ms; 6912 frames); this frame is a freeze
```

After (`60f4606a`), `viewer-20261021-203331-38648.log`:

```
measured 60 seconds of playback, 59 samples (38 rebuild reports), ending at tick 33934; 3726 frames, mean 16.10 ms
  pauses: 16 frames over 100 ms, 3 heartbeat gaps over 50 ms
  sound cache: 1337 sounds, WAV 242 MB, MP3 102 MB held; 0 decodes in play
precached 1320 of 1321 sounds in 5459 ms
precached 12 of 12 sounds in 42 ms
loaded off the frame 'player/invulnerable_off.wav' (read 367.8 ms, decode 0.4 ms; 114944 frames)
loaded off the frame 'player/invulnerable_on.wav' (read 402.2 ms, decode 0.5 ms; 193008 frames)
loaded off the frame 'vo/null.mp3' (read 1.1 ms, decode 2.7 ms; 6912 frames)
loaded off the frame '>weapons/fx/nearmiss/bulletLtoR08.wav' (read 111.5 ms, decode 0.1 ms; 17178 frames)
loaded off the frame '>weapons/fx/nearmiss/bulletLtoR11.wav' (read 1.3 ms, decode 0.0 ms; 14742 frames)
```

- **Deviation from Method:** the `sound cache:` line was not given an off-frame field; the off-frame count was read from
  the five `loaded off the frame` lines, each logged where the load was taken in.
- The first precache names 1,320 against 1,372 because a name is now listed once per sfx key (two spellings of one sfx
  were counted twice before); the cache holds the same 1,337 sounds in both runs. The second precache names 12 against
  1,394 (B570).

## Verdict

**Confirmed.** Decodes paid on the render thread during play went 5 → 0, and the same five first plays were loaded off
the frame instead; three of those reads took 111-402 ms on this machine and would each have been a frozen frame. The
control held: the cache held 1,337 sounds in both runs. The second precache now lists 12 names, not 1,394.
