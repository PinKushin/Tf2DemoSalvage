# Main-thread CPU profile, f12 gummo first person (2026-10-24)

## Observation

The f12 first-person frame is ~8.2-9.4 ms. Three code-read estimates in a row came back null when measured: B583
(finding 97 §6), B585 and B586. Nobody had sampled the main thread method by method.

## Question

Where do the main thread's samples land, by method, inclusive and exclusive? Do the sampled phase shares agree with
`--measure`'s own phase timers?

## Method

- Build: `origin/main` @ `8fa05fb4`, **Release**, `dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` through
  `run-exclusive.ps1 -Shared`. 0 warnings.
- Viewer: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 tf2demoview.exe D:/measure-f12/f12.dem --tick 30000
  --spectate gummo --first-person --measure 180 +fps_max 0`. It was launched by `Start-Process` inside
  `run-exclusive.ps1`, with stdout redirected. Quiet gate first: three `Win32_Processor.LoadPercentage` samples, all
  under 15%.
- Trace: `dotnet-trace 10.0.745401`, attached by PID 230 s after launch for 25 s:
  `collect --profile dotnet-sampled-thread-time --providers Microsoft-Windows-DotNETRuntime:0x18:5 --buffersize 1024
  --duration 00:00:00:25`, with `DOTNET_EventPipeThreadSamplingRate=10` set in the viewer's environment.
- The trace window was placed in playback seconds by arithmetic: measure start = process exit − 180 s. The per-second
  `frame rate` lines for that span were averaged frame-weighted, which gives the timer figures for exactly the traced
  seconds.
- Analysis: `dotnet-trace convert --format speedscope`, then a throwaway PowerShell reader of the evented profile in
  the scratchpad. It is not in the repo, and no NuGet package was added. Inclusive time is counted once per stack.
  Exclusive time is charged to the deepest managed frame, skipping `CPU_TIME`, `UNMANAGED_CODE_TIME`, `?!?` and
  `PollGCWorker`. The main thread is the one whose stacks hold `Program.Main`.
- Runs: **G** gummo with mode 2 (the default), **E** the empty view (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`, no
  `--first-person`), **M0** gummo with `+mat_queue_mode 0`.

### Four instrument faults, each found by a control before any number was believed

1. **The first four traces measured the LOAD, not playback.** f12 takes ~3 min to decode and seek. A trace taken
   18-25 s after launch caught `DemoTimeline.Build` at 100% on a worker, and a loading-screen main thread that was
   ~44% in `ManualResetEventSlim.Wait` under `ProjectWorld`.
   - Control that failed: in mode 0, `MomentScene.Pose` had **zero** samples, against a `camera pose` timer of ~3 ms.
   - Either way, the instrument was not looking at a playing frame.
   - `--measure`'s stdout is flushed only at exit, so no line can be waited on. The fix is a fixed 230 s delay
     inside a 180 s measure.
2. **`dotnet-trace 10`'s `dotnet-sampled-thread-time` enables only the sampler.** `dotnet-trace list-profiles` shows
   no JIT or loader events in that profile, so `--providers Microsoft-Windows-DotNETRuntime:0x18:5` was added.
   About 20% "Missing Symbol" remained in the load-time traces, all threads pooled. In the playback traces the main
   thread resolved all but `?!?` native frames.
3. **The default 1 kHz sampling roughly doubles the frame.** The traced seconds ran at **15.3 ms** against 8.4 ms
   untraced in the same run (P2). `DOTNET_EventPipeThreadSamplingRate=10` did **not** remove it: G's traced seconds
   ran at **16.4 ms** against 8.6 ms in seconds 20-45 of the same run.
   - The phases sum to ~10.2 ms of that 16.4, so ~6 ms per frame is outside every timer: the runtime suspension the
     sampler imposes.
   - The sampler cannot see that time, so its shares are shares of the ~10 ms of real work.
   - The real work is inflated too. In the traced seconds `camera pose` was 4.39 ms against 3.26 untraced, and
     `camera particles` 2.52 against 1.52.
   - **Every share below is of a perturbed frame.** The ranking is usable; the absolute milliseconds are not.
4. **About 44% of main-thread samples end in `Thread.PollGCWorker`.** The sampler stops a thread at a GC safe
   point, so code with explicit polls is over-represented in EXCLUSIVE time. The main case is
   `Buffer.BulkMoveWithWriteBarrier`, which polls between chunks.
   - Its 18.9% exclusive share is an upper bound, not a measurement.
   - INCLUSIVE time along a stack is not biased that way.
   - The phase cross-check below holds within about 2 points, and that check uses inclusive time.

## Control

- **Phase shares against `--measure`'s timers, same seconds.** Timer shares are of the phase sum (sound + camera +
  advance + hud). Sampled shares are inclusive, of main-thread samples.

| phase (timer, ms in traced seconds) | G timer share | G sampled | E timer share | E sampled |
|---|---|---|---|---|
| camera (`PlaceCamera`) 8.44 / E 1.99 | 83% | 79.5% | 59% | 58.8% |
| camera pose (`MomentScene.Pose`) 4.39 / 0.57 | 43% | 41.1% | 17% | 17.0% |
| camera setcamera (`Device3D.SetCamera`) 1.45 / 0.00 | 14.3% | 13.8% | 0% | 0.3% |
| camera particles (`FramePresenter.DrawOver`) 2.52 / 1.39 | 24.8% | 23.7% | 41.5% | 40.5% |
| hud (`ViewerPresenter.BuildOverlay`) 0.70 / 0.60 | 6.9% | 7.2% | 18% | 16.3% |
| advance (`ViewerSession.Simulate`) 0.81 / 0.60 | 8.0% | 7.6% | 18% | 16.4% |
| sound (`ViewerSession.UpdateListener`) 0.22 / 0.16 | 2.2% | 2.0% | 4.8% | 0.0% (inlined; under 5 samples) |

  Agreement is within about 2 points everywhere except E's sound, where the sample count is too small to read.
- **The empty view shrinks the profile as it should.** `SetCamera` goes from 13.8% to 0.3% and `MomentScene.Pose` from
  41.1% to 17.0%. Particles remain, at 1.39 ms; they do not depend on what is in view. Untraced frames: E 3.47 ms,
  G 8.18 ms.
- **Mode 0 moves the draw onto this thread, and only the draw.** In M0, `ViewerSession.Draw` → `Device3D.DrawFrame`
  is 15.3% of samples. Over the whole run `draw` is 1.83 ms of 10.87 (17.5% of the phases), and mode 2's
  `render thread draw` is 1.81 ms. The rest of M0's ranking matches G's. In mode 2 there is **no main-thread wait**:
  `ManualResetEventSlim.Wait` is 0.4% of samples, and `camera wait` is 0.06-0.11 ms.

## Environment

Windows 11 Pro 10.0.26300; .NET 10. Release, quick JIT on (finding 84). G's untraced whole-run frame is 8.18 ms. Three
things are not being drawn or run (D129):

- skinning, which runs on the GPU;
- muzzle flashes and shell ejection, unchecked;
- decode, which has finished by the traced window (no `DemoTimeline.Build` samples in G).

## Result: G, main thread, 24,996 ms, ~2,500 samples at 10 ms

Signatures are stripped, and `Tf2DemoSalvage.` is dropped from names. One sample is ~0.04%.

### Top 40 inclusive (below the message loop)

| % | method |
|---|---|
| 96.9 | Presentation.ViewerSession.Idle |
| 96.5 | Presentation.ViewerSession.RenderFrame / FrameSequence.Run |
| 89.3 | Presentation.FrameSequence.Time |
| 79.5 | Presentation.ViewerSession.PlaceCamera |
| 79.4 | Presentation.ViewerSession.UploadCamera / ViewerPresenter.UploadCameraOver |
| 43.7 | CoreLib Thread.PollGCWorker (sampler safe point, see fault 4) |
| 41.1 | Scene.MomentScene.Pose |
| 39.2 | Scene.EntityModelSet.Instances |
| 23.7 | Presentation.FramePresenter.DrawOver |
| 18.9 | CoreLib Buffer.BulkMoveWithWriteBarrier |
| 13.8 | Render.Device3D.SetCamera |
| 12.5 | Animation.SkeletonPose.Build |
| 11.6 | Presentation.BurstEffects.Step / StepOver |
| 10.1 | Animation.AnimatingEntity.SetupBones |
| 9.3 | Scene.ModelLighting.For |
| 7.6 | Presentation.ViewerSession.Simulate |
| 7.2 | Presentation.ViewerPresenter.BuildOverlay / FramePresenter.BuildOverlay |
| 6.6 | Presentation.VguiHud.Frame |
| 6.5 | Scene.LevelLighting.Entry (and its lambda) |
| 6.4 | Presentation.ViewerSession ctor lambda (moment shown) |
| 6.2 | Presentation.MomentPresenter.Show |
| 6.1 | Presentation.FrameParticles.BuildOver |
| 5.8 | Scene.LevelLighting.SunFrom |
| 5.7 | Scene.Hud.VguiPanel.PaintTraverse |
| 5.2 | Scene.DetailSprites.Build |
| 5.0 | Scene.PropModels+SkinnedModel.LocalsInto |
| 4.8 | Scene.EntityModelSet.FlexFor |
| 4.8 | Animation.SkeletonPose.Accumulate |
| 4.6 | Scene.EntityModelSet.PosedAttachmentMatrix |
| 3.5 | Scene.Hud.VguiRichText.Paint |
| 3.4 | CoreLib Stopwatch.GetTimestamp |
| 2.7 | Presentation.EffectEntities.BuildEffectEntities |
| 2.7 | WinForms Control.WmPaint |
| 2.5 | Core.Scene.ScenePropTrack.At |
| 2.2 | Scene.WorldVisibility.Descend |
| 2.1 | Scene.OverlayRenderLists.Order |
| 2.0 | Presentation.ViewerSession.UpdateListener |
| 1.9 | Scene.ParticleEffects.Gather |
| 1.5 | CoreLib StreamWriter.Flush (FileLogWriter.Write) |
| 0.4 | CoreLib ManualResetEventSlim.Wait |

### Top 40 exclusive (biased toward GC-poll sites, see fault 4)

| % | method | | % | method |
|---|---|---|---|---|
| 18.9 | Buffer.BulkMoveWithWriteBarrier | | 1.4 | OverlayRenderLists.Order |
| 6.1 | SkeletonPose.Build | | 1.4 | VisibleWorld.Batches |
| 5.8 | LevelLighting.SunFrom | | 1.3 | VisibleWorld.Surfaces |
| 4.5 | EntityModelSet.Instances | | 1.3 | EntityModelSet.SettleEach |
| 3.5 | Hud.VguiRichText.Paint | | 1.2 | Object.MemberwiseClone (from FlexFor) |
| 3.4 | Stopwatch.GetTimestamp (from Instances) | | 1.2 | Device3D.SetCamera |
| 2.6 | ModelLighting.For | | 1.1 | ActorFace.InterpolateAndLatch |
| 2.4 | DetailSprites.Build | | 1.0 | SkinnedModel.PoseBetween |
| 2.1 | Buffer.MemmoveInternal | | 1.0 | WinForms TextExtensions.DrawText |
| 2.0 | StudioPoseBlend.BlendInto | | 0.9 | StudioFlexRules.Accumulate |
| 2.0 | Hud.VguiPanel.PaintTraverse | | 0.9 | DemoTimeline.Sample |
| 2.0 | WorldVisibility.Descend | | 0.9 | SkeletonPose.Accumulate |
| 1.9 | ArraySortHelper<(int,float,byte,bool)>.Sort | | 0.9 | SkinnedModel.PoseOf |
| 1.7 | Buffer.ZeroMemoryInternal (Array.Clear) | | 0.7 | DecalMesh.Build |
| 1.6 | AudioOutput.Play | | 0.7 | FramePresenter.DrawOver |
| 1.5 | PixelVisibility.FractionVisible | | 0.6 | Array.Copy |
| 1.5 | OSFileStreamStrategy.Write (log flush) | | 0.6 | WinForms TextExtensions.MeasureText |
| 1.5 | FaceFlex.IntoFlexedSlots | | 0.6 | HashSet.Resize |
| | | | 0.5 | ViewmodelScene.Build |
| | | | 0.5 | MomentScene.AddViewmodel |

### Who calls `BulkMoveWithWriteBarrier` (share of main-thread samples)

| % G | % E | caller |
|---|---|---|
| 2.7 | 5.4 | `List<ParticleBurst>.Enumerator.MoveNext`: `foreach` by value over a large reference-holding record struct |
| 2.5 | 4.9 | `ScenePropTrack.At`, from `MomentPresenter.Show`, `DemoTimeline.AdvanceSample`, `BulletStrikes.End`, `SoundPresenter.Stepper` |
| 1.9 | — | `ParticleEffects.Gather` |
| 1.2 | — | `EntityModelSet.Instances` |
| 1.1 | 3.9 | `EffectEntities.BuildEffectEntities` |
| 1.1 | 3.0 | `EntityModelSet.SimulateEach` |
| 0.9 | 2.1 | `PlayerProps.Add` |

In E, the empty view, `BulkMoveWithWriteBarrier` is **33% exclusive**. That is the largest single item in a frame that
draws nothing, but it carries fault 4's bias, so it is a lead to time and not a cost.

## What this settles and what it does not

- **The frame is not unexplained by the timers.** Over the whole G run, `camera` 6.30 = pose ~3.3 + setcamera ~1.4 +
  particles ~1.5, then advance 0.73, hud 0.69, between 0.31. The sampler agrees with that split. Nothing large hides
  outside the timers, and there is no main-thread wait in mode 2.
- **The pose is all `EntityModelSet.Instances`.** Bones (`SkeletonPose.Build` + `SetupBones`) are ~12%. Lighting
  (`ModelLighting.For` → `LevelLighting.Entry` → `SunFrom`) is ~9%, and `SunFrom` alone is 5.8% exclusive. Flex is
  ~5%. Each figure is a share of the perturbed frame.
- **Not settled:** absolute ms per method. A sampler that roughly doubles the frame cannot give them. The way to get
  them is a stage timer on each lead, or an ETW kernel-sampled profile (which needs elevation; not attempted).
