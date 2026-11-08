# A seek's particle replay on the workers: the landing frame before and after (B708, D221)

## Observation

`2026-11-08-landing-particle-rebuild.md`: the landing frame's particle time (0.60 to 0.83 s on `z1800.dem --tick 20000`)
is about 93% the effects' own simulation, run serially on the frame thread, while a played frame's steps already run on
the particle workers. The change under test plans a replay's steps on the frame thread in chunks of 66 and simulates them
on the workers, as a played frame's are.

## Question

On the same run, how much does the landing frame's `particles` part, and the landing frame as a whole, fall with the replay
on the workers?

## Hypothesis

The simulation spreads over the lane's threads (the machine has 6 cores, 12 logical); the serial asking stays.

## Prediction

`SLOW CAMERA` `particles` at the landing: A (before, `1542840c`) 0.55 to 0.90 s, as measured earlier the same day; B (after)
0.10 to 0.30 s. The landing `SLOW FRAME`: B at least 0.35 s below A. Falsified if B's particles part is above 0.40 s in
both B runs, or if B's landing frame is not below A's in both pairs.

## Method

Two Release builds: A from a temporary worktree at `1542840c` (the commit before the change), B from this branch. Alternated
under the desktop lock, A B A B, each:

```bash
TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh run-exclusive.ps1 <build>/tf2demoview.exe tools/corpus/demos/z1800.dem --tick 20000 --autoplay --measure 3
```

Read the `SLOW CAMERA` and `SLOW FRAME` lines right after `skip landed at 20000` in each run's newest log.

## Control

Each run's log must carry `skip landed at 20000` followed by a `SLOW CAMERA` with a `particles` part. A's figures must fall
in the range measured earlier the same day (`2026-11-08-landing-frame.md`, 600 and 833 ms). If A does not reproduce it,
the machine's state differs and the pair is not compared.

## Environment

The owner's desktop machine, Windows 11, 6 cores and 12 logical, viewport 1280x720. A: Release build of `1542840c` in a
temporary worktree; B: Release build of this branch with the change, uncommitted on top of `7b6528fe`. Alternated A B A B
under the desktop lock, no other viewer or agent running, TF2 closed. Load before and after was not recorded.

## Result

The control holds in all four runs: `skip landed at 20000`, then `SLOW CAMERA` with `particles`. A reproduces the earlier
range (656 and 592 ms against 600 and 833).

| run | log | landing `particles` | landing camera | landing `SLOW FRAME` | advance | GC paused |
|---|---|---|---|---|---|---|
| A1 | `viewer-20261108-165229-11892.log` | 655.9 | 858.1 | 1,237 | 337.7 | 157 |
| B1 | `viewer-20261108-165337-22596.log` | 364.9 | 549.8 | 763 | 173.7 | 136 |
| A2 | `viewer-20261108-165446-9004.log` | 591.7 | 789.1 | 1,140 | 314 | 155 |
| B2 | `viewer-20261108-165554-34628.log` | 417.1 | 621.2 | 979 | 320.7 | 158 |

Raw lines:

```
A  16:53:30.692 WARN [render] SLOW CAMERA 858 ms: fly 0, view 0, device 202.2, particles 655.9
A  16:53:30.733 WARN [render] SLOW FRAME 1237 ms: sound 39.7, camera 858.1, project 0, advance 337.7, capture 0, hud 1, draw 0; unaccounted 0 ms; gc 60/10/0 paused 157 ms
B  16:54:40.851 WARN [render] SLOW CAMERA 550 ms: fly 0, view 0, device 184.9, particles 364.9
B  16:54:40.891 WARN [render] SLOW FRAME 763 ms: sound 38.2, camera 549.8, project 0, advance 173.7, capture 0, hud 1, draw 0; unaccounted 0 ms; gc 52/10/0 paused 136 ms
A  16:55:48.438 WARN [render] SLOW CAMERA 789 ms: fly 0, view 0, device 197.4, particles 591.7
A  16:55:48.475 WARN [render] SLOW FRAME 1140 ms: sound 35.7, camera 789.1, project 0, advance 314, capture 0, hud 0.9, draw 0; unaccounted 0 ms; gc 67/13/0 paused 155 ms
B  16:56:56.711 WARN [render] SLOW CAMERA 621 ms: fly 0, view 0, device 204.1, particles 417.1
B  16:56:56.749 WARN [render] SLOW FRAME 979 ms: sound 36.4, camera 621.2, project 0, advance 320.7, capture 0, hud 0.8, draw 0; unaccounted 0 ms; gc 62/11/0 paused 158 ms
```

## Verdict

**Partly confirmed.** The landing's particle time fell by 38 to 44% (A 592 and 656 ms, B 365 and 417 ms), and the landing
frame by 161 and 474 ms. **The predicted range is falsified**: B's particles part was 0.365 and 0.417 s against a predicted
0.10 to 0.30, and only the first pair met the predicted 0.35 s frame drop. B1's `advance` of 174 ms against 314 to 338 in
the other three is not this change's (corpses run elsewhere) and is not explained. *Interpolated, the likely limit:* one
effect's replay is sequential, tick after tick, so the longest replays bound the landing whatever the threads; which
effects those are is the next measurement. The change stays: it is 40% off the largest part of the landing at no cost to
the result (`ParticleEffectsWorkersTests`, byte for byte).
