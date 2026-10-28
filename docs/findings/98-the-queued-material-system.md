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

## (9) Follow-up, 2026-10-23: rung 1, no snapshot at all, saves 1.7-1.9 ms

**The wrong turn, kept.** The plan ranked the double-buffers by copy cost and put the flex streams first. That ranking
assumed the engine's wait at `EndFrame`. Read against the code, the flex streams are written by the pose in `ProjectWorld`,
after the camera. So with the wait placed before the camera, *nothing on the overlapped path writes anything the draw reads*.
No buffer is double-buffered or copied in rung 1. The task's order would have built the most expensive part first for a
stage that does not yet overlap. (*Read from our code*: `MomentPresenter.Show` only selects, `MomentPresenter.PoseNow` poses
after the camera, and `EntityModels.AdvanceCorpses` is called only from the pose. A seek's replays DO pose, so they drain
first.)

**Measured** (`measurements/2026-10-23-queued-render-thread-rung-1.md`, ABBA, same binary):

- Mode 2 runs at 11.57-11.93 ms against 13.51-13.66 for mode 0, on f12 first person.
- The game thread's `draw` drops to 0.66-0.73 ms.
- The verifier hashes every queued frame's inputs at the hand-off and again after the draw: 0 mismatches.
- The overlap is the gap between frames plus `Simulate`. That is more than the prototype's 0.5 ms, because the wait moved
  from the start of the frame to before the camera, past the simulation.

**What the owner would see at mode 2:** nothing but the frame rate. Screenshots and `--shot` drain first. While the opening
countdown runs, a pending capture's settle test reads the device each frame, so a queued capture waits on the draw, as the
engine's read-backs need mode 0. Exclusive full screen draws serially. There is no held frame: frame N is presented as soon
as its draw finishes, as in the engine (finding 99 section 4). It shipped off (default 0) in beta.49; the default is TF2's
-1 since D229.

**Review fixes, 2026-10-23** (an independent thread-safety review of rung 1; each verified in the code before it was fixed):

- *Where a render-thread failure surfaces.* `Lock` used to rethrow it, so it came out of whichever drain ran first (a
  setter, `ClearMap`, the occlusion read) with a stack that blamed that caller. Now `Lock` only drains, and the failure
  is rethrown by `FinishPreviousDraw` and by `Submit`, the draw's own point, as in mode 0. Test:
  `Lock_AfterADrawThrew_DrainsWithoutThrowingAndTheNextSubmitRethrows`. Sabotage (rethrow put back in `Lock`) reddened it.
- *Teardown after a failed frame.* `Device3D.Dispose` began with `Queue.Dispose()`, whose drain rethrew, so leaving
  exclusive full screen, `ClearState`/`Flush` and every release were skipped. The queue's `Dispose` now drains without
  throwing and `Device3D.Dispose` logs `TakeFailure()`. Test: `Dispose_AfterADrawThrew_DoesNotThrowAndKeepsTheFailure`
  (queue level; the `Device3D` half has no test device and is read, not tested). Sabotage (rethrowing drain) reddened it.
- *Mode 0's cost.* `DrawScene` built two delegates and two display classes every frame whatever the mode. Mode 0 (and
  no `TF2VIEW_FRAME_HASH`) now calls `FinishPreviousDraw` and `DrawFrame` directly; the lambdas moved to a separate
  `SubmitScene`, since a display class for captured parameters is allocated at method entry and an early return in the
  same method would not have saved it. **Not tested**: no test can construct a `Device3D` (it needs a window), so the
  zero-allocation claim rests on that structure (no capture remains in `DrawScene`), not on a measurement.
- *DXGI's own Alt+Enter.* The swap chain allows mode switches and nothing called `MakeWindowAssociation`, so Alt+Enter
  made DXGI switch full screen inside the next `Present`: on the render thread in mode 2 while the window thread waited
  (a hang), and in mode 0 leaving `IsExclusiveFullScreen` stale. `Device3D.Create` now passes `DXGI_MWA_NO_ALT_ENTER`
  to the swap chain's parent factory. **Alt+Enter now does nothing**; the viewer has no Alt+Enter binding (no hardcoded
  controls), and its own full-screen toggle, which drains through `SetExclusiveFullScreen`, is unchanged.
  `DXGI_MWA_NO_WINDOW_CHANGES` was NOT set, against the review's suggestion: it stops DXGI giving the display back when
  exclusive full screen loses focus, a mode-0 change. So a focus loss can still leave `IsExclusiveFullScreen` stale, as
  before. **Not tested**: DXGI cannot read the flags back, and no test owns a window.

**Rung 2, first step, 2026-10-23 (branch `feat/queued-render-thread-rung-2b`).** Rung 2 moves the drain from before
`PlaceCamera` to the pose's entry, so `SetCamera` (0.7-1.8 ms, measurements/2026-10-23-queued-render-thread-rung-1.md)
overlaps the frame in flight. That does not fit one
session. This step builds the data structure and leaves the drain where it was.

*Done:*

- `ViewHalf` (`managed/Tf2DemoSalvage.Render/ViewHalf.cs`). Each half holds its own `WorldCulling` (via the new
  `WorldCulling.Twin()`), its own `OverlayRenderLists`, its own `CulledFor`, and its own runs: visible, translucent
  leaves, overlays and sky. `SetCamera` flips the half at entry and culls into it. A half that skips installs its own
  lists into the renderer.
- The cull now also reruns when the *previous* frame saw a different view. Two wrong turns got here:
  - *First:* each half kept its own copy of the sky and water decision and restored it on a skip.
  - *Then:* sabotage showed that restore was unobservable. Restoring the *other* half gave a cull part identical to
    baseline on all 188 compared ticks. It turned out the restore only matters when a view is revisited two frames
    apart (A, B, A).
  - *Now:* the extra condition makes every skip follow a frame of the same view, so the device's single sky, water and
    fog state is already right for it. A still camera still culls once per half, then skips.
- Invalidation clears both halves:
  - `SetWorldCulling`: runs dropped, culling and twin set.
  - `UploadWorldGeometry`: one overlay queue per half.
  - The `Draw3dSky` setter: `CulledFor` dropped.
  - B426, the material upload (`UploadWorld`, `UploadWorldTextures`): `DropMaterialRuns` drops the translucent leaves
    and overlays. These mirror what `WorldRenderer.ReleaseTextures` nulls in the renderer.
- Tests:
  - `Twin_OfASkyMap_CullsAsTheOriginalDoes` and `Twin_CullingNothing_LeavesTheOriginalsRunsStanding`. Sabotage
    (`Twin` returning `this`) reddened the second.
  - `UploadCameraOver_InModeTwoWithAFrameInFlight_OverlapsSetCameraButNotThePose`, the pending rung-2 contract.
    It is `[Explicit]` and red today for the predicted reason: the device read `SetCamera in flight True` … `posed in
    flight True`, because nothing drains inside `UploadCameraOver` yet.

*Equivalence* (`TF2VIEW_FRAME_HASH=1`, `TF2VIEW_FIXED_FRAME=0.015`, f12 from tick 30000, `--measure 6`; each tick
compared once by its first frame):

- Baseline, at `d31fbe20`:
  - two mode-0 runs agreed on cull and pose at 187/187 ticks;
  - mode 2 agreed with each of them at 183/183.
- After the change, cull equal on every compared tick:
  - fin-A1 vs fin-A2: 197/197;
  - baseline vs new mode 0: 188/188 and 187/187;
  - new mode 0 vs new mode 2: 200/200;
  - baseline mode 0 vs new mode 2: 188/188.
- The pose part diverges over a late run of ticks in some runs, for example from 30167 to the end of one run. It does
  this on the baseline binary too (two more `301facbb` runs diverge from 30179 and from 30181). So it is pre-existing
  and timing-dependent, and *not* this change. Rung 1's 183-188/183-188 pose equality happened to end before it.

**The serial cull part cannot tell one half from the other.** While the drain stays before `PlaceCamera`, both halves
hold the same contents for the same view. So a wrong half selection draws identical pixels and an identical cull part.
The twin's unit test is the only control that reddens until frames overlap.

*Found, pre-existing:* the queue verifier reports **1 changed** frame in about 805,000 on f12 mode 2. It shows up on two
runs of this branch and on one run of `301facbb`, the commit before `ViewHalf`. In all three the after-draw hash was
`797b7875ed158fa` and the change came at a "model set grew" upload stall. Rung 1's 715,000-frame 0 predates the cull
part (`d31fbe20`), so the likeliest reading is that the cull part exposed a game-thread write the earlier parts did not
cover. *Interpolated*: the part that changed was not split out.

**Settled, 2026-10-23 (branch `feat/queued-render-thread-rung-2c`): the changed frame was the instrument, not a race.**
The wrong turns, kept, because each looked like a write:

- *Split by part* (the verifier now logs pose, particles, rest and cull separately, and the cull's seven pieces): only
  the cull changed, always on the first played frame (tick 6 of the opening state, label `6.6667`), never the pose. The
  "model set grew" stall beside it is `UploadModels`' `Lock` waiting for that frame's draw, not a writer.
- *A monitor pass rewriting the main list* (the aliasing below): no monitor pass ran on f12.
- *Another thread culling*: a stack stamp on `SetCamera`, `WorldCulling.Batches` and `SkyRunsFrom` saw thread 2 only;
  nothing was in flight at the hand-off.
- *Then the control that decided it*: the hand-off copied the world and overlay lists, compared the copy element by
  element with the live list (equal) and hashed both twice. The copy hashed to one value on the first call and another
  on every later call, on both threads, the same pattern every run. Same content, two hashes.

The batches were hashed as a `ValueTuple<int, int, int>`'s raw bytes. `ValueTuple` is auto-layout, and its bytes are
not its fields': `StableHashTests.Batches_TwoRuns_HashToTheFnvOfTheirCountAndFieldBytes` predicted the exact FNV over
the fields and was red on the tuple version. *Not established*: why the bytes vary between calls (padding is the
reading, `docs/memory/padding-is-not-zero.md`; tiered compilation off did not stop it). The hash moved to
`StableHash.cs` and writes every field on its own, the pose tuple and the leaf lines included. *Measured* after the fix:
`queue verify: 784500 frames checked, 0 changed` on f12, mode 2, the opening frame included.

**Consequence for every earlier hash comparison**: the pose and rest parts hashed tuples the same way, so the pose
part's "pre-existing, timing-dependent" late divergence above may be this instrument too. Every hash value from before
this commit is incomparable with one after it.

*Re-measured* at `058feadd` (step 0 included), two mode-0 runs, `TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015`, f12
from 30000, `--measure 6`, loads 5-8%, first frame per tick, 191 common ticks: **cull 191/191, pose 189/191,
particles 180/191, rest 4/191.**

- The pose no longer diverges to the end of the run: it differs at 30168 and 30169 only and agrees after. So the
  persistent half of the old late divergence was the tuple hash. A two-tick difference at 30168 remains: real,
  run-to-run in mode 0, cause not looked for.
- Rest differs almost everywhere in mode 0, as rung 1 found (the VGUI quad count rides with the frame rate); it was
  never a control.
- **These two runs are the step-2 baseline**: compare step 2's mode-0 runs against them on cull and pose, with this
  pair's 189/191 as the floor.

**Not covered: monitor frames, and the halves change them.** `DrawMonitorPass` caches
`_monitorCull = (pointCamera, Culling?.Batches(...))`, which holds the culling's reused main list.

- *Before the halves*, the main runs and the monitor's runs shared that one list. The next main cull overwrote it, so
  the monitor drew the main view's runs. That is a pre-existing aliasing bug.
- *Now* the cached reference points into one half. On that half's frames the monitor draws this frame's runs; on the
  other half's frames it draws the previous frame's.

Neither instrument sees this. The cull part is taken before the monitor pass, and the image hash matches on no frames.
The f12 logs mention no monitor at all, so the equivalence above most likely covers zero monitor frames. *Not
measured*: no `MonitorCamera` feed was checked for.

The fix is a third `Twin()` for the monitor, and rung 2 needs it anyway. It changes mode-0 output on monitor frames,
away from the buggy baseline, so it is the lead's call. It is step 0 below.

**Step 0 done, 2026-10-23 (decided: yes, it fixes a divergence).** `MonitorCull` (`managed/Tf2DemoSalvage.Render/
MonitorCull.cs`) holds the monitor's own `Twin()`, keyed on the camera as the old cache was, and is rebuilt by
`SetWorldCulling`. **What mode 0 now draws differently, on monitor maps only: the monitor shows its own camera's
world.** *Measured* on `b511_boardwalk_mirror.dem` at tick 1900, the same `--shot` before and after: the three
mirrors showed the soldier against black, because they drew the main view's runs, culled from the player looking at
the stage, which hold nothing behind him; after, they show the room's back wall behind him, as the engine's
`ViewDrawScene` from the camera does. The soldier, which the `MonitorCaptureUiTests` region measures, is unchanged.
Tests: `MonitorCullTests` (four). Sabotage (`MonitorCull` culling through the main view's culling instead of a twin)
reddened `Batches_AfterTheMainViewCullsSomethingElse_StillHoldTheMonitorsOwnRuns` and
`Batches_OnTheMonitorsFirstCull_LeaveTheMainViewsRunsStanding`. *Read, not tested*: the `Device3D` wiring, which no
test can construct. The old cull also rewrote the main culling's `VisibleByLeaf`, which the entity cull reads; the twin
ends that too.

*Left, in order:*

0. ~~Give the monitor cull its own `WorldCulling.Twin()`; log the parts separately on a verifier mismatch.~~ Done
   (above): the changed frame was the hash, and the monitor has `MonitorCull`. Steps 1-7 are untouched as of
   `058feadd`; with step 0 in, `DrawMonitorPass` is no longer on step 3's list of `Culling` readers (it reads
   `_monitorCull`, which only `SetWorldCulling` replaces), but it still reads `_monitorView` and the main
   `world.VisibleBatches` it swaps back, which step 1 moves into the half.
1. (Done, below.) Move into the half everything the gate still writes once:
   - `_skyVisible`, `_eye`, `_placed`, `_skyFrustum`, `_skyEye` and `_skyCamera`;
   - `_waterDraw` (a closure reading `_eye` and `_skybox` while it draws), `_waterMainFog` and `_simpleViewDraws`;
   - `_frustum`, `_screenView`, `_translucentEye`/`Forward`, `_monitorView` and `_worldCamera`;
   - the detail sprites: `_detailBuiltFor`, the instances, `_detailCorners` and `_detailLeaves`.
2. Record the immediate-context uploads into the half and replay them at the start of the draw:
   - `_world.SetCamera` from `SetCamera(float[])`;
   - `ReapplyCamera`, called by `PlanWater` and by the Specular and Phong setters;
   - `_detailSprites.Upload`.
3. Split the readers of `Half`/`Culling` into game-thread readers and draw-time readers. Each draw-time reader must
   read the half its frame installed, because `_cullHalf` flips at the next `SetCamera`. Known draw-time readers:
   - `DrawMonitorPass`. Its `Culling.Batches` call reuses the culling's main list, as it did before the halves;
   - `DrawDetailStep` and `HasDetailAtPlace`;
   - the translucent interleave (`HasTranslucentAtPlace`, `_world.TranslucentLeaves`).

   `Views` and the leaf-system collation run on the game thread.
4. Move the feed setters into the half without their `Lock`: `WorldFog`, `SkyFog`, `MonitorCamera`, `Tonemap`,
   `ExposureSeconds` and `ObserverTarget`. The tone-mapping reset in `ObserverTarget` becomes a flag the draw applies.
   Then drop `Lock` from both `SetCamera`s. Any lock left behind just moves the wait.
5. Move the wait to the pose's entry and remove `[Explicit]` from the pending test. The presenter sees only
   `IViewerDevice`, which has no drain, so this step also adds a device member the presenter calls at the pose's entry.
   `FakeViewerDevice` must route that member to `Queue.FinishPreviousDraw()`, or the test stays red for a wiring reason. Then sabotage: break the half
   selection and watch the verifier and the test redden.
6. Cull-hash runs: two mode-0 runs as the control, then mode 2. Then the mode-2 wait ledger.
7. A measurement record, then a quiet-gated ABBA run on f12.

The pre-existing verifier change is found (the instrument, above), so a change after step 5 is the overlap's.

**Step 1 done, 2026-10-23 (branch `feat/queued-render-thread-rung-2d`).** Everything the gate wrote once now lives in
`ViewHalf`: the sky decision and eye, the sky frustum, eye and matrix, the water plan (its sky closure reads the half's
eye), the main frustum, screen view, translucent eye and forward, the monitor view, the world camera and the detail
sprites and models. A new `ViewHalves` (`managed/Tf2DemoSalvage.Render/ViewHalves.cs`) holds the two halves, the
game thread's `Cull` half and the draw's `Drawn` half. `SubmitScene` takes the half at the hand-off and the job begins
it (`ViewHalves.Job`); mode 0's direct path and `DrawAndPresent` begin `Cull` before `DrawFrame`. Every draw-time
reader reads `Drawn`; the game-thread answers (`Frustum`, `ScreenView`, `Eye`, `Views`, `DrawsMonitor`) and the
setters read `Cull`. The frame-hash and verifier cull parts hash the handed half.

- *The detail buffer.* The device has one sprite buffer and each half its own quads, so an A, B, A revisit would draw
  B's quads through A's leaf ranges. `ViewHalves.TakeDetailUpload` uploads when the buffer holds quads built for another
  key. Keyed on the build key, not the half, because the build is a pure function of the key and the props, so a still
  camera uploads once. Cost: a still camera now BUILDS the detail sprites once per half, one extra build when it stops.
- *Kept on purpose:* the gate's "previous frame saw another view" condition. It is no longer needed for correctness
  (each half has its own decision) but keeps mode 0 culling on exactly the frames it did. Dropping it is for steps 6-7.
- *Tests:* `ViewHalvesTests` (seven). Sabotage 1, `Job` beginning `Cull` instead of the handed half, reddened
  `Job_RunAfterTheNextFrameFlipped_DrawsTheHalfItWasHanded` and `Job_WhenRun_ReplaysTheHandedHalfsUploadsBeforeTheDraw`.
  Sabotage 2, `TakeDetailUpload` recording nothing, reddened `TakeDetailUpload_ARevisitAfterTheOtherHalfsView_UploadsAgain`
  and `TakeDetailUpload_BothHalvesBuiltForOneKey_UploadsOnce`. *Read, not tested:* the `Device3D` wiring (`Begin(Half)`
  on the direct path, `Job(half, …)` in `SubmitScene`); no test constructs a `Device3D`, and a wrong half there reddens
  nothing while the wait stays before the camera, because both halves then hold the same view.
- *Equivalence, measured.* The two step-2 baseline logs above could not be reproduced: they are
  `viewer-20261023-082812-39196.log` and `viewer-20261023-083057-1008.log` (191/189, as recorded), but they show no
  `fps_max` on their command line and every first-person run here diverged from them on the pose from the first tick,
  so their exact command is unknown. The control was rebuilt instead: the baseline commit `f8c768fc` built in a
  separate worktree and run with the same command as the change (`TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015
  TF2VIEW_AUTOPLAY=1`, f12 `--tick 30000 --first-person --measure 6 +fps_max 0 +mat_queue_mode 0`, loads 3-22%):
  - baseline vs baseline: 204 ticks, cull 204/204, pose 201/204 (differs 30171-30173);
  - step 1 vs step 1: cull 200/200, pose 189/200 (differs 30169-30179);
  - baseline vs step 1, all four pairs: cull 200/200, pose 191-198/200, differing only inside 30169-30179.
  The pose's two-to-eleven-tick run near 30170 is in the control too, so it is the known run-to-run difference, not this
  change. The cull hash cannot see which half the draw reads (both are equal here); that rests on the sabotage above.
- *Mode 2:* `queue verify: 853000 frames checked, 0 changed` (f12, `--measure 20`, same flags).

**What step 2 must do, exactly (not started as of `fa42d9e0`).** The hook is in place: `ViewHalves` calls
`Device3D.ReplayUploads(half)` at `Begin`, before `DrawFrame`; it is empty today.

- Record, do not send, the GAME-THREAD uploads only: the `_world.SetCamera` in `SetCamera(float[])`, the gate's
  `ReapplyCamera(half)` after `PlanWater`, the `WorldFog`/`Specular`/`Phong`/`Fullbright`/`Debug` setters'
  `ReapplyCamera(Half)`, and the `_detailSprites.Upload` in `RebuildDetailSprites` (move its `TakeDetailUpload` with
  it: what the buffer holds follows draw order, not cull order). Record VALUES (matrix, colours, specular, fullbright,
  debug, phong, fog), last one wins per half; replay one `_world.SetCamera`.
- The draw-time `ReapplyCamera(Drawn/view)` restores stay immediate sends (after the 3D sky, the water views, the
  monitor, the viewmodel pass, the HUD model panels); a recorder there would draw the map through the sky or viewmodel
  camera.
- Replay before `DrawFrame` sets `_world.LinearLightScale`: `WorldRenderer.SetCamera` writes that scale into the
  constant buffer, and the old game-thread upload saw the previous frame's end value.
- Test the recorder as a pure type: values captured at record time, last wins, each half replays its own.
- **The hashes cannot see GPU uploads**, so step 2's mode-0 hash runs come back equal whether it is right or wrong.
  The instrument for it is a `--shot` pixel comparison, step-1 binary against step-2 binary, at fixed ticks on a map
  with water, a 3D sky and detail sprites, with two shots from the step-1 binary first as the control. If that control
  differs, say so and rely on the recorder's tests. The rebuilt-baseline method above (a second worktree at the parent
  commit, same command) is the control for the hash runs.

**Step 2 done, 2026-10-23 (branch `feat/queued-render-thread-rung-2e`, `c839e604`).** The game thread no longer sends
the world camera. `SetCamera(float[])`, the gate after `PlanWater`, and the `WorldFog`, `Specular`, `Phong`,
`Fullbright` and `Debug` setters call `RecordCamera(half)`. That stores a `CameraUpload` (matrix, colours, specular,
fullbright, debug, phong, fog: values captured when recorded, last one wins) in the half. `ViewHalves.Begin` hands
`half.TakeCamera()` to `Device3D.ReplayUploads`, which sends it once. The same replay now does the detail sprite upload,
with `TakeDetailUpload` moved alongside it, so the one buffer follows draw order. `RebuildDetailSprites` only builds.
`Begin` runs before `DrawFrame`, so the replay comes before `LinearLightScale` is set, and it sees the same end-of-frame
scale the old game-thread send saw. Every draw-time `ReapplyCamera(Drawn/view)` restore is still an immediate send.

- *Tests:* three new `ViewHalvesTests` (ten in all). Sabotage, each restored with the inverse edit:
  - replaying the OTHER half's recording reddened `Job_ForTheHalfACameraWasRecordedInto_ReplaysItOnceBeforeTheDraw`,
    `Job_ForTheOtherHalf_ReplaysNoneOfThisHalfsCamera` and `RecordCamera_TwiceBeforeTheDraw_ReplaysTheLastValues`;
  - running the replay after the draw (that is, after `LinearLightScale`) reddened
    `Job_ForTheHalfACameraWasRecordedInto_…`, `Job_WhenRun_ReplaysTheHandedHalfsUploadsBeforeTheDraw` and
    `Job_RunAfterTheNextFrameFlipped_…`;
  - never replaying the camera reddened `Job_ForTheHalfACameraWasRecordedInto_…` and `RecordCamera_TwiceBeforeTheDraw_…`.
- *Read, not tested:* which `Device3D` call sites record and which send immediately. No test constructs a `Device3D`.
- *Pixels, measured* (the instrument for uploads). Step 1 (`fa42d9e0`, built in `D:/r2s1`) and step 2 were run the
  same way: `f12.dem --tick T --first-person --shot`, two step-1 shots then one step-2 shot per tick, at 542,184 px:

  | tick | step 1 vs step 1 (control) | step 1 vs step 2 |
  |---|---|---|
  | 30000 | 12 | 34, 46 |
  | 31500 | 21,534 | 62, 21,546 |
  | 33000 | 60 | 28, 32 |

  Each step-2 shot is within the control's own noise of one of the step-1 shots. A wrong camera, fog or detail buffer
  would change most of the frame, so this rules those out. It cannot resolve a difference smaller than roughly 60
  pixels.
- *Hashes, mode 0* (same command as step 1's, two runs per binary), first frame per tick, about 205 common ticks: cull
  agrees on every tick in all six pairs. Pose agrees on 202-206 ticks and differs only inside 30180-30183. The control
  pairs show the same (step 1 vs step 1: 203/206, at 30180-30182; step 2 vs step 2: 202/204). The window sits about
  ten ticks later than step 1's recorded 30169-30179, with the same size and the same control behaviour.
- *Mode 2:* `queue verify: 884000 frames checked, 0 changed` (f12, `--measure 20`).

*Left:* steps 3-7 as listed above, unchanged. The game thread now records into the half; nothing is sent from it, and
every remaining shared write is a step-4 feed setter or the `Lock()` the step-4 note removes.

**Steps 3 and 4, 2026-10-23 (branch `feat/queued-render-thread-rung-2f`).** The wait stays at `FinishPreviousDraw`, before
`PlaceCamera`. Nothing overlaps yet.

*A correction to the step-3 list, kept.* That list said "`Views` and the leaf-system collation run on the game thread".
Only `Views` does. The device's `CollateRenderables` is called from `DrawFrame` (the call after the 2D sky), so it runs on
the render thread in mode 2, and it read `Culling`, which was the `Cull` half. Reading the call site settled it.

*Step 3: who reads what.* Draw-time readers read `Drawn` (the half the draw was handed); game-thread readers read `Cull`.

| reader | thread | reads |
|---|---|---|
| `SetCamera(FreeCamera)` cull, `PlanWater`, `RebuildDetailSprites`, `ReportWorldCull` | game | `Cull` (`half.Culling`, the half's own lists) |
| `Frustum`, `ScreenView`, `Eye`, `Views`, `DrawsMonitor` (pose inputs) | game | `Cull` |
| `RecordCamera` (from `SetCamera`, Specular/Phong/Fullbright/Debug setters) | game | live `_specular`…`_phong`, `WorldFog` |
| `ReplayUploads` (at `Begin`): tone-mapping reset, **install of the half's runs into the renderer**, camera, detail buffer | draw | handed half |
| `CollateRenderables` main leaves | draw | `Drawn.Culling` (was `Cull`) |
| translucent interleave: `worldLeaves`, `HasTranslucentAtPlace`, `HasDetailAtPlace`, `DrawDetailStep` | draw | `Drawn.Culling`, `Drawn.TranslucentLeaves` (were `Cull` / the renderer) |
| 3D sky gate and camera | draw | `Drawn.SkyBatches`, `Drawn.Feeds` (switches, `SkyFog`) |
| `ReapplyCamera(Drawn)` after the 3D sky, water views, water intersection, monitor, viewmodels, HUD model panels | draw | `Drawn.Feeds` (was the live `_specular`/`_phong`/`_fullbright`/`_debug` and `WorldFog`) |
| viewmodel pass, HUD panel camera | draw | `Drawn.Feeds` |
| `DrawMonitorPass` | draw | `Drawn.MonitorView`, `Drawn.Feeds.MonitorCamera`/`WorldFog` |
| water refraction fog | draw | `Drawn.WaterMainFog ?? Drawn.Feeds.WorldFog` |
| bloom and exposure walk | draw | `Drawn.Feeds.Tonemap`, `.ExposureSeconds` |

**The cull no longer writes the renderer.** `SetCamera` used to write `_world.VisibleBatches`/`TranslucentLeaves`/`Overlays`/
`SkyBatches` and copy them into the half; a skip installed the half. Now it writes only the half, and `ReplayUploads`
installs the handed half into the renderer when the draw begins. That is the same lists in mode 0: every invalidation
(`SetWorldCulling`, `UploadWorldGeometry`, `DropMaterialRuns` with `ReleaseTextures`) clears the halves and the renderer
alike. `SetCamera(float[])` no longer copies `mat_wireframe`/`r_drawworld` into the renderer every frame. `EnsureWorld`,
now used by all five creation sites, gives a new renderer the values, and the setters keep an existing one in step.

*Step 4: the feeds.* `DrawFeeds` (in `ViewHalf.cs`) holds specular, fullbright, debug, phong, the world fog, the sky fog,
the monitor camera, the tone map, the exposure seconds and a tone-mapping reset flag. `HandOff()` stamps it on `Cull`
through `ViewHalves.Hand` at all three hand-offs: the direct path, `SubmitScene` and `DrawAndPresent`.

- `WorldFog`, `SkyFog`, `MonitorCamera`, `Tonemap`, `ExposureSeconds` and `ObserverTarget` take no lock. Each is now a game-thread field
  that the draw does not read.
- `ObserverTarget`'s `ResetToneMapping` became `ViewHalves.RequestToneMappingReset`. The next hand-off carries it and the
  draw applies it first in `ReplayUploads`. In mode 0 this gives the same values, because nothing reads the exposure between
  `UploadCamera` and the draw (the capture's settle test runs in `TakeShot`, at the frame's start). *Read, not tested*: no
  f12 moment where the followed player changes was measured.
- `WorldFog` no longer calls `RecordCamera(Half)`. It ran before the flip, so it recorded into the PREVIOUS frame's
  half, and that half's next cull overwrote the record unread. `SetCamera` records the camera with the current fog for its own half.
- `SetCamera(FreeCamera)` takes no lock. It calls a private `RecordWorldCamera`, which locks only to build the renderer the first time.
  **The public `SetCamera(float[])` keeps its `Lock()`, against the step-4 note**: `WorldPresenter` calls it alone on a
  world upload. It does not flip there, so `Half` is the half last handed, which may be the one in flight.
- **A drain was added at the flight path** (`ViewerSession` `Fly` → `UploadCamera`, outside `PlaceCamera`). The removed
  locks were its only wait before the pose.

*Public members, re-audited.* Every public `Device3D` member except the eight above still takes `Lock()` first, or only
reads. Those eight are game-thread fields the draw no longer reads, plus `Cull`-half writes after the flip. The grep for
`WorldFog|SkyFog|MonitorCamera|Tonemap|ExposureSeconds|_specular|_phong|_fullbright|_debug` now finds draw-time reads
only through `Feeds`. The live fields are read only in `HandOff`, `RecordCamera`, `PlanWater` (the `WaterDraw`'s fog, built
on the game thread) and `SetCamera`'s monitor view.

*Tests:* three more `ViewHalvesTests` (13 in all). Sabotage, each restored:

- `Begin` replaying `Cull` reddened `Job_RunAfterTheNextHandOffFedOtherValues_ReadsTheFeedsItsHalfWasHanded` and
  `Job_WhenRun_ReplaysTheHandedHalfsUploadsBeforeTheDraw`.
- `Hand` not clearing the reset reddened `Hand_AfterAResetRequest_CarriesItToTheNextHandOffOnly`.
- **The device-level sabotage asked for**, `DrawMonitorPass` reading `Half.MonitorView`: Rendering.Tests 1167/1214 (47
  skipped) and Presentation.Tests 1278/1278 stayed green. **No instrument can see a draw-time reader on `Cull` until step 5**:
  `Cull` is the drawn half during every draw, the verifier does not hash feeds, and no test constructs a `Device3D`.

*Step-5 preconditions found here:*

- `HandOff` writes `Cull.Feeds` before `Queue.Submit`'s wait. That is safe only while `Cull` is never the half in flight, and it
  stops being true on a frame that hands off without a `SetCamera` flip (`UploadCamera`'s early return, `DrawAndPresent`).
  Stamp after the wait, or flip at the hand-off.
- `PlanWater` reads `_waterLod.Current` on the game thread. Check whether the draw writes `_waterLod`.
- `Specular`/`Phong`/`Fullbright`/`Debug` still lock and `RecordCamera(Half)`. Under overlap, between frames, that is the
  handed half's pending camera, which is harmless because `Begin` already took it, but it is a write to the drawn half.

*Left:* the mode-0 hash runs and pixel shots against the step-2e binary (f12 plus `b511_boardwalk_mirror.dem` at 1900 for
the monitor), the mode-2 verifier, then steps 5-7.

**Steps 3-4 verified, then step 5, 2026-10-23 (branch `feat/queued-render-thread-rung-2g`).**

*Steps 3-4, verified against step 2e* (`f809924e`, built in `D:/r2base2e`). Same commands as step 2, two runs per binary.
The instruments said "regression" first, and it was the instrument again:

- Each binary agreed with itself on the mode-0 cull hash (2e vs 2e 190/190, 2f vs 2f 168/168). 2e against 2f agreed on
  only 42-52 of 168-206 ticks. The mode-2 verifier on 2f reported **519 changed frames** in its 20 s, every one in the
  cull's world and overlay pieces.
- *Read, then settled*: `CullPart` and `CullPieces` hashed `_world.VisibleBatches`/`SkyBatches`/`Overlays`/
  `TranslucentLeaves`, which are the renderer's lists. Since step 3 the renderer holds a half's lists only from
  `ReplayUploads`, at the draw's start. So at the hand-off it still held the previous frame's lists, and after the draw
  it held this frame's. They now hash the handed half's own lists, the objects the renderer is given. *Measured after
  the fix* (step-5 binary, below): cull equal to 2e on every tick, and the verifier at 0.
- *Pixels* (542,184 px; 2e twice as the control, then 2f): at 30000 the control is 18 and 2f is 23,811 against both.
  At 33000 the control is 27,058 and 2f is 29 against one of them. At 31500 both are under 61. **Every pixel differing by
  more than 8 lies in rows 0-36** (the top HUD bar and the `cl_showfps` counter) in the 2f pairs and in the control pair
  alike. The 3D view is identical. The mirror at 1900 (third person, camera `-1168 700 101 0 90`): control 72, 2f 12 and
  70. The earlier session's `mirror-new.png` has a different viewport size and could not be compared. The 2e control
  shows the monitor's own camera view and 2f matches it.
- So steps 3-4 changed nothing visible, and their verifier "change" was the hash.

*Step 5: the wait moved to the pose's entry.* `IViewerDevice.FinishDrawBeforePose` (`Device3D` →
`Queue.FinishPreviousDraw`, named in the ledger as itself) is called first in `ViewerPresenter.PoseOver`. That one call
covers the camera stage's pose and `ProjectWorld`'s pose on a frame whose camera upload returned early. It is timed as
the `camera wait` part, carried from `PoseOver`. `FrameSequence` no longer waits: `IFrameSteps.FinishPreviousDraw` and
the flight path's drain are gone. So `SetCamera` (the cull) now runs while the previous frame draws.

The hazards, each settled before the move:

- **(a) the no-flip hand-off.** `ViewHalves.Flip` now picks the half NOT last handed, where it used to toggle. A second
  `SetCamera` in one frame (the flight path's upload, then `PlaceCamera`'s) would have toggled back onto the half in
  flight. `HandOff` drains first when `CullWasHanded`, that is when no flip happened since the last hand-off
  (`UploadCamera`'s early return, `DrawAndPresent`), so it never stamps the feeds of the half being drawn. On every
  other frame the drain is free, because the pose already waited. Tests: `Flip_TwiceAfterAHandOff_StaysOffTheHandedHalf`
  and `CullWasHanded_WithNoFlipSinceTheHandOff_IsTrueAndAFlipClearsIt`. Sabotage: a toggling `Flip` reddened the first;
  `CullWasHanded` always false reddened the second.
- **(b) `PlanWater` reads `_waterLod.Current`.** *Read and proved*: `_waterLod`'s only writer is `EnterMap`, in
  `UploadWorldTextures` after its `Lock()`, and no draw-time code reads or writes it. The same holds for `PlanWater`'s
  other reads (`_water`, `_waters` and `_materialNames` are set only there; `_skyRoom` and `DrawSkybox` only behind a
  `Lock()`).
- **(c) the locked `Specular`/`Phong`/`Fullbright`/`Debug` setters.** *Kept locked; read and proved*: they are console
  switches (`ViewerSession.Switches`), never called per frame (none appears in the wait ledger below). Their `Lock()`
  drains first, so `RecordCamera(Half)` writes an idle half. The next `SetCamera` records the live values again into
  the half it hands.
- **(d) not on the list: the culling twins share one row cache.** Every `WorldCulling.Twin()` is built from the map's
  one `BspVisibility`, and its decompressed-row cache was a plain `Dictionary` filled on first use. With the cull
  overlapping the draw, the game thread's cull and the render thread's `MonitorCull` insert into it concurrently. It is
  a `ConcurrentDictionary` now. Test: `Visible_FromFourThreadsAtOnce_AnswersAsOneThreadDoes`, red first with the
  dictionary's own "concurrent update … corrupted its state" exception. The rest of the twin was read: tree, PVS bytes,
  leaf faces and spans are immutable, and every scratch list (`VisibleWorld`'s, `WorldVisibility`'s) is per instance.

*Public `Device3D` members, re-audited.* `SetCamera(FreeCamera)` and the six feed setters take no lock and write only
game-thread fields or the flipped-to half. `FinishDrawBeforePose` is the wait. `HandOff` drains when the half was last
handed. Every other public member takes `Lock()` first or only reads.

*Tests* (each red first for the predicted reason):

- `UploadCameraOver_InModeTwoWithAFrameInFlight_OverlapsSetCameraButNotThePose` lost `[Explicit]`. It read
  `posed in flight True` before the move.
- `Run_InModeTwo_OverlapsTheStagesUpToTheCamerasPose` replaces `…OverlapsOnlyTheStagesBeforeTheCamera`: `PlaceCamera`
  now runs in flight.
- The exact-order test now carries `FinishDrawBeforePose` between `SetCamera` and the pose.
- *Sabotage*, the wait moved after the pose: both `UploadCameraOver` order tests reddened (Presentation 1276/1278).
- *Gate phase 1*: twelve assemblies, 0 failed, all at or above their floors (presentation 1278, rendering 1235,
  content 1845).

*Measured on the step-5 binary* (`2a5e5699`; f12 from 30000, first person, `TF2VIEW_FIXED_FRAME=0.015`; loads 3-19%):

- **Mode-2 verifier, 40 s, the cull overlapping the draw: `queue verify: 962000 frames checked, 0 changed`.**
- *Wait ledger*: `queue wait FinishDrawBeforePose` on 39 of the 40 rate lines. Besides it, `ReplaySkip` and
  `UploadModels` once each: the seek's replay and a "model set grew" upload, both pre-existing drains. No
  `FinishPreviousDraw` is left.
- *Mode-0 hashes*, two runs against the two 2e runs. Cull equal on every common tick in all pairs (190/190, 199/199,
  202/202). Pose differs only inside 30177-30188; the control pairs show the same window (2e vs 2e 30177-30183, step 5
  vs step 5 30185-30188).
- **The half selection, broken on purpose** (the `Flip()` in `SetCamera` removed, so the cull writes the half in flight):
  the verifier reported 37 changed frames in the first seconds after the seek (cull world and detail pieces), and the
  viewer died with `InvalidOperationException: Collection was modified`. That was the draw enumerating a list the game
  thread was culling into. **Now that frames overlap, the verifier sees a wrong half**, which step 4's note said no
  instrument could. Restored with the inverse edit and rebuilt; the clean run above is the restored binary.

*Left:* step 6's remaining question and step 7.

- Whether the gate's "previous frame saw another view" condition can go, since each half now holds its own decision.
- The quiet-gated ABBA measurement of the saving on f12 (step 7). Not measured here, by instruction. **The columns
  moved**: the wait is now in `camera` (its part `camera wait`, and `queue wait FinishDrawBeforePose` in the ledger),
  not `draw`. So rung 1's "`draw` drops to 0.66-0.73 ms" (measurements/2026-10-23-queued-render-thread-rung-1.md) and its `queue wait FinishPreviousDraw` are not comparable
  column for column; compare the frame time.
- Then rung 3: double-buffer the pose, particles and HUD, so the wait can move to the hand-off as `EndFrame`'s does.
- *Mode 2 on the monitor map*, `b511_boardwalk_mirror.dem` from 1900, 20 s, verifier on: `17000 frames checked,
  0 changed`, no exception, `camera wait` on the rate line. **Not established**: that the monitor pass drew in those
  frames. The log names no monitor, and the verifier's cull part does not cover `MonitorCull`. Step 0's shot from the
  same opening view drew the mirrors, so it most likely did (*interpolated*). (d) still rests mainly on the threaded
  unit test and the reading.

## (10) Follow-up, 2026-10-23: the default is -1 (D229), and the engine's fall-backs to 0

`mat_queue_mode` now defaults to the engine's `-1`, so a machine with 2 or more physical cores draws in mode 2. The
device logs the mode it USED and the count it resolved from (`mat_queue_mode -1: QueuedThreaded on 6 physical cores`).
The engine drops back to 0 in the cases finding 99 section 1 lists. Here is each one against this viewer:

- *Exclusive full screen*: served serially by `Device3D` (section 9). *Read from our code.*
- *`-threads <n>` (1 or less turns threading off)*: **a gap, left.** The viewer takes no engine-style `-x` arguments,
  only `+cvar` and `--flag`. *Read from `LaunchOptions`*: `-threads` is warned about and ignored, and the `1` after it
  is taken as a demo path. The user's off switch is `+mat_queue_mode 0`, or the same line in `settings.cfg`.
- *The shader API's capability veto*: no counterpart. Every D3D11 device this viewer creates can be driven from one
  other thread under the queue's lock. *Read, not tested.*
- *The force-off flag and the deferred-call list*: these are the engine's own sync points. Here, `Lock` and the
  drains in section 9 serve them.
- *Alt-tab (the shader API not active)*: **no counterpart.** Nothing in `Render` or `Presentation` changes mode when
  the window is minimized or occluded, so mode 2 keeps running. *Read, not tested.* Nothing seen goes wrong because of
  it.

## (11) Follow-up, 2026-10-25: what the render thread's 2.2 ms is (B597)

*Measured* (measurements/2026-10-25-render-thread-cpu-vs-gpu.md), per pass, timed on the render thread and by GPU timestamps:

| pass | render-thread CPU ms | GPU ms at 1280x720 | GPU scales with pixels? | draws / maps |
|---|---|---|---|---|
| OpaqueModels | 1.06-1.08 | 0.83-0.85 | no (0.87 at 640x360, 0.84-0.87 at 1644x931) | 209 / 421 |
| Occlusion | 0.36-0.38 | 0.05 | no | 376 / 752 |
| Sky3D (with collation) | 0.22 | 0.08 | yes | 19 / 36 |
| World | 0.15-0.16 | 0.36 | yes (0.23 -> 0.48) | 378 / 379 |
| everything else | ~0.4 | ~0.45 | partly | ~156 / ~210 |
| **all** | **2.18-2.22** | **1.75** | ~0.5 ms of it | 1140 / 1800 |

The queue's job is CPU submission, not waiting on the GPU (`Present` costs it 0.065 ms). Half of it is the model path, at
~5 µs a draw against the world's 0.4 µs. The driver worker adds 1.6-2.2 ms on its own thread (finding 100 reproduced). Open:
whether OpaqueModels' flat 0.85 ms of GPU time is vertex work or submission pacing. If it is vertex work, the GPU alone is
over 1 ms at 720p.

## (12) Follow-up, 2026-10-26: the model pass's 5 µs a draw is not its maps (B597)

**What was believed first.** Section 11 put the model path's ~12x gap over the world on "two buffer maps a draw against
the world's one", and the obvious fix followed from that: write every draw's constants into one ring and bind by offset.
The record marked the cause as interpolated. **The world pass already argued against it**: it maps the material buffer
once a draw (`WRITE_DISCARD`) and still costs 0.4 µs a draw, so a discard-map cannot be most of 5 µs.

**What killed it** (*measured*, measurements/2026-10-26-b597-constant-ring-and-occlusion.md). A lap of the clock at each
boundary of the model draw path, read where the work runs:

| OpaqueModels, ms a frame | |
|---|---|
| the face's deltas uploaded (`BindFlex`, 3.16 MB a frame) | 0.27-0.28 |
| per-batch filters, raster, blend, 8 resource binds | 0.16 |
| the model materials' proxies (0.73 µs a material; the world's 0.035) | 0.15 |
| `BindPipeline` and the streams, per model | 0.075 |
| nearest-cubemap search, per model | 0.07 |
| body report and decals, per renderable | 0.06 |
| submission reports keyed on (model path, pass, body) | 0.045 |
| **all three constant maps** | **0.10 (9%)** |

And the experiment: per-slot D3D11.1 rings (`WRITE_NO_OVERWRITE`, offset binding) took **nothing** off the pass; matched
by load they cost about 0.01 ms more in each of World and OpaqueModels (measurements/2026-10-26-b597-constant-ring-and-occlusion.md).
On this driver a small `WRITE_DISCARD` is no dearer than a `NO_OVERWRITE` map plus an offset bind. Measured at +0.011 to
+0.017 ms in OpaqueModels (measurements/2026-10-26-b597-constant-ring-and-occlusion.md), against maps that were ~9% of the pass, the owner reverted them — *"Revert, keep occlusion"*
(D235) — and the ring-less runs are the state that ships.

**What did pay** is the other half of section 11's guess: the 376 occlusion queries each re-uploaded their own quad and
camera. One upload a frame, each query drawing at its offset: Occlusion 0.37 → 0.18 ms, its maps 752 → 2 (measurements/2026-10-26-b597-constant-ring-and-occlusion.md).

**What it says about the shape of the cost.** Ours is managed work around the D3D calls, not the calls:
the face uploaded whole every frame for every flexing head, proxy chains on item materials, a linear cubemap search, and
string-keyed diagnostics. How the engine's studio render pays for the same work was not read for this section.

## (13) Follow-up, 2026-10-26: no face holds still, so the saving is in sending less of each one (B597)

Section 12 (branch `perf/model-pass-constant-ring`) split OpaqueModels into spans. The largest was the face: `Flex`,
0.27-0.28 ms and 3.16 MB a frame (measurements/2026-10-26-b597-constant-ring-and-occlusion.md). A lap there is every
model draw, so the 172 laps a frame are not 172 uploads. A class
model's stream is about 27,000 vertices x 24 bytes, so 3.16 MB is about five whole faces a frame. *Arithmetic.*

**How the engine does it.**
- *Read from published source:* `C_BaseFlex::SetupWeights` (`c_baseflex.cpp:1147-1156`) is the per-draw callback. It
  rebuilds the global weights (`:1189-1263`), runs the rules and the delay (`:1285-1330`, `RunFlexDelay` `:1265-1279`
  decaying with `curtime`) on every draw. It has no change test and no distance cut.
- *Read in disassembly* (x64 `studiorender.dll`; the names come from their telemetry strings, the field meanings are
  *interpolated*): `R_StudioFlexMeshGroup` (`0x1800124f0`) is called per mesh group per draw by the mesh-group draw
  (`0x180011de0`). It locks a dynamic flex mesh for the group's vertices and writes a position, normal and wrinkle delta
  for each vertex. A vertex not flexed this draw gets zeros, judged by a per-draw stamp (`+0x3706d0` against a table at
  `+0x3706e8`). The branch is gated per mesh group (`+0xc & 4`) and by a config bit read as `bFlex` (`+0x24 & 0x40`).
  The hardware-morph branch (`ProcessFlexedMesh_SO`, `0x1800130f0`) is the alternative.
- *Read from the shipped binaries' strings:* no flex LOD exists. The only cvars are `r_flex` (engine), `r_randomflex`
  (studiorender), and `flex_rules` and `flex_smooth` (client). A face is cut only through model LOD, which this viewer
  does not implement.

**So the engine re-sends every face on every draw, as we did.** Skipping a face whose floats have not changed is an
addition above Valve, allowed when the output is the same.

**The change tried** (`FaceStreams`, `e784275c` + `f1f94056`, **reverted in `7e3490ec`, D236**):
- Each face array carries the stamp of the write its floats came from (`ModelInstance.FlexWritten`). The stamp is taken
  from one counter: on `FlexFor`'s weight-equality miss, which is where the stream is rewritten in place, and once per
  resting face.
- The renderer keeps one dynamic buffer per array, keyed on the array, and re-sends it only for a new stamp, a new
  array, or the stamp 0. Stamp 0 means unknown, so a face built by hand always uploads.
- Because the cache is keyed on the array, a resting face shared by every instance of a model goes up once, and a class
  change (a new array) gets a new buffer.
- A buffer not drawn in the last frame is released at the start of `DrawFrame`.
- **Queue:** the stamp is a value captured at the hand-off. The pose waits for the previous draw before any rewrite
  (section 9). A re-send uses `WRITE_DISCARD`, so the GPU keeps what an earlier draw was given.

**Identity:**
- `FaceFlexRenderTests.Draw_AFaceSequenceThroughTheCache_IsPixelIdenticalToUploadingEveryDraw` draws six frames, cache
  on against off, and every pixel is equal. Its stale control shows the cache is live.
- `FaceFlexAnimationDemoTests.FlexWritten_AMedicPlayingAFlexAnimation_ChangesExactlyWhenItsFaceDoes` checks the
  scene's half: 66 held, 65 changed. Every re-pose at the same time held, and every new tick changed.
- Sabotage: keeping the stamp on a rewrite reddened the second test. Skipping regardless of stamp reddened the first.

**What it measured — the wrong premise** (measurements/2026-10-26-b597-face-cache.md): f12 draws about 5.4 faces a
frame, and **under 1%** of them held still since the previous frame. Every one was re-sent, about 580 KB each. Flex CPU
was 0.277-0.295 ms against 0.270-0.277, and the frame 9.51-9.77 against 9.54-9.60 (measurements/2026-10-26-b597-face-cache.md), so the ranges
overlap. The weights
are taken at the continuous render time (the interpolated `m_flexWeight`, the blink, `RunFlexDelay` on `frametime`),
and TF2's idle scenes animate the face continuously. The prediction (30-70% held) assumed faces change at the tick rate.
They change at the frame rate, as the engine's per-draw recompute does. **Lesson:** count what changes before caching
on "unchanged". The count needed one viewer run, while the cache took a branch. The owner reverted it (D236). The lever
left is the bytes per face, not the number of faces.

**The bytes, then.** A class face's flexed slots cluster: first to last covers 25-55% of the buffer, and the flexed
count is 23-49% (measurements/2026-10-26-b597-flex-extent.md, a probe of the production scene over three real
matches). The packer lays vertices out material by material, and the head is a few materials (*interpolated*). That is
also the engine's shape: `R_StudioFlexMeshGroup` builds a flex stream only for the flexed mesh groups.

**What was built.** The face now carries that range. The renderer keeps a zeroed default buffer per model and writes
only the range, with `UpdateSubresource`. The output is identical: pixel A/Bs on a quad and on the laughing soldier,
and the medic test that every moving slot falls inside the range. Each was sabotaged and went red.

**Measured** (measurements/2026-10-26-b597-flex-range.md):
- upload: 3.16 → 1.26 MB a frame;
- flex span: 0.296-0.305 → 0.149-0.156 ms;
- OpaqueModels: 1.169-1.201 → 1.055-1.093 ms;
- render thread: 2.40-2.44 → 2.30-2.38 ms;
- frame: within its spread.

The risk that `UpdateSubresource` would copy twice on a buffer the GPU still reads did not show: time fell with bytes.

## (14) Follow-up, 2026-10-28: rung 3, step 1 — the pose writes its own half, and the wait moves after it

**The motive** (measurements/2026-10-28-pvs-node-prune.md): after the PVS prune the cull got 0.30 ms cheaper, and all of
it became `camera wait` (0.25 → 0.55-0.57 ms, measurements/2026-10-28-pvs-node-prune.md). The game thread reached the
pose's entry with about a third of the render thread's draw left (arithmetic, measurements/2026-10-28-pvs-node-prune.md).

**The engine** (finding 99 §2-3, *binary*): `EndFrame` waits for the previous frame's job, flips its context index
(`0x2f00`) and hands the context just recorded to `MatQueue`; the game thread records the next frame into the OTHER
context. Each context owns its own memory stack, and per-frame render data lives there: *"Renderdata goes out of scope
at frame end in multicore"* (`imaterialsystem.h:1623-1626`, `LockRenderData`). The client double-buffers its own data the
same way: `CMemoryStack m_QueuedRopeMemory[2]`, swapped by `SwitchStack` (`c_rope.cpp:178-206`). So the game thread
never writes what the frame in flight reads; it writes the other copy.

**What the pose writes that the draw reads by reference** (*read from our code*): the world, undrawn and viewmodel
instance lists (`MomentScene`), each list's matrix pool (`InstanceMatrices`, keyed per list), each entity's skinning
matrices (`EntityModelSet.Skinning`) and face stream (`FlexFor`'s `_lastFace`). Everything else in a `ModelInstance` is a
value (the light cube, the sun, the cloak bind), a fresh array (local lights, baked colours), or immutable after load
(skin swaps, body parts, resting faces).

**Built** (`perf/double-buffer-pose`, `815bd95f`): each of those is kept twice over. `PoseHalves` picks the half a pose
writes, and **it moves only when a hand-off happened since the last pose**, counted by `Device3D.HandOffs` at `HandOff`.
Two simpler rules each fail one case:

- *a toggle per pose*: a seek's replays pose several times between hand-offs, and the second would land on the half in
  flight;
- *the hand-off count's parity*: a paused viewer hands one posed list several times, and an even count would put the
  next pose on the half being drawn.

`PoseHalvesConformanceTests` holds both cases.

The wait (`FinishDrawBeforePose` → `FinishDrawBeforeEffects`) now comes after the pose and before the particles, decals and
lightmaps, which still hand the device what the draw reads in place. The queue verifier hashes the light cube and local
lights by value now; `TF2VIEW_FRAME_HASH` does not, so earlier hash records stay comparable.

**Verified:**

- *Verifier*, mode 2, f12: 773,000 frames checked (20 s), then 746,000 (40 s, rebuilt after the sabotage), **0 changed**.
- *Sabotage*: `PoseHalves` never moving. The verifier logged the pose part changed at 30001 and 30004, and the viewer died
  with `Collection was modified` in the render thread's hash. Restored with the inverse edit.
- *Pixels*: 0 world pixels differ, A/B and A/A, in the empty view and in first person.
- *Gate*: phase 1 green.
- *Phase 2, NOT green*: one UI test hung at the first world frame of a z1800 load
  (`PausedSoundUiTests.Footsteps_HeardWhilePlaying_StopWhenTheDemoIsPaused`). The overlap is the suspect. Since then:
  - 5 more full suites on the branch were green, the last with the stall report below in, so the branch is 1 hang in 6
    suites;
  - 35 isolated or harness-exact scripted runs passed, beside a second viewer;
  - the baseline passed 15 of 15 in isolation. That does not discriminate, because the branch passes in isolation too.

  No stack was captured: the watchdog's `dotnet-trace` instrument reads stacks, proved on the idle shared viewer, but
  nothing hung while it was armed. The cause is unknown, and no fix was made without a red-first reproduction. The next
  occurrence logs `queue stall: <caller> … last finished pass <RenderPass>` (`QueuedRenderThread.Stalled`, 5 s).

  **The like-for-like control:** 6 full suites on the baseline (`7946c74d`) against 6 on the branch, alternating, plus
  a replacement for a run the owner's hands touched. **0 hangs on either side**, so the branch stands at 1 hang in 12
  suites and the baseline at 0 in 6. At that count the two cannot be told apart.

  Both of the owner's hypotheses were checked:
  - *the test does not wait for the load*: it failed on its 300 s load wait, with the viewer already silent;
  - *the load is longer now*: both builds load in 53-65 s, so no wait is within a factor of 3.

  Full account: measurements/2026-10-28-double-buffer-pose.md, "Gate" and "Verdict on the hang".
- *Review fixes* (two reviews; `6991bf4c`):
  - the hand-off count is the scene's own source (`MomentScene.HandOffs`), never a defaulted argument;
  - `SkinningOf` builds into its own buffers;
  - tests were added that redden for a per-half face stream, for the undrawn and viewmodel lists, and for the stall
    report held on its own gate. Each was sabotaged red.

  A map change drains before clearing either half (`ClearMap` → `Queue.Lock()` → `Shutdown`).
- *Final gate:* phase 2 green (84 total, 0 failed). Phase 1 aborted in rendering, on a native crash in D3D11 `Map` on
  the offscreen screen-overlay path, which this branch does not touch. The crash did not recur in 10 isolated runs on
  either build. It is filed apart from this work as B610 (measurements/2026-10-28-double-buffer-pose.md, "The gate,
  once").
- **Status: merge-ready, by the owner's decision (D239, 2026-10-28).** The hang is filed as a known flake, B609, which
  reopens if a `queue stall` line ever appears or the branch's hang rate exceeds the baseline's. His reading, *"probably
  just a slow load because tf2 was open"*, is neither confirmed nor refuted by the evidence.

**Measured** (measurements/2026-10-28-double-buffer-pose.md, ABBA BAAB, quiet-gated, Release):

- `camera wait`: 0.49-0.51 → 0.000-0.002 ms (measurements/2026-10-28-double-buffer-pose.md);
- steady first-person frame: 6.85-6.93 → 6.31-6.46 ms, **−0.49 ms (7.2%)** (measurements/2026-10-28-double-buffer-pose.md);
- whole window: −0.62 ms (measurements/2026-10-28-double-buffer-pose.md);
- empty view: no difference, as predicted;
- render thread and pose within +0.04 ms.

**Left: still not the engine's shape, and on f12 no longer worth any time.** The engine waits at the hand-off. We wait
after the pose because the effects and the HUD still write in place:

- `SetParticles` locks and uploads sheets on the immediate context;
- `ModelDecals`, `UploadShotDecals`, `UpdateLightmap` and `SetOcclusionProxies` all lock;
- `ScreenOverlay`, `MonitorOnlyModels` and `DetailFovFactor` are setters that lock;
- the VGUI list is reused.

Moving the wait to the hand-off needs each one recorded and replayed at `Begin`, as step 2 did for the camera, or
double-buffered. That also means `_particleSheets` stops being written by both threads. On f12 the wait there measures
0.00 ms, because the pose (~3 ms) outlasts what is left of the draw. So the rest of rung 3 is parity of structure. It
buys frame time only where the pose is shorter than the draw's remainder (the empty view: 0.55 ms pose against a
0.32 ms draw, where neither build waits).
