# Detail sprites built only for the view's leaves (2026-11-06)

**Change:** `DetailSprites.Build` takes `visibleLeaf` (`WorldCulling.IsMainLeaf`) and skips every prop in a leaf
outside the world list, as Valve builds the sprite mesh only from the world list's translucent leaves
(`viewrender.cpp:4591-4639`, `ShouldDrawDetailObjectsInLeaf`, `clientleafsystem.cpp:1383`). Drawn output is
unchanged: the translucent pass already drew only world-list leaves' quads. A new cull clears `DetailBuiltFor`.

**Command:** `TF2VIEW_AUTOPLAY=1 run-exclusive.ps1 tf2demoview D:/measure-f12/f12.dem --tick 5000 --first-person --measure 20 +fps_max 0`,
debug build. Control is the same build with the predicate forced to null (`CanCull: false`), run back to back in the same session.
The figures are means of the 18 per-second samples. The tool prints no interval-wide breakdown by part.

| | frame mean | frames | setcamera | setcamera detail |
|---|---|---|---|---|
| control (every leaf) | 8.37 ms | 2390 | 1.44 ms | 0.57 ms |
| visible leaves only | 7.72 ms | 2592 | 1.24 ms | 0.39 ms |

**Verdict:** detail build −0.18 ms (−32%), setcamera −0.20 ms. Frame mean moved −0.65 ms, more than this change
alone can account for, so most of it is run-to-run noise. Not drawn, per D129: see the `tf2-measure` skill's list.

## Beam strips: last-list shortcut in `SpriteStripBatches.Add` (same day)

The new `camera particles/build/*` and `…/entities/*` parts put f12's particle build at 1.05 ms: systems 0.38, entities 0.59,
of which beams 0.54 (211 beams a frame, one quad each). Inside the beam draw, the halo path was 0.20 ms, `strips.Add` 0.13
and the corner build 0.05. Passing the material by reference and reading the corners through a span changed nothing
(0.133 ms), so that change was dropped. Reusing the last list when the path (by reference) and pass repeat took `strips.Add` to 0.098 ms and
beams to 0.57 ms, so about −0.035 ms a frame.

**Instrument note:** three runs in between measured a stale binary. Every build since 18:42 failed on Sonar errors, but the
command chained the run after `grep | head`, which always succeeds. Those runs' figures are discarded.
Gate a run on `0 Error(s)` in the build log, never on a filtered grep's exit code.

## The profiler's struct-copy lead, falsified (same day)

`cpu-trace` on a fresh `dotnet-trace` of HEAD (f12, game thread 30532, `under:ViewerSession.RenderFrame`, the last 25 s)
put `Buffer.BulkMoveWithWriteBarrier` at 22.9% inclusive. `callers:` spread it over dozens of sites at 0.2–1.5% each,
and `SceneProp.get_Pose` (a copy of the 48-field `ScenePose`) came to about 2.5%. **Prediction:** binding `Pose` to a
field, which copies nothing on read, saves about 0.18 ms of a 7.2 ms frame.

| run | frames | frame mean | camera |
|---|---|---|---|
| B1 (field) | 2758 | 7.25 | 5.63 |
| B2 (field) | 2762 | 7.24 | 5.77 |
| A1 (property, HEAD) | 2807 | 7.13 | 5.67 |
| A2 (property, HEAD) | 2335 | 8.57 | 6.77 (load) |

**Falsified:** no change. The EventPipe sampler lands on safepoints, and this copy routine is one, so its share is the
sampler's bias, not cost. The experiment is reverted. **Rule:** a `cpu-trace` inclusive share is a lead until a `--measure` A/B
moves by the predicted amount.

## TF2's own main thread on f12, by function (2026-11-07)

**Capture:** WPR `-start CPU` (kernel sampling, about 1 kHz), started and stopped by the elevated scheduled tasks
`TF2Prof-Start`/`TF2Prof-Stop`, TF2 playing f12 near tick 5000, 14 s. Main thread 37,177 samples, about 98% busy.
TF2's overlay showed 151 fps, but part of the screen drew black, so the frame time is not a reference.

**Instrument note:** the first Ghidra project's `client.dll` was imported 2026-10-02 and the live one is dated
2026-10-17, so every address resolved into unrelated code. The live DLL was imported fresh (`tf2client1017`) and the
functions named by the profiler-zone strings each one references (`FuncStrings.java`, `D:\ghidra-proj\scripts`).
Check a Ghidra project's binary date against the running one before trusting a name.

Inclusive share of main-thread samples:

| function (named by its own VPROF string) | share |
|---|---|
| `CViewRender::Render` → `RenderView` | 22.3% |
| `ViewDrawScene` | 19.6% |
| `DrawWorldAndEntities` | 16.2% |
| `DrawTranslucentWorldInLeaves` | 10.7% |
| client simulation (`OnRenderStart`, `SimulateEntities`, `ParticleMgr()->Simulate`) | 8.3% |
| `DrawOpaqueRenderables` | 4.3% |
| `CParticleMgr::UpdateNewEffects` | 3.7% |
| `DrawTranslucentRenderables` | 3.5% |
| `C_BaseAnimating::SetupBones` | 3.2% |
| `BuildWorldRenderLists` | 2.6% |
| `CAttributeManager::AttribHookValue` | 2.5% |
| `DrawTranslucentWorldAndDetailPropsInLeaves` | 2.3% |

Modules: client 42.6%, engine 27.4%, materialsystem 5.7%, vgui2 3.8%, vguimatsurface 2.8%, datacache 2.8%,
studiorender 2.4%. **Reading:** TF2 spends about 3% of its main thread in bone setup; ours spends about 3 ms of a
7.2 ms frame in pose. This is a lead for the pose work, not a paired measurement: TF2 draws on a second thread
(30,117 samples), and our side has not been captured by the same instrument yet.

## Our viewer under the same instrument, and the scrub bar read (2026-11-07)

**Capture:** WPR `-start CPU -start DotNET` (the task now carries the DotNET provider, so `cpu-trace` names JIT code from
a `.etl`), f12 `--tick 5000 --first-person`, recording started 50 s after launch. Game thread 2976, render-queue thread
22288, timeline builder 4716. **Instrument note:** only about 18% of kernel samples carried a stack, and a stacked sample
is likelier to be in a system call, so shares among stacked samples overweight kernel time. Treat them as leads.

Under `ViewerSession.RenderFrame` (1,011 stacked samples): `ManualResetEventSlim.Wait` in `ProjectWorld` 36.5% (waiting
on the render thread), `TrackBar.GetTrackBarValue` 12.8%, `FreeLookCamera` 10.4%. The trackbar read is `TBM_GETPOS`, a
SendMessage per read of `TransportBar.CurrentTick`/`Position`, reached several times a frame through
`CameraPresenter`'s window snapshot. The fix keeps a copy from `ValueChanged`.

| run | frames | frame mean | camera |
|---|---|---|---|
| A1 (TrackBar read) | 2482 | 7.25 ms | 5.79 |
| B1 (copy) | 2573 | 7.00 ms | 5.60 |
| B2 (copy) | 2672 | 6.74 ms | 5.39 |

Machine under owner background load (browser, Epic launcher, about 40% CPU idle) for all three, back to back.
**Verdict:** −0.25 to −0.5 ms a frame (3–7%). One A run only; the B runs agree with each other.

**The `ManualResetEventSlim.Wait` lead, falsified.** The 36.5% is `QueuedRenderThread.Lock`, and the viewer's own wait
ledger (`camera wait` part, B2) says 0.03 ms a frame. A wait is a system call, the samples most likely to carry a kernel
stack, so the share is the instrument's bias. The trackbar read held up under `--measure`; this did not. Where the camera's
5.6 ms goes by the viewer's own parts (B2): pose 3.10, particles 2.29, setcamera 1.14.

**Face history `Array.Clone`, falsified.** A `dotnet-trace` put `ActorFace.InterpolateAndLatch`'s per-frame clone at
4.3% of pose samples. Reusing the dropped entries' arrays measured 6.86 and 7.03 ms against 6.74–7.00 for the same build
without it, and allocation stayed at 0.74 MB a frame: no change, reverted. The `ClientLeafSystem.InsertIntoTree`
head insert (3%) was not tried: its order is the engine's linked-list order, read through `RenderablesInLeaf`.

**TF2 itself on f12, first person (2026-11-07).** Live TF2 (`tf/f12.dem`, same size as `D:/measure-f12/f12.dem`),
1280×720 windowed, `fps_max 0`, `engine_no_focus_sleep 0`, `demo_gototick 5000 0 0`, then `spec_mode 4` on Beleleu
(soldier), `cl_showfps 2` read off TF2's own screenshots with `demoui` open: 198 fps at tick 6553 and 223 fps at tick
6923, so about **4.5–5 ms a frame**. Before `spec_mode 4` the demo's own camera sat outside the world (mostly black):
264 and 281 fps. Against this viewer's 6.7–7.0 ms on the same demo in first person, TF2 is about 1.5× faster on this
machine, not the 7× a `cp_badlands` reference suggested. Not a paired frame: TF2 followed a different player at a
later tick than `--tick 5000`.

**The viewer at TF2's ticks, TF2 first (same day).** Owner correction: measure TF2, read the tick and subject it
reached, then the viewer there. `--tick 5800 --spectate Beleleu --measure 20 +fps_max 0`, per-second frame counts:
second 11 (about tick 6553) **161 fps** against TF2's 198; second 17 (about tick 6923) **224 fps** against TF2's 223.
Seconds 1–7 after the seek ran 45–102 fps. **Reading:** warmed up, this viewer is within about 20% of TF2 at the
same tick and player, and level at one of the two. The interval mean (7.27 ms) is dragged by the first seven seconds,
so every earlier `--measure 20` mean on f12 includes a warm-up cost that TF2's figures do not. That warm-up, not the
steady frame, is the larger gap.

**Threaded bone setup is not a parity route.** `cl_threaded_bone_setup` defaults to 0, `FCVAR_DEVELOPMENTONLY`,
"because it currently crashes" (`c_baseanimating.cpp`, quoted in `D:/measure-f12/engine-read-setupbones.md`), so
Valve's shipped client sets up bones on the main thread, as this viewer does.
