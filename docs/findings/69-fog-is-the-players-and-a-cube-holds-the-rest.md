# 69 — The view's fog is the local player's, and a model's cube holds every light it has no slot for

**B139, B452, B453**, 2026-10-02. Two audits of things the viewer already decoded or drew.

## Fog

**What was believed:** fog is "the" `CFogController` — the timeline took the first enabled one in the entity list.
**What the client does** (*published source*): `CRendering3dView::EnableWorldFog` (viewrender.cpp:4708) reads
`C_BasePlayer::GetFogParams()`, which is `m_CurrentFog`, copied by `UpdateFogController` (c_baseplayer.cpp:2802) from
the controller the local player's `m_PlayerFog.m_hCtrl` names — a `DT_Local` handle. No handle: `enable = false`,
however many controllers the map holds. The 3D skybox's fog is not a controller's at all: it is the same player's
`m_skybox3d.fog`, with start and end divided by the sky scale (viewrender.cpp:4806-4831).

**Three things learned about the engine rather than the format:**

- **The server forces radial fog on official maps.** `CFogSystem::LevelInitPostEntity` carries
  `// HACK(misyl): If this is an official map then force on radial fog.` and sets `m_fog.radial = true`
  (fogcontroller.cpp:374-377). Radial fog is `CalcRadialFog_NonFixedFunction` — straight-line distance from the eye
  instead of projected depth — so the edges of a wide view fog harder than the centre. The demo carries the flag, so
  the viewer takes it from there: the f12 demo (`cp_process_f12`, not official) sends 0. *Published source, measured.*
- **The fog colour leaves the client in GAMMA space**: `GetFogColor` divides 0–255 by 255 with a `FIXME: convert to
  linear colorspace` beside it, and the pixel shader reads `g_LinearFogColor`. The conversion is in the closed shader
  API; its string table names `m_bFogColorSpecifiedInLinearSpace` and `m_bFogColorAlwaysLinearSpace`, so a conversion
  exists. The curve used here is mathlib's `pow(x, 2.2)`. *Interpolated.*
- **The first fog constant is start over range**, `CalcRangeFog( z, fogParams.x, fogParams.z, fogParams.w )`
  subtracts it, and the radial path names the same register `flFogEndOverRange` while using it identically. Max
  density clamps before the saturate, and the blend squares the factor. *Published source.*

**A wrong turn, kept:** the first render test predicted 138 for a quarter-strength red fog and the pixel came back
74. The offscreen target is UNORM, not sRGB, so the shader's linear output is what it stores — the prediction had
re-encoded it. The arithmetic was right; the instrument's format was not what was assumed.

**Measured, then explained (B452):** every point-of-view recording read here seemed to lack both the handle and
`m_skybox3d` on its player, while the SourceTV recording of the same session carried them, and for a day the server's
master rule — the first controller found (fogcontroller.cpp:363-383) — stood in. **The values were in the file all
along, in the one block nothing read.** A recording started mid-match misses every string table update before it, so
the demo writes each table whole into `dem_stringtables`; the signon's `instancebaseline` predates `CTFPlayer`'s
baseline, which lives only in that block. Without it the player's first full update deltas against nothing, and every
field equal to the baseline — the fog handle, `m_skybox3d`, even `m_flStepSize 18` — never arrives. Read, the
movement-test POV's player carries `m_hCtrl 2042050`, the same handle its SourceTV twin sends, and `m_skybox3d.area 8`.
*Measured on the corpus; the block's layout cross-checked with demostf/parser (the engine's reader is closed).* The
stand-in went: with the handle present, no handle means no fog, as `UpdateFogController` has it. The wrong turn worth
keeping is the order of suspicion — the probe that said "no baseline" read only the signon's tables, an absence with
no control.

## A model's lights

**What was believed:** the four strongest local lights are what matters; the rest are dropped. The `istudiorender.h`
comment — the cube is "ambient, and lights that aren't in locallight[]" — was quoted in this project for weeks as the
reason folding was acceptable, and never as a description of what the engine does with light five.

**What `engine.dll` does** (`FUN_1801b60a0`, *disassembly*): ranks each light by Rec.601 luminance times falloff
(`0.299, 0.587, 0.114` at `0x18047be38`), not by its brightest channel; keeps the slots for the strongest; and folds
every loser — the evicted light, or the newcomer when nothing is weaker, or any light below `r_worldlightmin` by
luminance — into the ambient cube through `FUN_1801b5db0`: `max(0, n·d) · ratio · intensity` per face. A separate,
earlier test in `FUN_1801b8e20` drops a light outright when its BRIGHTEST channel times falloff is under the minimum,
so the two minimums use different weights on purpose. The SDK has none of this; it is the closed lightcache.
