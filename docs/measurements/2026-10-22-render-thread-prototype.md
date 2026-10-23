# Measurement 2026-10-22: a throwaway render-thread prototype (B581, finding 98 step 2)

## Record (written and committed before the first run)

**Observation.** Finding 98 estimates a queued render thread would save about 1-1.5 ms of a 13.4 ms f12 frame
(arithmetic, not measured): `draw` is about 2.1-2.8 ms of processor time and is the only stage that would move.

**Question.** On f12 from tick 30000 first person, how much frame time does running `Draw` on a second thread
save, (a) at the only join point that is safe in today's code, and (b) at an unsafe ceiling?

**Method.** `FrameSequence.Run` behind `TF2VIEW_PROTO_RENDER_THREAD` (throwaway; nothing else changed):

- `end` (control): `Draw` is handed to a worker and joined at the end of the same frame. Same work as serial plus
  the hand-off; it must read the same as serial within noise, or the hand-off itself costs something.
- `start` (safe): joined at the start of the NEXT frame. Overlaps only the time between frames (message pump, idle).
  Safe, because every stage of the next frame runs after the join; this is a lower bound.
- `racy` (ceiling, UNSAFE): joined just before the next frame's `Draw`, so shot, simulate, camera, sound, project and
  hud all overlap the previous draw while sharing its lists and the immediate context. It may crash or draw garbage;
  if it runs, its frame time is the most a render thread could save without a snapshot cost.

Runs, each `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 [TF2VIEW_PROTO_RENDER_THREAD=<mode>] pwsh
C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000
--first-person --measure 20 +fps_max 0`, order A B B A for serial (A) against each prototype mode (B). Machine load
read before each run with `Get-CimInstance Win32_Processor | Select LoadPercentage`.

**Control.** The `end` mode (identical work, worker thread) and the ABBA order (drift shows as A1 against A2).
Ticks advanced should be about 66.7 x 20.

**Verdict rule (from the task).** A measured saving under about 0.5 ms, or a game thread blocked on the render
thread most of the time, stops the full design.

## Results

**Deviation from the record, stated.** A sibling agent's run-exclusive hold (60 minutes, measuring TF2 and the viewer)
overlapped the first serial run, which read 14.65 ms at load 76%; it is discarded. The rest ran 19:48-20:20 after the
hold ended, in the order below rather than a strict A B B A per mode, because the run-to-run spread turned out to be
larger than the effect and the deciding number is not the frame mean (below). Same command for every run; no
`--spectate gummo`, so these are not comparable with the 2026-10-22 TF2 pair, only with each other.

| # | mode | load % | frames | mean ms | phase sum ms | frame - phases ms | draw ms |
|---|---|---|---|---|---|---|---|
| 1 | racy | 27 | - | crashed: `ArgumentOutOfRangeException` (index out of range), exit 0xE0434352 | | | |
| 2 | serial | 96 | 919 | 21.77 | | | 3.09 |
| 3 | start | 45 | 1291 | 15.49 | 13.37 | **2.12** | 0.01 |
| 4 | start | 36 | 1522 | 13.14 | 11.26 | **1.88** | 0.01 |
| 5 | serial | 23 | 1164 | 17.18 | 17.18 | 0.00 | 2.73 |
| 6 | end | 21 | 1148 | 17.42 | 17.42 | 0.00 | 2.83 |
| 7 | end | 35 | 1411 | 14.18 | 14.18 | 0.00 | 2.28 |
| 8 | serial | 20 | 1465 | 13.65 | 13.66 | 0.00 | 2.17 |

Run 2 is at 96% load and is not used. Ticks advanced 1250-1345 against the expected ~1334: playback windows are full.

**The deciding number is `frame - phases`.** In `start` mode the join sits before the first phase, so the time the
game thread spends waiting for the previous frame's draw is the only time in the frame no phase owns. It is
1.88-2.12 ms against a serial `draw` of 2.17-2.83 ms: **the game thread waits for the render thread for about 80-90% of
the draw**, and the overlap actually won is the inter-frame gap (`between`, 0.46-0.62 ms) and no more, which is what
the `start` mode's design predicted. The controls hold: serial and `end` have zero unaccounted time, so the phase
timers cover the whole frame and the hand-off itself costs nothing measurable.

The prototype's own `worker draw` / `join wait` counters were not windowed to the measured interval (they count every
frame since launch, including ~580,000 near-empty loading frames), so they read 0.2 ms and are **not** used; the
`frame - phases` residual is carried from the same report the means come from.

**The ceiling could not be measured.** `racy` overlaps the next frame's simulate, camera and hud with the previous
draw, and crashed in the first second on a shared list, which is exactly finding 98's blocker: every list `draw`
reads is written by the next frame's stages. So in today's code a render thread can overlap only the gap between
frames, and any saving above ~0.5 ms exists only after the snapshot of finding 98's design C, whose copy cost is
unmeasured.

## Verdict

**The checkpoint stops it.** Measured saving at the only safe join point: at most the inter-frame gap, about 0.5 ms,
with the game thread blocked on the render thread for ~2 ms of every frame. Both of the task's stop conditions hold.
The frame means themselves cannot separate the modes: same-mode runs differ by up to 3.3 ms (13.14 against 15.49), more
than the whole of `draw`.
