# Measurement 2026-10-23: `mat_queue_mode 2`, rung 1 (B581, D228 checkpoint)

## Record (written and committed before the first run)

**Observation.** Branch `feat/queued-render-thread` at `46275fc9` draws the frame on a render thread in mode 2 (`QueuedRenderThread`,
one thread, one frame in flight). The previous frame's draw is waited for before `PlaceCamera`, the first stage that writes
what the draw reads; only the gap between frames (0.46-0.62 ms, `2026-10-22-render-thread-prototype.md`), the shot stage
(about 0) and `Simulate` (about 1.1 ms, finding 101) overlap a draw of 2.1-2.8 ms. That is a divergence from the engine's
`EndFrame`, which waits only at the hand-off (finding 99), and it is chosen because the cull, particles and pose are not yet
double-buffered. The task's flex-first double-buffer was not built: flex is written by the pose, which runs after the camera,
so with the drain before the camera nothing on the overlapped path writes it.

**Question.** On f12 from tick 30000 first person, (1) does the queued frame draw exactly the inputs it was handed (no
game-thread stage rewrites them while it draws)? (2) How much frame time does mode 2 save against mode 0, on the same binary?
(3) Does `draw` itself get slower on the render thread (the snapshot record's unexplained slowdown, now without any copy)?

**Prediction** (*arithmetic*, before the runs): game-thread wait (`queue wait FinishPreviousDraw`) about 0.5-1.2 ms; frame
saving about 1.0-1.6 ms. Below 0.8 ms the task's checkpoint stops further work.

**Method.** Release build of `46275fc9`. Validation run V first: `TF2VIEW_QUEUE_VERIFY=1`, mode 2; the device hashes every
model instance's matrix, bones, flex and baked colours, the local lights, the VGUI quads, the particle corners, the leaf
lines and the screen overlay at the hand-off on the game thread and again on the render thread after `DrawFrame`, and logs
`queue verify:` on a mismatch. Then timed runs, verifier off, order A B B A (A = `+mat_queue_mode 0`, B = `+mat_queue_mode 2`),
each:

`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1
managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe D:/measure-f12/f12.dem --tick 30000
--first-person --measure 20 +fps_max 0 +mat_queue_mode <0|2>`

Machine load read before each run with `Get-CimInstance Win32_Processor | Select LoadPercentage`. Reported per run: frames,
mean ms, the phase sum and `frame - phases` (the prototype's residual), `draw` (the game thread's share: the wait plus the
hand-off), `queue render thread` (the render thread's own drawing time, carried from the queue, B243), and every
`queue wait <caller>` part.

**Control.** (a) Mode 0 must print no `queue` part and a `frame - phases` of 0.00, as serial did in the prototype: the new
wait is timed inside `draw`, so the phase timers still cover the frame. (b) In mode 2 `queue render thread` must be about the
size of A's `draw` (2-3 ms); near zero means the draw did not go to the thread. (c) Validation run V must report checked
frames in the hundreds (`queue verify` counts) — a verifier that checked nothing proves nothing. The decision rests on the
phase columns and the residual, not the frame mean (same-mode spread up to 3.3 ms in the prototype).

**Verdict rule.** Mode 2 saves less than ~0.8 ms against mode 0 → stop and report with numbers; read the wait ledger first
(an early `Lock` on the overlapped path collapses the overlap and is fixable).

## Equivalence (added 2026-10-23, after the timing runs, at the coordinator's request)

**Method.** `TF2VIEW_FRAME_HASH=1` and `TF2VIEW_FIXED_FRAME=0.015`, so playback advances one tick a frame and both modes
show the same ticks. The device logs, for every presented frame, the tick the game thread handed with it and four FNV-1a
hashes. Three cover what the draw read at the hand-off:

- **pose**: every model, viewmodel and monitor-only instance's path, entity, frame, body and alpha, plus its matrix, bones,
  flex and baked colours, and the viewmodel camera;
- **particles**: every particle corner;
- **rest**: the leaf lines, the VGUI quad and model-draw counts, and the screen overlay.

The fourth, **image**, is the back buffer read back after the frame's own Present.

Same f12 command as above, `--measure 6`, four runs: A1 A2 in mode 0, B1 B2 in mode 2. Loads read beforehand were 7-26%.
Each tick ≥ 30000 is compared once, by its first frame.

**The first attempt measured nothing, and is kept as the wrong turn.** It hashed with `HashCode`, which .NET seeds per
process, so no value could match across runs and two mode-0 runs agreed on 0 frames. That was the instrument, not the
viewer. The in-process queue verifier above is unaffected, because it compares two hashes from one process.

| pair | frames | pose equal | particles equal | rest equal | image equal |
|---|---|---|---|---|---|
| A1 vs A2 (mode 0 floor) | 183 | 183 | 171 | 169 | 0 |
| B1 vs B2 (mode 2 floor) | 197 | 197 | 184 | 187 | 0 |
| A1 vs B1 | 188 | 188 | 177 | 160 | 0 |
| A1 vs B2 | 188 | 188 | 176 | 161 | 0 |
| A2 vs B1 | 183 | 183 | 171 | 162 | 0 |
| A2 vs B2 | 183 | 183 | 173 | 163 | 0 |

**What this shows, and what it cannot.**

- **The pose is deterministic run to run, and mode 2 equals mode 0 on every compared frame (183-188 of them).** That
  covers every model, viewmodel and monitor-only instance at a frame's tick: matrices, bones, flex, baked colours and the
  viewmodel camera. It is the input the render thread could have corrupted, and it is bit-identical.
- **Particles are not reproducible even in mode 0** (171/183). Mode 2 sits inside that floor (171-177 of 183-188), so
  nothing here distinguishes the modes.
- **`rest` differs a little more across modes than within one:** 160-163 equal across modes against 169-187 within one. By
  elimination this is the VGUI quad count: the leaf lines are off and the overlay is fixed. Mode 2 runs at a different frame
  time, and frame-time-driven HUD content is a candidate cause. That is *not established*: the counts were not split
  further.
- **Image equivalence cannot be shown on this build.** Two mode-0 runs never produce the same pixels on any frame (0 of
  183), which matches the floor agent's finding. Comparing mode 2 against that floor is meaningless.
- The 200-frame target was 183-197 compared frames, because `--measure 6` reaches about 190 played ticks past 30000.
- Not done: a synthetic-scene image variant. The synthetic evidence is `FrameSequenceTests.Run_InModeTwo_*`, which covers
  ordering, not pixels.

## Results

**Deviations from the record, stated.** Binary: `6a73f54f` (the record's `46275fc9` plus a verifier log line only). Two other
agents measured on the machine during the session. The first A1/B1 pair (13.48 / 12.71 ms) ran at 65-70% load and is
discarded. One B2 attempt is discarded at 14.49 ms: two seconds held 14-17 ms waits and every phase, including the camera,
was slower, which is the machine. Another B2 attempt exited 1 with no output. While investigating it, this agent stopped a
tf2demoview process by PID believing it was its own orphan. It was probably another agent's run; that is reported, not
hidden. "Load before" was sampled before the lock wait, so it describes the queue, not the run. A2 was sampled at 93-100%
while another run held the lock. Its numbers match A1 within 0.15 ms and it is kept, flagged.

**Validation run V** (mode 2, `TF2VIEW_QUEUE_VERIFY=1`, load 37%). The log read `queue verify: 715000 frames checked, 0
changed` by the end. That count includes the loading screen's frames. During playback the count rose about 500 every 6 s,
which is every played frame. **No queued frame's inputs changed while it was drawn.** The only lock that waited in any mode-2
run was `FinishPreviousDraw`: nothing on the overlapped path touched the device.

| # | mode | load before % | frames | mean ms | draw (game thread) | between | camera | advance | frame - phases |
|---|---|---|---|---|---|---|---|---|---|
| A1 | 0 | 27-45 | 1464 | 13.66 | 2.15 | 0.45 | 8.64 | 1.21 | 0.00 |
| B1 | 2 | 18-45 | 1677 | 11.93 | 0.73 | 0.43 | 8.24 | 1.33 | 0.00 |
| B2 | 2 | 29-57 | 1729 | 11.57 | 0.66 | 0.40 | 8.08 | 1.29 | 0.00 |
| A2 | 0 | 93-100 (flagged) | 1481 | 13.51 | 2.15 | 0.52 | 8.45 | 1.20 | 0.00 |

`frame - phases`: each row's phases plus `between` sum to its mean within 0.01 ms. Mode 0 prints no `queue` part (control a).

Mode 2 per-second parts:

- B1: `queue render thread` 1.26-7.48 ms, with 2.83 the unweighted mean of the seconds; `queue wait FinishPreviousDraw` 0.02-5.56, with 1.05 the unweighted mean.
- B2: render thread 1.20-7.21 ms (2.62); wait 0.04-4.84 ms (0.94).

These per-second means are unweighted by frames, so they are not directly comparable with the frame-weighted `draw` of mode 0.

**Results.**

- **Mode 2 saves 1.73-1.94 ms a frame on f12**: 11.57-11.93 ms against 13.51-13.66, about 13%, or 74 → 85 fps. That is at the
  top of the 1.0-1.6 ms prediction. **The checkpoint passes.**
- The game thread's `draw` falls from 2.15 ms to 0.66-0.73, which is the wait plus the hand-off.
- `camera` read 0.2-0.6 ms lower in mode 2 than in mode 0. This is observed, not explained. A candidate, untested: the camera
  no longer runs straight after a draw that evicted its cache.
- **Is the draw slower on the render thread? Not settled.** The render thread's per-second mean (2.6-2.8, unweighted)
  is above serial's frame-weighted 2.15. The two are different averages, and the overlap of the first seconds, which are
  heavier, inflates the first. A frame-weighted render-thread column is needed to answer it. The snapshot record's
  0.9 ms slowdown, with a 3.4 MB copy, is not present at that size: the frame saving exceeds the whole serial draw
  minus the remaining wait.
