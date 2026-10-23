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
as its draw finishes, as in the engine (finding 99 section 4). It ships off: `mat_queue_mode` defaults to 0 here, where TF2's
is -1.

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
part's "pre-existing, timing-dependent" late divergence above may be this instrument too. *Not yet re-measured.*
Every hash value from before this commit is incomparable with one after it; the baseline for step 2 is this commit.

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

*Left, in order:*

0. Give the monitor cull its own `WorldCulling.Twin()`, so `_monitorCull` never aliases a half's main list. Decide
   first: this changes mode-0 output on monitor maps. Also log the four parts separately on a verifier mismatch, to
   find which one the pre-existing changed frame is.
1. Move into the half everything the gate still writes once:
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

Before step 5, find the pre-existing verifier change above. Otherwise it will be blamed on the overlap.
