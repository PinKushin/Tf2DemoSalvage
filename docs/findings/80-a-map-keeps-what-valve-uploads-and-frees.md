# 80: A map keeps what Valve uploads and frees

**The question.** On the f12 demo the viewer peaks at 7.6 GB working set. TF2 plays the same map in about 3 GB.
What does one loaded map hold, which field holds it, and does Valve keep the same thing?

**Read this first: the map is not most of the 7.6 GB.** The viewer keeps 0.83 GB of managed memory for one map
(f12). That leaves the GPU, native memory and the demo timeline as the larger share, and this census sees none of
them. Fixing every row below closes at most about 0.7 GB per map. It matters more where several maps are held at
once, as in Rendering.Tests.

**The instrument.** `map-memory <map> [maxTextureSize]` reads the map through `LoadedMap.Read`, the viewer's own
path, with no demo. It walks the fields of `MapLevel`, `MapAssets` and `LoadedMap`, then `GameContent` and every
static field. Each owner is charged with the bytes it reaches first (`StateCopy.Footprint`, the D217 step 4 census).
The probe runs three controls:

| control | f12 @256 | koth_nucleus @256 |
|---|---|---|
| census sum vs heap delta, as loaded (MB) | 1,397.7 vs 1,398.6 | 1,188.5 vs 1,189.3 |
| census sum vs heap delta, after `ReleaseUploaded`, the call `WorldPresenter.Project` makes (MB) | 832.7 vs 834.9 | 724.4 vs 726.6 |
| base texture slots holding pixels | 686 of 688 | 529 of 529 |
| released pixel arrays still alive, by weak reference | 0 of 6,044 | 0 of 4,651 |

## The census: what the viewer keeps per map after upload

Ranked by bytes, f12 at 256. **Game** marks an owner that is identical on koth_nucleus, which makes it game
content charged once per map. The evidence classes: **src** = read from source-sdk-2013; **dis** = disassembled from
TF2's x64 binaries (Ghidra, output on D:); **meas** = measured by the probe; **interp** = interpolated.

| # | owner | ours (MB) | what Valve keeps resident | evidence | the fix it points at |
|---|---|---:|---|---|---|
| 1 | `Assets.EntityModels`: 366 models, of which about 296 MB is **game** (koth 296.0 against f12 328.9; the top 12 are identical). Unskinned models bake one full vertex list per animation frame: `w_breadmonster.mdl` holds 57 lists of 13,896 vertices at 120 B each, 90.7 MB. Skinned models such as the player classes hold one list. | 329.0 | One bind-pose vertex set per model, posed by bones on every draw, with no frames baked (`bone_setup.cpp`). The vertex is `mstudiovertex_t`, 48 B, plus a 16 B tangent. The VVD sits in the evictable `MDLCACHE_VERTEXES` datacache section. Models load when precached. | src (`studio.h`, `imdlcache.h`, `bone_setup.cpp`); meas | (a) Skin every model from one bind pose, as the `Skinned` path already does for the classes. This is structural. (b) Hold game models once per process, not once per `LoadedMap`. |
| 1a | the same: `Byte[]`, about 8 MB in each player class model, 69.8 MB in all | (in 1) | not known | meas; holder not split | Split the holder field before deciding anything. |
| 2 | `Assets.ParticleSystemsByName`, **game**: every `.pcf` definition (about 9,050) kept as its DMX tree, one `Dictionary<string, DmxValue>` per operator | 239.3 | Every definition is resident too, since `ParseParticleEffects` reads every manifest file, but once per process. Each one is typed: `CParticleSystemDefinition` holds operator-instance vectors and has no field that keeps the DMX. | src (`particle_parse.cpp`, `particles.h`). The body of `DecommitTempMemory` is unreadable in `particles.obj`. Valve's size is interp. | Hold the definitions once in `GameContent`, and unpack each operator's parameters into a compact form. |
| 3 | `LoadedMap._game` (GameContent: VPK indexes, item schema), **game** | 48.4 | resident once per process | interp | No per-map change needed. Charged once. |
| 4 | `Assets.ScreenOverlays`, **game**: CPU pixels for jarate, bleed and similar overlays | 45.4 | no CPU texels, as for every texture (below) | dis; meas | **Not safe to release as things stand.** `WorldRenderer.DrawScreenOverlay` uploads each overlay lazily on first draw and drops the upload in `ReleaseScreenOverlays`, so it still needs the pixels. Upload eagerly with the world, then release, and load once per process. |
| 5 | `Assets.Lightmaps`: atlas `Byte[]` 24.5 MB plus `Single[]` 17.3 MB of decoded style samples, on top of `Level.LightSamples` 8.6 MB | 43.2 + 8.6 | The lighting lump (3.75 MB on f12, `ColorRGBExp32`, 4 B a luxel) for `R_BuildLightMap`, plus GPU pages | meas (lump sizes); interp (residency). `Mod_LoadLighting` is called from `CModelLoader::Map_LoadModel` at `engine.dll` 0x1800fffcb; its body was not read. | Keep the samples once, in lump form. Do not keep a whole CPU copy of the packed atlas. Rebuild a face's rect in scratch. |
| 6 | `Assets.StaticModelColours`: static-prop vertex lighting as `float[]`, 16 B per vertex, on the CPU | 42.0 | `.vhv` RGBA8, 4 B per vertex, in a pooled GPU vertex buffer (`CPooledVBAllocator_ColorMesh` under `CManagedDataCacheClient<CColorMeshData>`) | dis (strings and RTTI); src (`hardwareverts.h`) | Store 4 B per vertex. Drop the CPU copy after upload. |
| 7 | `Assets.AnimatedDetails` 38.4 + `Assets.Cubemaps` 26.9: CPU pixels `ReleaseUploaded` leaves in place | 65.3 | no CPU texels | dis; uploader read | **Fixed in `fe48ec49`.** Both are uploaded inside `UploadWorldTextures` (`WorldRenderer.cs` around 3264 and 3579), the same pass as the base textures, so B407's release, and its reload on a lost upload, covers them. |
| 8 | `LightWarps` 5.8, `PlacedCubemaps` 5.2, `SkyFaces` 3.3, `BlendModulations` 2.3: CPU pixels not released | 16.6 | no CPU texels | dis | Each needs its uploader read first. `SkyFaces` goes up through `SetSkyFaces`. |
| 9 | `Assets.PlacedProps`: `Vector3[]` model shapes for static-prop decals (B421) | 12.4 | decals project onto the model's own vertices | interp | Check whether this duplicates row 1. |
| 10 | lumps: `Surfaces` 9.6, `Ambient` 3.5, `Displacements` 2.6, `Entities` 1.6, `Terrain` 1.1, `Leaves` 0.9, **`Visibility` 0.5**, **`StaticProps` collision 0.4**, `Normals` 0.3, the rest under 0.1 | 20.5 | The engine keeps surfaces, leaves, vis, collision, displacement collision and the entity lump for the life of the map | interp (`Mod_Load*` strings; bodies not read) | Near parity. Low priority. |
| — | released correctly (as loaded): `Textures` 278.9, `Bumps` 235.8, `Details` 50.3 | 0 after upload | no CPU copy | dis | Parity already (B407). They still coexist with everything above during the load. |

Rows 1, 2, 4, 6, 7 and 8 come to about 740 MB. Of that, about 580 MB (rows 1-game, 2 and 4) is the same on every
map. **Textures stay block-compressed** in memory and on upload: f12 holds 412 DXT1, 270 DXT5 and 4 RGBA8888, as
Valve uploads them (meas).

## The whole process: where the viewer's 9 GB of private bytes go

**The setup.** The viewer is a Debug build, which is what the owner runs (D176). It played the f12 demo
(`demostf-cp_process_f12-2026-08-08-2207.dem`) from tick 30000 with `--first-person --measure 20 +fps_max 0`, under
`run-exclusive.ps1`, on an RTX 3060, a discrete GPU. Several instruments ran alongside it:

- **`process-memory tf2demoview 5`** walked the process with `VirtualQueryEx` every 5 s and summed the committed bytes
  by region type.
- **`\GPU Process Memory(pid_*)` counters** gave dedicated and shared GPU memory.
- **`dotnet-gcdump`** was taken the moment private bytes passed 6 GB.
- **Two in-process probes** ran on the same demo: `timeline-heap --map` and `timeline-windows … cost`.

**The control.** At the peak, the walk counted 8,906 MB of committed private memory, against the process's own
8,972 MB of private bytes (0.7% apart). Every 5-second sample agreed within about 65 MB. The slices below are slices
of the walk.

**How memory rises.** The timeline build takes 283 s in Debug and climbs to 1.5 GB private. Uploading the map and
models then lifts private bytes from 4.5 to 8.7 GB within 5 seconds (22:55:41 to 22:55:46). The peak during
playback is 8,972 MB private and 6,495 MB working set.

| # | slice at the peak | MB | what Valve keeps | evidence | the fix it points at |
|---|---|---:|---|---|---|
| A | **One private allocation that grows with the managed heap**: 6,234 MB committed | 6,234 | — | meas; identified as the GC's region reservation by its growth (interp) | — |
| A1 | the live managed heap, after the full collection `dotnet-gcdump` forces | 3,121 | | meas (gcdump) | |
| A2 | **committed but not live**: A minus A1, which the GC holds after garbage it has not returned | ~3,100 | nothing analogous | meas − meas | Find the allocation churn that leaves it, using the load's phase timers as `5dc4a9be` did. GC settings (`GCConserveMemory`, `RetainVM`) are an owner decision, not a parity fix. |
| A1a | the demo timeline, built whole | 1,249 | the engine streams the demo and keeps no timeline at all | meas (`timeline-heap`, Release) | D217's checkpointed windows. The checkpoints themselves retain 718 MB (`timeline-windows … cost`: `entities._entities` 124 MB, `frames` ScenePlayer[] 78 MB, …). Which of the two the viewer holds at this point was not separated. |
| A1b | the map, the game content and this demo's models, after `ReleaseUploaded` (texture quality 0) | 1,441 | the table above | meas (`timeline-heap --map`) | Rows 1–8 above. With this demo's models the entity-model row is larger than the 329 MB measured without a demo. |
| A1c | **the packed entity-model set's CPU vertices**: 4,737,795 vertices in 729 MB (`viewer` log "entity model vertices"). The gcdump's largest single object is a 382 MB `WorldVertex[]`. | 729 | the hardware mesh lives on the GPU; the VVD stays in an evictable cache | meas (log, gcdump); src (`imdlcache.h`) | Keep no CPU copy once the vertices are on the device. Today the set re-uploads WHOLE when it grows ("STALL uploading … because the model set grew"), which is why it keeps them. An append-only device buffer would remove the reason. This is the second copy of the same models as row 1. |
| B | **native private memory in about 600 allocations under 64 MB**: it jumps from 92 to 1,791 MB in the 5 s of texture and model upload, and reaches 2,500 | 2,500 | Source's D3D9 textures are most likely `D3DPOOL_MANAGED`, which keeps a system-memory copy of each (interp, not read: shaderapidx9 is not in the SDK) | meas (walk, timing); interp (owner: the D3D11 runtime and driver) | Find out which D3D11 creation path keeps a CPU-side copy: textures created with initial data, staging resources never released, deferred destruction. It is the same order as the GPU's 2.2 GB, which suggests a mirror. |
| C | other private allocations of 64 MB or more, apart from A | ~170 | | meas | — |
| — | mapped files 225, images 371 | (not private) | | meas | — |
| — | **GPU dedicated memory** (VRAM, outside the working set on this discrete card) | 2,226 | | meas (counters) | Not a working-set cost here. On an integrated GPU it would be. |

**What the slices say.** The owner's "something very wrong" is three things, in this order:

1. About 3 GB of GC heap committed but not live.
2. About 2.5 GB of native memory that appears exactly when we upload.
3. A live managed heap of 3–4 GB. That heap is the timeline (1.2 GB), the map with its game content (1.4 GB) and a
   second CPU copy of every packed model vertex (0.7 GB).

The map census above accounts for only part of the third.

**What this does not settle.**

- That allocation A is the GC's is an interpolation from its growth. A `GC.GetGCMemoryInfo().TotalCommittedBytes`
  printed by the viewer itself would prove it, and the viewer's own "memory after load" line already reports
  committed 4,017 MB after a forced collection.
- Who owns B. That needs a native heap trace (ETW `HeapAlloc` stacks), which was not run.

## The GC's own account, and what it overturned (2026-10-19)

**`--measure` now prints the GC's view** (`8f595683`): `GC.GetGCMemoryInfo()` at the end of the measurement, with
no forced collection, plus the large-object allocations of the measured frames by type. Each run below is the
same setup as above (f12, tick 30000, Debug, `--measure 20`, under the lock), with the region walk as the control.
Walk and private bytes agreed within 70 MB in every run.

**The inference above was wrong in kind.** It read A2 (about 3 GB) as "committed but not live", meaning slack. The
GC's own reading at the end of the baseline run:

- committed 8,072 MB;
- heap 8,061 MB;
- fragmented 1 MB;
- slack 9 MB;
- **large-object heap 6,497 MB**, against 1,562 MB in gen2.

The memory is not slack. It is the **large-object heap holding dead arrays** that no gen2 collection has yet
reclaimed. The dump above saw 3.1 GB because `dotnet-gcdump` forces exactly that collection. The cause is
allocation churn on the LOH during playback: 1.9–2.4 GB of large arrays in 20 seconds.

| large allocations over 20 s of playback (baseline) | MB |
|---|---:|
| `float[]` | 1,666–1,859 |
| `Dictionary<string, ParticleSystem>` entries | 321–352 |
| `WorldVertex[]` | 77 |
| `short[]` | 59–74 |
| `byte[]` | 17 |

**The particle lookup copies are fixed** (commit below). `ParticleEffect` copied the whole game's 9,050-system lookup
once per child it resolved, to drop the parent's name as a cycle guard. It now carries the chain of ancestor names.
Measured after the fix: 0 MB, where the baseline runs showed 321–352 MB.

**The `float[]` owner is not found.** Two candidates were fixed, measured and reverted, because neither moved it:

- decal vertex packing written straight into the mapped buffer;
- a reused flex-delta scratch.

`float[]` stayed at 1,666–1,781 MB with each. Naming the owner needs allocation stacks. The in-process listener
cannot give them, because runtime events arrive on another thread. The next step is a `dotnet-trace` gc-verbose
trace read with TraceEvent, which is a package this repository does not reference. That is asked, not assumed.

**The GC levers**, one run each. Run-to-run spread on unchanged code was ±1 GB private and ±2 ms. Settings were
set through environment variables. Where a kept one would live is said beside it.

| run | peak private (MB) | working set at end | LOH at end | mean frame (ms) | where it would go |
|---|---:|---:|---:|---:|---|
| baseline | 10,689 | 8,203 | 6,497 | 39.8 | — |
| `GCConserveMemory=5` | 9,344 | 6,736 | 3,228 | 41.2 | the viewer `.csproj`: `<RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="N" />`, which lands in `tf2demoview.runtimeconfig.json` |
| `GCConserveMemory=9` | 8,329 | 5,700 | 3,597 | 42.2 | as above |
| `gcConcurrent=0` | 10,467 | 7,654 | 6,021 | **54.7** | `<ConcurrentGarbageCollection>false` — rejected, +15 ms |
| `TieredPGO=0` | 9,894 | 6,878 | 3,920 | 38.0 | `<TieredPGO>false` — no memory effect beyond noise |
| particle fix, run 1 / 2 | 9,961 / 10,346 | 6,977 / 7,868 | 3,926 / 6,128 | 40.3 / 39.0 | (code) |

`GCHeapAffinitizeMask` applies only to server GC, which the viewer does not use (workstation, concurrent), so it has
nothing to act on. The compacting collection after the map load already exists (D199, `ViewerPresenter`). The churn
this table measures happens after it, during playback.

**`ConserveMemory=9` is the one lever that clearly moves memory.** It cuts about 2.4 GB of peak private bytes and
about 2.5 GB of working set, at about +2.4 ms a frame (+6%, within twice the noise). That is a trade for the owner to
judge, not one taken here. The better fix is to stop the churn it compensates for.

### The stacks, and the wrong turn they correct (2026-10-19)

`alloc-trace` (`769733c3`) reads a 20 s `dotnet-trace` capture of the runtime's GC events
(`Microsoft-Windows-DotNETRuntime:0x1:5`), taken during the same f12 playback. **Control:** the trace summed 2,312 MB
of large allocations per 20 s. The `--measure` line of that same run said 4,482 MB over 40 s, which is 2,241 per 20 s
(3% apart). Every stack resolved.

| large allocations per 20 s, by allocating frame (before) | MB |
|---|---:|
| `float[]` ← `FaceFlex.InBufferOrder` ← `EntityModelSet.FlexFor` | 1,439 |
| `float[]` ← `FaceFlex.Deltas` ← `EntityModelSet.FlexFor` | 543 |
| `float[]` ← `WorldRenderer.Pack` ← `UploadShotDecals` | 99 |
| `WorldVertex[]` ← `PlacedProp.InWorld` ← decal replay | 85 |
| `short[]` ← `AudioOutput.Play` / `ToStereo16` | 77 |
| `float[]` ← `VguiRenderer.BuildVertices` | 26 |

**The wrong turn.** A flex scratch for `Deltas` alone had been tried above, measured, and reverted as moving nothing.
It covered 543 of the 1,982 MB the flex path allocates. The other 1,439 MB is the per-frame stream from
`InBufferOrder`, and the run-to-run spread of the tally hid the 543. The trace named both frames at once. **A type is
not an owner. Only a stack names one.**

**Fixed: a talking face writes into arrays the set keeps.** Every frame a face changed, `FlexFor` allocated three
`.vvd`-sized arrays. Now the deltas accumulate into one grown-only scratch pair, and the buffer-order stream is the
entity's own, rewritten in place. That needed a renderer change: `BindFlex` skipped the upload when handed the array it
uploaded last, so it now forgets that array whenever a draw unbinds its face. The skip then holds within one draw, which
is the refract rebind it existed for.

| f12, `--measure 20` | large allocations / 20 s | mean frame | peak private |
|---|---:|---:|---:|
| before (two runs) | 1,867–1,957 MB | 36.4–38.1 ms | 10,092–10,442 MB |
| after (two runs) | **306–314 MB** | **35.2–36.9 ms** | 9,326–10,031 MB |

Trace after: 366 MB per 20 s. The largest remaining owner is now `WorldRenderer.Pack` from the shot decals at
161 MB, the other change reverted above. It is real, and was lost in the same noise.

**Fixed next: decal vertices are packed straight into the mapped buffer.** The shot-decal and model-decal passes now do
this, so no per-frame `float[]` is made.

| f12, `--measure 20`, two runs | large allocations / 20 s | of which `float[]` | mean frame |
|---|---:|---:|---:|
| before (the face fix's after) | 306–314 MB | 130–137 MB | 35.2–36.9 ms |
| after | **217–219 MB** | **41–42 MB** | 36.1–37.4 ms |

The frame-time change is inside the measured noise.

**Fixed next: a decal shot on a static prop projects onto a reused world copy.** Each shot used to get a new copy of the
prop's triangles. `DecalEffects` now keeps one grown-only array, and `PlacedProp.InWorld` writes into it. The
projection copies the corners it keeps, so the array is free again at the next shot.

| f12, `--measure 20`, two runs | large allocations / 20 s | of which `WorldVertex[]` | mean frame |
|---|---:|---:|---:|
| before (the decal fix's after) | 217–219 MB | 77–78 MB | 36.1–37.4 ms |
| after | **152–153 MB** | **13 MB** | 36.1–36.5 ms |

**Fixed next: a sound is converted to 16-bit stereo in a buffer the output keeps.** OpenAL copies the samples when the
buffer is created, so the conversion is scratch. The sound is unchanged: on a real clip, the new path reproduces
byte for byte the digest that the old conversion printed before the change.

| f12, `--measure 20` | large allocations / 20 s | of which `short[]` | mean frame |
|---|---:|---:|---:|
| before (the prop-decal fix's after) | 152–153 MB | 72 MB | 36.1–36.5 ms |
| after, three runs | **67–87 MB** | **0** | 34.1, 36.6, (59.9) ms |

The 59.9 ms run overlapped my own builds of Core and Presentation. The third run was taken alone to replace it.

### The re-census after the four fixes (2026-10-19): what the "4.5 GB LOH" is

**After all four fixes**, large allocations fell from 1,867–2,241 MB to 67–87 MB per 20 s of playback. The GC's
end-of-run reading still showed a large-object heap of 3.6–4.4 GB. The question was whether that memory is HELD.

**It is mostly not held.** A `dotnet-gcdump` taken 25 s into playback (private 8,502 MB at that moment) forces a full
collection and lists what survives. Every surviving object of large-object size comes to about 0.15 GB together:

| type | MB |
|---|---:|
| 7 × `WorldVertex[]` | 23 |
| `TimelineFrame[]` | 20 |
| 2 × `byte[]` | 18 |
| 2 × `float[]` | 12 |
| `DemoViewCommand[]` | 11 |
| VPK directories | 7 |

The GC's 3.6–4.4 GB "LOH" was therefore **dead arrays awaiting a gen2 collection**, plus free LOH space it had not
returned. That is the residue of the load and of the churn now removed. It is not a holder. The gcdump's live total
is 4.29 GB in 6.4 million objects, almost all of them small-object-heap objects.

**What the whole process now holds:**

| f12, Debug | baseline | after the four fixes |
|---|---:|---:|
| private bytes at the end of playback | 10,689 MB | 8,494–8,733 MB |
| working set at the end of playback | 8,170 MB | 6,015–6,249 MB |
| peak private bytes (at the upload) | 10,092–10,689 MB | 9,326–11,129 MB |

The control is the same as before: the walk and private bytes agree within 110 MB. The upload peak did not move. It is
the load's transient plus the 2.2–2.5 GB of native upload memory (slice B above), and none of the fixes touched it.

**Naming owners of the 4.3 GB live set by retaining path is not done.** Two routes failed:

1. **TraceEvent 4.0.0 has no heap-graph reader.** Its assembly carries the `GCHeapDump` name and no `MemoryGraph`, so a
   gcdump cannot be read for retaining paths with the approved package.
2. **An in-process by-field walk was tried and abandoned.** It used the D217 `StateCopy.Footprint` over the viewer's
   session. It drove the viewer's private bytes to 40 GB, through the walk's own visited set and the boxing on
   millions of objects, before it was stopped. The code was removed; its two emptied files await deletion.

The route that would answer it is a full dump (`dotnet-dump collect`) read by ClrMD (`Microsoft.Diagnostics.Runtime`),
which walks real roots. That is a second diagnostic package, so it is asked, not assumed.

### The live set by retaining path (2026-10-19, ClrMD)

**The instrument.** `heap-owners` (`c6e58dfb`) attaches to the running viewer through ClrMD and suspends it for the walk,
so no dump file is written. It starts from the real GC roots and walks breadth first. Each object is charged once, to
the nearest field of one of this project's types on its first path.

The walk ran 25 s into f12 playback, at 8,495 MB private. It reached 6,457,785 objects, **4,094 MB in total**. The
control is the gcdump's live total of 4,290 MB at the same point of a different run, 4.6% apart. The full dump the plan
asked for was not needed: `dotnet-dump` is not installed here, and ClrMD's attach reads the same heap.

| # | retained by | MB | what Valve keeps | the fix it points at |
|---|---|---:|---|---|
| 1 | `TimelineFrames` frames and their `ScenePlayer[]`, `SceneTeam[]` and detail arrays: one whole frame per tick, 96,589 of them | ~800 | none; the demo is read as it plays (interp) | D217's windows. The frames are the precomputed timeline the viewer samples. |
| 2 | **`SoundCache._cache`**: every decoded sound, kept for the life of the session as `float` samples | **650** | a bounded sound cache of the files' own 16-bit PCM (interp; `snd_*` is not in the SDK) | Store 16-bit (half), and bound the cache LRU. |
| 3 | **`Device3D._skinnedVertices`**: the renderer's CPU copy of every skinned vertex | **623** | none: skinned meshes live on the GPU (src, `studiorender` design; interp for the exact buffer) | Drop it after upload, as B407 did for the rigid models. Find what reads it first. |
| 4 | `EntityModelSet._raw` (305) and `ModelFrames.Geometry` (272): each model's raw `PropVertex` list, then its baked per-frame lists | 577 | one bind-pose VVD in an evictable cache | Item 3 (one bind pose, no frames), plus releasing `_raw` once packed. |
| 5 | `ParticleFunction.Parameters` and related: the DMX dictionaries | ~196 | typed definitions, once per process | Item 4. |
| 6 | Prop tracks' histories: `_keyframes` 85, `_simulation` 91, `_animation` 55, `_poseParameters` 47 | ~280 | the client keeps a short interpolation history per entity, not the whole demo (src, `InterpolatedVar`) | The timeline's design (D217), not a leak. |
| 7 | the rest above 30 MB: skinned models 69; GameArchives 48; screen-overlay frames 44; `MapAssets.AnimationFrames` 41 (not released after upload); static-prop colours 42; physics ledge trees 47; corpse record 33 | ~324 | | `AnimationFrames` joins `ReleaseUploaded`, if it is uploaded with the textures. |

**What this overturns.** The earlier map census charged row 4 and row 7's model data to the map. Rows 2 and 3 were not
in any census: 1.27 GB together, more than items 3 and 4 combined. Row 1 matches the 1.25 GB the `timeline-heap` probe
measured. That earlier census was right.

### Fixed: the sound cache holds WAVs as 16-bit, and is bounded (2026-10-19)

**What it held.** The new `--measure` line gave the split: 590 sounds, **WAV 387 MB and MP3 66 MB, both decoded to
float**. The heap walk's 650 MB adds the dictionary, the cache's own entries, and MP3 decode buffers sized past their
fill.

**WAVs are now held as 16-bit.** That is the file's own precision. A 16-bit or 8-bit PCM sample over 32,768 is exactly
representable as a float, so the samples reach the mixer bit for bit as before.

**MP3 stays float. This is the "stop on that kind" case.** NLayer decodes to float, which carries more precision than
16 bits, so quantising it would change what is heard.

**The cache is bounded** at `SoundCache.CapacityBytes`, 512 MB, least recently used out first. **The cap is
interpolated**, because the engine's figure is not in the SDK. It is twice what f12 now holds, so an ordinary match
evicts nothing.

| f12, `--measure 20` | WAV held | MP3 held | private at end | working set at end | decodes in play | mean frame |
|---|---:|---:|---:|---:|---:|---:|
| before | 387 MB | 66 MB | 8,493–8,514 MB | 6,052–6,060 MB | 35 | 36.6–38.5 ms |
| after, two runs | **194 MB** | 66 MB | **8,298–8,305 MB** | **5,844–5,854 MB** | 33–35 | 35.9–36.6 ms |

**The 33–35 decodes in play** are sounds the precache did not name, played for the first time. They are not
evictions, since nothing was evicted on f12. Each is a frame that decoded a sound.

The real-clip digest held through the change. That clip is a 16-bit WAV, so the test now also proves the 16-bit
storage.

**A wrong turn, caught in review: the change broke the sound's identity.** A voice named its sound by the sample's
float memory, as `ch->sfx` names the engine's. Once WAVs were held as 16-bit, every WAV's float memory was empty and
equal to every other's. A stop on an entity's `CHAN_STATIC` therefore matched the first WAV voice there, whatever it
played. Eviction broke the same identity the other way: an evicted sound decodes again to new memory, so a stop for a
sound still playing matched nothing. A sample now carries the sfx key the cache decoded it under (`SoundSample.Key`),
and a voice matches on that. `AudioOutputDeviceTests` holds both cases with two reader-built WAVs.

**Should eviction skip a sound a voice is still playing? No, here.** OpenAL copies the samples into its buffer at
`BufferData`, so the voice never reads the cache again, and eviction only costs a fresh decode if the sound is asked for
again. Whether Valve's cache pins a sound while a channel mixes it is not in the SDK: the wave data cache is engine
code. Interpolated, and not decompiled.

### Fixed: the renderer keeps no copy of skinned models' vertices (2026-10-19)

**The one reader.** `Device3D._skinnedVertices` held every skinned model's packed vertices, 623 MB, for one reader: the
decal a bullet leaves on a player (`DecalEffects`, through `IDecalUpload.SkinnedVertices`). The model set already keeps
each model's corners (`_raw`) and the order it packed them in. `EntityModelSet.PackedVertices` now rebuilds the list
for the rare decal, through the same corner-to-vertex helper the packing uses. The interface member moved from the
device seam to `IPosedEntities`, and the device copy is gone.

**The tests.**
- `PackedVerticesTests` builds a model whose two materials interleave, so the packing reorders it. It holds the rebuild
  to the list the set packed, and fails when the order is ignored.
- `PackedVerticesRealModelTests` holds the shipped soldier's rebuild to the slice the renderer was handed. That test
  cannot see the order: the soldier's corners are already grouped by material, and ignoring the order passes there
  (tried).

**Measured: the live heap fell, private bytes did not.**

| f12 | before (the sound fix) | after |
|---|---:|---:|
| live heap, heap-owners walk at 25 s | 4,094 MB (before both fixes) | **3,280 MB** |
| `Device3D._skinnedVertices` | 623 MB | gone |
| private at the end, two runs | 8,298–8,305 MB | 8,344–8,353 MB |
| working set at the end | 5,844–5,854 MB | 5,862–5,887 MB |
| large allocations / 20 s | 77–78 MB | 118–120 MB (decal rebuilds: `WorldVertex[]` 13 → 49–58) |
| mean frame | 35.9–36.6 ms | 34.1, 37.6 ms |

The live heap fell by 814 MB, which matches the two fixes (sound 194 + skinned 623). Private bytes did not move,
because the GC still carries **a 4.1 GB large-object heap of dead arrays** that its next gen2 collection has not
reached. The tally cannot see those arrays: it starts at the first measured frame, and they are allocated before it,
during the load and the seek to tick 30000 (183,783 particle replay steps in this run). Until something collects them,
shrinking the live set does not shrink the process. **The next lever is that pre-measurement churn, or a collection
after the seek** like the one after the load (D199). The decal rebuild's new 36–45 MB per 20 s is a follow-up:
memoise the rebuilt list per model that has been struck.

**The rebuild is now memoised per struck model**, and released with the level's models. Large allocations per 20 s
went from 118–120 to **88–90 MB** (`WorldVertex[]` 49–58 → 21–22). Mean frame 35.2–35.7 ms, private bytes 8,311–8,315 MB.

**Item 2, the packed models' CPU copy, is already released.** `EntityModelSet.ReleaseUploaded` (B407) drops the
729 MB once the map's world is on the device. Every run above ends with "entity model vertices on the CPU 0 in
0 MB". The 729 MB is real only between the load and the first upload. That makes it part of the load peak, not the
playback peak.

### The churn from process start, by owner (2026-10-19)

**The instrument.** `dotnet-trace` on the viewer from process start: f12, seek to tick 30000, `--measure 20`, under
`run-exclusive.ps1`. `alloc-trace` reads the large-object ticks by allocating stack. **The control** is the trace's
total over every tick, against the GC's own `GC.GetTotalAllocatedBytes`, which the `--measure` line prints as "allocated
since start". Before the fixes, those were 54,770 MB against 54,631 MB, so the trace sees the whole allocation stream.

**The finding.** One map load and a 20 s measurement allocate about **52 GB**, 9–12 GB of it large objects. The dead
large-object heap that private bytes carry is this load and seek churn. Valve loads a map without anything like it.

| site | type | before, MB per 20 s | after, MB per 20 s |
|---|---|---:|---:|
| `VpkArchive.ReadFile` | `byte[]` | 268 | 258 |
| `EntityModelSet.Add` (precache) | `WorldVertex[]` | 255 | 126 |
| `CreateBlockTexture` | `byte[]` | 91 | 88 |
| `Device3D.UploadModels` | `WorldVertex[]` | 56 | 54 |
| `WorldRenderer.Pack` in `UploadModels` | `float[]` | 56 | 54 |
| `PropModels.Read` | `PropVertex[]` | 48 | 51 |
| `DecodedDemo.Report` roster | `ScenePlayer[]` | 103 | gone |
| MP3 decode | `float[]` | 24 | 23 |

"Per 20 s" is the probe's normalisation: the whole run's bytes are divided by its length, so multiply by about 11.1
(before, 221.7 s) or 11.5 (after, 230.3 s) for the whole run.

| whole run | before (221.7 s) | after (230.3 s) |
|---|---:|---:|
| large allocations | 12,071 MB | 9,359 MB |
| allocated since start | 54,631 MB | 52,203 MB |
| private bytes at the end | ~8.3 GB | 8,121 MB |
| working set at the end | ~5.85 GB | 5,672 MB |
| live GC heap at the end | | 5,487 MB (LOH 3,930) |

**The fixes.** The roster is one pass instead of grouping every player of every frame. Packing groups corner indices
and writes each vertex straight into the model set's list, instead of building a list of packed vertices per material
and copying it.

**Caveats.**
- The "before" rows for the smaller owners are rounded back from earlier whole-run totals divided by 11.085. Only the
  roster and packing rows were read off the stacks on both sides.
- The "after" trace's all-tick total was not re-read, so the after column's control is the GC counter alone.
- Private bytes moved by only about 200 MB. The dead large-object heap waits for a gen2 collection, so less churn shows
  first as less garbage, and in private bytes only when the GC compacts.

### The upload copies, and a reserve that measured little (2026-10-19)

Same instrument and run as above. MB per 20 s:

| site | type | after the roster fix | reserve, rented arrays | reserve, view + scratch |
|---|---|---:|---:|---:|
| `EntityModelSet.Add` | `WorldVertex[]` | 126 | 104 | 150 (`EnsureCapacity`) |
| `Device3D.UploadModels` slice | `WorldVertex[]` | 54 | 78 | **0** |
| `WorldRenderer.UploadModels` pack | `float[]` | 54 | 17 | 17 |
| large allocations, whole run | | 9,359 MB | 8,888 MB | 8,498 MB |
| allocated since start | | 52,203 MB | 51,907 MB | 50,836 MB |
| private bytes at the end | | 8,121 MB | 8,367 MB | 7,959 MB |
| GC heap at the end (LOH) | | 5,487 (3,930) | 5,738 (4,183) | 5,347 (3,793) |

- **The slice.** A rented array measured worse than a fresh one, because the pool rounds each model up to a power of
  two. A view over the model set's own list removes the copy outright.
- **The pack.** It goes into one grow-only scratch array. The 17 MB left is that array's growth.
- **The reserve.** Counting first, then setting the capacity exactly, took `Add` from 126 to 104 only. `EnsureCapacity`
  measured worse, at 150. The list is grown by more than one precache call, and each call grows a list that is already
  large, so a per-call reserve cannot remove the copies. The shipped code keeps the exact reserve. The remaining fix is
  a list that never copies to grow, such as per-model arrays. That is not built.
- **Single runs.** Private bytes swing by about 400 MB between runs (8,121 → 8,367 → 7,959), so a single run's private
  bytes is not evidence of a fix. The allocation columns are steadier.
- **The post-seek collection is not measured.** At the end, private bytes (7,959 MB) are 2.6 GB above the GC heap
  (5,347 MB). That gap is native memory and the runtime, not dead managed arrays, so a collection after the seek would
  not reach it. The dead arrays are inside the GC heap's 3,793 MB LOH; a collection would show as a smaller GC heap,
  and is left for after the VPK reads.

### Plan: VPK reads into the caller's buffer (not built)

`VpkArchive.ReadFile` is about 264 MB per 20 s, about 2.9 GB over the run. Valve reads a VPK entry straight into the
caller's buffer. The callers of `GameArchives.Read`, by agent-lsp references: about 40 in production code across
Scene, Presentation and Content, and about 90 more in tests and probes.

**By bytes, from the trace's stacks.** Only the top three frames were read.

| caller | MB per 20 s | kind |
|---|---:|---|
| `MapAssets.Resolve` → `ResolveBump` → `LoadFrame` (VTF) | 83 | (a) decoded, then dropped |
| `MapAssets.Resolve` → `Decode` (VTF, for `PropModels.Register`) | 45 | (a) |
| `MapAssets.Resolve` → `LoadFrame` → `Find` | 23 | (a) |
| `MapAssets.Resolve` → `ResolveDetail` → `Find` | 22 | (a) |
| the rest | ~90 | not split yet |

- **(a) Consumes the bytes at once:** every texture path in `MapAssets.Resolve`. `VtfTexture` decodes the bytes into
  its own pixels, and the byte array is dropped.
- **(b) Retains the array:** none found among the four. Every caller still has to be checked before the allocating
  overload is narrowed. Material text (`VmtMaterial.Parse`), model files (`PropModels`) and sounds (`SoundCache`
  decodes into samples) are the next to classify.

**The contract change.** Add `GameArchives.Read(string path, IBufferWriter<byte>)`, or a lease returning an
`ArrayPool` array with its length, beside the allocating `Read`. A call site in class (a) rents, decodes and returns.
Class (b) keeps the allocating overload. `VpkArchive.ReadFile` gains the same overload and reads the entry's preload
and archive bytes straight into the span.

**The steps, one commit each:**
1. The overload, with a test that it gives the same bytes as the allocating path on a real VPK entry.
2. `MapAssets.Resolve`'s VTF reads, the biggest (a) caller, with a byte-identical test on a real texture.
3. The rest of class (a), largest first. Each step reports the MB per run it saves.

**The plan's split was wrong, and reading the decoder killed it before any build (2026-10-19).** The four texture
callers are class (b), not (a). `VtfTexture.Read` keeps a block-compressed texture as slices of the file's own bytes:
`chain = new(...) { file.Slice(at, bytes) }` and `smallerLevels.Add(file.Slice(faceAt, smallerBytes))` (B149: nothing
is expanded, so the GPU gets the blocks as stored). The byte array that `ReadFile` returns therefore *is* the texture
until upload, and `ReleaseUploaded` drops it after. Returning that array to a pool would hand a live texture's bytes to
the next read. Only the expanded formats drop the file at once.

So most of the 2.9 GB is texture data that lives from load to upload, which is the engine's shape: `CTexture` frees its
scratch VTF after upload. **What is not the engine's shape:**
- **No texture is shared between materials.** `Resolve` reads and decodes every material's textures afresh, and nothing
  in `MapAssets` keys a texture by name. `CTextureManager::FindOrLoadTexture` loads a texture name once and hands every
  material the same `ITexture`. How many of the 2.9 GB are repeat reads of one name is not measured yet; that is the
  next number to take.
- **An animated texture re-reads its whole file once per frame** (`LoadFrame` in the detail, base-texture and water
  normal frame loops). Each frame keeps its own copy of the same file until upload.

**Measured: 78% of the texture bytes read are repeat reads of a file already read.** The `texture-reads` probe loads f12
by the production path (`LoadedMap.Read`, with the demo's timeline so its models' materials resolve, as in the viewer, at
full texture size). It counts each `.vtf` read inside the resolve's own `Find` (`MapAssets.TextureReads`), so the counts
are carried from the read, not derived. The control: 926 distinct files is what a real map with players must read; an
empty tally would mean a broken hook.

| f12 | |
|---|---:|
| distinct texture files | 926 |
| reads | 4,796 |
| bytes read | 2,455.6 MB |
| bytes read again for a file already read | **1,913.3 MB** |
| of those, a later frame of an animated texture | 103.1 MB |

The worst repeats:

| file | reads | repeated MB |
|---|---:|---:|
| `water/tfwater001_normal.vtf` | 58 (no frames) | 855.0 |
| `effects/tiledfire/fireLayeredSlowTiled512.vtf` | 935 (240 later frames) | 296.0 |
| `models/player/medic/medic_normal.vtf` | 22 | 56.0 |
| `models/player/demo/demoman_normal.vtf` | 19 | 48.0 |
| `models/player/spy/spy_normal.vtf` | 18 | 45.3 |
| `festive_lights_red.vtf`, `festive_lights_blue.vtf` | 125 each (50 later frames) | 31.0 each |

- **The 2,455.6 MB read** agrees with the trace's ~2.9 GB for `VpkArchive.ReadFile` over a viewer run, which also
  reads non-texture files.
- **Most of the waste is one name read for many materials.** Only 103 MB is the per-frame re-reading of animated files.
  The water normal map alone is 855 MB: every water material names the same file.
- **The fire texture** is read 935 times, so its frame loop is multiplied by the number of materials that name it.

**Fixed: one read and one decode per texture file per load (2026-10-19).** `TextureFiles` keys a load's files by path
and its decodes by path and frame. Every material resolve in the load shares it: brushwork, models, detail sprites,
particles, sprites and override materials.

- **The key is the name**, as the engine's is: `FindTexture( char const* pTextureName, const char *pTextureGroupName,
  bool complain = true, int nAdditionalCreationFlags = 0 )` and `IsTextureLoaded( char const* pTextureName )`
  (`imaterialsystem.h:948-951`). Read from published source.
- **No case needs a wider key.** A decode here depends on the file, the load's size cap and the frame. The frame is in
  the key, and the cap is one value per load. Everything a material decides (translucency, additive, tint, modulation)
  is applied to the `MapTexture` built over the shared decode, never inside the decode.
- **One fix covered both repeat kinds.** Animated frames read their file once because the file is cached; there was no
  separate per-frame fix.
- **Lifetime.** The table lives for one `Load` and is dropped on return, so it holds nothing past `ReleaseUploaded`.
  Shared frames are slices of one array, and a slot's release only drops that slot's reference.
- **The wrong turn: a race.** A first version used `GetOrAdd` with a plain factory, and two files were still read
  twice: the brushwork resolve is a `Parallel.For`, and `GetOrAdd` may run its factory twice in a race. The values are
  now `Lazy`.
- **The instrument had the same fault.** The tally was a static field, so a concurrent test's load counted into
  another test's tally. It is now async-local.

**Tests and sabotage** (`MapAssetsTests`):
- `Load_ARealMap_ReadsEachTextureFileOnce` reddens with sharing turned off.
- `Load_SharedTextures_AreByteIdenticalToEachMaterialsOwn` compares every texture, bump, detail and animated-detail
  slot's RGBA against a load with sharing off. It reddens when the decode key drops the frame.

**Measured on f12:**

| | before | after, two runs |
|---|---:|---:|
| `texture-reads`: reads / MB read | 4,796 / 2,455.6 MB | **926 / 542.3 MB** |
| `VpkArchive.ReadFile`, MB per 20 s | 264 | 97 |
| large allocations, whole run | 8,498 MB | 6,095 MB |
| allocated since start | 50,836 MB | 48,468 / 48,362 MB |
| private bytes at the end | 7,959 MB | 7,487 / 7,498 MB |
| working set at the end | 5,141 MB | 5,189 / 5,207 MB |
| GC heap at the end (LOH) | 5,347 (3,793) | 4,872 (3,244) / 4,873 (3,242) |
| mean frame | 38.3 / 41.3 ms | 42.7 / 40.6 ms |

The frame times overlap, so this measured no frame-time change. The before rows are the earlier runs' numbers, one
run for the memory rows and two for the frame.

**Fixed: a block texture's upload pins its levels where they lie (2026-10-19).**
- **The cost.** `CreateBlockTexture` copied every mip of every DXT texture into a new array only to pin it, at 89 MB
  per 20 s after the texture table landed. It now pins the `ReadOnlyMemory` itself (`Memory.Pin`).
- **Measured.** The stack is gone from the trace. Large allocations over the run fell from 6,095 to 5,091 MB, and
  allocated since start from about 48.4 to 47.6 GB. Private bytes held at 7,496 MB on both runs, and the mean frame
  was 38.5 / 38.1 ms.
- **The test is the Rendering suite's block-texture renders** (1109 passed). No sabotage reddened a test for it: the
  call is private and device-bound, and a level pinned from the wrong span would still be valid memory.

**`PropModels.Read`'s 48 MB per 20 s is the baked animation frames themselves** (`List<PropVertex>(cornersPerFrame)`,
one per frame). That is item 3's subject, the bake that GPU skinning replaces, so it is not fixed separately here.

**The pool lease is therefore not built.** The candidate fixes are a per-load texture cache keyed by name, as
`CTextureManager` keys it, and one file read per animated texture, shared by its frames. Both keep the bytes alive to
upload, as now, and both are byte-identical by construction.

**The rounding trap.** `ArrayPool` rounds a request up to a power of two, which measured worse than a fresh array for
the model slice above. VTFs repeat sizes far more often than models do, so a pool is likelier to win here, but each
step is measured, never assumed.

### Fixed: particle definitions, one compact copy per install (2026-10-19)

**What changed:**
- **Parameters.** Each definition's and operator's parameters are a `ParticleParameterDictionary`: names sorted
  ordinally, with values in a parallel array, read by binary search. It keeps the `IReadOnlyDictionary` contract and
  the DMX reader's ordinal comparer, so `Number`, `Vector` and `Text`, and their "absent means the caller's default",
  are unchanged.
- **Sharing.** The definitions come from the install's archives alone, so `MapAssets` keeps them in a
  `ConditionalWeakTable` keyed by the `GameArchives` instance. Every map on one install gets the same set, and it is
  dropped with that install. It lives there rather than in `GameContent` because the archives are the only input, and
  `MapAssets.Load` already takes them.

**Tests:**
- `ParticleParameterDictionaryTests`: ordinal lookups and absence. Sabotage: an `OrdinalIgnoreCase` search reddens
  both tests.
- `ParticleDefinitionSweepConformanceTests.Read_EveryManifestDefinition_...` sweeps every definition in every manifest
  file, against the DMX reader's own attribute dictionaries. It covers the definition's parameters and every emitter,
  initializer, operator, renderer and constraint, each pair by type, number, text, vector, elements and numbers. It
  passed on the old form first, as the instrument's control. Sabotage: three-letter names read as absent reddens it.
- `Step_EveryDefinitionARocketReaches_LeavesTheStoreUnchanged` hashes the whole store (SHA-256 over every pair), steps
  the rocket trail and its children for five seconds, and hashes again.
- `Load_TwoMapsOnOneInstall_ShareOneSetOfParticleDefinitions`. Sabotage: clearing the table before each load reddens it.

**Measured** (map-memory, f12):

| | before | after |
|---|---:|---:|
| `ParticleSystemsByName`, attributed to the map | 239.3 MB (`Entry[]` 184.4) | 148.0 MB (`DmxValue[]` 95.8) |
| managed heap live after the load | 1,026.0 MB | 934.2 MB |
| live after `ReleaseUploaded` | 763.2 MB | 671.4 MB |

The 148 MB is now paid once per install, not once per held map. `DmxValue` is a wide struct (a double, a string, a
`Vector4` and two lists), so it is most of what remains. Typing operators one by one, as the engine does, is the
further step, and is not built.

### Item 5, base-texture animation frames: nothing to release on f12 (2026-10-19)

`UploadTextures` uploads each material's `AnimationFrames` beside its base texture, so they would belong in
`ReleaseUploaded`. But on f12, with the demo's models, all 688 slots are null: 0 animated, 0 frames. The 41 MB this
review once filed under animation frames was a misattribution. Nothing was built, because a release with no frames to
empty has no control that can pass: the test written for it failed its own control ("frames hold pixels before").

What `ReleaseUploaded` still leaves, after the census above:

| owner | MB |
|---|---:|
| `ScreenOverlays` | 45.4 |
| `PlacedCubemaps` | 5.2 |
| `SkyFaces` | 3.3 |

They are texture bytes, so they are the next candidates if they too go up with the world textures. That is not
checked yet.

## Plans not yet built

These were drafted for review before the churn work, and are ordered behind it.

### Plan (a): entity models, once per process and one pose each

**Design, in two steps that land separately.**

1. **Stop baking frames.** Today an unskinned animated model stores one full vertex list per animation frame
   (`PropModels`: `MaximumBakedFrames` 64, `MaximumBakedCorners` 2,000,000). That covers breadmonster, ammo packs,
   resupply lockers and doors. Route every model with an animation through the existing `SkinnedModel` path instead.
   The model then keeps one bind-pose vertex list plus bone weights, and is posed by bones on the GPU, as player
   classes already are. Valve does this for every studio model (`bone_setup.cpp`). The baked path and its two budgets
   are then removed, not kept as a fallback.
2. **Share game models across maps.** Load the models `DemoModels.Needed` names for every demo through a cache in
   `GameContent`, keyed by model path. Each map's `MapAssets` then adds only the models its own placements and demo
   name. **The trap:** a map's pakfile can override a model's materials or textures. Geometry and bones depend only
   on the model's files, so those are shared; material resolution stays per map. The cache must also refuse to share
   a model the map's pak itself ships.

**Tests.** Conformance first: for breadmonster and a door, posing the bind pose by bones at cycle c reproduces the
frame the bake would have produced at c, vertex by vertex within float tolerance, with the bake as the oracle before
it is deleted. Then `ModelFrames.Geometry.Count == 1` and `Skinned` for every animated model; two `LoadedMap.Read`
calls on different maps share `models/player/scout.mdl`'s geometry; a map whose pak overrides a material resolves its
own. Output-level: a resupply locker opening and an arrow in flight match golden captures taken before the change.

**Risk the owner could see.** Animation becomes continuous instead of snapping to sampled frames: closer to Valve, but
visibly different on doors and lockers. A model whose bone weights the skinning path has never read could draw in its
bind pose or exploded; the sweep exists to catch that. Getting the pak trap wrong draws a custom map's model with the
stock material.

**Bytes.** On f12, about 300 MB of the 329 MB entity-model row.

### Plan (b): particle definitions, one typed copy per process

**Design.**
1. Move the load of every `.pcf` definition out of `MapAssets.LoadRocketTrail` into a lazily built, immutable
   `GameContent.ParticleSystems`, forced before the content is shared. `MapAssets` keeps only its per-demo material
   uploads.
2. Replace each operator's `IReadOnlyDictionary<string, DmxValue>` with interned names and one sorted `DmxValue[]`
   per operator, searched by binary search. `ParticleFunction.Number`, `Vector` and `Text` keep their "absent means
   the caller's default" contract, so nothing above them changes.

**Tests.** Two `MapAssets` loads return the same `ParticleSystemsByName` instance. A conformance sweep reads every
parameter of every system through both forms, with the old reader as the oracle, before it is deleted. An absent
parameter still returns the caller's default. Output-level: an explosion and a medigun beam on f12 match golden
captures. The store is hashed before and after a demo plays, since sharing would leak any write across maps.

**Bytes.** 239 MB per held map down to one copy per process, plus what the compact form saves (not measured).

## What Valve does with a texture after upload (disassembled)

`CTexture::ReconstructTexture` in TF2's x64 `materialsystem.dll` runs four steps:

1. It reads the file into a per-thread scratch VTF.
2. `WriteDataToShaderAPITexture` uploads every frame.
3. `CTexture::ReleaseScratchVTFTexture` clears the texture's pointer to the scratch VTF.
4. "Final Cleanup" frees the read buffer.

The header-only path does the same and keeps only width, height and flags. **A `CTexture` holds no texels on the
CPU after upload.** The SDK does not ship `ctexture.cpp`, so this rests on disassembly, not source. `mat_picmip` was
not traced. Our `maxTextureSize` stands in for it, and the probe confirmed it is honored: the largest decoded texture
is 256 at 256 and 2048 at 0.

## Wrong turns, kept

1. **A census over `MapAssets.Load` alone reported 467 MB and measured the wrong subject.** The viewer holds a
   `LoadedMap`, which also loads every class, viewmodel, weapon and gib model through `DemoModels.Needed`, with or
   without a demo, and twice the texture slots. Measuring through `LoadedMap.Read` gave 833 MB and showed the
   biggest owner. *A probe runs the production path* (CLAUDE.md).
2. **The probe's own stack kept the "released" pixels alive.** The census said 467 MB where the heap said 655. Three
   hypotheses died first: that the walk skips delegates (following them changed nothing), a static cache (walking
   every static found nothing), and the archives (their footprint did not move). Weak references then showed every
   one of the 3,220 arrays alive, and the old list too. The holder was `Run` itself: one `assets.Textures.Count` read
   in the probe's own frame, which the JIT kept live for the whole method. With that read moved into a non-inlined
   helper, 0 survived and the heap agreed with the census to 2 MB. A heap control that disagrees with a census means
   the instrument is at fault until shown otherwise
   (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
3. **Full texture size looked ignored.** f12 at 0 measured the same as at 256 (heap 1,399.4 against 1,398.6 MB).
   The format line shows the argument is honored, since the largest decoded texture is 2048 at 0 and 256 at 256. So
   the near-equal totals mean f12's slots are nearly all 256 or smaller already. That is interpolated: no per-slot
   size histogram was taken.
4. **"The props phase keeps about 400 MB" was a delta on a phase timer, not an owner.** The census splits it into
   rows 1, 6 and 9, plus prop materials in the texture slots.

## Not settled

- Row 1a's holder.
- Valve's resident size for particle definitions, which would need TF2 running. TF2 is a shared fixture.
- Which lumps `Map_LoadModel` frees. Lighting's call site is located, but the bodies are not read.
- The census sees no GPU memory, no native memory and no timeline.
