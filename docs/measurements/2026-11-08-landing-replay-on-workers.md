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

## Result

## Verdict
