# 71 — The water views

B62 removed the missing-material chequer from water and drew it plain white, the shader's own "draw something"
fallback, and filed the real thing — the reflection and refraction render passes — as the owner's call. This is
that port, and what it took to find where each piece lives.

## Which views run — read from published source

`CViewRender::DrawWorldAndEntities` (`game/client/viewrender.cpp:2652`) asks the engine for the visible fog volume,
then `DetermineWaterRenderInfo` (`:2488`) for what that volume's material costs this frame:

- no volume, or no material: cheap, opaque, no surface — one `CSimpleWorldView`;
- `mat_drawwater 0`: cheap and NOT opaque;
- otherwise reflection needs `bForceExpensive` (the cvar OR the material's `$forceexpensive`, never past
  `$forcecheap`) AND `r_WaterDrawReflection` AND a `$reflecttexture` — and **a local reflection is drawn at any
  distance**, because the LOD exit at `:2606` is `(distance >= end && !bLocalReflection) || bForceCheap`;
- refraction needs `r_WaterDrawRefraction` and a `$refracttexture`, and makes the water not opaque.

**The trap in that list is `$forceexpensive`'s default.** `DetermineWaterRenderInfo` reads it with `IsDefined()`,
which reads as "only when the VMT says so" — but `water.cpp`'s `SHADER_INIT_PARAMS` runs first and sets it to **1**
on anything but the X360, under Valve's own comment *"By default, we're force expensive on dx9. NO WE DON'T!!!!"*.
So every PC water material with a `$reflecttexture` reflects at any distance unless it says otherwise.

Then `CAboveWaterView` (reflection, refraction, main, and an intersection view when the near plane crosses the
water) or `CUnderWaterView` (refraction from inside, then main), each with its own `DF_` flags, clear, fog and
`PushView` height clip (`:5295`: ±2 by `DF_FUDGE_UP`, above for `DF_CLIP_BELOW`, below otherwise). All of it is
`WaterViews.Plan`, test by test against the lines.

## Whose world list each view draws (B711) — read from published source, then in decompilation

**The reflection is the only water view with a world list of its own.** Every view builds one in
`CBaseWorldView::DrawSetup` (`viewrender.cpp:5411-5440`), but through `g_WorldListCache` (`r_worldlistcache` 1, `:127`;
`BuildWorldRenderLists`, `:3512-3526`). The cache keys on the camera fields only: ortho, fov, origin, angles, near, far,
aspect and off-centre (`:302-352`). It is flushed once a frame (`:2459`). The refraction, intersection and under-water
views copy the main view's `CViewSetup`, and the refraction's `AdjustView` changes only its rectangle (`:5253-5263`).
So they get the main view's list. With the cache off they would build the same one again: same camera,
`iForceViewLeaf` −1, no reflection height. The reflection's `AdjustView` mirrors the camera (`:5282-5284`), so its key
differs.

**What the reflection's own build does with its extra arguments** (engine.dll, the `tf2enginex64` Ghidra project's
build). `IVRenderView::BuildWorldLists` (slot 12, thunk `0x18012e680`) reaches `R_BuildWorldLists` at `0x1800defb0`.
Its force-view leaf (`m_nVisibleFogVolumeLeaf`, kept by `r_ForceWaterLeaf` 1, `:158`, `:2697-2701`), its vis override
and its water height go only to `0x180113f00`. That function is the area bits: the view leaf's area, then the portal
flood `0x180113b60`, which hands the height to the portal test `0x180112dd0`. The world walk itself,
`R_RecursiveWorldNode` (`0x1800e0600`), takes neither. It walks from the view `DrawSetup` pushed (`:5419`) against the
PVS stamps of the last `ViewSetupVis`. That is the main view's (`SetupVis`, `:1415`), as no water view calls it. **So
the reflection is the main eye's PVS, walked from the mirrored eye.** This project models no area portals
(`WorldVisibility`), so neither the force leaf nor the height changes its list here.

**The wrong turn this replaced:** the water views drew the main view's culled runs, the reflection included. A surface
outside the main camera's frustum that the mirrored camera sees was missing from the reflection. A naive fix, a twin
cull from the mirrored camera alone, would have marked the PVS from the mirrored eye. That eye is usually under the
floor, in solid space, where cluster −1 stamps every leaf.

## Whose renderables each view draws (B711) — read from published source

**Every view collates its own, the main one included, and the list it collates is pruned.** `DrawSetup` runs, in order:
- `AdjustView` and `Push3DView`;
- `BuildWorldRenderLists` and `PruneWorldListInfo`;
- with `DF_DRAW_ENTITITES`, `BuildRenderableRenderLists`;
- then `PopView` (`viewrender.cpp:5411-5440`).

`PruneWorldListInfo` (`:3539-3582`) keeps every leaf when both water sides draw and none when neither does. Otherwise it
keeps the leaves whose `m_pLeafFogVolume` is set exactly when the view renders under water, and records each one's index
in the unpruned list as `m_pActualLeafIndex`. So above expensive water the main view, drawing `DF_RENDER_ABOVEWATER`,
collates no renderable in a submerged leaf. The refraction collates only those.

The collation (`SetupRenderablesList`, `:3431-3465`) walks that pruned list. Its frustum test is `engine->CullBox`, under
the frustum the view pushed: the reflection's is mirrored (`clientleafsystem.cpp:1574`, `:1813`). Each leaf's new
translucent entries sort from the view's own `origin` and `CurrentViewForward()`. The translucent pass (`:4547-4695`) then
walks the pruned leaves back to front. It draws each leaf's translucent world surfaces by `m_pActualLeafIndex` (`:4298-4323`)
before that leaf's entities. In a water target it skips any renderable that reads the frame buffer (`:4614-4617`).

**The wrong turn:** the water views drew the main view's collated models, sorted from the main eye, and the monitor's
draw of the same list walked the sorted entries forwards, nearest first. Their translucent world was drawn after their
entities, whole, where the engine interleaves the pruned leaves. Each water view now collates its own, and walks its own
pruned leaves in its translucent pass. The main view's own pruning is open (`docs/RISKS.md` B711).

## Which volume is visible — read in disassembly

`R_GetVisibleFogVolume` is engine-side; its VPROF string names it at engine.dll `0x1800e0080`:

- the eye leaf's own `leafWaterDataID` (`dleaf_t` +0x42 in memory): eye in the volume, its `surfaceZ`, and the
  material from `0x1800dffd0` with its second argument set — which looks up **`$bottommaterial`** (the string at
  `0x18038eae0`). From inside, the view decides with the beneath material, not the surface;
- else, ONLY for a leaf with `CONTENTS_TESTFOGVOLUME` (0x100), a front-to-back walk (`0x1800e0f70`) for the first
  leaf that is in this frame's visible set, inside the frustum, wet, and not `CONTENTS_SLIME`;
- `m_flDistanceToWater` is `LUMP_LEAFMINDISTTOWATER[eye leaf]` in every branch (`0x1800e031f`);
- a `fast_fogvolume` shortcut takes volume 0 unwalked when the map has exactly one — its default string is `"0"`
  (`0x18035de18`), so it is off.

## Where the per-frame LOD reaches the shader

**The question this was filed with — "how does the shader know the view chose cheap water?" — has no answer in the
engine, because the shader does not need to know.** `SHADER_DRAW` decides its passes from the material alone and
draws the expensive pass whenever `$refracttexture` is a texture. The LOD reaches it through the material: TF2's
water VMTs run a `WaterLOD` proxy (`WaterLODMaterialProxy.cpp:61`) that writes the VIEW's cheap-water distances
into `$cheapwaterstartdistance`/`$cheapwaterenddistance` every bind, and the cheap pass draws over the expensive
one with `alpha = saturate(fresnel + saturate(dist / (end - start) - start / (end - start)))`, times the refraction's
fog depth. The view's distances come from `water_lod_control` (server defaults 1000/2000), and with none on the
map, from the view's constructor: **0 and 0.1** (`viewrender.cpp:937`). So on a map without the entity the cheap
pass covers everything but water shallower than the 0.05 fog-depth knee — and `DetermineWaterRenderInfo` agrees,
because the same 0.1 makes every distance cheap.

## Measured on ctf_2fort

The 2fort water VMT (`vmt materials/water/water_2fort.vmt`) declares `$refracttexture`, no `$reflecttexture`, an
`$envmap`, `$fogstart -100`/`$fogend 400`, a `WaterLOD` proxy, and **`$refractblur 1` — which is not a parameter
the shader declares**; `BLURRY_REFRACT` reads `$blurrefract`. Valve's own map, ignored by Valve's own shader.

`WaterViewRenderTests`: the water drawn over a red and a green floor five units down reads (205,0,0) and (0,107,0);
with the refraction view removed, (0,0,0) both times.

## A surface is two faces, and the cull picks one (B657) — measured, then read

**ctf_2fort's water drew grey-green where TF2's is brown, 20-50 levels off on every pixel**, against a live pair at a
pinned exposure. The suspects filed with it were the reflection, the cubemap and the refraction's height fog. Every
one was wrong. The material's constants were right too, but they belonged to the wrong material.

vbsp builds a water surface as **two coplanar faces**: the upward one with the surface material, and the downward one
re-textured with that material's `$bottommaterial` (`utils/vbsp/faces.cpp:1255-1289`, `AssignBottomWaterMaterialToFace`).
The engine's world walk does not separate them: a surface in a water leaf skips the plane-side test that culls every
other node surface (engine.dll `0x1800e0600`). **The hardware cull does it.** The `Water` shader leaves culling on
everywhere but the editor (`water.cpp:393-397`, *"In edit mode, use nocull"*). That culling is otherwise on is
*interpolated* from that line, because shaderlib is unpublished. The live pair confirms it. This renderer drew every
world batch two-sided, so the two faces z-fought, and 2fort's `water_2fort_beneath` drew first and won.

**The instrument that found it** was a scratch build that output one quantity from the water shader at a time. The raw
refraction texel already matched TF2 (42 30 13 against 41 31 10). The material's own fog colour painted the whole
surface 92 100 79, which is the beneath's `$fogcolor`, not 2fort's 51 43 13. With back faces culled, the surface
reads 42 31 11. The numbers and the open under-water gap are in `docs/RISKS.md` B657.

## Water on a brush entity is just a surface of its model (B659): read in disassembly

**A water material on a `func_brush` or `func_water_analog` gets no water view and no water rule of its own.** The SDK
side hands a brush entity to `render->DrawBrushModelEx` (`c_baseentity.cpp:1959`, `:1152`). `DetermineWaterRenderInfo`
(`viewrender.cpp:2488`) builds the reflection and refraction views from the eye's WORLD fog volume, and nothing else
does, except `func_reflective_glass`.

In engine.dll, `R_DrawBrushModel` (`0x1800df3d0`) picks one of three paths:
- The opaque batches, `0x1800db920`. Its batch builder `0x1800ddfa0` keeps every surface of the model whose flag bit
  `0x20` is clear, sorts them by sort id, and batches them by material and lightmap page.
- The translucent walk, `0x1800dd600` to `0x1800dabb0` to `0x1800dcaa0`. It takes the model's nodes back to front and
  keeps the surfaces with `0x20` set that face the eye (bit `0x40` against the side, or `0x200`).
- An installed `IBrushRenderer` per surface, `0x1800df720` to `0x1800e13e0`.

The bit uses are disassembly. Their names (`SURFDRAW_TRANS`, `_PLANEBACK`, `_NOCULL`) come from the unpublished
`gl_model_private.h` and are *interpolated*.

**None of the three tests a water or warp flag**, and both drawing paths bind each batch's own material. So the
`Water` shader runs on the face. It reads only its parameters (`water.cpp:534-584`) and samples
`_rt_WaterRefraction` and `_rt_WaterReflection` as they stand, filled this frame only by the world water's views.

The third path runs only while an `IBrushRenderer` is installed. Its global is written only by a bare setter
(`0x1800e0390`). That setter is reached only through a thunk in the `VEngineRenderView014` vtable (slot 31,
`InstallBrushSurfaceRenderer`), so the engine never calls it itself. clangd finds no caller of it in the SDK's
client; as a control, `DrawBrushModelEx` on the same line shape finds three. The live `client.dll` is not checked.

The opaque path also front-face tests each surface in software (`dot - dist < -0.01` skips it) before the
hardware cull.

**What a target holds before any view writes it** (B662): the SDK stops at `baseclientrendertargets.cpp:16-34`, but
the closed material system answers. `CTexture::ReconstructTexture` (materialsystem.dll `0x18003cd60`) clears every
new colour render target opaque black, `( 0, 0, 0, 255 )` (disassembly; the clear's names are interpolated from its
arguments). shaderapi's `CreateD3DTexture` clears nothing. Alpha 1 is "deep", so water over a never-written
refraction fogs fully to its `$fogcolor`. TF2 launched with the water views switched off shows exactly that.

A map load keeps the targets unless the map's HDR-enabled state flips (`Mod_LoadMapFlags`, engine.dll
`0x1800ffa10`). A flip recreates them, and the recreated targets are cleared again. The state compared is "map has
HDR lighting, `mat_hdr_level` > 1, DX level > 89", which is our `MapAssets.Hdr != None`; ours now releases the water
targets on exactly that flip and keeps them otherwise (B662's second half, disassembly).

**A wrong turn, and what killed it (B664):** over koth_toxic's vat, TF2's water is teal and ours was black. The
`r_debugcheapwater` tail showed a world water "1500" away with "refract: true", so the first reading was that TF2 ran
a refraction view there and we did not.

That function prints only once it has a fog volume (`viewrender.cpp:2503-2504` returns first), and RCON output never
reaches `console.log`, so the tail was the spawn camera's. Searched after a fresh log mark at the vat, it printed
nothing for 10 s. The control, a camera over the world water, printed "dist to water: 176" at once, the same value
our plan logs there.

Both games plan cheap water at the vat. TF2's teal is the refraction target's opaque-black alpha, either from earlier
views or from the creation clear above. A fresh session with every water view off gives the same teal (B662).

**Inside a water view the engine samples its own render target (B663, disassembly).** The refraction view draws
entities (`viewrender.cpp:6033`), so a brush entity's water face is drawn while `_rt_WaterRefraction` is the output.
shaderapidx9's `BindTexture` (`0x1800149b0`) goes straight to `SetTextureState` (`0x180023840`) and
`IDirect3DDevice9::SetTexture`, with no check against the render target. The one guard, `UnbindTexture`
(`0x180025470`), runs only when a render target is set. D3D9 leaves the result undefined. This renderer samples a
copy of the target taken at the view's first water bind that reads it. The copy holds the view's clear, its world,
and entities drawn before that face. It is taken once per view. That is an interpolation and not parity (D244, RISKS
B663).

## The fog the water views draw under — read in disassembly

`IVRenderView::SetFogVolumeState` is vtable slot 30 of the object `VEngineRenderView014` registers (vtable
`0x180394378`), a thunk to `0x1800e0cd0`. It asks `0x1800dffd0` for the volume's material with the second argument
`!bUseHeightFog` — so **height fog takes the SURFACE material and plain volume fog the BOTTOM one** — reads
`$fogcolor`, `$fogenable`, `$fogstart`, `$fogend`, and only when `$fogenable` is non-zero and `fog_enable_water_fog`
(default `"1"`) is on sets the fog z to the volume's `surfaceZ`, the mode (2, linear below fog z, for height fog;
1 otherwise), the colour, start, end and a max density of 1.0; otherwise fog is off. **An undeclared `$fogenable` is
no fog** — the earlier port fogged every water volume regardless.

`DoesBoxIntersectWaterVolume` is slot 34 (`0x18012ea20`): every leaf in the box is offered to an enumerator that
stops on the volume's water data ID.

**Translucent draws never write the fog factor.** `lightmappedgeneric_dx9_helper.cpp:907-918`: *"can't write a special
value to dest alpha if we're actually using as-intended alpha"*. Writing it anyway weighted every blended surface in
a refraction by its fog factor, so translucent glass under water vanished — the first version of the test caught it.

## What is interpolated, and what is still ours

The list is kept in one place — `docs/RISKS.md` under B62, "2026-10-03" — rather than restated here.
