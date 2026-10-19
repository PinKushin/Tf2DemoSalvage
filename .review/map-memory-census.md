# Map memory census: what one loaded map keeps, against what Valve keeps

Branch `fix/map-memory-census`, based on `bd98f61d`. Probe: `map-memory <map> [maxTextureSize]`
(`tools/Tf2DemoSalvage.Probe/Probes/MapMemoryProbe.cs`). The full account and the wrong turns are in
`docs/findings/78-a-map-keeps-what-valve-uploads-and-frees.md`. No fixes are implemented in this round.

## Method and controls

The map is read the way the viewer reads it: `LoadedMap.Read(bytes, game, timeline: null, 256)`. The probe
does not use a second loader. Each field of `MapLevel`, `MapAssets` and `LoadedMap`, then `GameContent` and
every static field of this project's types, is an owner. `StateCopy.Footprint` charges each owner for the
bytes it reaches that no earlier owner reached, the same method as the D217 step 4 census.

| control | cp_process_f12 @256 |
|---|---|
| census sum (less GameContent that existed before the load) vs managed heap delta, as loaded | 1,397.7 vs 1,398.7 MB |
| same after `ReleaseUploaded` (the call the viewer makes in `WorldPresenter.Project`) | 832.7 vs 834.7 MB |
| base texture slots holding pixels, a value that must not be zero | 686 of 688 |
| released pixel arrays still alive after `ReleaseUploaded` (weak references) | 0 of 6,044 |

**What this census cannot see:** GPU memory, native allocations (D3D, staging), the demo timeline and its
checkpoints, and anything the viewer keeps outside `LoadedMap`. The world build (`MapWorld`) is reported on
its own line: 30 MB, uploaded and then dropped.

**The props phase's ~400 MB.** That figure was a private-bytes delta over the load's "props" phase timer.
It is not one owner. The owners the props phase leaves behind are EntityModels, StaticModelColours,
PlacedProps and the prop materials in Textures and Bumps. The census below attributes them one by one.

## The table: what the viewer keeps per map after upload (f12, texture size 256)

Ranked by bytes. "Valve" is what the engine keeps resident for the same thing. Evidence classes:
**src** = read from published source (source-sdk-2013), **dis** = disassembled (TF2 x64 binaries, Ghidra, output
kept on D:), **meas** = measured by this probe, **interp** = interpolated, not shown.

| # | owner | ours (MB) | Valve resident | evidence | fix it points at |
|---|---|---:|---|---|---|
| 1 | `Assets.EntityModels`: `PropVertex[]` 231.8 MB, 120 B per vertex, **baked per animation frame** (`ModelFrames.Geometry`). `w_breadmonster.mdl` alone is 90.7 MB. | 329.0 | One bind-pose vertex set per model, `mstudiovertex_t` (48 B) plus a 16 B tangent. The VVD sits in the evictable `MDLCACHE_VERTEXES` datacache section, and the hardware mesh lives on the GPU. Bones are posed every draw, so no frames are ever baked. | src (`imdlcache.h` `MDLCACHE_VERTEXES`, `studio.h`, `bone_setup.cpp`); meas | Stop baking frames. Skin every model from one bind pose, as the `Skinned` path already does for some models. This is the biggest single defect, and it is structural, not a trivial fix. |
| 1a | the same: `Byte[]` 69.8 MB, about 8 MB per player class model | (in 329) | not known | meas; holder not split | Split the holder field before deciding. The census gives the array type, not the field that holds it. |
| 1b | the same: `StudioVertAnim[]` 19.8 MB (flex) | (in 329) | flex deltas in the cached MDL/VVD, evictable | src (enum); interp size | Probably parity in kind. Check the format width. |
| 2 | `Assets.ParticleSystemsByName`: **every** `.pcf` definition (about 9,050) kept as the DMX tree, one `Dictionary<string, DmxValue>` per operator (`Entry[]` 184 MB) | 239.3 | All definitions are resident too (`ParseParticleEffects` reads every manifest file), but each is unpacked into typed operator instances, and the DMX is temporary (`DecommitTempMemory`). They are read **once per process**, not per map. | src (`game/shared/particle_parse.cpp`, `public/particles/particles.h`); Valve's size interp | (a) Hold them once per game in `GameContent`, not in each `MapAssets`. Two maps held at once means two 239 MB copies, which is part of the Rendering.Tests peak. (b) Unpack the parameters into typed or compact form. |
| 3 | `LoadedMap._game` (GameContent, VPK indexes, item schema) | 48.4 | VPK directory and item schema resident once per process | interp | None per map. It is game-wide, and the census charges it once. |
| 4 | `Assets.ScreenOverlays`: CPU pixels of the jarate, bleed and similar overlay normal frames. `ReleaseUploaded` does not release them. | 45.4 | No CPU texels after upload (below), and loaded once per process | dis (`CTexture::ReconstructTexture`); meas | Release after upload, if the uploader reads them once (**not verified**). Load once per game: the content is identical on every map (f12 and koth_nucleus both 45.4 MB). |
| 5 | `Assets.Lightmaps`: atlas `Byte[]` 24.5 MB (pixels, plus `_baked` once a dlight rebuilds) and `Single[]` 17.3 MB of decoded style samples | 43.2 | The lighting lump stays resident for `R_BuildLightMap`: 3.75 MB on f12 (LDR and HDR are 3.75 MB each, one is used), `ColorRGBExp32` at 4 B per luxel, plus GPU lightmap pages | meas (lump sizes); interp (Valve's residency, `R_BuildLightMap` is not in the SDK) | Keep samples in lump form (4 B per luxel, not 12 B of floats). Do not keep a full CPU copy of the packed atlas once it is on the device. Rebuild a face into a scratch buffer and upload its rect, as dlights already update by region. |
| 6 | `Assets.StaticModelColours`: per-vertex static-prop lighting as `float[]`, 16 B per vertex, CPU | 42.0 | `.vhv` colour meshes, RGBA8 at 4 B per vertex, in a pooled GPU vertex buffer (`CPooledVBAllocator_ColorMesh`, a `CManagedDataCacheClient<CColorMeshData>`) | dis (engine.dll strings and RTTI); src (`hardwareverts.h`) | Store as 4 B per vertex and drop the CPU copy after upload. The 4x is from format alone. |
| 7 | `Assets.AnimatedDetails`: detail-texture animation frames, CPU pixels, not released | 38.4 | none on the CPU | dis | Extend `ReleaseUploaded`, if it is uploaded once (**not verified**). |
| 8 | `Assets.Cubemaps` 26.9 MB + `PlacedCubemaps` 5.2 + `SkyFaces` 3.3 + `LightWarps` 5.8 + `BlendModulations` 2.3: CPU pixels that `ReleaseUploaded` does not release | 43.5 | none on the CPU | dis | Same as row 7. |
| 9 | `Assets.PlacedProps`: `Vector3[]` model shapes for static-prop decals (B421) | 12.4 | not known. Decals on props project onto the model's own vertices (interp). | interp | Check whether this duplicates row 1's vertices. |
| 10 | `Level.Surfaces` 9.6, `Level.LightSamples` 8.5, `Level.Ambient` 3.5, `Level.Displacements` 2.6, `Level.Entities` 1.6, `Level.Terrain` 1.1 | 26.9 | The engine also keeps surfaces, displacement collision, leaf ambient and the entity lump for the life of the map | interp (`Mod_Load*` strings in engine.dll; bodies not read) | Near parity. Low priority. `LightSamples` (8.5) and Lightmaps' `Single[]` (row 5) may be two copies of one lump. Check this alongside row 5. |
| — | released correctly, for the record: `Textures` 278.9, `Bumps` 235.8, `Details` 50.3, `BlendTextures` 2.3 (as loaded) | 0 after upload | no CPU copy | dis | Parity already (B407). They still coexist with everything above **during** the load, which drives the private peak. |

**Total kept: about 833 MB per map, managed.** Rows 1, 2, 4, 6 and 7–8 account for about 740 MB of it, and none of
those bytes is something Valve keeps in that form. Even Valve's own copies of the particle definitions and
the VVD vertices are smaller and evictable, or held once per process.

koth_nucleus (MapAssets-only run, before the probe moved to `LoadedMap.Read`): 383 MB kept. ParticleSystems
239.3 and ScreenOverlays 45.4 are identical to f12's, which confirms they are game content charged per map. A
`LoadedMap` run on it was not repeated.

## What Valve does with a texture: the evidence for "no CPU copy"

`materialsystem.dll` (TF2 x64), `CTexture::ReconstructTexture` (string `ctexture.cpp` line 0xe91) runs these steps
in order:

1. It reads the file into the **scratch** VTF. This is a per-thread object (`DAT_180106a30[thread]`), not a field of the texture.
2. It calls `WriteDataToShaderAPITexture`, which loops over the frames and makes the shader-API upload calls.
3. It calls `CTexture::ReleaseScratchVTFTexture` (`FUN_18003d860`), which clears the texture's `+0x58` pointer.
4. "Final Cleanup" frees the read buffer.

The header-only precache path (`FUN_18003c120`) does the same with only width, height and flags copied into
the `CTexture`. So a `CTexture` keeps no texel memory on the CPU after upload. **Evidence: disassembly.** The
SDK does not ship `ctexture.cpp`.

## A trivially safe fix?

**None I can call trivially safe without one more read.** The candidates are rows 4, 7 and 8: CPU pixels the device
already holds and `ReleaseUploaded` leaves. Together they are about 127 MB on f12. They are safe exactly when the
uploader reads each of them once, at `UploadWorldTextures` / `SetSkyFaces`, and never again on a resize or a
device reset. I have not read the uploader to establish that. If the lead confirms it, the fix is to extend
`ReleaseUploaded` to those slots, with a test that predicts each is `TextureImage.None` after release and a
`Released` re-upload asking for a reload (`WorldPresenter` already handles that).

## Open questions this round did not settle

- `maxTextureSize 0` (full size) measured the same as 256 on f12 (Textures 187.3 vs 186.5 MB in the MapAssets-only
  run). Either the textures are already at most 256, or 0 is not full size on this path. That is worth one look
  before quoting any full-size figure.
- The `Byte[]` in each player model (row 1a). Its holder is unidentified.
- Valve's resident size for particle definitions (row 2) is interpolated. Measuring it would need TF2 running,
  and TF2 is a shared fixture.
