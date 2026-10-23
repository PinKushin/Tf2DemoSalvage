# 98 — The queued material system: TF2 draws on a second thread, we draw on one

**The question.** The owner asked whether TF2's separate material-system render thread (`mat_queue_mode`) should be
made parity. The standing rule answers it (Valve parity first, D89/D129/D131: a divergence is a defect, fixed whatever
it costs), so this finding asks the questions under it: what exactly the engine does, what the equivalent is for our
Direct3D 11 renderer, how much it could buy, and what the owner would see change. **Engine read, feasibility and design
only; no product code changed.** The per-frame pipeline audit (finding 97) is separate; this covers only threading.

## (1) What the engine does

**Read from published source** (`F:/src/source-sdk-2013`). The material system's implementation
(`CMatQueuedRenderContext`, `CMaterialSystem::SetThreadMode`) is **not** in the SDK; only its interface is.

- **Three thread modes** (`public/materialsystem/imaterialsystem.h:225-230`):

  ```cpp
  enum MaterialThreadMode_t
  {
  	MATERIAL_SINGLE_THREADED,
  	MATERIAL_QUEUED_SINGLE_THREADED,
  	MATERIAL_QUEUED_THREADED
  };
  ```

  and three context types, one of them a queue (`:236-241`: `MATERIAL_HARDWARE_CONTEXT, MATERIAL_QUEUED_CONTEXT,
  MATERIAL_NULL_CONTEXT`). The switch is `SetThreadMode( MaterialThreadMode_t mode, int nServiceThread = -1 )` /
  `GetThreadMode()` / `IsRenderThreadSafe()` (`:684-686`), and `AllowThreading( bool bAllow, int nServiceThread )`
  (`:1118`).
- **A queued context exposes its call queue**, NULL when not queued (`:1543-1544`): *"Raw access to the call queue,
  which can be NULL if not in a queued mode"*, `virtual ICallQueue *GetCallQueue() = 0;`. Client code queues its own
  calls onto it, e.g. the debug marker (`:1906-1908`):

  ```cpp
  CMatRenderContextPtr pRenderContext( pMaterialSystem );
  if ( pRenderContext->GetCallQueue() ) 
  	pRenderContext->GetCallQueue()->QueueCall( Plat_DebugString, CUtlEnvelope<const char *>( psz ) );
  ```

  `CUtlEnvelope` copies the argument into the queue: **what crosses to the render thread is a copy, not a reference
  into game state.**
- **The escape hatch that forces serial**: *"Methods to force the material system into non-threaded, non-queued mode"*,
  `Lock()` / `Unlock( MaterialLock_t )` (`:1076-1079`).
- **What the client cannot do while queued** — synchronous read-backs:
  - `DumpTGAofRenderTarget` refuses: *"Error: mat_queue_mode must be 0 to dump debug rendertargets"*
    (`game/client/viewpostprocess.cpp:1479-1486`).
  - The replay movie writer reads pixels then asserts serial: `pRenderContext->ReadPixels( ... )` (`replay/replayrenderer.cpp:884`),
    `// can't run in any other mode` / `Assert( mat_queue_mode.GetInt() == 0 );` (`:911-912`).
  - Nav generation sets `mat_queue_mode` to 0 for its duration and back to -1 after (`game/server/nav_generate.cpp:3523-3524`,
    `:3987-3988`).
- **Game-side data the render thread reads is double-buffered by the client.** Ropes keep two memory stacks and swap
  per frame (`game/client/c_rope.cpp:178-206`, `CMemoryStack m_QueuedRopeMemory[2]`, *"Overflowed rope queued rendering
  memory stack"*), and lock their cache because *"Toggling from queued mode on to off"* contends (`:407`). Colour
  correction notes a state that *"doesn't work when in queued mode"* (`colorcorrectionmgr.cpp:100-103`).

**Read from the game's shipped data** (`F:\SteamLibrary\steamapps\common\Team Fortress 2`):

- `bin/x64/materialsystem.dll` registers the cvar with the help text *"The queue/thread mode the material system should
  use: -1=default, 0=synchronous single thread, 2=queued multithreaded"*, beside `mat_queue_report`: *"Report thread
  stalls. Positive number will filter by stalls >= time in ms. -1 reports all locks."*
- TF2's own `tf/cfg/low.cfg:153` and `ultra.cfg:177` both set `mat_queue_mode -1`. **The shipped default is -1 at both
  ends of the quality range.**

**Not verified** (closed code, `materialsystem.dll`): what -1 resolves to for N cores; when `EndFrame` waits on the
render thread; and how many frames may be in flight. Commonly reported as "-1 picks 2 on a multicore PC, one frame
queued", but that is *interpolated*, not read. A Ghidra pass on `materialsystem.dll` (`D:\ghidra-proj`, per
`docs/DECOMPILING.md`) settles all three, and the latency answer is the one the design depends on (section 5).

**What it means, in one paragraph.** On a PC TF2 records every material-system call of frame N into a queue on the
main thread and a second thread plays the queue into the shader API (Direct3D 9) while the main thread starts frame
N+1's simulation. Read-backs are not available in that mode; anything needing one switches to mode 0 first.

## (2) The divergence

**Our frame is serial on one thread.** `FrameSequence.Run` (`managed/Tf2DemoSalvage.Presentation/FrameSequence.cs:111-143`)
runs shot, simulate, camera, sound, project, `BuildOverlay` (`hud`) and `Draw` in order, and `Draw` ends in
`Device3D.DrawAndPresent` on the same thread. That is the engine's mode 0 only. **We have no mode 2**, and TF2 ships
with -1. Filed as **B581**.

## (3) The candidates, against our code

What the render side would have to give the game side the same frame (*read from our code*):

| call | blocks? | where | queued answer |
|---|---|---|---|
| screenshot (`--shot`, F12) | yes: `CopyResource` + `Map(Read)` | `Device3D.cs:534-537` | run after the render thread drains, like the engine's mode-0 requirement for `ReadPixels` |
| water texel / offscreen target read | yes: `Map(Read)` | `WorldRenderer.Water.cs:1223`, `OffscreenTarget.cs:849` | tests and diagnostics only; run serial |
| exposure histogram | no: `GetData(... Donotflush)` unless `Wait` | `ExposureHistogram.cs:199-204` | already a later-frame read, as Valve's queries are (finding 95) |
| occlusion queries | no: `GetData(..., 0)` | `OcclusionQueries.cs:215` | same |
| `--measure` | no device read; times phases | `ViewerSession.Frame.cs:436-507` | must name the render thread's time as its own column, and `draw` on the game thread as a wait |

**A — recorded command stream played by a render thread** (the engine's own shape: `ICallQueue`). The game thread
records Device3D's calls with copied arguments; a render thread plays them on the immediate context. Closest to Valve,
and mode 0 is "play the queue immediately on this thread". Blocker: Device3D's draw reads Scene/Presentation lists by
reference (instance lists, particle vertices, VGUI draw list), so every argument must be copied or the lists
double-buffered — the ropes' `[2]` stacks are the engine doing exactly that.

**B — D3D11 deferred context + `FinishCommandList`, executed on a render thread.** Recording still runs on the game
thread, so it moves only the immediate context's `ExecuteCommandList` and `Present`. Deferred contexts on most drivers
record in software and re-validate on execute, so the recording costs about what the draw costs now. **It moves the
wrong half**; rejected on the evidence of finding 92 (the frame is processor-bound, the GPU 6-10% busy).

**C — two-stage pipeline**: the game thread builds frame N+1's snapshot (instances, particles, overlay list, camera)
while the render thread runs today's `DrawAndPresent` over frame N's snapshot. Same latency and same blocker as A,
without a call-by-call recorder: the snapshot is the queue, at the granularity Device3D already consumes. **Shared
mutable state that would cross**: the light cache, model-decal ring and particle buffers are written in `camera`
(finding 93's "order is output" list) and read in `draw`; GPU resources uploaded mid-frame (map load, model upload,
`UploadCamera`); and Device3D's own per-frame state (exposure goal, query rings), which stays on the render thread.

## (4) How much it could buy

**Measured already** (no new run): steady f12 frame 13.4 ms; `draw` about 2.1-2.8 ms and `hud` 1.0-1.1 ms
(findings 93/94, `measurements/2026-10-22-tf2-vs-viewer-f12.md`); GPU 6-10% busy (finding 92), so `draw` is processor
time in our submission and the driver, not a GPU wait.

**`hud` does not move.** `BuildOverlay` is VGUI layout and paint into a list; in the engine that runs on the main
thread too, and only its surface calls are queued. So the movable part is `draw` alone, not the "up to 3 ms" of
findings 93/94.

**No instrumented run was taken.** The one number missing — how much of `draw` is `Present` and driver time that would
also move, against Device3D's own CPU work — needs a timer inside `DrawAndPresent`, which is product code, and this
task changes none. It is step 1 of the plan.

**Estimate** (*arithmetic, not measured*): with a render thread the frame is max(game, render) plus the snapshot. The
ceiling is 2.8 ms (13.4 → 10.6 ms, 75 → 94 fps). Finding 94's estimate came in seven times high, so assume the
snapshot copy and hand-off eat half: **about 1-1.5 ms**, or 8-11% of the frame. It is still the largest threading lead
left (finding 94 section 5), and the TF2 gap (13.4 against about 4.8 ms) is mostly `camera`, which this does not touch.

**What would verify it**: a throwaway prototype that keeps everything serial except handing `DrawAndPresent` frame
N's snapshot on a second thread, measured ABBA against main with `--measure` on f12 at tick 30000 (the 2026-10-22
method).

## (5) What the owner can see

**One frame of latency, if the engine has one.** In mode 2 the picture on screen is the frame before the one the
game thread is simulating. In a demo viewer that shows as free-camera and mouse-look response one frame later
(about 10 ms at 94 fps), and nothing else: playback content is identical. **If the Ghidra read shows TF2 queues one
frame, matching it is parity, and it is the point that needs the owner's say** before it ships as the default.
Screenshots and `--shot` are unaffected: they drain the queue first, as the engine requires mode 0 for a read-back.

## (6) Recommended design and plan

**Recommendation: C, a two-stage pipeline over a frame snapshot, with a thread-mode switch mirroring
`mat_queue_mode`** (`0` serial, today's path, kept forever; `2` queued; `-1` whatever the Ghidra read says -1 picks).
It is the engine's own structure (record on the main thread, play on a second, copy what crosses) at the granularity
our renderer already has. A is the fallback only if the snapshot proves unbuildable.

1. **Ghidra read of `materialsystem.dll`**: -1's resolution, `EndFrame`'s wait, frames in flight. Quote into this finding.
2. **Measure** `Present` versus submission inside `draw` (a throwaway timer, recorded under `docs/measurements/`, record
   written before the run). If submission is under 1 ms, stop and file the result.
3. **Conformance test first**, in `Core.Tests`/`Presentation.Tests`: a thread-mode type whose values and help text
   cite `materialsystem.dll`'s string and `imaterialsystem.h:225-230`; mode 0 runs every stage on the calling thread
   (asserted by thread id); a read-back in mode 2 drains first (`viewpostprocess.cpp:1479-1486`).
4. **Unit tests**: the snapshot is immutable once handed off (mutating the next frame's lists does not change the
   frame being drawn); a seek or map load drains the render thread before tearing down resources.
5. **Frame-hash equality**: a `SyntheticDemo` scene and, at output level, f12 from tick 30000 for 200 frames, hashing
   each frame's draw input and the presented image, **serial against queued at every mode**; every frame must match,
   with the queued run's frame N compared with the serial run's frame N (latency shifts display, not content).
6. **Sabotage plan** (sabotage-verifier): (a) hand the render thread the live lists instead of the snapshot — the
   hash test must redden; (b) let a seek skip the drain — the teardown test must redden; (c) run mode 0 on the worker
   — the conformance test's thread-id assertion must redden.
7. **Measure after**, ABBA on f12, with the render thread's time reported in its own column and the game thread's
   `draw` named as the hand-off wait.

## (7) Follow-up, 2026-10-22: the prototype stopped it at the checkpoint

**Measured** (`docs/measurements/2026-10-22-render-thread-prototype.md`): `Draw` on a worker joined at the next frame's
start, the only safe join point in today's code, saves only the gap between frames (~0.5 ms). The game thread waits on
the worker for 1.9-2.1 ms of a 2.2-2.8 ms draw. Joining later, so that simulate, camera and hud overlap the draw,
crashed in the first second on a shared list. Finding 98's blocker is real: **any saving above ~0.5 ms needs the
snapshot first, and what the snapshot copy costs is still unmeasured.** Steps 3-7 were not started. The prototype's
code (a throwaway `TF2VIEW_PROTO_RENDER_THREAD` switch in `FrameSequence.cs`) is NOT on main; it is on the branch
`feat/queued-material-system` and should be removed before anything from it merges.

**Step 1 (the Ghidra read) was not done.** The session's Ghidra MCP bridge connected to `tf2materialsystem` but
registered its 214 analysis tools after this agent's tool list was fixed, so no read tool was callable. The
headless-script and HTTP routes are closed by the binary-reads hook. Three things are still interpolated: what -1
resolves to, when `EndFrame` waits, and how many frames are in flight.

## (8) Follow-up, 2026-10-22: the snapshot's cost (D228 step A)

**Measured** (`docs/measurements/2026-10-22-frame-snapshot-cost.md`): copying everything the draw reads costs
1.19-1.38 ms a frame (3.4 MB), and 0.80-0.87 ms of that is the face-flex streams. That is half the possible saving or
more, so the full-copy design stopped at checkpoint 1. If the flex and particle buffers were double-buffered instead, the
copy left would be 0.19-0.32 ms. The cull inside `SetCamera` stays on the game thread; its outputs are state to
double-buffer. The owner decides whether to build that shape.
