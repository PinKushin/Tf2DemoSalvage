# Measurement 2026-10-26: the model path's call kinds, and the constant rings and batched occlusion against them (B597)

## Record (written and committed before the first run)

## Observation

`2026-10-25-render-thread-cpu-vs-gpu.md`: on f12 gummo first person, mode 2, 1280x720, the render thread's 2.2 ms is CPU
submission; OpaqueModels is 1.06-1.08 ms for 209 draws and 421 maps (~5 us a draw), the world 0.15-0.16 ms for 378 draws
and 379 maps (~0.4 us a draw), Occlusion 0.36-0.38 ms for 376 one-quad draws and 752 maps. That the maps are the model
path's cost was an inference. **The world pass already argues against it**: it maps the material buffer once a draw
(`SetMaterial`, `WRITE_DISCARD`) and costs 0.4 us a draw with that map, so one discard-map a draw cannot be 5 us.

## Question

(1) Which kinds of work in the model draw path take the OpaqueModels pass's CPU: the pre-bucket reports, per-instance
setup, pipeline binds, the face upload, model constants, bone constants, cubemap choice, per-batch state and resource
binds, the material's binds, proxies and map, the draw call, the bookkeeping after it, and the renderable's own report
and decals? (2) What do the per-slot constant rings (`ConstantRing`, D3D11.1 offset binding, `WRITE_NO_OVERWRITE`) and
the batched occlusion upload each take off the render thread, OpaqueModels, Occlusion and the frame?

## Prediction (before the runs)

- **Spans (A):** the map-and-write spans (ModelConstants + Bones + MaterialMap) are under half of OpaqueModels' CPU; the
  per-instance and per-batch work around them (PipelineBinds, BatchState, MaterialBinds, Bookkeeping, Renderable,
  Diagnostics) is the larger share. MaterialMap costs about the same per lap in World and in OpaqueModels.
- **Rings (R against B):** OpaqueModels falls by 0.05-0.30 ms; World by under 0.05 ms.
- **Occlusion (A against R):** Occlusion falls from ~0.37 ms to under 0.15 ms (the per-query `GetData` polls stay).
- **Render thread draw (A against B):** falls by 0.25-0.6 ms. **Frame mean**: unchanged within its spread — the render
  thread at 2.2 ms is not the frame's critical path at ~9.5 ms.
- **Driver worker** ms a frame: lower in B than A, by an amount this method may not resolve.

Falsified per line if the measured value is outside the stated range beyond the run-to-run spread.

## Instrument

- `--measure`'s pass ledger (`PassProfiler`), unchanged, and **`SubmitSpans`** added on this branch (`c0584951`): each span
  is the clock at the boundary it names, read where the work runs (B243); the profiler differences span totals at its
  marks, so a span lands in the pass it ran in; `unspanned` is the pass's CPU minus its spans. On only inside a profiled
  frame. **TimerOverhead** is two laps back to back once per model: one lap's own cost, so the instrument's perturbation
  is laps x that. Bytes are those written into mapped memory (the face's are counted only when it is uploaded).
- Spans are valid in OpaqueModels (every model draw restarts at its entry) and for SetMaterial in World (it restarts at
  its own entry); a span seen in another pass is that pass's model draws.
- Driver worker CPU: the 2026-10-25 thread sampler beside each run.

## Method

Release builds: **A** = `c0584951` exported to `D:/b597r/A` (spans, discard-per-draw constants, per-query occlusion);
**B** = this branch's head (rings, batched occlusion); **R** = B with `TF2VIEW_CONSTANT_RING=0` (discard-per-draw
constants, batched occlusion), so A-R is the occlusion change and R-B the rings. Each run, one viewer, exclusive, through
`2026-10-25-render-thread-cpu-vs-gpu/run.ps1` (its quiet gate, its command: f12 tick 30000, gummo first person,
1280x720, `--measure 20 +fps_max 0 +mat_queue_mode 2`). Order **A B R A B R A B R** (`grid.ps1`), three each,
interleaved. A run failing the gate or the after-load check (any sample of three over 25%) is discarded and repeated at
the end. Raw stdout and sampler CSVs in `2026-10-26-b597-constant-ring-and-occlusion/`.

Reported per condition, as ranges over its runs: OpaqueModels CPU, Occlusion CPU, World CPU, the passes' CPU sum,
`render thread draw`, frame ms, the driver worker ms a frame, each span's ms and laps a frame, and the change as % of the
frame. A difference counts when it is at least 0.01 ms and the ranges do not overlap.

## Identity (output parity must be exact)

- **In-process, the strong one** (committed, `ConstantRingTests`): two offscreen renderers differing only in the rings
  draw one scene of cp_process_f12 materials (64 world batches, 48 skinned quads, lamps) and hash every pixel: equal,
  with controls (rings in use on one and not the other; a lamp 1% brighter changes the hash). A wrap test crosses every
  ring's wrap. `Count_SeveralProxiesIssuedTogether_EachCountsItsOwnQuad` holds the occlusion offsets. Rendering.Tests whole.
- **In the viewer:** `TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015 TF2VIEW_AUTOPLAY=1`, mode 0, `--measure 6`, gummo
  first person from 30000: **A, A, then B**, labels matched by tick. The A-against-A control decides which parts can say
  anything: the B599 record found image, particles and rest NOT deterministic run to run, and pose and cull
  deterministic. Where A agreed with A, B must agree with A. Expected: the image part fails its control again, so the
  viewer cannot show the image identical and the in-process test carries that claim.
- **Queue safety:** one B run, mode 2, `TF2VIEW_QUEUE_VERIFY=1`: `0 changed`. Positive control is the existing unit
  test that overwrites a handed-off matrix after `Submit` (B599 record); this change hands nothing new to the queue.

## Control

(a) Spans plus `unspanned` equal the pass CPU by construction, so the control is that `unspanned` is small against the
pass (under 0.1 ms) and that TimerOverhead x laps is small against the spans. (b) Draw counts per pass equal across A, B,
R (same work). (c) Maps per pass: B's OpaqueModels maps equal A's (rings still map per draw); Occlusion's fall from 752
to 2. (d) The printed back buffer is 1280x720. (e) 0 frames skipped/disjoint, as before.

## Environment

This machine, NVIDIA, windowed. Builds: A `c0584951`, B the head named in the result.

## Result

(not yet run)

## Verdict

(not yet run)
