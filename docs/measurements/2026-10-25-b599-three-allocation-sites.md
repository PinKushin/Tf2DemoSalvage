# Measurement 2026-10-25: the three largest per-frame allocation sites removed (B599)

**Status: written and committed before the run.** Sections above `## Result` are fixed from here; deviations go under Result.

## Observation

The allocation trace (measurements/2026-10-25-allocation-attribution.md, run G) put three call sites at the top of
f12 gummo first person's ~1.04 MB a frame, each allocating per element rather than per frame:

- `StudioDecalProjection.Vertex` → `Influences`, a `yield` iterator per vertex: 9.6% of bytes (12.2% in the empty view).
- `Device3D.File` → `Classify`'s capturing factory for `TranslucencyCache.For`: a closure and a `Func<bool>` per
  collated instance on the render thread, 8.8% (the
  `docs/memory/a-capturing-cache-factory-allocates-on-a-hit.md` pattern).
- `DetailSprites.Build`: two fresh lists (grown by `AddWithResize`) and a capturing sort comparison a frame, 8.9%.

Together ~27% of the gummo bytes. Build `C` (this branch) removes all three: an in-place weighted sum, a state
argument with a `static` lambda, and thread-static scratch lists with a comparison made once.

## Question

How much do allocated MB a frame, gc paused ms a frame, gen0 collections and the frame mean move on f12 gummo first
person and the empty view?

## Prediction

- Allocation falls by 0.22-0.32 MB a frame in gummo (27% of 1.04 ± the run spread) and by ~0.06-0.08 MB in the empty
  view (the decal iterator's 12% plus the detail lists, which the empty view builds too). Falsified if gummo falls by
  under 0.15 MB.
- gen0 count per 60 s falls roughly in proportion (~25% in gummo). The gc pause a frame falls by less than in
  proportion, since a gen0 pause scales with survivors, not with garbage. Inconclusive if inside the spread.
- The frame mean: no prediction beyond "not slower"; a 0.05 ms gain would be under the 9.47-10.51 ms gummo spread of
  measurements/2026-10-25-b585-matrix-pool.md.

## Method

Two Release builds, copied out: **A** `D:/measure-f12/b585-pool-before/` (`077d0175`, `origin/main`'s code), **C** this
branch's commit, `dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` through `run-exclusive.ps1 -Shared`, copied
to `D:/measure-f12/b599-after/`. Interleaved A C C A A C per subject (three each), 60 s each, one viewer at a time
through `run-exclusive.ps1`, quiet gate (three `Win32_Processor.LoadPercentage` samples under 25%, retried) and no other
`tf2demoview` before each:

- gummo: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo
  --first-person --measure 60 +fps_max 0`
- empty: `TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe>
  D:/measure-f12/f12.dem --tick 30000 --measure 60 +fps_max 0`

Read from each run's `measured …` and `gc:` lines: frames, mean ms, MB a frame, gc ms a frame, gen0, and the main
thread's bytes a frame (the `render thread … bytes a frame` column, which the trace showed is the main thread).

Safety: one C run gummo with `+mat_queue_mode 2` and `TF2VIEW_QUEUE_VERIFY=1`; its log's last `queue verify` line, and
any `inputs changed` line. The same log's `queue verify` lines' timestamps against the run's load and play, to say what
the counter counts.

## Control

- Same-build spread beside each difference (three runs a build).
- Ticks advanced ≈ 66.7 × 60 per run.
- Equivalence of each change is held by tests, each sabotaged: `StudioDecalProjectionTests` (a thousand vertices
  allocate what one does; a three-bone weighted sum), `DetailSpritesTests` (a repeated build allocates 0 and builds
  the same quads), `TranslucencyCacheTests` (a hit allocates 0), and the existing conformance suites for the values.
- The removed sites must be gone from a trace of C: one 60 s `GCAllocationTick` trace of C gummo, same method as the
  attribution file, must show no `<Influences>d__11`, no `Device3D.File` closure, and no `DetailSprites.Build` list
  growth. If any is still there the change did not reach the build that ran.

## Environment

Windows 11 Pro 10.0.26300, .NET 10, 1280x720 windowed. **C** = `a7fd6b65`, Release, 0 warnings, copied to
`D:/measure-f12/b599-after/`. No other agent timing. Every run passed the quiet gate (samples 1-23% on the accepted
try; three runs needed a second try after a spike: 21/21/100, 65/13/13); loads after 1-28% except run 01's
15/46/100. Raw stdout, the logs' names and the scripts are in the session scratchpad (`alloc/series/`), not committed.

## Result

Every timed run ended at tick 34013-34014 (≈ 4,000 advanced in 60 s: control passes). `main B/frame` is the column
`--measure` labels `render thread … bytes a frame`, which is the main thread (B600).

| run | build | subject | frames | mean ms | MB/frame | gc ms/frame | gen0 | gen0/frame | main B/frame | decals ms | detail ms |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 01 | A | gummo | 5589 | 10.74 | 1.19 | 0.23 | 938 | 0.168 | 997,228 | 0.348 | 0.508 |
| 02 | C | gummo | 6502 | 9.23 | 0.87 | 0.17 | 789 | 0.121 | 755,926 | 0.240 | 0.416 |
| 03 | C | gummo | 6191 | 9.69 | 0.90 | 0.18 | 765 | 0.124 | 768,432 | 0.253 | 0.429 |
| 04 | A | gummo | 6679 | 8.98 | 1.18 | 0.20 | 1114 | 0.167 | 955,303 | 0.263 | 0.434 |
| 05 | A | gummo | 6597 | 9.10 | 1.15 | 0.20 | 1083 | 0.164 | 958,433 | 0.269 | 0.443 |
| 06 | C | gummo | 6772 | 8.86 | 0.84 | 0.15 | 800 | 0.118 | 752,369 | 0.223 | 0.404 |
| 07 | A | empty | 16972 | 3.54 | 0.49 | 0.07 | 1189 | 0.070 | 455,204 | 0.125 | 0 |
| 08 | C | empty | 15995 | 3.75 | 0.44 | 0.07 | 998 | 0.062 | 405,342 | 0.121 | 0 |
| 09 | C | empty | 16810 | 3.57 | 0.44 | 0.07 | 1057 | 0.063 | 402,034 | 0.110 | 0 |
| 10 | A | empty | 17385 | 3.45 | 0.49 | 0.07 | 1202 | 0.069 | 451,191 | 0.120 | 0 |
| 11 | A | empty | 17168 | 3.50 | 0.49 | 0.07 | 1177 | 0.069 | 454,118 | 0.122 | 0 |
| 12 | C | empty | 17001 | 3.53 | 0.43 | 0.07 | 1048 | 0.062 | 401,136 | 0.110 | 0 |

`decals` and `detail` are the frame-weighted means of the per-second `camera particles/decals` and `camera setcamera
detail` parts (the stages holding the decal projection and `DetailSprites.Build`).

- **Allocation, gummo:** A 1.15-1.19, C 0.84-0.90 MB a frame; the ranges separate, about **−0.30 MB (−26%)**. Main
  thread A 955-997k, C 752-768k bytes, about −0.21 MB; the other ~0.09 MB is the render thread's `Classify` closure.
- **Allocation, empty:** A 0.49 throughout, C 0.43-0.44: **−0.05 to −0.06 MB (−11%)**, the decal iterator alone (the
  empty view collates no instances and builds no detail sprites: both probes read 0 in its trace).
- **gc pause, gummo:** A 0.20-0.23, C 0.15-0.18 ms a frame; the ranges separate, about **−0.045 ms (0.5% of the
  frame)**. Empty: 0.07 in all six.
- **gen0 per frame:** gummo A 0.164-0.168, C 0.118-0.124 (**−28%**); empty A 0.069-0.070, C 0.062-0.063 (−10%).
- **Stages:** `decals` gummo A 0.263-0.348, C 0.223-0.253 ms (separate; ~−0.03); empty A 0.120-0.125, C 0.110-0.121
  (overlap). `detail` gummo A 0.434-0.508, C 0.404-0.429 (separate by 0.005; ~−0.03).
- **Frame mean:** gummo A 8.98-10.74 (mean 9.61), C 8.86-9.69 (9.26); pairs in run order C−A −1.51, +0.71, −0.24.
  Empty A 3.45-3.54 (3.50), C 3.53-3.75 (3.62); pairs +0.21, +0.07, +0.03. **Neither separates**; the empty view's C
  runs are not faster and two of three pairs lean slower, within the 3.41-3.81 same-build spread of
  measurements/2026-10-25-b585-matrix-pool.md.
- **The removed sites are gone (trace of C, gummo, 60 s, same method as the attribution record):** `<Influences>`
  0 bytes (A 729 MB), `Device3D.File` 0.1 MB, one tick (A 648 MB), `DetailSprites.Build`/`EmitGroup` 0.1 MB, one tick
  (A 648 MB). Total ticked 4,886 MB against A's 7,326 in the same 60 s; the render thread 13.3% (A 18.1%).
- **Queue safety (C, run 13):** `+mat_queue_mode 2`, `TF2VIEW_QUEUE_VERIFY=1`, gummo first person, log
  `viewer-20261026-010036-6056.log`: `QueuedThreaded on 6 physical cores`, last line `queue verify: 731500 frames
  checked, 0 changed`, no `inputs changed` line.
- **What the verifier's count counts:** every frame handed to the queue since the device was made, not the measured
  frames. In run 13 the count read 728,500 at 01:03:14 and 729,000 at 01:03:49, around `first frame after the opening
  state` at 01:03:46.5: ~728,700 checks were the loading overlay, drawn at ~3,850 frames a second through the ~190 s
  decode (D182 draws behind the load), and ~2,800 were play (2,644 measured frames plus the opening). The B585 record's
  851,000 splits the same way: 758,000 at 23:17:00, 849,000 at 23:17:50, first played frame 23:17:46.
- **Differential against A (`TF2VIEW_FRAME_HASH=1`, `TF2VIEW_FIXED_FRAME=0.0125`, gummo, runs 14-16: A, A, C).**
  Labels matched on the ticks: 2,992-3,002 common. **The control fails for most parts:** A against A differs in the
  image hash on 2,984 of 2,992 labels, particles on 220 and rest on 18, so those hashes are not deterministic run to
  run and cannot show C bit-identical. Where A agreed with A, C agreed on **pose 2,985 of 2,985 and cull 2,992 of
  2,992**, differed on particles 40 of 2,772 and rest 21 of 2,974 (inputs this change does not touch: particles and the
  HUD). Bit-identity of the decal and detail outputs rests on the unit tests and conformance suites, not on this run.

## Verdict

**Allocation prediction confirmed for gummo (−0.30 MB, inside 0.22-0.32), falsified narrowly for the empty view
(−0.05 to −0.06 against 0.06-0.08); gen0 confirmed; the gc pause fell ~0.045 ms a frame, which clears the owner's
0.01 ms; the frame mean is inconclusive.** A quarter of the garbage and of the collections is gone at no measured cost to
the stages (both moved stages got faster), so the change is kept; the frame-level gain is below this method's
resolution. The rest of the ~0.85 MB is B599's open list.
