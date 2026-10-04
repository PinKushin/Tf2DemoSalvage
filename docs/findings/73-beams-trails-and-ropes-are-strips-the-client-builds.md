# 73 — Beams, trails and ropes are strips the client builds

Three entity classes override `DrawModel` with a camera-facing strip instead of a model: `CBeam`, `CSpriteTrail` and
`C_RopeKeyframe`. None of them drew in this viewer. This finding records why, what the engine does for each, and the
parts of the engine that had to be read out of a closed library.

## What the corpus carries (measured on the corpus)

The wire was counted before any admission gate (`entity-census`, 2026-10-04). Each count is one distinct
(index, serial) pair:

| demo | `CBeam` | `CRopeKeyframe` | `CSpriteTrail` |
|---|---:|---:|---:|
| 2007 granary POV / STV | 41 / 100 | 30 / 30 | 0 |
| 2008 granary POV / STV | 25 / 75 | 30 / 30 | 0 |
| 2009 badlands POV | 2 | 64 | 0 |
| 2011 viaduct POV / STV | 31 / 102 | 35 / 35 | 0 |
| 2013 badlands POV | 0 | 64 | 0 |
| 2013 foundry STV | 378 | 188 | 0 |
| z1800 | 228 | 0 | 41 |

- **Every beam is a `point_spotlight` shaft.** The type is 0 (`BEAM_POINTS`), the flags are `0x280`
  (`FBEAM_SHADEOUT | FBEAM_NOTILE`), the material is `sprites/glow_test02` and the halo is `sprites/light_glow03`.
- **No demo carries a temp-entity beam**, in either corpus: 60 demos, the 50 local ones included. z1800 alone
  carries 54,246 temp entities, and none of them is a `C_TEBaseBeam` subclass. The control is the same filter
  asked for `FireBullets` on z1800, which finds 3,601 `CTEFireBullets`. So `DrawTesla`, `DrawDisk`, `DrawCylinder`, `DrawRing` and `DrawFollow` are not
  ported: an entity beam cannot reach them, and nothing in either corpus sends one. The old gap marker blamed beams
  for the missing medigun link. That link is a particle system.
- **The local corpus has all three almost everywhere.** Of its 50 demos, 48 carry beams, 35 carry trails and 34
  carry ropes. Trails come from real matches: z1800 is the only era specimen with one, and the others are solo
  recordings.
- **Not every rope draws.** Rope flags `72` and `104` include `ROPE_SIMULATE`. Flags `64` and
  `96` do not. Those are the last keyframe of a chain, which has no next key and draws nothing.

## Why a beam never drew: a NOBASE table (read from published source)

`DT_Beam` (`beam_shared.cpp:147`) and `DT_RopeKeyframe` (`c_rope.cpp:42`) are `NOBASE` tables. They inherit nothing
from `DT_BaseEntity`. Each declares its own `m_nModelIndex`, `m_vecOrigin`, `moveparent`, `m_clrRender`,
`m_nRenderMode` and `m_nRenderFX`. `EntityState.ModelIndex()` read only `DT_BaseEntity.m_nModelIndex`, so it answered
null for every beam. `DemoTimeline`'s admission gate then dropped the entity as model-less. The beams were decoded
and thrown away before a track existed. The same accessors now fall back to the class's own table
(`EntityState.Effects.cs`).

**A beam is not interpolated.** `C_Beam::AddEntity` calls `MoveToLastReceivedPosition` (`beam_shared.cpp:1075`), so
a beam stands where its last update put it. The timeline snaps any pose that carries a beam.

## How the client draws an entity beam (read from published source)

`CViewRenderBeams::DrawBeam( C_Beam* )` (`view_beams.cpp:2126-2358`) builds a fresh `Beam_t` every frame from the
entity's fields:

- **The type is remapped.** `BEAM_ENTS` and `BEAM_ENTPOINT` become `TE_BEAMPOINTS`, `BEAM_LASER` becomes
  `TE_BEAMLASER` and `BEAM_SPLINE` becomes `TE_BEAMSPLINE`. `BEAM_POINTS` and `BEAM_HOSE` stay `TE_BEAMPOINTS`.
- **Only some flags pass**: sine noise, solid, shade in, shade out and no-tile. `FBEAM_FADEOUT` and `FBEAM_HALOBEAM`
  are stripped.
- **`UpdateBeam` re-seeds the noise from `(int)curtime` when the frame time is zero.** This is how a paused demo
  repeats its noise exactly.
- **A beam with a halo and without `FBEAM_HALOBEAM` draws its shaft at the START width at both ends, in two
  segments** (`DrawBeamWithHalo`, `view_beams.cpp:1777-1865`). That covers every corpus beam. The end width a
  spotlight networks never reaches the screen. The halo's colour is the colour BEFORE brightness, scaled by how
  directly the camera faces down the beam.
- **The render mode is the beam's, not the entity's**: `FBEAM_SOLID ? kRenderNormal : kRenderTransAdd`. A spotlight
  networks `kRenderTransTexture` and draws additively anyway.

### The shade is invisible on a spotlight (read from published source)

`DrawSegs` writes the shade-out fade into the vertex colours. The `Sprite` shader at `kRenderTransAdd` uses those
colours only when `$ignorevertexcolors` is 0 (`sprite_dx9.cpp:332-355`). That parameter defaults to **1**
(`sprite_dx9.cpp:39`), and `glow_test02` does not set it. So the shaft is drawn at the material's constant colour,
and its fade comes from the texture alone. The port routes beam vertex colours through the same shader rule as
entity sprites (`EntitySprites.ShaderInput`). An `UnlitGeneric` beam material takes `$vertexcolor` as that shader
reads it.

## `CBeamSegDraw` is closed (disassembly)

`tier2/beamsegdraw.h` ships with the SDK, but the implementation does not. It is compiled into `tier2.lib`, which is
linked statically into `client.dll`. `beamsegdraw.obj` was extracted from the SDK's own
`src/lib/public/x64/tier2.lib` and read in Ghidra (`D:\ghidra-proj\tier2`; `NextSeg` at `0x31290`, `SpecifySeg` at
`0x31a40`):

- The strip's side vector at a point is `fastnormalize( cross( this − next, this − camera ) )`. It is averaged with
  the previous pair's **raw** normal, not with the running average. The last point takes the last raw normal.
- `fastnormalize` adds `1e-10` under the root (`0x2edbe6ff`). A segment pointing at the camera therefore collapses
  to a line. It does not become NaN.
- Each edge is half the segment's width from the centre, with U 0 on the `+` side and 1 on the `−` side.
- Colour is packed to a byte by the `+2²³` float trick and keeps the **low byte**, so a channel above one wraps rather
  than clamps.

Every beam, trail and rope goes through this one function (`BeamSegDraw`).

## Evidence that it draws

- **Unit level:** `BeamStateConformanceTests` and `BeamTimelineTests` (the decode and the snap),
  `BeamSegDrawConformanceTests`, `BeamDrawConformanceTests` and `EntityBeamsConformanceTests` (the strip, shade,
  noise, laser, spline and halo). Each test cites its `file:line`.
- **Output level:** `EntityBeamRenderTests` draws the first spotlight of the committed 2011 viaduct STV demo through
  the production chain, from `DemoTimeline` to the offscreen renderer. The centre pixel measured (25, 24, 24) and
  every corner stayed black. With the strip's half-width sabotaged to zero it measured (1, 0, 0).
- **In the viewer:** cp_process_f12 at tick 48314 holds 210 beams, and the shaft and halo of the BLU-side
  spotlight show up in a headless capture.

**Interpolated:** the halo's occlusion uses the line-of-sight trace the entity sprites use, not the engine's GPU
pixel-visibility query. The halo pops where TF2 fades it, the same open half as B378. A sprite animation frame on a
beam is not drawn either, also B378.
