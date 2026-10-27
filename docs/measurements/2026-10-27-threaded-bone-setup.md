# Measurement 2026-10-27: `cl_threaded_bone_setup` — the pre-pass serial and forked, against none

**Status: plan.** Written and committed before any run, with the build it measures (`47db74a7`).

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

## Result

## Verdict
