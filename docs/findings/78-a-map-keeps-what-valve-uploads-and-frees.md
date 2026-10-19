# 78: A map keeps what Valve uploads and frees

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

**Item 2, the packed models' CPU copy, is already released.** `EntityModelSet.ReleaseUploaded` (B407) drops the
729 MB once the map's world is on the device. Every run above ends with "entity model vertices on the CPU 0 in
0 MB". The 729 MB is real only between the load and the first upload. That makes it part of the load peak, not the
playback peak.

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
