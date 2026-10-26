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
to 2. (d) The printed back buffer is 1280x720. (e) 0 frames skipped/disjoint, as before. (f) *Added after this record was
committed and before run 01* (advisor review): B's report says `constant rings: on` and R's says it is off for the
environment switch (`793b7ca2`); A predates the line. Without it R and B are indistinguishable in the output. B and A are
both built from `git archive` exports on `D:` (`D:/b597r/A` at `c0584951`, `D:/b597r/B` at `793b7ca2`), so path, missing
`.git` and build environment are common to both; each export's first build reported 117 warnings and a clean rebuild of
A reported 0 (not read further: the code is the branch's, which builds with 0).

## Environment

This machine, NVIDIA, windowed. Builds: A `c0584951`, B the head named in the result.

## Result

Raw stdout, stderr and sampler CSV of every run, discarded ones included, in `2026-10-26-b597-constant-ring-and-occlusion/`;
`grid.ps1` is the order, `summ.ps1` every pass and span of a run, `table.ps1` the per-run rows below.

**Deviations, stated.** (1) The after-load check (any of three samples over 25%) failed on 03-R, 07-A, 08-B, 11-A, 12-B,
13-A, 14-B, 15-A and 16-B. The machine carried 15-30% load this agent did not start (other sessions' `tf2mcp`, a
browser, `claude` processes; no build or test process), and a recursive file search this agent ran by mistake overlapped
13-A. After two rounds of repeats the run stopped. **Quiet: A 01, 04; B 02, 05; R 06, 09, 10** — two runs of A and B, not
three. The eight marginal runs (after-load peaks 26-63%) are reported separately; they move every figure up together and
reproduce every difference below. (2) A first `-File` relaunch passed `R,A,B` as one string and was stopped before its
viewer started; nothing was recorded from it.

| run | cond | frame ms | render thread draw | passes CPU | World | OpaqueModels | Occlusion | driver ms/frame |
|---|---|---|---|---|---|---|---|---|
| 01 | A | 9.58 | 2.33 | 2.263 | 0.183 | 1.140 | 0.372 | 1.95 |
| 04 | A | 9.84 | 2.37 | 2.295 | 0.188 | 1.151 | 0.377 | 1.94 |
| 02 | B | 9.89 | 2.24 | 2.164 | 0.198 | 1.194 | 0.180 | 1.96 |
| 05 | B | 9.98 | 2.26 | 2.185 | 0.200 | 1.210 | 0.177 | 1.99 |
| 06 | R | 9.53 | 2.17 | 2.094 | 0.186 | 1.164 | 0.174 | 1.79 |
| 09 | R | 9.98 | 2.26 | 2.181 | 0.204 | 1.193 | 0.176 | 1.93 |
| 10 | R | 10.42 | 2.31 | 2.228 | 0.191 | 1.232 | 0.192 | 2.05 |
| marginal | A (07 11 13 15) | 10.19-10.70 | 2.46-2.59 | 2.383-2.501 | 0.193-0.196 | 1.197-1.272 | 0.392-0.417 | 2.01-2.26 |
| marginal | B (08 12 14 16) | 10.31-10.48 | 2.35-2.41 | 2.268-2.329 | 0.201-0.220 | 1.267-1.287 | 0.184-0.188 | 1.90-2.11 |

Controls: (b) draws per pass equal across A, B, R (OpaqueModels 208.4-210.7, Occlusion 374.9-376.3). (c) OpaqueModels maps
equal (419.9-422.4); Occlusion maps 752 in A, **2.0** in B and R. (d) 1280x720 in every run. (e) 0 skipped, 0 disjoint.
(f) B prints `constant rings: on`, R `off: TF2VIEW_CONSTANT_RING=0 or a test`.

**OpaqueModels' spans, quiet A (01, 04), ms a frame and laps a frame; per lap = ms / laps.** Calls per lap read from the
code (arithmetic on measured laps, not counted at the device).

| span | ms | laps | us a lap | what a lap is, from the code |
|---|---|---|---|---|
| **Flex** | **0.271-0.281** | 171.8 | 1.6 | `BindFlex`: null stream (1 IA bind) or the face uploaded: Map DISCARD + **3.16 MB a frame** copied + IA bind |
| **BatchState** | **0.162** | 206.6 | 0.78 | 3 dictionary/set tests, RSSetState, OMSetBlendState, 8 PSSetShaderResources (0, 2, 3, 4, 5, 6, 10, 11), `BindBlendModulation`, `AnimationFrame`, the per-batch `_reportedBatchMaterials.Add(modelPath)` string hash, cloak lookup |
| **Proxies** | **0.152-0.155** | 209.0 | 0.73 | `SetMaterial`'s scratch copies, the proxy chain (`ItemTintColor`, `BurnLevel`, `YellowLevel` …), gamma conversion — **World's is 0.013 ms for 379 laps, 0.035 us** |
| PipelineBinds | 0.075-0.076 | 171.8 | 0.44 | `BindPipeline` (13 calls: layout, topology, 2 null streams, 2 shaders, 2 samplers, 2 camera binds, 2 SRVs, water view) + IASetVertexBuffers + colour stream |
| Cubemap | 0.071 | 171.8 | 0.41 | `BspCubemaps.Closest` over the map's cubemaps + a string-set add |
| Renderable | 0.060-0.061 | 171.8 | 0.35 | `ReportBodySelection`, `ModelDecals.For`, a decal draw where there is one |
| Bookkeeping | 0.045-0.047 | 171.8 | 0.27 | `_reportedDraw`/`_reportedDrawAt` keyed on (string, pass, body), a Stopwatch read, the stream unbinds |
| ModelConstants | 0.039-0.042 | 172.9 | 0.23 | Map DISCARD, 544 B cleared and written, Unmap, VS + PS bind |
| MaterialBinds | 0.036-0.037 | 209.0 | 0.17 | 3 PSSetShaderResources (7, 8, 9), OMSetDepthStencilState |
| MaterialMap | 0.034 | 209.0 | 0.16 | checks, Map DISCARD, 288 B copied, Unmap, PS + VS bind — **World's 0.045 ms for 379, 0.12 us** |
| Bones | 0.026-0.027 | 32.7 | 0.80 | Map DISCARD, 6,144 B cleared and written, Unmap, VS bind |
| DrawCall | 0.025-0.026 | 206.6 | 0.12 | the cloak lookup and `Draw` |
| InstanceSetup | 0.020-0.022 | 171.8 | 0.12 | argument checks, vertex-buffer lookup |
| TimerOverhead | 0.003-0.005 | 171.8 | 0.02 | two laps back to back: one lap's cost |
| unspanned | 0.114-0.116 | | | the bucket loop, `ModelBatches` lookups, call overhead |
| Diagnostics | 0.0003 | 1.0 | | `ReportRepeatedModels` + `ReportDrawOrder` (both nearly free) |

**The three constant maps together are 0.100-0.104 ms of OpaqueModels' 1.140-1.151 (9%).** The instrument's own cost
is about 3,000 laps x 0.02 us = ~0.06 ms, which matches A's OpaqueModels (1.14-1.15) against the uninstrumented
2026-10-25 record's 1.06-1.08 (different day: an indication, not a measurement).

**Rings (R against B, quiet; confounded by run order, see below):** OpaqueModels 1.164-1.232 (R) against 1.194-1.210 (B):
no difference resolved; the map spans are slower through the rings — ModelConstants 0.040-0.046 (R) / 0.046-0.049 (B), MaterialMap
0.033-0.039 (R) / 0.038-0.040 (B), World's MaterialMap 0.045 (A) / 0.052-0.057 (B) — and the marginal B runs agree
(ModelConstants 0.049-0.059).

**Occlusion (A against R and B):** 0.372-0.377 → 0.174-0.192 ms, **−0.18 to −0.20 ms**, GPU 0.056 → 0.031-0.032; the
marginal pairs agree (0.392-0.417 → 0.184-0.188).

**Render thread draw (A against B):** 2.33-2.37 → 2.24-2.26 ms, −0.07 to −0.13 ms (quiet, confounded; the matched figure
is below); −0.10 to −0.24 marginal.
The passes' CPU sum: 2.263-2.295 → 2.164-2.185. **Frame:** 9.58-9.84 (A) against 9.89-9.98 (B) and 9.53-10.42 (R):
not resolved; the render thread is not the frame's critical path. **Driver worker:** 1.94-1.95 (A), 1.96-1.99 (B),
1.79-2.05 (R): not resolved.

**The quiet A/B/R comparison above fails its negative control, and run order explains it** (advisor review). Passes the
change never touches rise in the same order as each condition's place in the triple, A first and R last: Sky3D
0.187-0.188 (A) / 0.195-0.198 (B) / 0.185-0.198 (R), Hud 0.082-0.085 / 0.087-0.088 / 0.083-0.092, Particles 0.079-0.084 /
0.084-0.087 / 0.082-0.089, and inside OpaqueModels the unchanged Flex, Proxies and Cubemap spans move with them. So the
OpaqueModels and render-thread differences in the table are not attributable; **only Occlusion's (−0.19 ms, ten times
the drift) is.**

**Reversed order, B A B A (17-20)**, after B was rebuilt with `--no-incremental` as A had been (0 warnings, 0 errors), and
a whole-solution build of the head (0 warnings, 0 errors). The machine's outside load held at 16-30%; 17, 18 and 19 peak at
28-30 after, 20 at 25. **Matched by load: 18-A between 17-B and 19-B**, where the untouched passes agree (Sky3D 0.194 /
0.195-0.196, Hud 0.093 / 0.089-0.092, Particles 0.086 / 0.085-0.088):

| run | cond | frame ms | render thread draw | World | OpaqueModels | Occlusion | ModelConstants | MaterialMap (models) |
|---|---|---|---|---|---|---|---|---|
| 17 | B | 10.65 | 2.36 | 0.207 | 1.272 | 0.189 | 0.0510 | 0.0435 |
| 18 | A | 10.75 | 2.55 | 0.194 | 1.255 | 0.407 | 0.0435 | 0.0308 |
| 19 | B | 10.46 | 2.34 | 0.205 | 1.266 | 0.189 | 0.0484 | 0.0440 |
| 20 | A | 9.90 | 2.39 | 0.185 | 1.186 | 0.377 | 0.0386 | 0.0314 |

Against its neighbours, 18-A gives: **render thread draw −0.19 to −0.21 ms; Occlusion −0.22; OpaqueModels +0.011 to
+0.017; World +0.011 to +0.013** — the last two are the rings: ModelConstants +0.005 to +0.008, the models' MaterialMap
+0.013, and World's MaterialMap 0.045-0.049 in every A run and 0.047-0.053 in R against 0.056-0.057 in every B run. **The rings cost about 0.01 ms in
each of World and OpaqueModels**, which R (rings off, occlusion on) avoids.

**Identity.**

- *In-process* (`ConstantRingTests.Draw_AWorldAndModelsThroughTheRings_IsByteIdenticalToDiscardPerDraw`, committed before
  the runs): byte-identical over every pixel, rings against discard-per-draw, with its controls green. Rendering.Tests
  whole at `793b7ca2`'s code: 1200 passed, 47 skipped, 1247 total (floor 781). The occlusion offsets: `Count_SeveralProxiesIssuedTogether_EachCountsItsOwnQuad`.
- *Viewer frame hash* (`identity.ps1`, `compare.ps1`; mode 0, `TF2VIEW_FIXED_FRAME=0.015`, `--measure 6`, runs h1-A,
  h2-A, h3-B; 201 labels common — a label seen twice keeps its last line, so loading-screen frames collapse):

  | part | A = A (control) | where A = A, B = A |
  |---|---|---|
  | pose | 195 of 201 | 195 of 195 |
  | cull | 201 of 201 | 201 of 201 |
  | particles | 172 of 201 | 167 of 172 |
  | rest | 180 of 201 | 176 of 180 |
  | image | **2 of 201** | 2 of 2 |

  **The image part fails its control, as predicted** (B599's record found the same), so the viewer cannot show the
  picture identical; the in-process test carries that claim. Pose and cull are deterministic and B matches them
  everywhere. Particles and rest are inputs this change does not touch; B's misses there (5 and 4) are within those
  parts' own A-against-A misses (29 and 21). The hash files (about 10 MB each) are kept out of the repository.
- *Queue verifier* (`v1-B`, mode 2, `TF2VIEW_QUEUE_VERIFY=1`): `QueuedThreaded on 6 physical cores`, last line `queue verify:
  741500 frames checked, 0 changed`, no `inputs changed` line (`v1-B-verify.txt`). Positive control: the existing unit test
  that overwrites a handed-off matrix after `Submit` (B599 record).

## Verdict

- **Prediction on the spans: confirmed.** The constant maps are 9% of OpaqueModels. The rest is per-draw managed work
  and binds. **The face upload (Flex, 3.16 MB a frame) is the largest single span at a quarter of the pass**, then
  BatchState and the model proxies at 0.15-0.16 ms each. MaterialMap costs about the same a lap in World and in models
  (0.12 / 0.16 us). **"The maps are the cost" (the 2026-10-25 inference) is falsified.**
- **Rings: falsified** (predicted −0.05 to −0.30 ms). Matched by load they cost about +0.01 ms in each of World and
  OpaqueModels (their map spans), and World's MaterialMap is higher in every B run than in every A and R run. A map a draw
  at WRITE_NO_OVERWRITE plus an offset bind costs this driver a little more than a small WRITE_DISCARD.
- **Occlusion: confirmed in direction, narrowly outside in size** (predicted under 0.15 ms, measured 0.174-0.192). What
  is left is the 376 Begin/Draw/End and the 376 per-proxy `GetData(…, 0)` polls, which can flush and are out of scope
  here: making them not flush changes when results arrive.
- **Render thread: −0.19 to −0.21 ms matched by load (8% of it), below the predicted −0.25 to −0.6 because the rings
  bought nothing; the quiet triples' −0.07 to −0.13 is confounded by run order. Frame unchanged within spread
  (predicted).** About 2% of a ~10 ms frame, on a thread off the critical path.
- **Where the model pass's next ms is**: the face upload (only the deltas that move, or a face's buffer kept per model
  and rewritten only when its weights change), the per-batch binds (a state cache inside the bucket loop: 8+3 SRVs,
  blend, raster, depth set every batch), the model proxies (0.73 us a lap against the world's 0.035), the per-model
  cubemap search and the string-keyed reports.
