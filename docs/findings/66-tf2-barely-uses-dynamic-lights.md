# 66 — TF2 barely uses dynamic lights

B425 was filed as "explosions, muzzle flashes and burning players light nothing but their own sprites". The
premise was wrong: in TF2 they light nothing in the engine either. Nearly the whole dlight system is still in the
engine, but TF2's client code barely allocates from it.

## The list, read from `engine.dll`

Evidence class: **disassembly**. `dlight_t` itself is published (`public/dlight.h:43`, 64 bytes).

- `IVEfx` (`VEngineEffects001`, object `0x180472160`, vtable `0x180393760`). Slot 4 `CL_AllocDlight` jumps to
  `0x18008a9c0`, which uses **32** slots at `0x180533c80` (`MAX_DLIGHTS`, `iefx.h:22`). Slot 5 `CL_AllocElight` jumps
  to `0x18008aa60`, which uses **64** slots at `0x180534480`. `CL_ClearState` (`0x18008b030`) zeroes `0x800` and
  `0x1000` bytes of them.
- **Which slot** (`0x18008aac0`): the slot whose key equals a nonzero key, else the first slot whose `die` is below
  the client time, else slot 0. The slot is zeroed and keyed. A dlight also gets its bit in `r_dlightactive`
  (`0x1806996c4`) **at allocation**, so a new light counts before any decay has run.
- **Decay** (`CL_DecayLights`, `0x18008b2f0`). Nothing happens unless the frame time is positive. A dlight of positive
  radius goes to radius 0 once the time is past `die` (`time <= die` keeps it). Otherwise it loses
  `decay · frametime`, clamped at 0. The active mask is rebuilt from what is left. Elights work the same way without
  a mask. `_Host_RunFrame_Render` (`0x1801a5fa8`) calls it **after** `SCR_UpdateScreen`: a frame is drawn with the
  lights its think produced, and they decay afterwards. So a light already past `die` still lights one more frame.

## What a model draw makes of one

- `LightcacheGet` (`0x1801b9cd0`), when flag 2 is set, calls `0x1801b7a10` once per frame per cache entry. It takes
  every active dlight and every elight of positive radius, **skipping any light with a bit set in `flags & 0xe`**.
  That is `DLIGHT_NO_MODEL_ILLUMINATION` plus the two displacement-alpha bits, so `DLIGHT_NO_WORLD_ILLUMINATION` alone
  still lights models. It keeps only lights whose origin cluster is in the PVS of the entry's leaf. Each survivor is
  converted and added through the same `AddWorldLightToLightingState` (`0x1801b60a0`) as a world light, into a
  dynamic state at `entry + 0x100`. `0x1801b5890` then merges that state into the output: each light is ranked
  again against the static lights' weights, and the dynamic cube is added. **A dlight competes for the four
  local-light slots like any world light.**
- **The conversion** (`0x1801bb940`) builds a `dworldlight_t`:
  - `emit_point`, or `emit_spotlight` when `m_OuterAngle > 0`, with `stopdot = cos(inner)` and
    `stopdot2 = cos(outer)`.
  - Intensity is `color · table[exponent]`. The table at `0x18047e280` holds `2^e / 255`: `0x3B808081` is 1/255.
  - Radius is `max(radius, 0.1)`.
  - `quadratic = 1 / (r · max(minlight, floor) · r)`, with no constant or linear term. So the light falls to exactly
    `minlight` at its radius.
  - The `floor` is `DAT_18047be1c`, which `FUN_1801b8d30` sets per map: **1/256 above BSP version 19** and 20/256
    otherwise. Those are `dlight.h`'s `MIN_LIGHTING_VALUE` and `HL2_BROKEN_MIN_LIGHTING_VALUE`, both of which the
    SDK leaves commented out.
  - The exponent field is never written and stays zero.
- **Static props.** `0x1801baf60` enumerates the static props in each live dlight's radius. `FUN_1801b5260` then sets
  the dlight's bit in the handle's `+0x1a4` and stamps `+0x1a8`, but only for a prop in the PVS of the light's
  origin. In the model draw (`0x1800f1bd0`), a baked prop is sent to full lighting by that bit (`FUN_1801bb8a0`) —
  **but only on the branch where the hardware-config slot `0x150` answers false.** When it answers true, the draw
  keeps the colour mesh and asks `FUN_1801ba590(handle, 6 + bit)`. B429 records slot `0x150` as
  `SupportsStaticPlusDynamicLighting` and says it is true on every DX9 part. If so, both B424's fallback and this
  one sit on the branch TF2 does not take. **Open question, recorded in B425. Not built.** *Settled 2026-09-29:*
  they do; the DX9 path is built, [67](67-a-baked-prop-adds-its-lights.md).

## Who allocates, read from the SDK

Evidence class: **published source**, through clangd's call hierarchy of `IVEfx::CL_AllocDlight` and `CL_AllocElight`.

| Caller | Fires in TF2? | Fields |
|---|---|---|
| `C_BaseExplosionEffect::CreateDynamicLight` (`fx_explosion.cpp:716`) | **no** — `Create` has `//FIXME: CreateDynamicLight();` | key 0, r 255, decay 200, (255,220,128), die +0.1 |
| `TFExplosionCallback` (`tf_fx_explosions.cpp:43`) | no light at all; particles and a sound | — |
| `C_BaseAnimating::ProcessMuzzleFlashEvent` (`c_baseanimating.cpp:3521`) — elight | **no** — `C_TFPlayer`, `C_TFWeaponBase` and `CTFViewModel` all override it without one | r 32–64, decay r/0.05, (255,192,64,5) |
| `C_BaseEntity::CreateLightEffects` (`c_baseentity.cpp:2993`), from `AddEntity` | only if the server sets `EF_BRIGHTLIGHT`/`EF_DIMLIGHT` | key index, r 400–431 or 200–231, die +0.001 |
| `TE_DynamicLight` (`c_te_dynamiclight.cpp:83`) | only if the server sends `CTEDynamicLight` | as sent |
| `C_DynamicLight::ClientThink` (`c_dynamiclight.cpp:129`) | only on a map with `light_dynamic` | as networked, die +1e6 |
| `CTFProjectile_BallOfFire::ClientThink` (`tf_projectile_dragons_fury.cpp:509`) | **yes**, for the LOCAL player's own fireball | key index, r 100, (255,100,30,8), die +0.05 |

Burning players have no allocator: `C_EntityFlame::ClientThink` only releases.

## Measured, 2026-09-29

Evidence class: **measured**. The CLI decompiled each demo to text, and the output was checked for each allocator's
input. Control: `ENTER class CTFPlayer` and `CObjectSentrygun` are present in the same output.

The sample was all ten gcor demos plus nine lcor demos (two 2026 pubs, `koth_harvest_event`, an RGL pug POV, an
ETF2L POV, `pl_badwater_pro`, `pass_sanctum`, `hackermgereddit`, and a `cp_process` movement test). Across all 19:

- **`CTEDynamicLight`: 0.** `CDynamicLight` entities: 0. `m_fEffects` with bit 2 or 4: 0.
- `CTFProjectile_BallOfFire`: 72 fireballs, in `tf2-2026-pub-pov-clean` only. All 72 are owned by entity 17, and the
  recorder is entity 9 (`roster` probe). **So none of them allocates a light.**

## What was built

`DynamicLights` (list, allocation, decay, conversion, the Dragon's Fury allocator), `LevelLighting.ModelLightingAt`
(the ranking), and `MomentScene` (thinks in `Build`, decays at the end of `Pose`, and clears on a backward jump,
because the engine cannot seek).

The world lightmap half is below.

## The world's lightmaps, read from `engine.dll` (2026-09-29)

Evidence class: **disassembly** (decompiler output, with the one surprising result checked in the listing), plus
published source for `GetBumpNormals` (`mathlib/bumpvects.cpp`) and `TexLightToLinear` (`mathlib.h:975`).

- **Marking.** `R_PushDlights` (`0x1800d48b0`) takes each dlight with `time <= die`, `radius > 0` and no
  `DLIGHT_NO_WORLD_ILLUMINATION` (flag 1 only) and walks the world's nodes (`0x1800d4520`). A node plane distance
  above `radius` goes to child 0, below `−radius` to child 1; otherwise the node's own faces are tried and both
  children walked. A leaf (`0x1800d4680`) tries its faces that are not `SURFDRAW_NODE` and lie within `±radius` of
  their plane, and its displacements by a sphere-against-box test. `R_TryLightMarkSurface` (`0x1800d4970`) needs
  `d = n·o − dist ≥ −15` and `r² − d² > 0`, then a circle of `luxelsPerWorldUnit · √(r² − d²)` luxels round the light's
  lightmap coordinate must reach the face's luxel rectangle `[mins, mins + extents]` (`0x1801735f0`). A hit ORs the
  light's bit into the face's bits (never reset there), stamps the frame and sets `SURFDRAW_HASDLIGHT`.
- **Which faces rebuild.** `R_RenderDynamicLightmaps` (`0x1800d09a0`) rebuilds a face whose style changed, or — with
  `r_dynamic` (default **"1"**, `0x180007c90`) — one marked this frame **or still carrying bits**. The per-face pass
  (`0x1800d0ba0`) ANDs the bits with `r_dlightactive`; a face not marked this frame loses them all, and a marked one
  keeps a light only if it is active, has no bit in `flags & 0xd`, and passes the same plane test. So a light that
  leaves or dies costs its faces exactly one more rebuild, without it, and then nothing. `r_maxdlights` (default 32,
  `0x180005f70`) caps distinct lights per frame and cannot bind with 32 slots.
- **The addition** (`R_BuildLightMap` `0x1800cfca0` → `0x1800ceb00`). With `s, t` the light's lightmap coordinate less
  the face's mins and `wupl = 1 / |lightmapVecs[0].xyz|` (`Mod_LoadTexinfo` `0x180103da0`), each luxel `(col, row)` has
  `dist² = ((s − col)·wupl)² + ((t − row)·wupl)² + d²`, and below `r²` it gains
  `colour · 2^e/255 · style/264 · min( (dist² == 0 ? 1 : minlight · r² / dist²) · (1 − dist²/r²), 2 )`, `minlight` floored
  at 1/256. A spotlight aimed with the face's normal adds nothing. The base lightmap is in `TexLightToLinear` units,
  `byte · 2^e / 255`; this port stores `byte · 2^e`, so its dlight colour is the engine's times 255.
- **Bumped faces** (`0x1800cf1b0`): the flat page as above; page `k` gains the same `· max(dir·bump_k, 0) /
  max(dir·n, 0.001)`, the basis from `GetBumpNormals` on the normalised lightmap vectors and the plane normal. **The
  direction is taken from the ROW's position only** — `origin + t_vec · row · wupl²`, loaded once per row at
  `0x1800cf460` and never moved by the column loop at `0x1800cf4d5`. Every luxel in a row shares the direction of
  its first. Reproduced, not corrected.
- **Displacements** take their own path: the leaf's list, then `CDispInfo` (vtable `0x18038d8b8`) slot 0x38
  (`0x1800c4540`: the bits, flag 1 only, no plane test) and slot 0x30 (`0x1800c3130` → `0x1800c0600`: a 3D position,
  normal and bump basis rebuilt per luxel from the disp's triangles). Built 2026-09-29, below.
- **Brush entities** (`0x1800e03a0`) move the light into the model's space and walk its own nodes. Built 2026-09-29,
  below.

## Displacements and brush entities, read from `engine.dll` (2026-09-29)

Evidence class: **disassembly** for the engine (the three `CDispInfo` adders are thunks whose only difference is the
callback they store at `+0x28` of a stack functor, read in the listing, not the decompiler); **published source** for
the tables the engine reads (`disp_powerinfo.cpp`, `disp_vbsp.cpp`, `builddisp.cpp`).

- **Marking a displacement.** The leaf pass (`0x1800d4680`) walks the leaf's displacements BEFORE its faces: a parent face
  not already carrying the bit this frame, whose box (slot 0x18) meets the sphere by `IsBoxIntersectingSphere`
  (`0x180172c60`, squared gap strictly below `r²`), takes the bit, the frame and `SURFDRAW_HASDLIGHT`.
- **Keeping.** `0x1800d0ba0` hands a `SURFDRAW_HAS_DISP` face to slot 0x38 (`0x1800c4540`): a light is kept when active,
  carried, `flags & 1` clear and under the `r_maxdlights` budget (`0x1800d0300`). No `0xd` mask and no plane test; no
  bit is dropped one by one.
- **Where a luxel is.** `0x1800c0600` walks the luxels row by row through `LUMP_DISP_LIGHTMAP_SAMPLE_POSITIONS` (34) at
  `ddispinfo_t::m_iLightmapSamplePositionStart` (offset 44): a triangle byte, 255 escaping to `255 + next`, then three
  bytes times `0.003921569` — the barycentric weights vbsp wrote as `(unsigned char)(b · 255.9)`
  (`disp_vbsp.cpp:93-136`). The triangle indexes `CPowerInfo::m_pTriInfos` (`+0x28` of the power info at `+0x268`,
  `disp_powerinfo.h:164`), wound by `InitPowerInfoTriInfos_R` (`disp_powerinfo.cpp:319-370`): from the centre into the
  four children, and at a node one step from the grid eight triangles round it from `g_TesselateVerts`. These are the
  full grid's triangles, NOT the drawn tesselation, which `m_AllowedVerts` thins.
- **Adding.** Slot 0x30 (`0x1800c3130`) sends a light with `flags & 0xc` to the alpha path (`0x1800bf7d0`, the blend alpha,
  not built), otherwise `0x1800bf8d0` (one page) or `0x1800bf9d0` (bumped, `SURFDRAW_BUMPLIGHT` via `0x1800cba50`). The
  functor carries `max(minlight, 1/256)·r²`, `1/r²`, the colour (as a face's), `r²` and the WORLD origin. Flat
  (`0x1800c0ad0`): `d² = |o − p|²`, the face's falloff by the true 3D distance. Bumped (`0x1800c0c40`):
  `dir = (o − p)·rsqrt(d² + 1e-10)` (one Newton step), n, s, t its dots on the luxel's interpolated normal (`+0xb0`) and
  tangents (`+0x108`, `+0x110`); page 0 takes `max(n, 0)·f` and pages 1–3 `max(g_localBumpBasis_k·(s, t, n), 0)·f`.
  **Unlike a face, no division by the normal's share and page 0 is not the full falloff.**
- **The tangents** are `GenerateDispSurfTangentSpaces` (`builddisp.cpp:1692-1727`): `T = |tAxis|`, `S = |N × T|`,
  `T = |S × N|`, `S` negated when `(sAxis × tAxis)·planeNormal > 0`. *Interpolated:* `+0x108` as `m_TangentS` and `+0x110`
  as `m_TangentT`, from which page each dot feeds.
- **Brush entities.** `R_MarkDlightsOnBrushModel` (`0x1800e03a0`), per drawn brush, while any light is active: the
  render matrix (`0x18027c260`: `AngleMatrix` and the origin); each dlight with `time <= die` and `radius > 0` — **no
  flag test, unlike `R_PushDlights`** — has its origin replaced by `Rᵀ(o − t)` and is walked from the model's head node
  (`0x1800d4520`) when that node's box meets it (`IsBoxIntersectingSphereExtents` `0x180172d10`, strict), then restored.
  The rebuild reads the light through the same matrix: `0x180279620` moves it before `0x1800d0ba0`'s plane test and
  before `0x1800cfca0` calls the adders. The spotlight direction is NOT moved.
- **`GetBumpNormals`**, the step-2 open item: the engine's copy (`0x18027a090`, called from `0x1800d0340`) tests
  `(s × t)·flat < 0`, builds `|phong × s|` then `|that × phong|`, negates the second when left-handed, and rotates
  `g_localBumpBasis` by `VectorIRotate` — `bumpvects.cpp` line for line, which is what the port does. Closed.

**Built:** `BspTerrain.SampleTriangles`/`LuxelPositions`/`ReadLuxelPositions` into `DecalDisplacement.Luxels`;
`WorldDynamicLights` marks and rebuilds displacements and takes `LitBrush`es (`BrushesOf` the frame's drawn `*N` props)
with `DecalNode`'s box. Tests: `DisplacementLuxelPositionConformanceTests`,
`WorldDynamicLightDisplacementAndBrushConformanceTests`, `MomentSceneTests.Pose_TheRecordersFireballOverTerrainAndADoor_…`.
**Named, not built:** the displacement alpha lights; the vertex normal, where the parent plane's stands in for
`CalcNormalFromEdges` and its neighbour smoothing (the port's terrain mesh has none either); and the leaves' displacement
lists, pushed down the tree by box because the load-time builder was not found.

**Built:** `WorldDynamicLights` (marking, the per-face pass, both additions), `LightmapAtlas.Rebuild`, the frame's
lights copied before the decay (`MomentScene.WorldLights`, as `EndUpdateLightmaps` `0x1800d5fa0` copies `cl_dlights`),
and the viewer's `StepLightStyles` calling it after the styles. On `tf2-2026-pub-pov-clean` no light reaches the world
(`CorpusWorldDynamicLightTests`, with a recorder-17 control that finds them).

## An `emit_surface` light is a runtime light, and a static prop's state folds (B669, 2026-11-03)

**The wrong conclusion, kept because it was confidently written in three places.** All 108 of cp_process's surface lights
have attenuation terms of exactly zero and intensities around 7,000. Evaluated through vrad's all-zero rule they did not
attenuate, the middle capture point drew at a luminance of 6.3, and the kind was excluded with the reasoning *"an area light
for the radiosity solver has no distance term because it is never evaluated at a distance. It is resolved at compile time
into the lightmaps and into the leaf ambient cube."* The measurement was real. Both halves of the reasoning are wrong.

**What killed it, read in disassembly** (`engine.dll`, x64), with the published twin where vrad keeps one:
- **It has a distance term and never reads those fields.** `FUN_1800efe40` case 0 returns `1 / max( 1, d² + 1e-10 )`, zero past
  a nonzero radius (`Engine_WorldLightDistanceFalloff`, `leaf_ambient_lighting.cpp:77`).
- **It has an angle.** `FUN_1800efbf0` case 0: `dot2 = -delta · normal`, zero at or under 0.01, else one cosine
  (`Engine_WorldLightAngle`, `:58-73`).
- **No `r_worldlightmin`.** `FUN_1801b9570`'s test and `FUN_1801b60a0`'s are both guarded by `type != 0`.
- **It is in the leaf cube only if vrad flagged it, and only a leaf-lit caller cares.** `FUN_1801b8270` asks `FUN_1801bb4b0` for
  the cube, which sets an out-flag only where it read the leaf (`FUN_180100f60`). `FUN_1801b5a50` skips a light only when
  that flag is set AND the light has `DWL_FLAGS_INAMBIENTCUBE`. A static prop's 162-ray cube holds no surface light, so a
  static prop takes all of them.
- **In a slot it is a spot** (`FUN_1800f0e10` case 0): attenuation 0/0/1, theta and phi pi, both dots 0, falloff 1.

**Measured** (`world-light-kinds`): **0 of cp_process_f12's 108 surface lights and 0 of koth_toxic's 4 carry the flag**, and that
is what vrad's rule predicts: `IsLeafAmbientSurfaceLight` (`leaf_ambient_lighting.cpp:183-198`) flags a style-0 surface light whose
brightest channel is under `0.005 · 512²` = 1310.7, and the dimmest of these are 3660.9 and 5941.2. So "already in the leaf cube" was
false for every surface light on both maps, and moving models take them too. No shipped specimen with the bit set has been seen,
so the reader itself is proved on a synthetic lump.

**The second defect it uncovered: the static state did not fold.** `StaticPropLightingAt` kept the strongest four and dropped
the rest, where `FUN_1801b60a0` sends every loser to the cube (`FUN_1801b5db0`). Harmless to notice while few lights reached a
prop; with 108 more candidates it is most of the light.

**The third: no PVS gate on the style-0 path.** `FUN_1801b60a0` returns before it traces when a light's cluster is negative or out
of the PVS `FUN_1801b8270` built from the entry's leaf, for every kind but the sky. Ours traced them all. Now gated. It changed
nothing in the far frame, byte-for-byte in these boxes; it is a parity and cost fix, pinned synthetically. **Both negatives, read:** a light
in cluster −1 is out on any map (`JS 0x1801b6523` at `0x1801b616e`, before the row is looked at). A POINT in cluster −1 sees nothing, on
any map: `CM_Vis` (`FUN_18016b230`) zeroes the row for exactly −1 before it looks for vis data; only with no vis data and a real cluster
is the row all ones. So a model standing in solid loses every world light but the sky, and that is the engine. `FUN_1801b5400`, a static
prop handle's styled list, goes straight to `FUN_1801b9570` and has no gate: its list was admitted at build.

**Where the cube came from decides the list.** With the rays unavailable a static state's cube is the leaf's, so it takes the leaf path's
list and skips what vrad flagged; otherwise that light counts twice.

**A slot-held surface light is not one shape everywhere, and that is the engine's.** Ranking and fold take one cosine of its own
(`FUN_1800efbf0`). The CPU path does not: `studiorender.dll` `0x180021b20` keeps N·L alone once the light's cosine is at or past
`ThetaDot`, and `FUN_1800f0e10` writes `ThetaDot` 0. *Read.* The GPU's cosine is the vertex shader's form with a zero spread taken as 1,
*interpolated*: what shaderapi uploads for that spread was not read.

**The route here, wrong turns included** (live TF2 build 11191711, `mat_hdr_level 2`, `mat_force_tonemap_scale 1`, bloom off,
far camera; linear luminance x1000; the reference reproduced to the digit, 228.1, before any manipulation):
1. *"Glass"* (B669's filed guess). `r_drawtranslucentworld 0` in TF2: the pipes stay white. The pipes alone read 355/281/232
   top to bottom against ours 226/154/99, so most of it is not the glass.
2. *"Ambient boost"* (`r_ambientboost`; ported nowhere). `FUN_1800f1bd0` reads it and it would be a constant lift, which is
   the shape measured. But the owner's config has `r_ambientmin 0`, so it cannot fire in this reference, and TF2 with it off
   measured the same to the digit. Ruled out here, and still unported: B672.
3. *"B666's trace drops too much."* Keeping every light moved the pipes to 217/185/173: the wrong shape, the bottom brighter
   than TF2 and the top darker. Restored; the trace stands.
4. *"Phong."* `mat_phong 0` darkened TF2's pipes to about ours (287/188/90), and looked like the answer. `r_worldlights 0` then
   left no lamp to make a highlight from and TF2 still read 329/265/232, so the light is not in the highlights. *Interpolated:*
   that `r_worldlights 0` empties the slots into the cube, and that `mat_phong 0` also changes the model's shading path, are
   what the numbers imply; neither was read.
5. **The measured shape**: TF2's pipes minus ours was nearly constant down the cylinder (+128, +127, +133). The cube is what adds
   a constant, and reading what writes the cube found the kinds this port refused.

**After the fix:** the static cube's up face on placement #214 went 0.137 to 0.273 (`prop-shade`). The numbers against TF2, and what
stays unexplained: [B669](../RISKS.md).

## What the pane reflects (B669, 2026-11-03)

Evidence classes marked per line. Linear luminance x1000, live TF2 build 11191711, the far camera; the numbers: `measurements/2026-11-03-b669-the-pane.md`.

**The filed suspect was the pane's diffuse, and it was not.** `mat_specular 0` in TF2 took the patch from 228.1 to 166.5 and left a pane that looks like ours: the 62 the pane adds beyond ours is the reflection. Ours' reflection was about a tenth of that. A shot of the reflection term alone read 0.018 to 0.031 on a surface whose cube holds 0.23 along the mirror ray, and **the vector drawn as colour read (0.40, 0.88, 0.23)**: the view ray with its z flipped. The normal in the pixel shader was (0, 0, 1).

- **No brush face had a normal.** *Read:* the world builder never set one, so `WorldVertex` kept its record default. RISKS said so in B194, and reasoned that the world pass "never uploads a normal", which was false and would have mattered only for a reflection, which it did not consider. `ReflectionRenderTests` proved the shader with hand-set normals, so every assertion held while production passed none. *Disassembly*, `FUN_1800f4d40`: the normal is `vertnormals[ vertnormalindices[ first + corner ] ]`; `BspVertexNormals` had read those lumps since D93 for a consumer that had not arrived. On a floor (0, 0, 1) is right, which is why nobody saw it.
- **A bumped brush face needs the frame too**, or it reflects flat where TF2's follows the bump. *Disassembly*, `FUN_1800f83c0` over `FUN_1800f8590`: S from the normal and `textureVecs[1]`, negated by `s × t` against the PLANE. Tested on the arithmetic and by the frame moving a pixel of a real bumped reflective material, by one count at the quad's centre: a weak manipulation. Plain bumps only: B678.
- **The translucent pass bound no cube.** With the normal right, the reflection came out blue where TF2's is grey. *Measured:* fitting each pane against all 40 of the map's cubes, TF2 is `c-3840_-880_656`, the name in the face's patched VMT, to 0.001-0.008, and ours was `c-3540_-1280_656` to 0.001-0.002, whichever run drew last. `DrawBlendedBatch` never set slot 5, where the world's opaque path binds it every draw as a comment there insists. *Read:* `lightmappedgeneric_dx9_helper.cpp:876` binds it on every draw.
- **`$envmapcontrast .5` is no contrast at all.** *Read, then read again in the shipped binary:* `bUsingContrast` also needs `$envmapsaturation != 1` (helper `:714`; `stdshader_dx9.dll` `FUN_18002c950`), so this material takes the fast path, whose constant is 0 (`ps2_3_x.h:54-57`) unless the value is exactly 1. A lone `$envmapsaturation` is likewise dropped. *Measured:* the near window's reflection is 23.9 in TF2 and 24.0 in ours, channel by channel.

**A wrong turn, kept.** With the normal fixed and the wrong cube still bound, keeping the VMT's contrast read closer on the wall and gap, and looked like TF2 contradicting the source. The cube fit is what killed that: it requires no reading of ours' shader, and it fits contrast 0 to three digits.

**The wire pattern is not settled.** At the far camera TF2's pane shows a sharp hatch and ours a soft one, on the lattice's 5.6-pixel period in TF2; `mat_forceaniso 1` and `mat_picmip 0` did not remove it from TF2. A mip bias in ours was tried only with the glass hidden, so it says nothing here. What decides it was not found; `mat_showmiplevels 1` did not read clearly through the tint. It does not move the mean: the texture's mean alpha is 65 at every mip.

## The reference carries a halo (B669, 2026-11-03)

Evidence class: **measured**, and one line *interpolated*. The numbers: `measurements/2026-11-03-b669-behind-the-pane.md`.

Ours' wall matched the lightmap lump to the digit, and TF2's did not, more so with distance. **The clue that broke it was one surface along one ray:** 42.8 at 40 units falling smoothly to 27.6 at 859, on a wall no layer of which is darker at distance. Fog was tried and moved nothing. `mat_fullbright 1` then showed a grey band where the wall should be saturated, which read as a prop only in the lower LOD; that was a wrong turn, and `r_lod 0` left it to the digit. **Removing the static props exposed the shape:** the two shots agree to the digit down to row 265, then differ more and more toward the pipe's edge, and again below it. About eleven rows at 215 units, and about ten at 859: its width is in screen pixels.

So not a decal, not a shadow, not LOD. Nothing named it, and `-vulkan` removed it, with the pipes, concrete and close-up world unchanged. *Interpolated:* the driver's ambient occlusion, applied to TF2's D3D9 path.

**Every D3D9 reference in this project has it**, which is why the ratio image of ours over TF2 embossed every edge in the frame, and why a far patch can match for the wrong reason. The patch's 229.1 against 228.1 was partly that: the pipes short, the wall dark from a thing that is not the engine. **A capture for parity is taken with `-vulkan` until the driver setting is ruled out.**

## The pipes behind the pane: a spot has no cosine at runtime (B669, 2026-11-03)

Evidence classes marked per line. The numbers: `measurements/2026-11-03-b669-pipe-lights.md`, then `measurements/2026-11-03-b669-pipe-traces-live.md`.

**Found by asking the running engine, after reading it could not.** A debugger on `FUN_1801b5db0` listed each light it folded for this one state, with the amount. *Measured:* the set matched ours, and the amounts did not: the spot at (-3400 -1128 486) folded 22 times what ours gave it. *Decompile, then read as what it had always said:* `FUN_1800efbf0` case 2 returns the fringe, no `dot2`. **B122 had put a cosine in**, from `lightmap.cpp:1929`, which does have one, because that is the bake. A correct citation of the wrong path, tested on-axis and off-axis against itself.

**Why reading missed it.** Every subset of lights failed because the trace was fine: the question "which lights" was wrong, and each answer sharpened it. `Cone` was read in this session and taken as ported.

**Two more, from the same log.** *Decompile and measured:* the rank has no cone (`FUN_1801b60a0`'s XMM6 is luminance times the return, and the return for that spot is 1/449). B665 had that *interpolated*. And admission is a sphere around the point against the cone (`FUN_1801736b0`), radius to the far corner of the 32x32x128 cell, with a radius `Mod_LoadWorldlights` computes (`FUN_1800cbdd0`): a lump's 0 is not "no cutoff" in memory. A point lamp 761 units off with a radius of 475.8 is never traced.

**And the instrument was stale.** The Ghidra project's `engine.dll` predates the game's, all checked functions at +0x1940: B684.

**A regression that was not one (B685).** The fix pushed a pipe over TF2 by 8%, on a 7-pixel box. The state was right, so the search went to the draw. *Measured:* every edge in TF2's frame sits half a pixel from ours (B686), and with ours resampled that far, the pipe reads 66.1 and 93.9 against 67.5 and 93.2. **Two TF2 switches misled on the way:** `r_rimlight 0` leaves the rim on at boost 1 (*arithmetic:* the rows fit that exactly, against `skin_dx9_helper.cpp:848`), and `mat_specular 0` is VertexLitGeneric's rule, which a `$phong` material's skin path does not have. A switch that does not move a thing is not evidence the thing is absent: the pipes' identical rows under `mat_specular 0` looked like "no envmap" for a moment.

**The half pixel was the rasteriser, and the engine never corrects it for 3D (B686, 2026-11-12).** *Disassembly:* shaderapidx9's only `SetViewport` call (`FUN_180015f10`) hands D3D9 the integer viewport untouched, so a 3D vertex lands under D3D9's rule, pixel `i` centred on `i`. DXVK keeps that rule by adding `cf = 0.5f` to every viewport, and Valve's GL layer adds it in clip space (`cglmprogram.h:240-242`); only screen-space rectangles are placed by hand for it. Ours now offsets its 3D viewports the same way, and the boxes that fit (-0.5, -0.5) before fit (0, 0) after (`measurements/2026-11-12-b686-pixel-centre.md`). **A side effect worth knowing:** two test fixtures had relied on Direct3D 11's rule without saying so — an odd target "so the middle pixel is on the axis", and a pixel picked by truncating a projection — and both were half a pixel wrong under TF2's.

**The review of that account found the fix half-applied.** *Decompile:* `Mod_LoadWorldlights` (`FUN_1801042d0`, re-read on the current binary at `0x1801054d0`) writes the radius into the light once, so it is what every later reader sees. The fix had computed it in the admission test only. **The radius still does not cut a folded point or spot**: `FUN_1801b99a0` passes flags 2 to `FUN_1800efe40`, so past the radius at the point a light folds in full, and only the box stops it. A surface light tests its radius whichever way it is reached. *Decompile:* the rewrite gives a point and spot of no attenuation quadratic 1. Earlier tests said constant 1, from vrad's value at compile time, the same error as the cosine: a right citation of the bake. *Disassembly:* `FUN_1801736b0` compares with `COMISS` and `JC`, so a `sine` of 0 makes NaN and refuses the light, and the C# had to be written `!(a >= b)` to agree. *Decompile:* the box is the point's cell in `FUN_1801b60a0` and `FUN_1801b5400`, and the prop's own in `FUN_1801b6bf0` and a static prop's dlight in `FUN_1801b6550`. So a lamp only the prop's box admits sits on the list and takes no slot.

*Decompile, the second review:* a radius `FUN_1800cbdd0` leaves at 0 is refused from outside the box, so 0 is a radius there and "no cutoff" only in the falloff's surface case. And that falloff has four shapes, not one: a clamp at 1 on a surface light only, none on a point or spot, `linear − d` on a quake light, 1 on the rest. The clamp this project applied to all was the vertex shader's, a path two functions away.

*Decompile, overlays:* `FUN_180112d30` gives each fragment corner the surface's unflipped plane normal, and `FUN_180111830` writes no tangent at all, so "the overlay's own basis" is not a frame the engine has.

*Measured:* none of it moves #215: 83 admitted of 83 traced, and every far box to the digit (`measurements/2026-11-03-b669-review-admission.md`). That is what a half-applied fix looks like from a frame: right where it was measured.

**And `BspSurface.Normal` points into the solid on a flipped face (B687).** *Measured,* compiled normals side with the plane on both kinds of face. It came up asking whether a brush entity's normal mattered: the plane's normal and the compiled one differed by exactly 2 in `1 − dot`, on half of a door's corners, which is a sign and not smoothing.

The first part of this account, kept for the record of what was ruled out:

**A wrong turn worth keeping, because it was an instrument and it looked like a discovery.** The back of the pipe read 0.021, and the colour was exactly the map's `emit_surface` tint, r/g 0.84. That named four lights, and all of their rays stop on detail brush 3637, so it read as TF2 tracing through detail. A fit with two more lights came within 1% on four faces, and it was wrong. **A camera at yaw 270 has −x on its right**, the reading was the left of the frame, and that is the next placement along. On #215's side the back is zero. The colour was real and belonged to another pipe. Six shots and a brush-by-brush table went into a question about the wrong object; the fit agreeing was the part that should have been distrusted.

- *Disassembly:* the light cache's trace has no detail rule. `FUN_180167e40` tests a leaf brush's contents against the mask, and 0x8000001 has the solid bit. `FUN_18016bfc0`, the box-brush case, is a plain slab test.
- *Disassembly:* a world-only trace ends after `FUN_180168cb0` on head node 0 (`FUN_18018e870`), so a static prop cannot block a light.
- *Disassembly:* the static handle's point is the lighting origin stored as given (`FUN_1801b8350`). `r_DrawModelLightOrigin` puts its cross there (*measured*).
- *Measured:* `r_dynamic 0` changes no digit on the pipe, so no dlight supplies the gap.
- *Arithmetic:* no subset of the lights ours rejects gives TF2's surplus of 0.065 on −y and 0.063 on +z with nothing on −z.

**Seen in passing, in the same clip routines, and filed apart:** ours' `ClipToBrush` differs from `FUN_180167050` in two ways (B681), and `IlluminationPoint` turns the point by yaw alone (B682). Neither is this gap: #215's point has zero y and z in the model, and its bevels only make a brush larger in the engine.

## ComputeLighting reads the cache and folds its slots (B668, 2026-11-03)

Evidence class: **disassembly**, `engine.dll` x64. This ended an inference: B668 had reasoned that `ComputeLighting` must reach the lights through
`LightcacheGet` because nothing else reaches `FUN_1801b60a0`.

- **`FUN_1801b7fd0` is `IVEngineClient::ComputeLighting`.** It takes `LightcacheGet` with flags 0xf, so the state it starts from is the cached entry's, already traced, gated and folded, with the live dlights merged in (flag 2).
- **With no normal, it folds each non-dlight slot at the asked point** through `FUN_1801b9570` with flags 1 and `FUN_1800efbf0`'s angle. That is a fold, not a new ranking and not a trace. A slot whose pointer lies in `DAT_180773730..0x18077582f`, the converted dlights, is skipped, so a dlight in a slot adds nothing to this answer.
- **Past the miss budget it takes the nearest entry and builds nothing** (`FUN_1801b9cd0`, flag 8). That scan treats a zeroed pool entry as key 0 like any other, so a point in the map's far corner can be nearest one and get zero. A review feared this drew full bright. It does not: zero is the engine's answer too, and our only readers are `RopeLight`, which averages the faces, and a log line. The remark that said a default cube means "no cube supplied" did not hold for either reader.
- **Our port leaves the dlight merge out, a named ceiling and not parity.** A world light a dlight evicts folds at the entry's point in the engine and the asked one in ours, and a dlight that loses a slot reaches the engine's cube and not ours.

So our `AddTo` route was wrong in more than the trace it was filed for. It re-ran the admission over every light at the asked point. A rope or an eye light reads the cube of the cache entry it stands in.

## A static prop has no cache entry, and a moving model's light has a time constant (B674, B675, B676, 2026-11-03)

Evidence class: **decompile**, `engine.dll` x64, and **measured** where a number is given. The review that filed B675 asked what a model draws from a zeroed entry, and the answer was not in `LightcacheGet` at all.

- **With a handle, `FUN_1800f1bd0` never calls `LightcacheGet`.** `FUN_180205b20` makes a handle for every renderable prop, so a static prop has no entry, spends no `lightcache_maxmiss` and evicts nothing. Ours asked anyway and discarded the answer: 86% of lookups on f12 (`measurements/2026-11-03-static-props-leave-the-light-cache.md`). Taking them out moved no world pixel and did move the viewmodel's; *interpolated:* it had been lit from an entry a static prop built, the one path that changed.
- **A zeroed entry's dlights are asked from (0 0 0).** `FUN_1801b7a10` receives no PVS row past the budget and looks one up from the entry's origin `+0x184`. An earlier reading here took it from the entry's leaf, `+0x178`, which is 0, the solid leaf, and would have meant no dlights; `param_4` is that leaf and the function does not read it. Kept because a guard written on that reading would have looked right.
- **The state is not what a moving model draws.** `FUN_1800f3110`, under `r_lightaverage` 1 and `r_lightinterp` 5, blends it with the instance's last state by `exp(−5·dt)` (the `exp` *interpolated* then, read since: `FUN_1803366d0`'s table and `64 / ln 2`), lights by pointer, and lets a light that left the slots fade out. A first draw takes the state as it stands. **Ported 2026-11-04, B676.** So a zeroed entry dims by 1 − exp(−5/60), 8%, at a 1/60 s frame (arithmetic, not run), there and here.
- **The time average has one more branch than it appears to, and it decides what a seek means.** At `dt <= 0` f is 1 and the stored time stays: the model draws its history until the clock passes where it was. The engine can afford that, since nothing it does moves the clock back: a skip back reloads the demo and every instance is new, and `+0x128`'s only other write is at creation (`FUN_1800eb5d0`; a teleport keeps the history). A viewer with an instant rewind cannot, so here a jump back past a second starts the instance again, which is what the engine's reload comes to.
- **A slot's blend is of the raw intensity**, and `FUN_1800f2010` applies the style when it builds the `LightDesc_t`: a flickering lamp is not eased, though the light it stands for is. **How much it moves:** along a soldier's walk through cp_process_f12, a cube channel steps by up to 0.83 between ticks and the drawn one lags it by up to 0.77, measured once through `EntityModelSet.Instances`; `CorpusModelLightAverageTests` holds the same on cp_granary, 0.25 and 0.23.

## A static prop's flags 6 and 7 add to a copy, and a dlight has the right of way (B669 review, 2026-11-03)

Evidence class: **disassembly**. The second review asked what happens to the light `Strongest` dropped, and the answer was the same defect as the static state's, one call later.

- **`FUN_1801b5400`** walks a handle's styled list straight into the state, flags 0, so it traces, and with no PVS gate: the list was admitted at build.
- **`FUN_1801b6550`** adds a dlight with flags 1, no trace, and with a threshold of 100000: once the slots are full a dlight evicts the weakest whatever its own strength, unless all four rank over that (`FUN_1801b89a0`).
- **`FUN_1801b60a0`'s eviction folds the loser (`FUN_1801b5db0`).** Nothing vanishes. Ours kept four and dropped the rest, and the comments called that a difference "only past four lights". With 108 surface lights on f12, past four lights is the normal case.
- **`FUN_1801b5260` asks from the dlight's row**, not the prop's, so the clusters are that way round in `WithDynamic`.

**The PVS copies disagreed, and it mattered on one edge.** Three loops had their own reading of the gate. `CM_Vis` (`FUN_18016b230`) zeroes the row for a cluster of exactly −1 before looking for vis data, and with no vis data and a real cluster the row is all ones. That is now one
method, `LevelLighting.InPvs`. **One edge left as a choice:** `FUN_1801b6bf0` tests the row bit with no negative test, so a lamp in cluster −1 reads `row[-1]`, memory before the row. We call it out of the PVS. *Read; what that byte holds in a shipped map was not measured.*

**A wrong turn in the record's own control:** the cost probe's record predicted every dlight 64 units above a prop would re-rank it. 836 of 1,347 did, and a count of the gate from its two inputs gave 836. The other 511 are the gate refusing, so the path ran for everything it should have. Kept here because the first reading of that number was "the instrument failed".

## A world light behind a wall lights nothing; a dlight behind one still does (B666, 2026-11-02)

Evidence class: **disassembly**, read by the lead in Ghidra on `engine.dll` (x64), except where marked.

`AddWorldLightToLightingState` (`0x1801b60a0`) gets each light's ratio from `FUN_1801b99a0` → `FUN_1801b9570`, or from
`FUN_1801b8e20` when the caller has a shadow cache (`LightcacheGet`'s world lights pass none). **Both return a float in
XMM0 that Ghidra's decompile drops.** It shows up in the listing at `0x1801b9930`/`0x1801b996b` (`9570`) and
`0x1801b929d`/`0x1801b94e2`/`0x1801b9520` (`8e20`). The value is the light's ratio, or 0.0 when the light is rejected.
Read from the decompile alone, the function looks as though it returns nothing, and that is how B453 and B665 came to
argue about it from two partial records.

For types 0, 1, 2 and 4 (surface, point, **spot**, quake) the path is the same:
1. The distance and cone ratio (`FUN_1800efe40`).
2. The brightest channel × ratio against `r_worldlightmin`.
3. Then, **unless flags bit 0 is set**, `IEngineTrace::TraceRay` from the lit point to the light's origin, at full
   length. The trace uses `MASK_OPAQUE` (`0x4081`) and a filter (`0x18035e800`) whose `GetTraceType` is
   `TRACE_WORLD_ONLY`, so it sees world brushes and displacements, but no entity and no static prop.
4. The light is rejected when `(1.0 − fraction) · distance > 8.0`. Both constants were read: `0x18035c93c` = 1.0 and
   `0x1803adc70` = 8.0.

Type 3, the skylight, has its own sky trace (B514). Type 5 returns 0.

**So it is not "for point lights".** B453 had recorded it that way. It covers spots, and a spot was what the open
question turned on. **A rejected light is not folded either**: the add returns before ranking, so the light is in
neither the four slots nor the cube.

**Who traces** is decided by `skipTrace`, the 7th argument of `0x1801b60a0`, which becomes flags bit 0:

| Caller | `skipTrace` | Traced? |
|---|---|---|
| style-0 world lights (`0x1801b5a50`) | 0 | yes |
| `LightcacheGet`'s styled lights (`0x1801ba322`) | 0 | yes |
| a stored list ranked into a state (`0x1801b5890`) | 0 | yes |
| a static prop handle's styled list (`0x1801b5400`, flags 0) | 0 | yes |
| live dlights (`0x1801b7a10`) and `0x1801bb040` | 1 | no |
| the handle list's admission (`FUN_1801b6bf0`, flags `9\|2`) | bit 0 set | no |

A dlight behind a wall therefore still lights a model, while the world lamp at the same spot does not. The trace's
start is whatever point the caller passes: the cache cell's point for a moving model, and the handle's own lighting
origin (`+0x184`) for a static prop.

**Measured on `koth_toxic`** (`prop-shade koth_toxic vat_large_02 600`, which reports the production trace): vat #130's
three white spots, at (-206 1807 205), (224 1248 667) and (204 815 419), are blocked 226, 490 and 627 units short
of its lighting origin. After the fix the vat holds only the
green lamp and two dim spots in its floor. Through the viewer, at the owner's camera `-208 1360 80 60 90`, the right
rim top reads 16/76/50 against TF2's 14/74/47. The same tree with the trace off reads 62/96/73. On `cp_process_f12` both observatory domes
now hold no lamp: every lamp that reaches them is behind the roofs.

**What it cost** (measured, Debug, `docs/measurements/2026-11-02-b666-light-trace-load.md`):
- 5.9 s more at map load on koth_toxic. That is 2,225 static states, each tracing nearly every lamp on the map, because
  a 70,000-intensity lamp clears `r_worldlightmin` out to about 18,700 units.
- About 0.3 ms more a frame in the pose's lighting on f12 (measurements/2026-11-02-b666-light-trace-load.md).
