# Measurement 2026-10-22: what is inside the `camera` phase on f12

## Observation

`2026-10-22-f12-steady-frame.md` measured the steady f12 frame at 14-15.5 ms, of which the `camera` phase is 8.4-9.5 ms
(59-62%). Its raw `parts:` lines already split `camera` into `view`, `setcamera` (with the device's own split), `pose` and
`particles` (with the effects' own split), and the `moment cost` lines split one rebuild of the moment; but nobody has read
those lines together, and in A2's heaviest second `camera pose` was 7.37 ms against a `moment cost` of about 4.6 ms, so the
pose may hold time the moment's own split does not name.

## Question

On lcor's f12 (`demostf-cp_process_f12-2026-08-08-2207.dem`), `--tick 30000 --first-person`, 1280x720, `+fps_max 0`,
built from this branch's base `311a765d`: (1) how do the steady `camera` milliseconds divide between `view`, `setcamera`
(and its parts), `pose` and `particles` (and theirs); (2) how much of `camera pose` is the moment rebuild's own `moment cost`
split, and what holds the rest; (3) which single named part is the largest, and does the engine pay for that work?

## Method

The `--measure` phase line, its `parts:` list and the `moment cost` line, all printed by the viewer from the clock readings
the code itself used (B243); nothing recomputed here. Two runs, R1 and R2, back to back:

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem \
  --tick 30000 --first-person --measure 20 +fps_max 0
```

CPU load (`Get-CimInstance Win32_Processor` LoadPercentage, 3 x 2 s) before and after each run. Raw output in
`2026-10-22-f12-camera-breakdown/`. Steady = the per-second lines after the first five. If (2) leaves more than 1 ms of
`pose` unnamed, a timer is added around the unnamed calls (carried, not recomputed) and the pair repeated as R3, R4; that
addition is recorded here before R3 runs.

## Control

- **Sum control:** `camera view + camera setcamera + camera pose + camera particles` must be within 5% of the `camera`
  phase column on the same line; if not, the parts are not timing the phase they claim to split.
- **Sub-split control:** each `setcamera` part list and `particles/*` list must sum to no more than its parent (they are
  nested intervals of the same clock); a sum above its parent means a part is counted twice.
- Ticks advanced about 66.7 x 20; the run reports particles and model decals (a run with no demo loaded reports none).
- R1 and R2 agree within 15% on `camera`, or the pair is void and repeated with the load recorded.

**Trace T1, added after R1/R2 and before it runs** (the cross-check the timers cannot give: what inside the largest
parts is spent where). The same command under
`dotnet-trace collect --profile dotnet-sampled-thread-time --show-child-io -o T1.nettrace -- <exe> <args>`, read with
`Probe cpu-trace T1.nettrace <from> <to> <render thread> 80` over the steady window. **Trace control:** the inclusive share
of `UploadCameraOver` must be within a factor of 1.5 of `camera`'s share of the frame on T1's own `--measure` lines.

## Environment

- Machine: RTX 3060, i5-10600K. Debug viewer built from `062b4562` (this record on `311a765d`; no code change). Every run
  through `run-exclusive.ps1`.
- Load (3 x 2 s, before / after): R1 12/13/4% / 15/22/7%; R2 6/7/6% / 3/20/8%. The lightest load of any f12 run so far.

## Result

Steady = 13 per-second lines after the first five, frame-weighted (`parts.ps1`, scratch; raw in the folder). ms a frame.

| part | R1 | R2 |
|---|---:|---:|
| **camera** (phase column) | **7.16** | **6.99** |
| camera view | 0.02 | 0.01 |
| camera setcamera | 0.74 | 0.68 |
| ... batches / overlays / sky / detail / water | 0.19 / 0.12 / 0.13 / 0.28 / 0 | 0.17 / 0.11 / 0.13 / 0.26 / 0 |
| **camera pose** | **4.04** | **3.87** |
| **camera particles** | **2.37** | **2.42** |
| ... bursts (of which step / muzzles / dispatches / beams / overheals) | 0.91 (0.35 / 0.15 / 0.15 / 0.12 / 0.11) | 0.99 (0.36 / 0.23 / 0.16 / 0.11 / 0.10) |
| ... build | 0.66 | 0.65 |
| ... decals | 0.52 | 0.53 |
| ... trails / lights / hand / sounds / impacts | 0.09 / 0.07 / 0.05 / 0.04 / 0.03 | 0.08 / 0.06 / 0.05 / 0.03 / 0.03 |
| other phases: draw / advance / hud / sound | 2.25 / 1.10 / 1.04 / 0.21 | 2.11 / 1.07 / 0.99 / 0.20 |

**The pose, from the `moment cost` lines** (each a mean over 100 rebuilds, one rebuild a frame; the last two of R2):
4.6 and 3.9 ms = sample 0.3, drawlist 0.4-0.5, models 0.1, pose 3.0-3.7, viewmodel 0.3; the inner pose = lighting
0.2-0.5, simulate 0.5, setup 0.3-0.4, anim 0.1-0.2, collate 0.2, baked 0.3-0.4, **place 0.7-0.8** (build 0.62-0.74, of
which flex 0.34-0.45, binds 0.2), rest 0.3. Posed 26-29 of about 1,870 selected; about 1,300-1,370 hidden by the PVS.

**Controls.** Sum: R1 0.02 + 0.74 + 4.04 + 2.37 = 7.17 against 7.16; R2 6.98 against 6.99 (met). Sub-split: particles'
eight parts sum to 2.37 and 2.42, exactly their parent; setcamera's to 0.72 and 0.67, under it; bursts' to 0.90 and 0.97,
under it (met). R1/R2 `camera` agree within 2.4% (met). Ticks and content: both runs print a decal census (75 model
decals) and particles (met). Pose control: `camera pose` 4.04/3.87 against `moment cost` 3.9-4.6 a rebuild, so the pose
column IS the moment rebuild and question (2) leaves under 1 ms unnamed; no extra timer added.

**Mean frame** (frame-weighted over the same steady lines, `parts.ps1` in the folder): R1 11.86 ms, R2 11.99 ms, T1 12.03 ms.
The first reading of this script printed 0.00 for all three (its regex stopped at the comma in `(0 worst, 9606 best)`);
fixed and re-read before anything was ranked.

**T1** (`T1.txt`, load 16/8/4% / 11/9/12%): camera 7.24 ms of 12.03 (60.2%). Render thread 17308, 208-224 s of a
226.5 s trace, 8,028 samples (`T1-17308.txt`). **Trace control:** `UploadCameraOver` 59.9% inclusive against 60.2%; met.
Inclusive, under the camera: `MomentScene.Pose` 33.0%, `EntityModelSet.Instances` 31.3%, `FramePresenter.DrawOver`
(particles) 18.6%, `Device3D.SetCamera` 7.9%, `BurstEffects.StepOver` 7.0%, `SkeletonPose.Build` 6.8%, `ModelLighting.For`
6.2%, `FrameParticles.BuildOver` 5.2%, `FlexFor` 4.0%, `DecalEffects.StepModelDecals` 2.6% (2.3% of it its own body, a
leaf), `ExposureHistogram.TryRead` 2.6% (outside camera). `BulkMoveWithWriteBarrier` is 22.0% inclusive; every one of its
samples has `PollGCWorker` as its leaf (`T1-17308-bulkmove.txt`), so whether that is time in the copy or the sampler's
charge at its safe point is B574's open question, not settled here. Under `Instances` (`T1-17308-instances.txt`) the light
cache's build lambda is 9.2% of `Instances`, with `SunFrom` (the sky trace) inside it.

## Verdict

**The `camera` phase is a long tail; no single named part is above 0.8 ms.** Of about 7.1 ms: pose 3.9-4.0 (place
0.7-0.8, about half of it the flex already cut in finding 92; simulate 0.5; drawlist 0.4-0.5; lighting 0.2-0.5; baked,
setup, collate, sample, viewmodel 0.2-0.4 each), particles 2.4 (bursts 0.9-1.0, build 0.65, decals 0.52), setcamera 0.7.
The exposure count (B575) is not inside `camera` at all. **The largest part found doing work the engine does not do is
`StepModelDecals`' own body** (a 2.3% leaf, about 0.3 ms a frame): it walks every client bullet of the match (12,518 on
f12) and every entity impact, each frame, to keep those that landed since the last frame; the engine places a decal when
its event arrives. That is the first item taken (`2026-10-22-model-decal-window.md`). Second, to be settled before it is
believed: the light cache's builds (a counter first, since the engine's throttle allows two a frame) and B574's copies.
