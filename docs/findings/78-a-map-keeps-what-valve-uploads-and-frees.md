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
| 7 | `Assets.AnimatedDetails` 38.4 + `Assets.Cubemaps` 26.9: CPU pixels `ReleaseUploaded` leaves in place | 65.3 | no CPU texels | dis; uploader read | **The one candidate for a safe fix.** Both are uploaded inside `UploadWorldTextures` (`WorldRenderer.cs` around 3264 and 3579), the same pass as the base textures, so B407's release, and its reload on a lost upload, covers them. Not built yet. |
| 8 | `LightWarps` 5.8, `PlacedCubemaps` 5.2, `SkyFaces` 3.3, `BlendModulations` 2.3: CPU pixels not released | 16.6 | no CPU texels | dis | Each needs its uploader read first. `SkyFaces` goes up through `SetSkyFaces`. |
| 9 | `Assets.PlacedProps`: `Vector3[]` model shapes for static-prop decals (B421) | 12.4 | decals project onto the model's own vertices | interp | Check whether this duplicates row 1. |
| 10 | lumps: `Surfaces` 9.6, `Ambient` 3.5, `Displacements` 2.6, `Entities` 1.6, `Terrain` 1.1, `Leaves` 0.9, **`Visibility` 0.5**, **`StaticProps` collision 0.4**, `Normals` 0.3, the rest under 0.1 | 20.5 | The engine keeps surfaces, leaves, vis, collision, displacement collision and the entity lump for the life of the map | interp (`Mod_Load*` strings; bodies not read) | Near parity. Low priority. |
| — | released correctly (as loaded): `Textures` 278.9, `Bumps` 235.8, `Details` 50.3 | 0 after upload | no CPU copy | dis | Parity already (B407). They still coexist with everything above during the load. |

Rows 1, 2, 4, 6, 7 and 8 come to about 740 MB. Of that, about 580 MB (rows 1-game, 2 and 4) is the same on every
map. **Textures stay block-compressed** in memory and on upload: f12 holds 412 DXT1, 270 DXT5 and 4 RGBA8888, as
Valve uploads them (meas).

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
