# Measurement 2026-10-27: `cl_threaded_bone_setup` — the pre-pass serial and forked, against none

**Status: measured 2026-10-27, inconclusive on the frame.** The plan above the Environment was committed (`92ea95fe`,
`f7808f19` for the added identity runs) before the runs it describes.

## Observation

1. `2026-10-26-offthread-bones-and-hud-go-no-go.md` measured eligible roots (at least 16 bones, no move parent, no
   ragdoll) at 0.529-0.555 ms a frame over 6.25-6.36 builds on f12 gummo first person, slowest 0.12 ms: finding 105's A
   GO. Its verdict put the saving's ceiling at ~0.3-0.35 ms (*estimate*), less whatever of a root's call must stay
   serial: `BeforeBuild`, the blend inputs, was inside those ticks.
2. Branch `perf/offthread-bones-hud` now has the pre-pass (`cl_threaded_bone_setup 1`; default 0, the engine's), the
   blend inputs run serially ahead of it, and the fork on the shared pool (`47db74a7`). `TF2VIEW_BONE_THRESHOLD=1000`
   keeps the pre-pass on the calling thread (six roots never pass it), which is finding 105 A7.2's serial stage.

## Question

- A. Does the serial pre-pass (P) cost what no pre-pass (S) does — `setup`, `camera pose`, frame — as A7.2 requires?
- B. How much do `setup`, `camera pose`, `camera` and the frame fall from S to the fork (T), in ms and % of the frame?
  How much of the pre-pass stays serial (`prepose … blend`)?
- C. Is the output identical: the frame hash's `pose` part (which hashes the bones) S against T, matched by tick, where
  S agrees with S; and with `mat_queue_mode 2`, does the queue verifier see 0 changed frames under T (A3.8: the bones
  are reused in place while the render thread draws)?

## Prediction

- A: P within S's run spread on `setup` (±0.03 ms) and frame (±0.15 ms); `prepose` ≈ the 0.53 eligible ms moved out of
  the draw loop. Falsified if P's `setup` is above S's by more than 0.05 ms in both pairs.
- B: T's `setup` 0.20-0.40 ms below S's (eligible 0.53 forked over five threads, about two roots deep ~0.15-0.2 ms, plus
  ~0.04 fork, plus the serial blend); blend 0.05-0.15 ms. Frame 0.1-0.4 ms lower, possibly inside the frame's spread.
  Falsified if T's `setup` is not below S's in every pair.
- C: `pose` equal S-S and S-T on every common tick; 0 changed under the verifier.

## Method

One Release build of `47db74a7`: `run-exclusive.ps1 -Shared dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release`,
copied to `D:/measure-f12/threaded-bones/`. One viewer at a time under `run-exclusive.ps1`, each timed run behind the
quiet gate of `run.ps1` here (three `Win32_Processor.LoadPercentage` samples all under 25%, no other
`tf2demoview`/`dotnet`/`MSBuild`/`testhost`):

- timing, 60 s each: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 60 +fps_max 0 +r_threaded_particles 1 +cl_threaded_bone_setup <0|1>`, mode
  default (2). Order, ABBA with the reverse repeated (`grid.ps1`): 01 S, 02 P, 03 T, 04 T, 05 P, 06 S, 07 S, 08 T.
  P = `1` with `TF2VIEW_BONE_THRESHOLD=1000`; T = `1` with the default threshold.
- identity (`identity.ps1`), as `2026-10-26-b597-constant-ring-and-occlusion.md` ran it: `TF2VIEW_FRAME_HASH=1
  TF2VIEW_FIXED_FRAME=0.015`, mode 0, `--measure 6`, gummo first person from 30000: h1 S, h2 S, h3 T, `frame hash at`
  lines matched by tick; then v1 T in mode 2 with `TF2VIEW_QUEUE_VERIFY=1`.
- *Added after h3-T differed from both S runs at labels 30021-30022, which S1 and S2 agreed on* (`identity-extra.ps1`):
  h4 T, h5 S, the same command, to tell a T-only difference from the warm-up's run-to-run one.

Reading (`read.ps1` here: the go/no-go reader, extended to the `prepose` column and the census's bone forks): moment lines after the first, rate lines after the first five,
frame-weighted. % of frame = part / that run's `measured … mean`.

## Control

- **The switch reached the code:** S's moment lines read `prepose 0 over 0` and the census `bone forks so far 0`; P's
  `prepose` is non-zero with forks 0; T's forks climb. Any other pattern voids the run.
- **Same work:** `built … draw` and the root/eligible/child build counts agree across S, P and T within 3%.
- **The hash's own control:** S against S; a part that differs S-S says nothing S-T (finding 97: image, particles and HUD
  are not deterministic run to run; pose and cull are).
- **The verifier's control** is its existing unit test that overwrites a handed-off matrix after submit (B599 record).
- S runs agree within 15% on `setup` and within 0.3 ms on the frame, or the set is void.

## Environment

RTX 3060, i5-10600K (6 physical cores, pool degree 5), Release build of `47db74a7` at `D:/measure-f12/threaded-bones/`,
2026-10-27 00:50-02:46, `mat_queue_mode` default (2) for the grid. Load before / after (3 × 1 s, %):

| run | before | after | run | before | after |
|---|---|---|---|---|---|
| 01 S | **not run** (gate not met in 20 min) | | 05 P | 16 11 12 | 93 79 100 |
| 02 P | **not run** (gate not met in 20 min) | | 06 S | 23 13 20 | 48 88 100 |
| 03 T | 15 13 5 | 38 88 100 | 07 S | 6 7 6 | 7 3 7 |
| 04 T | 6 8 2 | 40 90 90 | 08 T | 3 1 3 | 44 97 60 |

01 and 02 never ran: the machine stayed above the gate (29-49% load sampled at 00:56, other processes) and `grid.ps1`
went on to the next run rather than stopping, so the ABBA order degraded to T T P S S T. **The whole S and P block
(05-07) ran in a slow window**: phases bone setup never touches were up in all three against the T runs either side —
`sound` 0.17-0.19 against 0.15-0.16, `between` 0.35-0.37 against 0.33, `advance` 0.76-0.80 against 0.72-0.75, render
thread draw 2.14-2.24 against 2.11-2.14 (each run's `phases` line). That is the machine, not the switch.

Identity: h1-h3 ran in the grid's process and inherited `TF2VIEW_VIEWPORT_SIZE=1280x720`; h4-h5 (added, below) ran in a
fresh one at the default viewport, so the two batches compare only within themselves (cull differs on 37 labels between
batches and on 0 within).

## Result

Raw output: `2026-10-27-threaded-bone-setup/0N-*.txt`, read by `read.ps1` there. Identity counts from `compare.ps1`
(the hash files, ~6 MB each, are not committed). ms a frame; per rebuild = per frame (every steady frame rebuilt).

| run | frame | setup | eligible (builds) | prepose (built) | blend | children | built draw | camera pose | bone forks |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 03 T | 8.69 | 0.385 | 0.451 (4.76) | 0.226 (4.04) | 0.038 | 0.042 | 36.4 | 3.21 | 5,196 |
| 04 T | 8.88 | 0.396 | 0.462 (4.74) | 0.228 (3.99) | 0.037 | 0.043 | 36.3 | 3.27 | 5,116 |
| 08 T | 8.71 | 0.384 | 0.441 (4.77) | 0.220 (4.04) | 0.039 | 0.042 | 36.8 | 3.24 | 5,204 |
| 05 P | 9.56 | 0.567 | 0.424 (4.71) | 0.393 (3.95) | 0.040 | 0.046 | 36.4 | 3.67 | 0 |
| 06 S | 9.57 | 0.666 | 0.567 (6.13) | 0 (0) | 0 | 0.045 | 37.0 | 3.69 | 0 |
| 07 S | 9.42 | 0.689 | 0.590 (6.44) | 0 (0) | 0 | 0.047 | 38.1 | 3.58 | 0 |

Controls:

- **Switch reached the code: held.** S: `prepose 0 over 0`, bone forks 0 throughout. P: prepose non-zero, forks 0. T: forks
  climb from ~50 at the first note to ~5,200 (one a frame).
- **Same work: FAILED.** With the pre-pass (P and T) the eligible builds are 4.7-4.8 a frame against 6.1-6.4 without it,
  and all builds 36.3-36.8 against 37.0-38.1. The pre-pass removes about 1.5 eligible builds a frame, so S against T is
  not the same work, and P (0.567) is cheaper than S (0.666-0.689) in the same slow window.
- S runs agree on `setup` (0.666-0.689, 3%) and frame (9.42-9.57): held, but both are in the slow window.
- **Identity, pose part, matched by tick:**
  - batch 1: S1-S2 differ on 8 of 211 labels (30023-30030); S1-T on 10 (30021-30030); S2-T on 2 (30021-30022).
  - batch 2 (added): S5-T4 differ on 8 of 225 (30020-30027).
  - In every pair the differences are confined to the first ~10 played ticks after the opening seek, and every label
    from 30031 on is equal in all five runs within a batch. The S-S pair differs there too, so the instrument cannot
    speak about those ticks; T's 30021-30022 in batch 1, where the two S runs agreed, is **not shown to be noise**.
  - cull: 0 differ within a batch.
- **Queue verifier, T, mode 2:** `811000 frames checked, 0 changed` (`v1-T-verify.txt`'s last line). Its count includes
  the loading frames (finding 97), so it says nothing about how many of the measured frames it saw.

## Verdict

**Inconclusive on the frame; the setup drop is real but is not all the fork's.** `setup` is 0.38-0.40 ms under T against
0.67-0.69 under S (−0.27 to −0.30 ms, ~3% of a 9.5 ms frame), a gap far larger than the slow window's few percent; but the
same-work control failed, so part of it is the pre-pass removing ~1.5 builds a frame, not the fork. Frame, `camera` and
`camera pose` cannot be compared: every S and P run sat in the slow window.

- A: **falsified, in the cheap direction** — the serial pre-pass is not cost-neutral, it is cheaper (0.567 against
  0.666-0.689 in the same window), because it builds less.
- B: the `setup` drop (0.27-0.30) is inside the predicted 0.20-0.40, but cannot be credited to the fork alone; blend 0.04
  is **below** the predicted 0.05-0.15 (falsified low). The frame: inconclusive.
- C: pose identical from the 11th played tick on; the first ten ticks are open (the S-S control fails there, and batch 1
  has two labels where T alone differs). Verifier 0 changed.

**What changes:** the missing builds point at the bone clock, which `Instances` advances on the world, viewmodel and
undrawn passes where the engine bumps `g_iModelBoneCounter` once a frame (`InvalidateBoneCaches`, `OnRenderStart`,
cdll_client_int.cpp:2163). An attachment query after those passes is then a first touch on a stale clock: it rebuilds a
drawn skeleton, and rolls the entity's previous mask down to the attachment bit, so next frame a child drawn before its
wearer builds the wearer narrow and the wearer's own call builds it again. The pre-pass's pre-rollover read happens to
dodge the second half. That is a parity defect to fix first, in its own commit; the fork is measured again on that base,
in a new file.
