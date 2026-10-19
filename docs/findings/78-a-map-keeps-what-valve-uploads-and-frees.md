# 78 — A map keeps what Valve uploads and frees

**The question.** The viewer peaks at 7.6 GB working set on the f12 demo, where TF2 plays the same map in about 3
GB. A loaded map was measured at 0.6–0.9 GB live. Which owners hold those bytes, and does Valve keep any of
them?

**The instrument.** `map-memory <map> [maxTextureSize]` reads the map through `LoadedMap.Read`, the viewer's own
path, with no demo. It charges every field of `MapLevel`, `MapAssets` and `LoadedMap`, then `GameContent` and every
static field, with the bytes it reaches first (`StateCopy.Footprint`, the D217 step 4 census). The ranked table
and the fix each row points at are in the review note for this round. The durable findings are here.

## What the census found (measured, cp_process_f12, texture size 256)

- **833 MB kept per map after upload** (census 832.7 MB, heap control 834.7 MB), and 1.4 GB while loading.
- **329 MB is entity-model vertices baked once per animation frame.** `ModelFrames.Geometry` holds a full vertex
  list per frame at 120 B per vertex. `w_breadmonster.mdl` alone is 90.7 MB. The engine never bakes frames: it poses
  bones per draw from one bind-pose vertex set (`bone_setup.cpp`). That set is `mstudiovertex_t`, 48 B, in the
  evictable `MDLCACHE_VERTEXES` datacache section (`imdlcache.h`). *Read from published source.*
- **239 MB is every particle definition the game ships, held per map, as a DMX tree.** One string-keyed
  dictionary per operator. The engine reads every manifest file too (`ParseParticleEffects`,
  `particle_parse.cpp`), but once per process. It unpacks each operator into a typed instance and frees the DMX
  (`DecommitTempMemory`). *Read from published source;* Valve's resulting size is *interpolated*.
- **About 127 MB of texture pixels survive `ReleaseUploaded`**: screen overlays, animated details, cubemaps, sky,
  light warps, blend modulations. The base, bump and detail textures are released correctly (B407).
- **42 MB of static-prop vertex lighting is kept as floats on the CPU.** The engine keeps `.vhv` colour meshes as
  RGBA8 in a pooled vertex buffer (`CPooledVBAllocator_ColorMesh`, a `CManagedDataCacheClient<CColorMeshData>`).
  *Disassembled: strings and RTTI in engine.dll.*
- **43 MB of lightmap atlas and decoded samples, against a 3.75 MB lighting lump.**

## What Valve does with a texture after upload (disassembled)

`CTexture::ReconstructTexture` in TF2's x64 `materialsystem.dll` runs four steps in order:

1. It reads the file into a per-thread scratch VTF.
2. `WriteDataToShaderAPITexture` uploads every frame.
3. `CTexture::ReleaseScratchVTFTexture` clears the texture's pointer to that scratch.
4. "Final Cleanup" frees the read buffer.

The header-only path does the same and keeps only width, height and flags. **A `CTexture` holds no texels on
the CPU once it is on the device**, so any pixel array we keep past upload is a copy Valve does not keep. The SDK
does not ship `ctexture.cpp`, so this is disassembly and not source.

## Wrong turns, kept

1. **A census over `MapAssets.Load` alone reported 467 MB kept, and it was the wrong subject.** The viewer holds a
   `LoadedMap`, whose read loads 366 entity models and twice the texture slots (688 against 368). Moving the probe
   to `LoadedMap.Read` took the figure to 833 MB and surfaced the largest owner, the baked model frames, which the
   first run could not see. *The instrument must run the production path* (CLAUDE.md, Probes).
2. **The probe's own stack kept the "released" pixels alive.** After `ReleaseUploaded`, the census summed 467 MB
   while the heap held 655 MB. Three hypotheses died first:
   - a delegate the walk skips: following delegates changed nothing;
   - a static cache: walking every static field of the project found nothing;
   - the archives: their footprint did not move.

   Weak references on the old texture list and its 3,220 pixel arrays showed every one of them alive. The holder
   was `Run` itself: a line reading `assets.Textures.Count` in the probe's own method, which the JIT reported live
   for the whole frame. Once that read moved into a non-inlined helper, 0 of 3,220 survived and the heap matched
   the census to 2 MB. **A heap control that disagrees with a census is the instrument's fault until shown
   otherwise**, which is the rule in `docs/memory/instrument-bugs-outnumber-decoder-bugs.md`.
3. **"The props phase keeps about 400 MB" was a phase-timer delta, not an owner.** The census splits it into
   model vertices, prop colours, placed-prop shapes, and prop materials in the texture slots.

## Not settled

- Full texture size (`0`) measured almost the same as 256 on the `MapAssets` path (187.3 against 186.5 MB). Either
  f12's textures are at most 256 already, or 0 is not full size there.
- About 8 MB of `byte[]` in each player model's frames has no identified holder.
- Nothing here sees GPU memory, native memory, or the demo timeline. The census accounts for about 0.8 GB of the
  viewer's 7.6 GB, so most of the viewer's memory sits outside the map.
