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

- The development machine, Debug viewer, under `run-exclusive.ps1`, built in the locked command, on `fd31d8d2` (+ this
  file), 00:45. CPU load 27% before, 25% after.

## Result

stdout: `measured 60 seconds of playback, 59 samples (48 rebuild reports), ending at tick 33986; 5060 frames, mean
11.86 ms`; `sound cache: 1334 sounds, WAV 240 MB, MP3 102 MB held; 0 decodes in play`.

`viewer-20261022-004531-25168.log` (the run's own; a newer 113-line log in the folder is another process), every
`precached`, `loaded off the frame`, `waiting for`, `late ...`, `stopped while loading` and `invulnerable` line:

```
[audio] precached 1320 of 1321 sounds in 2807 ms
[audio] precached 12 of 12 sounds in 12 ms
[audio] waiting for 'vo/null.mp3' (tick 31864, started)
[audio] loaded off the frame 'vo/null.mp3' (read 172.8 ms, decode 0.4 ms; 6912 frames)
[audio] late start 'vo/null.mp3' (tick 31864) at tick 31877
[audio] waiting for '>weapons/fx/nearmiss/bulletLtoR08.wav' (tick 32994, started)
[audio] loaded off the frame '>weapons/fx/nearmiss/bulletLtoR08.wav' (read 0.2 ms, decode 0.0 ms; 17178 frames)
[audio] late start '>weapons/fx/nearmiss/bulletLtoR08.wav' (tick 32994) at tick 32996
```

- No line names `invulnerable`: the stop at tick 30097 no longer loads either sound. Off-frame loads 4 -> 2 against
  `2026-10-21-uber-sounds-path-f12.md`, exactly the two stop-only loads. Held sounds 1,336 -> 1,334, the same two.
- The control held: both starts logged their wait and late start. No `stopped while loading` line.

## Verdict

**Confirmed.** A stop now loads nothing (the Uber pair is gone from the loads and from the cache), the starts still start
late (2 of 2), and nothing decodes in play.
