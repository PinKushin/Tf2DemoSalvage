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

**What this leaves unknowable from here:** a map with brush water and no world water samples targets created once at client start
(`baseclientrendertargets.cpp:17,28`). Nothing in the SDK clears them on a map load, and whether the closed engine
does is *unread*. They hold whatever an earlier map's views or
the driver left, which depends on the session, not the frame. See `docs/RISKS.md` B662.

**A wrong turn, and what killed it (B664):** over koth_toxic's vat, TF2's water is teal and ours was black. The
`r_debugcheapwater` tail showed a world water "1500" away with "refract: true", so the first reading was that TF2 ran
a refraction view there and we did not.

That function prints only once it has a fog volume (`viewrender.cpp:2503-2504` returns first), and RCON output never
reaches `console.log`, so the tail was the spawn camera's. Searched after a fresh log mark at the vat, it printed
nothing for 10 s. The control, a camera over the world water, printed "dist to water: 176" at once, the same value
our plan logs there.

Both games plan cheap water at the vat. TF2's teal is what the targets kept from earlier views (B662).

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
