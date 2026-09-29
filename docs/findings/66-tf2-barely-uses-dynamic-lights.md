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
  normal and bump basis rebuilt per luxel from the disp's triangles). **Not built.**
- **Brush entities** (`0x1800e03a0`) move the light into the model's space and walk its own nodes. **Not built.**

**Built:** `WorldDynamicLights` (marking, the per-face pass, both additions), `LightmapAtlas.Rebuild`, the frame's
lights copied before the decay (`MomentScene.WorldLights`, as `EndUpdateLightmaps` `0x1800d5fa0` copies `cl_dlights`),
and the viewer's `StepLightStyles` calling it after the styles. On `tf2-2026-pub-pov-clean` no light reaches the world
(`CorpusWorldDynamicLightTests`, with a recorder-17 control that finds them).
