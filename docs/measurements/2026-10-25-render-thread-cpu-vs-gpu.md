# Measurement 2026-10-25: the render thread's 2.2 ms, CPU submission against GPU execution, per pass

## Record (written and committed before the first run)

## Observation

Rung 2 (`2026-10-23-queued-render-thread-rung-2.md`) measured `render thread draw` at 2.24-2.35 ms a frame on f12 first
person, mode 2. Finding 100 measured the NVIDIA driver worker (`nvwgf2umx.dll`) at 1.75-2.20 ms a frame on its own thread.
The owner's target is 1000 fps (a 1 ms frame); a 2.2 ms render thread caps the viewer near 450 fps even with a free main
thread. Nobody has split that 2.2 ms by pass, or into CPU submission and GPU execution.

## Question

On f12 from tick 30000, gummo first person, 1280x720, mode 2: per pass of `Device3D.DrawFrame`, how many ms of
render-thread CPU, how many ms of GPU time, and how many draws, buffer maps and primitives? Per frame, how much CPU does the
driver worker spend? Which of the three dominates, and what would have to change for the draw to fit under 1 ms?

## Prediction (before the runs)

The draw is CPU-bound: the GPU passes sum to under 1 ms at 1280x720, the render thread's CPU is spread over the world,
opaque models and translucent interleave in proportion to their draw counts, and the driver worker's 1.75-2.2 ms is the
same order as the render thread's own time. Falsified if the GPU sum at 1280x720 exceeds the render thread's CPU, or if
GPU time per pass grows with pixels by more than its spread at 640x360 -> 1920x1080 for the passes with most CPU time.

## Instrument (in `Render/PassProfiler.cs`, on only under `--measure`)

Carried from where the work runs, never recomputed (B243):

- **Boundaries** are marks in `DrawFrame`, one Stopwatch read and one `D3D11_QUERY_TIMESTAMP` at each, inside one
  `TIMESTAMP_DISJOINT` per frame. A pass is the interval since the previous mark: `clear`, `monitors`, `sky 2d`,
  `sky 3d`, `water views`, `world`, `opaque models`, `entity decals`, `translucent` (world translucents, translucent models
  and grass interleaved, one pass because the interleave is), `water intersection`, `occlusion`, `viewmodels`,
  `particles`, `overlay`, `exposure`, `bloom`, `debug lines` (map lines and points), `hud`, `present`.
  A pass whose code did not run that frame gets no mark and no time.
- **GPU per pass** = timestamp delta. Read back on the render thread at the start of the frame five frames later with
  `D3D11_ASYNC_GETDATA_DONOTFLUSH`; a frame not yet answered is skipped and counted; a frame whose disjoint query says
  `Disjoint` is dropped and counted. Both counts print.
- **Primitives and pixel-shader invocations per pass** from one `D3D11_QUERY_PIPELINE_STATISTICS` per pass, same ring.
- **Draws and buffer maps per pass**: one increment at each `Draw`/`Map`/`UpdateSubresource` call in `Render` (the only
  assembly that touches the device), read at each mark. **Cut, named:** state-change counts and upload BYTES are not
  instrumented; maps are counted as calls.
- **Driver worker CPU** per frame: `2026-10-22-viewer-helper-threads/sample-threads.ps1` beside each run (finding 100's
  instrument), the `nvwgf2umx.dll` thread over the run's last 15 s, divided by the frames in that span.
- Printed after the `--measure` phases as `render passes, mean per frame` with the backbuffer size actually used.

## How the table is read (stated before the runs)

GPU timestamps are taken when the GPU REACHES the mark, so when the GPU waits for commands, a pass's GPU delta is roughly
max(submission pacing, execution). Therefore:

- A pass whose GPU ms grows with pixels across 640x360 -> 1280x720 -> 1920x1080 (beyond its spread) is **GPU-execution
  bound**.
- A pass whose GPU ms stays flat across sizes and is near its own CPU ms is **submission bound**: its GPU figure is the
  CPU's pacing, not execution.
- The empty view (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`) is the floor: what a frame costs with nothing in view.

## Method

Release build of this branch through `run-exclusive.ps1 -Shared dotnet build`, `MSBUILDDISABLENODEREUSE=1`. Each run, one
viewer, exclusive:

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=<size> pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 <exe>
D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2`

Conditions: **S** 640x360, **M** 1280x720, **L** 1920x1080, **E** 1280x720 with the empty-view camera. M five runs, S, L
and E two each, interleaved M S L E M M L S E M M. Quiet gate before each: no other `tf2demoview`/`run-one`/measure process
in `Get-CimInstance Win32_Process`, three consecutive `Win32_Processor LoadPercentage` samples under 25%; load sampled
again after; a run failing either is discarded and repeated. Raw stdout and sampler CSVs in
`2026-10-25-render-thread-cpu-vs-gpu/`.

**Overhead A/B**: origin/main `077d0175` against this branch, M, two runs each; the instrument is cheap if the frame
means are within the spread.

## Control

(a) The per-pass CPU sums to `render thread draw` within ~0.05 ms (the remainder is the job wrapper outside
`DrawFrame`). (b) The printed backbuffer size matches the condition (L may be clamped by the display; recorded either way).
(c) E's draw count collapses against M's, proving the camera override took effect under `--first-person`. (d) Skipped
and disjoint frames are a small share of frames counted. (e) VSync is off (`+fps_max 0`, interval 0); a large `present`
CPU is back-pressure from the GPU, not submission.

## Environment

This machine, branch `perf/render-thread-gpu-vs-cpu` at `4a2878b0` (Release, 0 warnings) and origin/main `077d0175`
(Release, built in a scratch worktree, run from `D:/measure-f12/b597-main077`). Windowed; NVIDIA driver.

## Result

Raw stdout of every run, discarded ones included, and each run's thread-sampler CSV: `2026-10-25-render-thread-cpu-vs-gpu/`.
`run.ps1` is one run, `grid.ps1`/`grid3.ps1` the orders actually run, `summ.ps1` the summariser behind every number below.

**Deviations, stated.** (1) `05-A`/`09-A` did not launch (the scratch path was too long for `Start-Process`; rerun from `D:` as
`14-A`/`16-A`). (2) **L is not 1920x1080**: the back buffer was clamped to **1644x931** by the display (control b). (3) **E is
void**: under `--first-person` `TF2VIEW_CAMERA` is ignored, so `04-E`/`11-E` drew M's scene, draw for draw (control c failed).
The free-camera repeat (`-Free`, `18-F`/`19-F`) never passed the gate. (4) The gate waited out three spans of outside load this
agent did not start (idle reused MSBuild nodes and a `VBCSCompiler`, 20:14-20:35, 20:35-21:05, 21:19-21:39); after the third
the run stopped, per the task's 20-minute rule. **So M has two quiet runs, not five** (`06`, `07`); `01`, `12`, `15` breached
the after-load check and are discarded (`12` visibly: frame 11.58, render thread 2.62). The discarded runs' per-pass CPU is
within 0.02 ms of 06/07 except `12`.

| run | cond | load before / after | frame ms | render thread draw | sum of passes, CPU | driver worker ms/frame |
|---|---|---|---|---|---|---|
| 02 | S 640x360 | 11 9 7 / 20 1 5 | 9.63 | 2.25 | 2.187 | 1.95 |
| 03 | L 1644x931 | 6 16 6 / 9 20 27 | 9.44 | 2.22 | 2.158 | 2.17 |
| 06 | M | 10 6 8 / 16 12 19 | 9.65 | 2.29 | 2.220 | 1.63 |
| 07 | M | 15 18 9 / 11 4 18 | 9.41 | 2.25 | 2.178 | 1.90 |
| 08 | L 1644x931 | 8 6 10 / 14 15 21 | 9.88 | 2.33 | 2.257 | 1.98 |
| 10 | S 640x360 | 12 15 10 / 8 16 21 | 9.80 | 2.31 | 2.244 | 1.92 |
| 14 | **A** (main, no instrument) | 10 7 9 / 10 11 6 | 9.55 | 2.21 | - | 1.83 |
| 16 | **A** (main) | 22 4 7 / 40 54 74 | 9.81 | 2.29 | - | 1.87 |

**Per pass, quiet M (06, 07), with S (02, 10) and L (03, 08) GPU beside; ms per frame; counts per frame.**

| pass | CPU M | GPU S | GPU M | GPU L | draws | maps | primitives (M) |
|---|---|---|---|---|---|---|---|
| OpaqueModels (props, players, detail models) | **1.063-1.080** | 0.867-0.872 | **0.828-0.846** | 0.838-0.869 | 209 | 421 | 227,000 |
| Occlusion (pixel-visibility proxies) | **0.363-0.376** | 0.057-0.060 | 0.053-0.055 | 0.052-0.060 | 376 | 752 | 752 |
| Sky3D (collation + sky room) | 0.222-0.225 | 0.050-0.051 | 0.083-0.084 | 0.113-0.116 | 19 | 36 | 7,300 |
| World | 0.149-0.159 | 0.225-0.231 | 0.355-0.361 | 0.469-0.486 | 378 | 379 | 16,100 |
| Hud | 0.085-0.086 | 0.026-0.028 | 0.030-0.031 | 0.034-0.036 | 72 | 73 | 920 |
| Particles | 0.081 | 0.058-0.060 | 0.066 | 0.075-0.078 | 17 | 34 | 2,460 |
| Present (CPU of the call) | 0.065-0.067 | - | - | - | 0 | 0 | 0 |
| Translucent (interleave) | 0.060-0.062 | 0.031-0.055 | 0.037-0.060 | 0.038 | 32 | 57 | 2,740 |
| Viewmodels | 0.043 | 0.048-0.051 | 0.075-0.076 | 0.098-0.103 | 3 | 9 | 9,150 |
| EntityDecals | 0.013-0.014 | 0.019 | 0.019-0.022 | 0.020-0.022 | 24 | 25 | 375 |
| Exposure | 0.011 | 0.018-0.019 | 0.037-0.038 | 0.056 | 1 | 1 | 1 |
| Sky2D | 0.009 | 0.013 | 0.022 | 0.030 | 5 | 6 | 10 |
| Bloom | 0.004-0.005 | 0.046-0.047 | 0.086-0.087 | 0.118-0.121 | 4 | 4 | 4 |
| Clear, Monitors, WaterViews, WaterIntersection, Overlay, DebugLines | under 0.01 together | | | | 0 | 0 | 0 |
| **all** | **2.178-2.220** | **1.50-1.53** | **1.75-1.76** | **1.98-2.04** | **1138-1142** | **1795-1801** | |

GPU "all" excludes the two frame-edge intervals, which are not work: `Clear` (1.29-1.35 ms at every size) is the GPU
waiting from the frame's first timestamp to the first command, and `Present` (9.4-10.5 ms, about the frame time) is the
interval to the next frame's commands. 0 frames skipped, 0 disjoint in every run (control d). VSync off (control e):
`Present` costs the render thread 0.065 ms, so there is no GPU back-pressure.

**Controls.** (a) The passes sum to 2.15-2.26 ms against `render thread draw` 2.21-2.33: 0.05-0.08 ms is outside `DrawFrame`
(the job wrapper). (b) L clamped, recorded. (c) Failed, E void, as above. (d) Met. (e) Met.

**Overhead A/B.** main 9.55 and 9.81 ms (render thread 2.21, 2.29) against the branch's quiet M 9.41 and 9.65 (2.25, 2.29),
and every branch run 9.33-9.88: inside the spread; the instrument costs nothing measurable even when on. Off (no
`--measure`) it is one bool read per frame. Only two A runs, one of them after-load-breached.

## Verdict

**The render thread's 2.2 ms is CPU submission, and half of it is one pass.** Per pass:

- **OpaqueModels is 1.07 ms of the 2.2 (49%)** for 209 draws and 421 buffer maps: **about 5.1 µs a draw**. The world draws
  378 surfaces for 0.15 ms, **0.4 µs a draw**, so the model path costs ~12x the world's per draw — two buffer maps a draw
  against the world's one is the visible difference (which buffers is not broken out; that the maps are the cost is
  interpolated, not measured). Its GPU time is **flat with pixels** (0.83-0.87 ms at
  S, M and L) with 227,000 primitives at every size: it is not pixel-bound. Whether it is vertex-bound or paced by the
  submission behind it, this set cannot tell (both are constant across sizes); a control would hold the draws and empty
  the vertices. **Unresolved, and it decides the GPU ceiling.**
- **Occlusion is 0.37 ms (17%)** for 376 one-quad draws with 752 maps — a vertex-buffer map and a camera map per proxy
  query, two queries per proxy. GPU 0.05 ms. Pure submission.
- **Sky3D is 0.22 ms (10%)** for 19 draws: the CPU there is collation (`CollateRenderables` is inside this interval), not
  the sky room.
- Everything else together is about 0.4 ms of CPU.
- **The GPU's own work is 1.50 (S) / 1.75 (M) / 2.0 (L) ms.** Only ~0.5 ms of it scales with pixels (World, Bloom,
  Exposure, Viewmodels, Sky3D, Sky2D); the flat 1.25 ms is mostly OpaqueModels' 0.85. **If that 0.85 is execution, the GPU
  alone is a 1.75 ms ceiling at 720p and the 1 ms frame is out of reach without cutting model GPU cost** — the prediction's
  "GPU under 1 ms" is falsified as stated, pending the vertex control.
- **The driver worker** spends 1.63-2.17 ms a frame on its own thread (finding 100's 1.75-2.2, reproduced). It runs beside
  the render thread, so it bounds the frame only if it exceeds it; at 1 ms it would.

**What would have to change for the draw to fit under 1 ms** (resolution scaling excluded, parity):

1. **The model path's per-draw cost**, ~1.07 -> ~0.15 ms if it reached the world's 0.4 µs a draw: the per-draw maps replaced
   by one per-frame constant buffer written once and bound by offset (D3D11.1 `VSSetConstantBuffers1`/`PSSetConstantBuffers1`
   on `ID3D11DeviceContext1`), or instancing for repeated models. The engine's own path batches by material in
   `CMatRenderContext`/the mesh builder; parity is not at stake in how constants reach the GPU.
2. **Occlusion proxies batched**: one vertex buffer for every proxy's corners, written once a frame, one draw per query
   with an offset, the camera bound once (~0.37 -> under 0.05 ms). **Parity check first**: 376 proxies a frame is a lot —
   confirm which renderables the engine gives a `PixelVisibility` handle (`CPixelVisibilityQuery`, `pixelvisibility.cpp`:
   glows, sprites, lens flares) and whether ours issues both the counting and the real query per proxy every frame, as
   `PixVis_Update` does, or more.
3. **Collation off the render thread or cheaper** (0.2 ms in Sky3D).
4. **Then the GPU**: run the vertex control on OpaqueModels; if it is execution, model GPU cost (skinning on the GPU per
   draw, overdraw) is the next ceiling.

Not supported by this set: any statement about the empty view (void), about 1920x1080 (clamped), or with five-run ranges
(two quiet M runs).
