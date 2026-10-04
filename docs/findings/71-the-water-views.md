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
