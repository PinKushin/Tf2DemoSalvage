# Measurement 2026-10-22: what a frame snapshot costs (B581, D228 step A)

## Record (written and committed before the first run)

**Observation.** The render-thread prototype (`2026-10-22-render-thread-prototype.md`, branch
`feat/queued-material-system`) found that without a frame snapshot a render thread saves at most ~0.5 ms, and that
joining later crashes on shared lists. `draw` is 2.1-2.8 ms of a 13.4 ms f12 frame. D228's queued design needs a
snapshot of everything `Draw` reads that a later game-thread stage rewrites; its copy cost is unmeasured.

**What `Draw` reads, sorted into three buckets** (read from the code at `504673db`):

- **(a) built fresh, never written again before the next draw: a reference costs nothing.** The viewmodel camera
  (`ViewmodelCamera?.ToMatrix()` allocates), `ScreenOverlay`.
- **(b) same object rewritten in place by the next frame's stages: needs a copy or a double-buffer.**
  Every `ModelInstance.Matrix` (`EntityModels.Placement.cs:83-120`, `_placements[entity]` rewritten each frame, static
  props included, by `CopyTo`), every `ModelInstance.Bones` (`Skinning`, `:224-249`, `_skinning[entity]` reused "because
  an instance list is consumed within the frame that built it"), `Locals`, `Flex`, the instance lists themselves
  (`_moment.Instances`, `ViewmodelInstances`, `UndrawnInstances`), the VGUI draw list (`VguiDrawList.Clear` per frame),
  the particle batches' corners (`FramePresenter.cs:429` into `Device3D.SetParticles`, which also `Clear`s
  `_particleBatches`), the leaf lines.
- **(c) Device3D work done in game-thread stages, touching the immediate context or Device3D fields `DrawFrame` reads.**
  Not a copy: in queued mode it moves to the render thread, with its inputs joining the snapshot. `Device3D.SetCamera`
  (world cull, overlays, sky, water plan, detail-sprite rebuild, the constant upload through `_context`; timed today as
  `camera setcamera` and its seven sub-parts), `SetParticles` (sheet uploads through `_context` on first sight; timed as
  `camera particles/hand`), `UploadShotDecals` and `UpdateLightmap` (through `_context`; not timed today).

**Question.** On f12 at tick 30000 first person, what does a deep copy of bucket (b) cost per frame at the hand-off, and
how much game-thread time does bucket (c) take?

**Method.** A throwaway `SnapshotCostProbe` behind `TF2VIEW_PROTO_SNAPSHOT=1`: in `ViewerPresenter.DrawSceneOver`, just
before `device.DrawScene`, deep-copy every bucket (b) input (fresh arrays, element by element); the particle corners are
copied just before `SetParticles`. Reported as `--measure` parts: `snap copy` and `snap particles` (ms), and counts carried
from the copy itself as parts whose printed value is the per-frame count (`snap n instances`, `snap n bones`, `snap n KB`,
`snap n quads`, `snap n corners`). Bucket (c): the existing `camera setcamera*` and `camera particles/hand` parts, plus
`devc decals` and `devc lightmap` timed around the two uploads (these two are always timed, probe or not).

Runs, each `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 [TF2VIEW_PROTO_SNAPSHOT=1] pwsh
C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 <Release tf2demoview.exe> D:/measure-f12/f12.dem --tick 30000
--first-person --measure 20 +fps_max 0`, order A B B A (A = probe off, B = probe on). Machine load read before each run
with `Get-CimInstance Win32_Processor | Select LoadPercentage`.

**Control.** A runs carry no `snap` parts at all (the probe off is the zero). Before believing a small `snap n
instances`, it must be in the hundreds on f12 (finding 82: ~1,600 static props are drawn there); a count near zero means
the probe copied the wrong list. The decision rests on the `snap` columns, not on frame means (same-mode spread up to
3.3 ms in the prototype run).

**Verdict rule (task checkpoint 1).** If `snap copy + snap particles` exceeds about half the best-case saving (half of
`draw`, ~1.05-1.4 ms), stop and report. Bucket (c) is reported separately as design cost, by name.

## Follow-up record (written before runs 5-6)

Runs 1-4 put about 3.3 MB of the 3.4 MB copied per frame in `ModelInstance.Flex`, which is bucket (b) (the per-entity
stream is rewritten in place, `EntityModels.FacesAndLighting.cs:51-72`) and is the one array a double-buffer would
remove from the copy most cheaply (a second stream per entity, alternated per frame). **Question:** how much of
`snap copy` is the flex copy? **Method:** the same probe, the flex clone timed on its own as `snap of which flex`, with
its size as `snap n flex KB`; two B runs, same command. **Control:** `snap n flex KB` must be most of `snap n KB`
(~3,300 of ~3,400) or the split timed the wrong arrays.

## Results

Release build of `361b040c` (runs 5-6) and `9d25c099` (runs 1-4); same command each run. Means are over the run's
per-second lines.

| # | mode | load % | frames | mean ms | draw ms | snap copy | of which flex | snap particles | copied KB / frame | flex KB | instances | bones | corners | quads |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | A | 47 | 1206 | 16.59 | 2.62 | - | - | - | - | - | - | - | - | - |
| 2 | B | 45 | 1261 | 15.86 | 3.17 | 1.03 | (not split) | 0.19 | 3,371 | | 218 | 685 | 7,498 | 470 |
| 3 | B | 90 | 1265 | 15.81 | 3.29 | 1.09 | (not split) | 0.17 | 3,391 | | 218 | 688 | 7,434 | 471 |
| 4 | A | 52 | 1510 | 13.25 | 2.13 | - | - | - | - | - | - | - | - | - |
| 5 | B | 26 | 1200 | 16.67 | 3.45 | 1.19 | 0.87 | 0.19 | 3,379 | 3,333 | 219 | 687 | 7,434 | 468 |
| 6 | B | 3 | 1353 | 14.78 | 3.05 | 1.06 | 0.80 | 0.17 | 3,383 | 3,337 | 219 | 688 | 7,414 | 469 |

Bucket (c), game-thread device work (ms/frame, runs 1-6 range): `camera setcamera` 1.42-1.85, of which `upload` (the
only immediate-context call in it) 0.002-0.010 and the rest CPU (`batches` 0.46-0.63, `detail` 0.41-0.51, `overlays`
0.30-0.40, `sky` 0.23-0.28); `camera particles/hand` 0.12-0.37; `devc decals` 0.085-0.096; `devc lightmap` 0.000-0.003.

**Controls.** A runs print no `snap` part. `snap n flex KB` is 3,333-3,337 of 3,379-3,383 copied KB: the split timed the
flex arrays. **The record's instance control was wrong and is corrected here, not hidden:** it predicted "hundreds" from
finding 82's ~1,600 static props, but the props the moment poses per frame are ~35-50 (`posed`/`built` in the rebuild
reports) and the copy saw 218-219: that number includes everything in the drawn list (static props and brush entities
collated into the list as well as the posed ones, the viewmodels and the monitor-only list). It is stable across four
runs and far from zero, so the probe copied a real list; which entries make up the 218 was not broken down.

**Results, in four lines.**

- **A full deep copy costs 1.19-1.38 ms a frame** (`snap copy` + `snap particles`), 3.4 MB.
- **0.80-0.87 ms of it is one array kind, `ModelInstance.Flex`** (3.3 MB: the per-entity face stream, rewritten in
  place). Everything else (218 matrices, ~690 bone matrices, local-light lists, 470 VGUI quads, leaf lines) is
  0.19-0.32 ms, and the particle corners (~7,400) another 0.17-0.19.
- **`draw` itself read slower whenever the probe ran**: 3.05-3.45 ms on B against 2.13-2.62 on A, in every pairing. It is
  observed, not explained; copying 3.4 MB straight before the draw evicting the cache the draw reads is a candidate,
  untested.
- **Bucket (c) is mostly CPU that must stay on the game thread**: the cull inside `SetCamera` writes the frustum, eye and
  views that `PoseOver` reads in the same game frame, so only its immediate-context upload (≤0.01 ms) moves; the
  Device3D fields the cull writes and `DrawFrame` reads (`VisibleBatches`, `TranslucentLeaves`, `Overlays`,
  `SkyBatches`, `_skyCamera`, the water plan, the detail sprites) join bucket (b) as state to double-buffer.

## Verdict

**Checkpoint 1 fails as the full copy.** 1.19-1.38 ms against half the best-case saving (half of a 2.1-2.8 ms draw:
1.05-1.4 ms), plus a slower draw when the copy runs. Stopped before step B, as the task requires.

**The double-buffer shape is not ruled out, and the numbers say where it pays.** Double-buffering only the flex stream
(a second stream per entity, alternated per frame, which `FlexFor` already keeps one of) removes 0.80-0.87 ms; the
particle corners the same way another 0.17-0.19. What would be left to copy is 0.19-0.32 ms (measured as the remainder
of `snap copy`, not run alone), against a saving ceiling of 2.1-2.8 ms. That is a design the owner can choose; it is
also the larger rewrite, because every bucket (b) and (c) producer named above has to write into the frame's half
instead of one shared object. **Interpolated:** the remainder's cost if bones and matrices were also double-buffered,
and whether the draw slowdown goes away without the bulk copy.
