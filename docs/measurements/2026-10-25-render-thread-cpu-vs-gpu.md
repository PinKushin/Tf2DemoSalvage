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

## Result

## Verdict
