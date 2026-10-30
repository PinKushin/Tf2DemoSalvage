# Measurement 2026-10-30: the face-flex vertex work after the rewrite, three runs against three

## Record (written and committed before the first run)

## Observation

`2026-10-30-flex-for-cost.md` split `flex` (0.527-0.546 ms a rebuild on f12 tick 30000, gummo first person) into its
parts: the accumulation (`deltas`) 0.203-0.211, the write into the flexed slots (`slots`) 0.156-0.159, the global
weights (`driving`) 0.081-0.092 and the scratch clear (`clear`) 0.029-0.032, at 5.8 faces a rebuild, nearly all misses.
Commit `3b7d98a1` rewrote the three vertex parts without changing an output bit (`FaceFlexIdentityTests`): the
accumulation indexed instead of enumerated through the interface, the clear limited to the vertices the model's flexes
name, and the slot write range-checked once.

## Question

On the same scene and instrument, how much do `deltas`, `clear`, `slots`, `flex`, `place`, the `camera` phase and the
frame mean move between the build before the rewrite (A, `ffd51e49`, the instrument alone) and after (B, `3b7d98a1`),
with the runs interleaved?

## Hypothesis

The accumulation's cost was the interface enumeration, not the arithmetic; the clear's was the whole scratch; the slot
write's was partly its per-slot checks.

## Prediction (before the runs)

Per run, the mean over its `moment cost` lines (the first left out, as before):

1. B's `deltas` at most 0.10 ms in every run (A about 0.20).
2. B's `clear` at most 0.010 ms in every run (A about 0.03).
3. B's `slots` at most 0.13 ms in every run (A about 0.16).
4. B's `flex` at most 0.42 ms in every run, and every B run's `flex` below every A run's.
5. `driving`, `rules` and the counts (faces, misses, vertices) unchanged within the A runs' own spread plus 10%.
6. `camera` and the frame mean: no prediction beyond "B not above A by more than the A runs' spread"; a 0.1-0.15 ms
   change is inside their run-to-run spread (0.47 ms in the baseline), so they are reported, not judged.

## Falsified if

1. Any B run's `deltas` above 0.10, or `clear` above 0.010, or `slots` above 0.13.
2. Any B run's `flex` above 0.42, or the B and A `flex` ranges overlap.
3. B's faces, misses or vertices outside A's range by more than 10% (the scene differs; the comparison is void).

## Instrument

As `2026-10-30-flex-for-cost.md` (`FlexCounters`, the `flex parts` print); both builds carry it unchanged.

## Method

A: `D:/measure-f12/flex-a/tf2demoview.exe` (built for the baseline from a `git archive` of `ffd51e49`). B: a `git archive`
of `3b7d98a1` extracted no-clobber into a new `D:/measure-f12/flex-b-src`, `MSBUILDDISABLENODEREUSE=1 dotnet build
managed/Tf2DemoSalvage.Viewer3D -c Release`, the output copied no-clobber to `D:/measure-f12/flex-b/`. Six runs in the order
A B B A A B, named `04-A 05-B 06-B 07-A 08-A 09-B`, by `2026-10-30-flex-for-cost/all.ps1` with the same `run.ps1` (quiet
gate, then under `run-exclusive.ps1`): `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe
D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2`. Read with
`summ.ps1`.

## Control

- Each run passes the baseline record's controls (parts within 15% of `flex`, counts consistent, 67 ticks a second).
- The A runs here reproduce the baseline's A runs: `flex` within 0.05 ms of 0.527-0.546.
- Identity at the output is separate from this record: the byte tests, and a paused `--shot` pair per build at tick 30000
  and at a talking-face tick, compared pixel for pixel, with a same-build pair as the comparison's control.

## Environment

Same machine and settings as the baseline; load before and after each run in `runlog.txt`.

## Result

Raw output: `2026-10-30-flex-for-cost/04-A.txt` to `09-B.txt`; console and load in `runlog.txt`; figures from
`summ.ps1 -Runs '04-A;05-B;06-B;07-A;08-A;09-B'`, each the mean over a run's `moment cost` lines after the first.
Milliseconds a rebuild.

| run | frame | camera | place | **flex** | driving | rules | clear | **deltas** | **slots** | faces | misses | vertices |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 04-A | 8.02 | 6.37 | 0.871 | **0.581** | 0.103 | 0.027 | 0.033 | 0.215 | 0.165 | 5.76 | 5.72 | 16,417 |
| 05-B | 6.79 | 5.34 | 0.718 | **0.428** | 0.086 | 0.022 | 0.023 | 0.159 | 0.105 | 5.98 | 5.93 | 16,754 |
| 06-B | 7.01 | 5.53 | 0.706 | **0.424** | 0.083 | 0.021 | 0.023 | 0.161 | 0.102 | 5.71 | 5.68 | 16,947 |
| 07-A | 11.89 | 9.68 | 1.042 | **0.675** | 0.124 | 0.033 | 0.037 | 0.240 | 0.202 | 5.89 | 5.85 | 17,398 |
| 08-A | 8.38 | 6.69 | 0.860 | **0.565** | 0.098 | 0.025 | 0.030 | 0.212 | 0.165 | 5.89 | 5.84 | 16,836 |
| 09-B | 9.36 | 7.35 | 0.936 | **0.537** | 0.118 | 0.034 | 0.033 | 0.187 | 0.124 | 6.03 | 6.00 | 17,525 |

Ranges: `flex` A 0.565-0.675, B 0.424-0.537; `deltas` A 0.212-0.240, B 0.159-0.187; `slots` A 0.165-0.202, B
0.102-0.124; `clear` A 0.030-0.037, B 0.023-0.033; `place` A 0.860-1.042, B 0.706-0.936; `camera` A 6.37-9.68, B
5.34-7.35; frame A 8.02-11.89, B 6.79-9.36.

Control:
- Parts against `flex`: 97-98% in every run (05-B: 0.415 of 0.428; 07-A: 0.660 of 0.675). Counts consistent as in the
  baseline. Playback 30000 to 31346-31348 in 20 s in every run.
- A here against the baseline's A (0.527-0.546): 04-A and 08-A within 0.05 (0.581, 0.565); **07-A is not** (0.675), and
  neither its frame (11.89 against 6.79-7.40). 07-A and 09-B are the two slowest runs of the six in every column,
  `driving` and `rules` included, which this change does not touch (0.124 and 0.118 against 0.083-0.103): the machine
  was slower during them (load after 07-A 33/24/16, before 09-B 18/15/4, after 25/21/22), not the build.

Identity at the output (not a measurement; paused `--shot`, 1280x720, compared with `pixdiff.ps1` in the run folder,
every pixel; captures kept outside the repository in `D:/measure-f12/flex-scratch/`):
- **The talking face, from the front**: tick 30515, camera `TF2VIEW_CAMERA='-3400 -1268 648 5 135'`, 55 units in front
  of scout entity 5 at his head (origin -3445.1 -1224.1 583.1, yaw -44.7, from `moment-dump f12.dem 30515 5`), 0.18 s
  into `vo/scout_StandOnThePoint03.mp3` and scene `scenes/Player/Scout/low/1305.vcd` (started tick 30503). A1 against
  A2, B1 against B2, A1 against B1, A2 against B2: 0 pixels differ in each.
- **Control that this camera sees a face error**: a third build, B with one deliberate change (the slot write's
  position deltas doubled, `D:/measure-f12/flex-c-src`, never committed), against B1: 1,877 pixels differ, all inside
  (353,145)-(597,349), which is the scout's head and a distant player's.
- Tick 30000, gummo first person (no face is on screen in this view, so it shows only that the rest of the frame is
  unchanged): A, A, A, B, B. A2 = A3 = B1 = B2, 0 pixels differ in each pair. The first capture of
  the session (A1) differs from all the others in the top bar only (42,997 pixels inside (0,0)-(1279,48)), the same
  against A2 as against B1, so it is the session's first HUD draw, not the build.
- Tick 30515 from behind the scout (`TF2VIEW_CAMERA='-3430 -1130 650 10 270'`, his face not in view; a soldier and a
  pyro are): A1 against A2, B1 against B2, A1 against B1, A2 against B2, 0 pixels differ in each. A at 30515 against
  A at 30516: 757,877 pixels differ (the comparison sees a frame's change).
- Allocation (`gc:` line of each run): A 0.63-0.92 MB a frame, B 0.66-0.79; B inside A's range, so the array copy
  `Accumulate` makes for a non-array vertex list is not being taken in production.
- Byte level: `FaceFlexIdentityTests` (195 frames, three models, bit-identical streams), and the Rendering medic tests
  through the production path, green on B.

## Verdict

**The rewrite lowers `flex` by about a quarter: B 0.424-0.537 against A 0.565-0.675, ranges apart; the means of the
three are 0.463 against 0.607, 0.144 ms a rebuild (24%, *arithmetic*).** Adjacent pairs differ by 0.153 (04-A, 05-B),
0.251 (06-B, 07-A) and 0.028 (08-A, 09-B): the two slow runs fall one on each side, so the pairs are noisier than the
means. `slots` fell most (0.165 to 0.102-0.105 in the quiet runs, about 38%), `deltas` about a quarter (0.21 to
0.16), `clear` about a quarter (0.03 to 0.023). Predictions 1, 2 and 4's first half are **falsified**: `deltas` stayed
above 0.10 (0.159-0.187), `clear` above 0.010 (0.023-0.033), and `flex` above 0.42 in every B run (0.424-0.537); 3
holds (`slots` at most 0.124) and 4's second half holds (no overlap). 5 holds for the counts; `driving` and `rules`
moved with the machine in 07-A and 09-B, as said in the control. 6: `camera` and the frame mean are lower in B's quiet
runs (5.34-5.53 against 6.37-6.69) but A's own spread is 3.3 ms, so no claim is made. **The hypothesis that the
accumulation's cost was the interface enumeration is only partly right**: removing it took a quarter, and the remaining
~27 µs a face (about 9 ns a vertex animation applied) is somewhere this split does not resolve (the per-flex ramps over
every flex of the model, the scattered writes); that is the next question, not answered here.
