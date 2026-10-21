# 88. Bytes a frame, the 'between' bucket, and what the stalls are not (2026-10-21)

**The question.** Real TF2 draws f12 (`demostf-cp_process_f12-2026-08-08-2207`, tick 30000) at about 600 fps. We drew it
at about 14 ms in first person and 7.3 ms in an empty view, and allocated 7.8 MB a frame. Valve's engine allocates
nothing per frame. So what does a sampled trace of steady frames say the render thread spends? Where do the bytes come
from? What is the unnamed `between` bucket? And can the largest allocation sources be removed in the engine's shape?

**How it was measured.**
- `dotnet-trace` launched the Debug viewer: `--measure 25 +fps_max 0`, first person and the empty view
  (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`).
- One run used CPU sampling (`--profile dotnet-sampled-thread-time`, read by `cpu-trace`). A separate run used allocation
  ticks (`--profile gc-verbose`, read by `alloc-trace`). The two were never mixed, because stack-walking allocation ticks
  distorts a CPU profile.
- Each window is the last 25 s of the trace with the first 5 s dropped (tier-0 JIT, finding 84). Only the render thread
  is shown, picked by id (the thread under `ViewerSession.Idle` / `FrameSequence.Run`).
- Every run took the machine-wide lock and sampled load inside it, before and after. Traces live under `D:\n87-traces`.
- **Void runs.** Finding 86's rule was applied: a run is void when it drew clearly fewer frames, or showed a clearly
  higher `between`, than its sibling runs. One run was voided and rerun (fix 2's first run: load 22% after, 1,211 frames
  against 1,363). Several others started at 21–36% load, among them fix 1's first run, fix 3's first run, the second
  final 60 s run and fix 2's empty-view run. Their frame counts and `between` sit with their siblings, so they were kept.
- **Controls.** The allocation trace's total was 9,419 MB per 20 s. The `gc:` line gave 7.95 MB × 1,177 frames ≈
  9.36 GB. The render thread came to 4,851 MB per 20 s, or 4.1 MB a frame, against the new per-thread figure of
  3.99 MB. The CPU profile's `RenderFrame` is 89% of the render thread's samples, and the frame takes the rest of them
  through `Idle`.

## The metric: bytes a frame

`--measure`'s `gc:` line now ends `render thread N bytes a frame`. That is `GC.GetAllocatedBytesForCurrentThread`, read at
the first counted frame and when the report prints, on the frame's own thread, and carried to the line (B243). It stands
beside the every-heap `allocated … MB a frame`, because the corpse worker (D181) allocates as much again, and the
collector's pause belongs to every thread. Every number below gives both.

## (a) The render thread, ranked by cause (first person, before any fix)

*Measured*, 8,987 samples in 19 s. The percentages are inclusive shares of the render thread. A sampler that suspends
the runtime makes `PollGCWorker` a leaf in 38% of samples, and part of that is the instrument (finding 82). The runtime's
own pause counter is the number to believe: 1.5 ms a frame.

| cause | where | share |
|---|---|---:|
| **allocation and copying** | `PollGCWorker` (leaf) | 38.3% |
| | `BulkMoveWithWriteBarrier`: copies of structs that hold references (`StudioBone`, the scene records) | 18.3% |
| | `MemmoveInternal` 4.4, `List<StudioBonePose>.AddWithResize` 2.2, `ZeroMemory` 2.0, `MemberwiseClone` 0.9 | 9.5% |
| **per-prop walks** | `EntityModelSet.Instances` (setup 5.9, lighting 5.8, flex 5.4, simulate 2.7, collate 1.6) | 30.1% |
| | `TimelineMoments.PropsAt` 1.4, `ScenePropTrack.At` 1.3, `SettleEach` 0.8 | 3.5% |
| **hashing and interface dispatch** | `SceneProp.GetHashCode`, a record hashed in full as a dictionary key | 1.8% |
| | `List<ScenePlayer>`/`List<ParticleBurst>` enumerated through the interface | 2.6% |
| **the device (P/Invoke)** | `Device3D.DrawFrame` | 15.5% |
| | of it, `ExposureHistogram.TryRead` (a GPU readback, leaf) 2.4, `SetMaterial` (leaf) 2.6, occlusion queries 1.2 | |
| **outside the frame** | `ViewerSession.Idle`'s own time (leaf): the traffic wait, see (c) | 7.6% |
| | WinForms dispatch: the transport bar's `Label`/`TrackBar` repaint, `TransportBar.ShowTick` | 3.1% |
| **locks and I/O** | `FileLogger.Log` → `StreamWriter.Flush`, synchronous, on the render thread | 1.0% |
| | `Monitor.Enter_Slowpath` (leaf) | 0.4% |

## (b) Where the bytes came from (first person, before; MB per 20 s ÷ 1,214 frames)

| type | MB / 20 s | a frame | site |
|---|---:|---:|---|
| `StudioBonePose[]` | 1,702 | 1.40 MB | `SkinnedModel.PoseOf`/`PoseBetween`, `StudioPoseBlend.Expand`/`ExpandIdentity` under `Locals` ← `SkeletonPose.Build` ← `SetupBones` |
| `(float, float, float)[]` | 439 | 0.36 MB | `LevelLighting.SunFrom` ← `Entry` ← `ModelLighting.For` ← `Instances` |
| `ScenePlayer[]` | 390 | 0.32 MB | `SpectatorTarget.Observable`, from six callers of `SpectatorView.Target` per frame |
| `(int, int, int)[]` | 320 | 0.26 MB | `StudioAnimation.Chain`'s list, under `PoseOf` |
| `float[]` | 292 | 0.24 MB | `VguiRenderer.BuildVertices` 127, `OcclusionQueries.Count` 35, `PropTransform.ToMatrix` 21 |
| heal-beam closure | 234 | 0.19 MB | `BurstEffects.AddHealBeams` |
| `StudioDecalProjection.Influences` iterator | 141 | 0.12 MB | `ModelDecals.Add` / `AddClipped` |
| `Device3D.File` closure | 124 | 0.10 MB | `CollateRenderables` |

**Off the render thread**, the corpse worker allocated 4,498 MB per 20 s. That comprised `CorpsePhysics.Advance` 1,642,
`UpdateClientSideAnimations` 840 (`SceneProp` copies, and a `Dictionary<string, string>` per corpse per tick), `IkFor`
827 (the error tuples 448), `AutoLayersFor` 320 (`PoseLayer[]`), and `PlacementOf` 257.

## (c) The 'between' bucket is a millisecond's wait for traffic, run every frame

`between` is the frame clock minus the sequence's total, at 1.4–1.7 ms. The CPU profile put 7.6% of the render thread in
`Idle`'s own frame and 3.1% in WinForms dispatch. A sampler cannot see past a managed frame into native code, so a
throwaway timer was added (not committed): `Idle` against `RenderFrame` against the time outside `Idle`. *Measured*, one
20 s first-person run:

| | ms a frame |
|---|---:|
| `Idle`, whole | 17.90 |
| `RenderFrame` inside it | 16.32 |
| the sequence (`FrameSequence` total) | 16.30 |
| `IsActiveApp`, `PeekMessage` | 0.03 |
| outside `Idle`: the window's own message dispatch | 0.45 |
| bursts ended by a message | 1,082 of 1,082 frames; the message `0x000F`, `WM_PAINT` |

So 1.55 ms a frame sat in `Idle`, outside the frame and outside every call it makes. The only other code there is
`FramePacer.AwaitsTraffic` → `ArrivesWithin(0.001)`. That is a spin of up to 1 ms, waiting for a follow-up cross-thread
COM call, and it runs whenever the last burst was ended by a message that is not input.
- The transport bar repaints after every frame, so `WM_PAINT` ended every burst, and the spin ran on every frame.
- *Read in our code*: the wait exists for UIA's one-call-after-the-reply pattern (`FramePacer.AwaitsTraffic`'s remarks;
  commit `643373a5`, a `FindFirst` timeout at CI's 68 ms software frames).
- **Fix 1, first form: exclude every `WM_PAINT`.** Gate phase 1 reddened
  `Idle_AfterAPaintEndedTheLastBurst_WaitsForTrafficAndYieldsWhenItComes`. A paint can be part of a UIA chain: the call
  is served with no frame drawn, its handler invalidates the window, and that paint ends the burst while the next call
  is on its way.
- **Fix 1 as kept:** a paint waits for traffic only when its burst drew no frame. The frame's own paint does not wait;
  the paint after a served call still does.

The remaining `between` (0.3–0.6 ms) is the WinForms dispatch of that repaint: `TransportBar.ShowTick` sets the
`TrackBar` value and a `Label`'s text on every frame. The engine's frame has no equivalent; B572 files it.

## Fix 1: a paint no longer waits for traffic

| f12 tick 30000, steady 20 s | before (2 runs) | after (2 runs) |
|---|---:|---:|
| `between`, first person | 1.56, 1.59 ms | 0.59, 0.54 ms |
| mean frame, first person | 16.48, 17.07 ms | 15.81, 15.43 ms |
| `between` / mean frame, empty view | 1.34 / 7.32 ms | 0.30 / 6.18 ms |
| allocated a frame (every heap / render thread) | 7.55 / 3.95 MB | 7.35 / 3.96 MB |

The table is the first form. The kept form measured `between` 0.53 ms and a mean frame of 14.67 ms in one first-person
run on the final tree, so it keeps the saving.

Tests:
- `FramePacerTests.AwaitsTraffic_AfterAPaintThatEndedADrawnFrame_DoesNotWait` and `…WithNoFrame_Waits`.
- `ViewerSessionTests.Idle_AfterAPaintEndedABurstThatDrew_DoesNotWaitForTraffic` and
  `…ThatServedACall_WaitsForTraffic`.
- The old test now uses `WM_USER` as its non-input message.
- Sabotages: ignoring `drew` reddens the two waiting tests; inverting the burst's flag reddens the drawn-paint test.

No output can change. **The UI suite (gate phase 2) is the local check that the wait still serves UI Automation**, on a
hardware device. The failure the wait was added for appeared at CI's 68 ms software frames, so CI is the stronger check.

## Fix 2: a skeleton poses in storage it owns

**The engine** (*read from published source*). `CBoneSetupMemoryPool` (`bone_setup.cpp:49-79`) hands out arrays of
`MAXSTUDIOBONES` from a lock-free free list (`CTSListBase`). `CalcPose` and `BlendBones` take their temporaries from it
and give them back, so a frame's poses reuse the same storage.

**Ours.** Every intermediate pose was a new array or list: each frame read, each blend of a frame with the next, each grid
corner, and the grid's own blends. So was the bone chain, and the three-way weights.

**The change.**
- `StudioPoseWorkspace` holds the lists, and each `SkeletonPose` owns one. That is per skeleton, not per model, because a
  model is posed by the render thread and the corpse worker alike.
- `LocalsInto`, `PoseInto`, `BlendInto` and `ThreeWayInto` write into the workspace. The allocating forms remain, as
  wrappers over the same code.
- Each grid corner has its own slot, so a corner being read never shares storage with one being blended. `BlendInto`
  refuses an output that is also an input.
- The result is valid until the skeleton's next pose. `SkeletonPose.Build` reads it, and copies the base into `_layered`,
  before it asks for a layer.

| f12 tick 30000, steady 20 s | after fix 1 (2 runs) | after fix 2 (2 runs) |
|---|---:|---:|
| render thread a frame | 3.96, 3.93 MB | 2.25, 2.21 MB |
| every heap a frame | 7.35, 7.36 MB | 5.34, 5.53 MB |
| collector's pause a frame | 1.49, 1.51 ms | 1.27, 1.20 ms |
| mean frame, first person | 15.81, 15.43 ms | 14.68, 15.26 ms (a third run, load 22% after, was void and rerun) |
| empty view: render thread / mean frame | 1.77 MB / 6.18 ms | 0.87 MB / 5.78 ms |

**Equivalence.**
- *Measured*: shots of the old and new builds at first person ticks 30000, 30540 and 31000, and a free camera
  (`-3700 -2300 760 20 200`) at 30540, compared with `pair-diff`. They differ only in the fps text (y 2–10). The control,
  the old build shot twice, differs in the same box by the same amounts (28–77 px).
- Tests, each red under its own sabotage:
  - `StudioPoseWorkspaceTests.PoseInto_ReusedAcrossEveryAnimationOfAPlayer_EqualsPoseAndBlendIntoEqualsBlend`. It runs
    over every animation of `scout_animations.mdl`, walked backwards with reused lists. It is red without the `Clear`, and
    it asserts more than 100 animations were read. The first version read `scout.mdl`, which holds 2, and the count
    caught it.
  - `BlendInto_ASecondCallIntoTheSameLists_AllocatesNothing`.
  - `BlendInto_IntoAPoseItReads_IsRefused`, red without the throw.
  - `DeltaBlendGridTests.LocalsInto_ASecondPoseOfAGridBetweenFrames_AllocatesNothingAndEqualsLocals`. The first green
    attempt still allocated 80 bytes: the grid's `ThreeWay` arrays, which are now `stackalloc`. It is red with a fresh
    list for the last blend.

## Fix 3: the IK error list is the skeleton's

`SimulateEach` made a new error list for every prop on every simulate, mostly on the corpse worker. The engine's IK context
owns `m_ikChainRule` and empties it (`bone_setup.cpp:3319`). `ReusedIkErrors` returns the skeleton's own list, emptied.
Nothing reads `IkErrors` past the build that follows its simulate.

| f12 tick 30000, steady 20 s | before (2 runs) | after (2 runs) |
|---|---:|---:|
| every heap a frame | 5.56, 5.63 MB | 5.09, 5.04 MB |
| render thread a frame | 2.23, 2.22 MB | 2.18, 2.23 MB (the list was the worker's) |
| collector's pause a frame | 1.20, 1.19 ms | 1.08, 1.13 ms |
| mean frame | 14.76, 14.89 ms | 14.50, 15.32 ms (inside the noise) |
| empty view, every heap | 2.20 MB | 1.97 MB |

Test: `IkRuleDependencyTests.ReusedIkErrors_ASecondSimulate_GetsTheSameListEmptyAndAllocatesNothing`, red when the helper
returns a new list.

## All three: allocation a frame, start to end

| f12 tick 30000, first person, steady 20 s | start | end |
|---|---:|---:|
| render thread | 3.95–4.02 MB | 2.18–2.23 MB |
| every heap | 7.55–7.79 MB | 5.04–5.09 MB |
| collector's pause | 1.50–1.60 ms | 1.08–1.13 ms |
| mean frame | 16.48–17.07 ms | 14.50–15.32 ms |
| empty view: render thread / every heap / mean frame | 1.80 / 3.53 MB / 7.32 ms | 0.86 / 1.97 MB / 5.55 ms |

**What is left, by type** (first person, after, MB per 20 s, total 6,788):
- `float[]` 638: the worker's `PlacementOf` 211 and `ToBoneMatrix` 64, and `VguiRenderer.BuildVertices` 143.
- `SceneProp` copies 499, of which the worker's `UpdateClientSideAnimations` is 439.
- `SunFrom` 478.
- `ScenePlayer[]` 432.
- `PoseLayer[]` 413, the worker's.
- `Dictionary<string, string>` 282, the worker's.
- `Nullable<IvpMatrix>[]` 279, from `IvpRootFinder.March`.
- The heal-beam closure 264.

Each is under 0.35 MB a frame. They are filed in B567.

## (4) The empty view's floor: particles and decals with nothing drawn

*Measured*, after the fixes: the empty view, 8,769 render-thread samples, at a 5.9 ms frame.
- `FramePresenter.DrawOver` (the particle phase) is 32% of the render thread, about 1.9 ms.
  - `BurstEffects.StepOver` is 15.4%: dispatches, muzzles, heal beams, overheals.
  - `PosedAttachmentMatrix` is 4.3%. It sets up the bones of entities nobody can see, to place their effects.
  - `ParticleEffects.Bursts` is 5.3%, and a `List<ParticleBurst>` enumerated through its interface is 4.0%.
  - `FrameParticles.BuildOver` is 5.9%.
- **`DecalEffects.StepModelDecals` is a leaf at 5.7%**, about 0.34 ms: the 0.43 ms `decals` part with nothing in view. It
  is its own loop, not anything it calls, so it walks the model decals each frame whether or not any can be seen.
- Not particles, and as large: the HUD's rich text (`VguiRichText.Paint` 5.8%, leaf) and `HudEventFeed.Advance` (4.8%,
  leaf); `ExposureHistogram.TryRead` (4.6%, a GPU readback each frame); a synchronous log flush on the render thread
  (`StreamWriter.Flush` 2.1%); and `BulkMoveWithWriteBarrier` 24.8%, the struct copies again.
- B572 files these. Each one needs the engine read before it is changed (does `CDecalManager`/`StudioRender` skip an
  unseen model's decals; when does the engine read its exposure), and none was changed here.

## Stalls: frames over 100 ms per minute, after the Defender exclusion

**The instrument (new, `--measure` only).** A heartbeat thread waits 1 ms on its stop signal in a loop, and records every
interval over 50 ms. Each frame over 100 ms is printed with its dominant phase, the collector's pause inside its
sequence, and the heartbeat gap that overlaps it, if any. A gap means the whole process stopped: a GC suspension, CPU
starvation, the scheduler, or paging. A stall with no gap was the render thread's own. Every time is carried from the
`Stopwatch` ticks where it was read.

**A wrong turn in the instrument.** The first version took a stall's length from the frame clock. That clock is stamped
in the middle of the sequence, so its interval straddles two frames' phases, and every stall came out "mostly between".
The stall is now timed from the previous frame's end to this one's, by stamps around `FrameSequence.Run`.

**The controls.**
- **A suspension, the control the instrument has to see.** The viewer process was suspended for 300 ms three times, 5 s
  apart (`NtSuspendProcess`). The three gaps came back as 317, 310 and 313 ms. Each was paired with a stall of 323, 316
  and 328 ms, which happened to land in `draw`, `camera` and `sound`. The gaps were 5.315 s and 5.323 s apart, against
  the suspensions' 5.335 s.
  - So a process-wide pause lands in whichever phase is running. A phase name alone cannot tell "ours" from "the whole
    process stopped". The heartbeat can.
- **The CPU-load control was not delivered.** The run used 48 busy threads at High priority for about 12 s, on this
  12-thread machine. The per-second frame rate barely moved (61–98 fps, against 70–130 before it), and no gap appeared.
  - Nothing sampled the load inside the burn window, so whether the burner even saturated the machine is unknown.
  - A thread waking from a wait can also be scheduled ahead of a CPU-bound one. So the heartbeat may not see the kind of
    contention that slows a busy render thread.
  - So a missing gap rules out a process-wide stop: a suspension, a collector suspension, or paging. **It does not rule
    out CPU contention.** The suspension control shows only that a process-wide stop is visible.
- **With nothing wrong**, every 60 s run on the final tree recorded no gap: the three below, the burner run, and the
  suspension run outside its three suspensions.

"Before" is main (`Merge branch 'worktree-agent-a5caa403305d771af' into merge/batch-14`) with only this finding's three
instrument commits cherry-picked onto it. It has the same instrument, the same exclusion, and main's precache, so the
fixes are the only difference. "After" is this branch. All six runs were after the Defender exclusion.

| run | frames over 100 ms | the largest | heartbeat gaps | sound read `STALL` lines |
|---|---:|---|---:|---|
| before 1 (load 40% at start) | 7 | 415 ms (`hud`), 189 (`sound`), 164 | 0 | 1: `vo/null.mp3`, read 172.7 ms |
| before 2 | 3 | 133 ms | 0 | none |
| before 3 | 3 | 294 ms (`between`) | 0 | none |
| after 1 | 0 | — | 0 | none |
| after 2 | 4 | 182 ms (`camera`) | 0 | none |
| after 3 | 3 | 190 ms (`between`) | 0 | none |

Means over the 60 s, before → after: frame 13.2–14.0 → 11.06–11.13 ms; render thread 3.30–3.31 → 1.85 MB a frame;
every heap 5.08–5.22 → 3.07–3.08 MB; collector's pause 1.00–1.08 → 0.62–0.63 ms. The 60 s window plays to tick 34000,
so its frame is lighter than the 20 s window's.

- **How stable.** Before: 7, 3, 3. After: 0, 4, 3. The count barely moves. The size does: before, the largest were 415
  and 294 ms; after, every stall is under 200 ms.
- **None has a heartbeat gap.** A process-wide stop is ruled out. CPU contention is not (above).
- Each stall's collector pause was 0–10 ms, so the collector is not the cause either.
- The phase that held them is mostly `camera` or `between`, where `between` is now the window's dispatch.
- **An earlier run on the pre-rebase tree, before the instrument was fixed**, had 8 frames over 100 ms (three of 413–426
  ms) and two 396 ms `bulletLtoR` read stalls, with no gap. That run was taken after the exclusion.
- **The ~395 ms sound stalls survived the Defender exclusion.** Two of them came in that earlier run, 9.3 s apart. They
  lined up with two of its three 413–426 ms frames, which were 9.3 s apart as well. A 173 ms read of `vo/null.mp3` came
  in "before 1". No heartbeat gap came with any of them, so
  the process was not paused: only the render thread waited, which agrees with finding 87's 0.0 ms of thread CPU.
  - The traces and the TF2 install are on `D:`, which is excluded.
  - So real-time scanning of the archive read is not shown to be the cause. It is not ruled out either, because the viewer
    and its build output under `C:\Users\…` are not excluded.
- **They are gone in the after runs, and that does not settle the cause.** Both sides carry main's model-sound precache
  (finding 87), which removed eight of the twelve misses. The other four, the near-miss whiz among
  them, still miss and are still read on the render thread (B568). Those reads logged no `STALL`, so each took under the
  30 ms threshold. Finding 87's run 6 showed the same thing on a quiet machine before any exclusion. So "a slow read on a
  busy machine, a fast one on a quiet machine" is still the observation. These three runs being fast fits the exclusion,
  but does not show it was the cause.
- **The earlier counts (1, 9, 1, finding 86) were taken without the exclusion, with other agents' work on the machine, and
  with the stall taken from the log.** They are not comparable to these.

## Wrong turns

- **Two background runs measured the wrong thing.** One A/B script passed `HEAD` as the second commit after it had checked
  out the first. Another started while a commit had been refused, which left the work in the tree uncommitted. Both were
  stopped before they reported, and rerun with hashes.
- **The shell script driving the runs was edited while it was running.** Bash reads a script as it goes, and the first
  final run's report step failed (`unexpected EOF`). Its numbers were recovered from the viewer's own output and log
  files.
