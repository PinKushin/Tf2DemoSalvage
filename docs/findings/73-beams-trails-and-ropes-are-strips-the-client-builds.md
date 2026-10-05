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

## Evidence that beams draw

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

## A trail is points the client samples, not anything on the wire (read from published source)

`DT_SpriteTrail` inherits `DT_Sprite`, so unlike a beam a trail was always admitted. The sprite pass then drew it as a
plain quad of its own material at the projectile. `CSpriteTrail::DrawModel` (`SpriteTrail.cpp:422-531`) overrides the
sprite's and draws something else entirely:

- **`UpdateTrail` runs from `ClientThink` every frame**, whether or not the trail draws (`:279-284`, `:379-416`). It
  appends the render origin when the head has moved more than two units (`DistToSqr > 4`), at most once per
  `lifetime / 256` seconds. The ring holds 256 points and steals the oldest past that.
- **`DrawModel` strings the points and the current head through `CBeamSegDraw`.** A point's alpha is
  `brightness / 255` times the smaller of its remaining life and the tail fade over `m_flMinFadeLength`. Its width is
  the start width, or the lerp to `m_flEndWidth` by remaining life when that is not negative, plus the point's own
  variance. Its V coordinate is distance travelled times `m_flTextureRes`.
- **A dead point is drawn once more, at zero alpha, and then removed** (`:516-523`). The engine's loop decrements its
  own counter to stay on the next point.
- **The head is `GetRenderOrigin`** (`:537-553`): the attached entity's attachment. For attachment 0,
  `C_BaseAnimating::GetAttachment` refuses and answers the entity's own origin (`c_baseanimating.cpp:2129-2134`).

The trail's shape therefore depends on the frames it was sampled at, as in TF2. `EntityTrails` keeps a ring per trail
across frames and resets it on a seek, which the engine's forward-only demo player never needs.

### The head outlives its projectile (measured on the corpus)

On z1800, 34 of the 41 trails outlive their projectile's track by up to 117 ticks while still naming it as parent
and attachment (`trails` probe). Placed through the missing parent, the trail's local origin is the world origin, so
the first version drew a ribbon to (0, 0, 0). The client still holds the entity: dormant, or unlinked from its
children at its last place. So the viewer now holds the head where it was, and the ring fades out behind it.
**Interpolated:** which of the two the client does at each of those ticks was not measured. Both give the same head.

### What the corpus trails are (measured on the corpus)

Tallied on z1800, badwater and sanctum (`entity-census`), the trails are Sandman balls
(`effects/baseballtrail_*`), Rescue Ranger bolts (`effects/repair_claw_trail_*`), the passtime ball, and
`effects/beam001_*`. All are `kRenderTransAlpha` with no width variance and a skybox scale of 1. The first three are
`UnlitGeneric` with `$vertexcolor` and `$vertexalpha`, which the particle pass draws.
`beam001_*` is `Refract`, which needs the frame behind it, and it is the most common trail on several real matches.
Until B476 it was sampled and not drawn.

### A refracting trail warps a copy of the frame, one copy per trail (B476; read from published source)

`effects/beam001_white` as shipped is `$normalmap effects/beam001_normal`, `$refractamount .2`, `$bluramount 1`,
`$refracttinttexture effects/white`, `$vertexcolormodulate 1` and `$translucent 1` (`vmt` probe). `_red` and `_blu`
swap the tint texture for a `$refracttint`. Nothing about the strip changes: the same `CBeamSegDraw` corners are
drawn through a different shader.

**The image is the frame, and it is copied before every renderable that needs it.** `Refract` sets
`MATERIAL_VAR2_NEEDS_POWER_OF_TWO_FRAME_BUFFER_TEXTURE` (`refract_dx9_helper.cpp:59`), and with no `$basetexture`
binds `TEXTURE_FRAME_BUFFER_FULL_TEXTURE_0` (`:226-233`). `DrawTranslucentRenderables` calls `UpdateRefractTexture()`
inside its per-entity loop (`viewrender.cpp:4609-4635`, `:4651-4686`), and on the PC that copies every time
(`view_scene.h:50`, `IsPC() ||`). So one trail refracts another drawn before it. The viewer gives each refract trail
its own batch and copies the frame before each. The target is `_rt_PowerOfTwoFB`, 1024 square and clamped, created by
the engine (engine.dll `0x1800f69b3`-`0x1800f69ef`, disassembly) beside the water's targets. The copy reuses the
water's stretch path. Water views skip refracting renderables (`bRenderingWaterRenderTargets`, `:4614`), and the
viewer draws no particles in its water views.

**The shader** (`refract_ps2x.fxc`) moves each sample by `normal.xy · normal.a · $refractamount`. Under
`COLORMODULATE` it also multiplies that offset by the vertex alpha and the tint by the vertex colour. `BLUR 1` is a
four-tap polyphase box at 1/512. The alpha is vertex alpha times normal alpha, blended by alpha. A white tint texture
doubles the frame (`2.0 · g_RefractTint · tex`), which is why `beam001_white` reads as a bright streak over the scene.
Two defaults are easy to get wrong. An undeclared `$bluramount` is 0, not the parameter's 1 (`:47-50`). And the shader
**writes depth** although translucent (`EnableDepthWrites( bWriteZ )`, `:119`), unless `$nowritez`.

**What is not ported:** the `CUBEMAP`, `SECONDARY_NORMAL`, `MASKED` and `FADEOUTONSILHOUETTE` combos and a
`$basetexture`. A material that selects one loads without a refract draw and stays skipped. None of the three
`beam001_*` materials selects one; other shipped Refract materials were not surveyed. Pixel fog is not applied, as
for every particle and sprite draw here.

## Evidence that trails draw

- **Unit level:** `SpriteTrailStateConformanceTests` (the decode) and `EntityTrailsConformanceTests` (sampling, the
  ring, life and tail fade, width lerp, texture coordinates, death and removal, the held head, and the sprite pass
  leaving trails alone).
- **Output level:** `EntityTrailRenderTests` samples z1800's entity 806, a Rescue Ranger bolt, tick by tick through
  the production chain. It then draws the ribbon from a camera placed beside its last eight ticks of flight. The
  brightest pixel of the centre column measured 142 and every corner stayed black. With the segment alpha sabotaged
  to zero it measured 0.
- **Refract trails (B476):** `RefractMaterialConformanceTests` and `EntityTrailsConformanceTests` (a batch per trail,
  the raw vertex colour, the depth write). `RefractTrailRenderTests` draws a synthetic strip over a half-lit frame:
  the warp shows the lit half a quarter of the frame to the left, and the control at `$refractamount` 0 does not.
  Its output-level test samples `pass_sanctum_a2a`'s entity 355, a `beam001_white` trail, over 40 ticks on a grey of
  0.25. The centre column's brightest pixel summed to 250 against a background of 192, and the corners were unchanged.
  With the frame copy unbound it read 192.

## A rope is hung by the client, and only its ends are on the wire (read from published source)

`DT_RopeKeyframe` is NOBASE like `DT_Beam`, and it has no model index at all: the material arrives as
`m_iRopeMaterialModelIndex`. So a rope failed the same admission gate, and it now enters the timeline with its
material as its model path. Everything else is client state, built from `c_rope.cpp`, `rope_physics.cpp` and
`simple_physics.cpp`:

- **The nodes are a Verlet chain at a fixed fiftieth of a second.** The step's `dt² / 2` is a float and the
  accumulated time a double. The damping is 0.98, and three spring passes run per step. A spring only pulls: a slack
  rope hangs, and a stretched one is pulled in half the excess at each end.
- **The spring length is `( length + slack − 100 ) / ( nodes − 1 )` in integers.** `ROPESLACK_FUDGEFACTOR` is −100
  "so when a level designer enters a slack of zero … it doesn't dangle so low". A short rope with no slack comes out
  negative, which `ResetSpringLength` clamps to zero, and the rope is pulled taut. Viaduct's 26-unit pole drop
  (entity 94) is one of these.
- **A new rope is hung by simulating five seconds of gravity** (`ROPE_INITIAL_HANG`), and its light is sampled at
  each node then, once. Its ends are locked to their entities every spring pass. The rope rests when nothing moved
  more than √0.03 and no end moved a tenth. While the camera is within 1000 units, random gusts keep it swaying unless
  `ROPE_NO_WIND` is set.
- **`GetEndPointPos` always answers true.** A missing end entity leaves the cached position, zero until one has been
  seen. "Must have both entities to work" therefore cannot refuse, and such a rope hangs to the world origin. That is
  the engine's behaviour, kept.
- **`BuildRope` splines the nodes**: a Catmull-Rom with `rope_subdiv` points between each pair, from a precomputed
  `( t, t², t³ )` table. Texture V advances by an increment counted over `( nodes − 1 ) · subdiv + 1` points while the
  strip has `nodes + ( nodes − 1 ) · subdiv`, so the texture runs past its end. That is Valve's arithmetic, kept.
- **Without MSAA, the rope is drawn twice for fake anti-aliasing.** A translucent `_back` rope at least 0.3 pixels
  wide goes underneath, at alpha 0.2 to 0.5. A solid rope 1.4 pixels narrower goes on top. Far away, only the back
  pass draws. This renderer has no MSAA, so it takes this branch.
- **TF2's cable is black.** `cable/cable` is the `Cable` shader over a one-texel black texture, so the solid pass
  is black whatever the light. The light reaches only the translucent back pass. Read through `SpriteBlending`, the
  SpriteCard rule, the opaque cable was drawn as translucent. It now blends by its own flags.

**Measured on the corpus:** no demo carries an `env_wind` (`entity-census`), so only the gust branch of the wind is
reachable. No corpus rope sets `ROPE_COLLIDE`, a direction lock or `ROPE_PLAYER_WPN_ATTACH`. Those branches are
ported and reached only by the unit suite.

## Evidence that ropes draw

- **Unit level:** `RopeStateConformanceTests` (the decode), `RopePhysicsConformanceTests` (the step, damping and
  springs), and `EntityRopesConformanceTests` (the locked ends and sag, subdivision counts, texture increment,
  `ROPE_SIMULATE`, the fake anti-aliasing near and far, light as gamma, the missing end, and the sprite pass leaving
  ropes alone).
- **Output level:** `EntityRopeRenderTests` hangs viaduct's entity 95, a 320-unit span, through the production chain.
  It draws the rope over grey from 20 units to the side. The darkest pixel of the centre column measured 0 against a
  grey of 381, and every corner stayed grey. With both passes' widths sabotaged to zero it measured 381.
