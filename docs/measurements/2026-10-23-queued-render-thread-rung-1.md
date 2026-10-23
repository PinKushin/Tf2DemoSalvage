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
