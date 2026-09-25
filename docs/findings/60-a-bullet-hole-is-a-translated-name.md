# 60 — A bullet hole is a translated name

**Question.** A bullet that hits a wall leaves a hole. Which hole, on which surfaces, and how is it drawn? B415 filed
"no decals when they hit"; D186 asked for everything a gun or a player puts into the world.

## The chain, read end to end

Every step below is read from published source unless marked otherwise.

1. **`FireBullet` calls `UTIL_ImpactTrace` for every bullet that hit anything**, under the same
   `if ( trace.fraction < 1.0 )` as the tracer counter but outside it (`tf_player_shared.cpp`). So a bullet with no
   tracer still leaves a hole. It is skipped only when the struck entity is on the shooter's team, and the world is
   team 0.
2. **`UTIL_ImpactTrace` refuses the sky and nodraw** (`cdll_util.cpp:390`): `SURF_SKY`, `fraction == 1.0`, `SURF_NODRAW`.
3. **`ImpactTrace` dispatches `"Impact"` with `trace.surface.surfaceProps`** (`baseentity_shared.cpp:672`), and
   `ParseImpactData` turns that into `iMaterial = physprops->GetSurfaceData( surfaceProp )->game.material`.
4. **`GetImpactDecal` asks `DamageDecal`, which answers `"Impact.Concrete"` for the world**. Valve's own comment:
   the name "will get translated at a lower layer based on game material".
5. **`TranslateDecalForGameMaterial`** (`decals.cpp:325`): concrete keeps the name, `-` means no decal, and otherwise
   `scripts/decals_subrect.txt`'s `TranslationData` maps the material character to a group (`M` and `V` → `Impact.Metal`,
   `D` → `Impact.Dirt`, and so on).
6. **`GetDecalIndexForName` picks one file of the group by weight**, reservoir style, with the draw strictly below
   the weight.
7. **`AddBrushModelDecal` → `effects->DecalShoot` at `trace.endpos`**, the engine's decal system
   ([the port](../../managed/Tf2DemoSalvage.Scene/WorldDecals.cs), out of `engine.dll`).

## Where the surface comes from: two closed functions

**`trace.surface.surfaceProps` is per texdata, set at map load.** `CMod_LoadTextures` (`engine.dll` `0x18016eb00`,
disassembly) finds each texdata's material in the `"World textures"` group and, if it is not the error material, reads
`$surfaceprop` and stores `physprops->GetSurfaceIndex( name )`. That index can be −1, which is stored as-is.

**`GetSurfaceData( −1 )` is surface zero, not nothing.** vphysics' slot 5 (`FUN_1800184a0`, disassembly) remaps an index
past `0x7f` to zero unless it is the shadow index, and answers surface zero for a negative or past-the-end index. Its
sibling `GetIVPMaterial` answers null for the same inputs. The two differ, and only this one is on the impact path. So a
world texture with no `$surfaceprop` impacts as `default`, which is concrete.

**The struck surface is the brush SIDE's texinfo**: the side whose plane set the entry fraction, as `CM_ClipBoxToBrush`
reports it. It is not a face found another way. A brush side names a texinfo, not a face, so the texinfo table is read
on its own (`BspMaterials.ReadTexinfo`).

## The wrong turn: a square around every hole

The first picture showed each hole inside a darker square. The atlas `decals/decals_mod2x` is `DecalModulate`, whose
blend is `BlendFunc( DST_COLOR, SRC_COLOR )`: twice the product. A texel of 128 should leave the wall alone.

**It darkened instead, and the reason is the colour space, not the blend.** `DecalModulate` sets
`EnableSRGBRead( false )` and `EnableSRGBWrite( false )`, so the engine multiplies GAMMA values. This renderer's
target is sRGB, so it blends in linear space, and it decodes the texture. Grey 128 decodes to about 0.22, twice
that is 0.43, and the wall darkens to 43% wherever the atlas is "neutral".

**The same equation in linear terms** is `dst_lin · (2s)^2.2 = dst_lin · 2^2.2 · s_lin`. The blend supplies one 2 and
the world shader's overbright supplies another. The corners therefore carry `2^2.2 / 4 ≈ 1.149` as vertex light
(`WorldRenderer.ModulateTwiceLight`). *Arithmetic, on the 2.2 approximation of the sRGB curve.* After the change the
square is gone (`decals2.png`).

## Measured on f12

Through the viewer's own load (`tracers` probe; `demostf-cp_process_f12-2026-08-07.dem`):

| | count |
|---|---|
| impacts (bullets the world stopped) | 16,359 |
| with a decal | 12,105 |
| on terrain (displacement path, not built) | 1,424 |
| concrete / metal / dirt / glass / wood | 9,431 / 1,729 / 731 / 194 / 20 |

The rest of the 4,254 with no decal are the terrain hits, sky and nodraw surfaces, and surfaces marked `-`.

## A hole in a static prop is a model decal, cut to its square

Added 2026-09-24 (B421). **A bullet that stops on a static prop never reaches the world's decal code.** `Impact()`
(`fx_impact.cpp:149`) sends a hit on entity 0 with a nonzero hitbox to `staticpropmgr->AddDecalToStaticProp` alone.
Until then the viewer shot a world decal at the hit point instead. That clipped a dent onto whatever brushes lay within
its radius, often the wall behind the prop.

On a prop, the decal goes through `CStudioRender::AddDecal`, the same function a player's decal uses. That function has
a path that player decals never take: for a one-bone model with no flexes, each triangle is **cut** to the decal's
square rather than kept whole. The static-prop call also passes `noPokeThru`. Read out of `studiorender.dll`
(`0x18000b690`), that makes a corner count only when **|row2 · pos + t| < radius, measured from the ray's start**.

**That depth test is the thing to understand.** Fed the bullet's own ray, which starts at the shooter, it refuses every
triangle. The published half of the same shape is `C_BaseEntity::AddStudioDecal` (`c_baseentity.cpp:3640`). It traces
the model first, then builds a `betterRay` from the hit point one unit into the face before calling `AddDecal` with
`noPokeThru`. The viewer does the same for a prop. *Interpolated:* `CStaticPropMgr::AddDecalToStaticProp` is engine code
that has not been read, and neither has the clipper itself. The cut is Sutherland–Hodgman over U, then V.

**An instrument bug on the way.** The first count of "bullets that stopped on a static prop" asked whether the hit
carried the model's `$surfaceprop`. That gave 505 of 16,531. The real number is 834: 329 props' models declare no
`$surfaceprop`, which `GetSurfaceData` reads as surface zero. Those props had been silently falling through to world
decals, because every "is this a prop" test asked the surfaceprop instead of the prop index.

By tick 30,000 on f12, 187 prop hits are due and 75 decals are held (the `1.5 × r_maxmodeldecal` cap). Three hits took
no triangle. A container's face shows holes at tick 30,000 and none at tick 3,000 from the same camera.

**A metal door takes concrete holes, and that is right.** `red_sewer_door_2` (`*141`) is painted
`props/metaldoor01_192`, whose VMT declares no `$surfaceprop` (`vmt` probe, 2026-09-24). Its surface is therefore surface
zero, and `Impact.Concrete` goes untranslated. The texture looks metal, but the decal follows the material's declaration.

## A hole in terrain is cut from the triangles the world draws

Read from engine.dll (x64). The leaf pass `0x1801175c0` walks each leaf's displacements: a displacement whose parent
surface refuses decals (flag 0x4000) or that this shot already tagged is skipped; one whose bounding box overlaps the
decal's cube (centre ± radius, strict) goes to the face test `0x180118d70` with a flag of 1, which skips the
texture-extent rejection a brush face gets and calls `R_DecalCreate` `0x1801168b0` with the same flag.

With that flag, `R_DecalCreate` skips the brush polygon clip; the decal still takes a slot from the shared `r_decals`
pool, and the older-decal overlap pass still runs. Linking it (`0x1801166a0`) hands it to the displacement object:
CDispInfo, constructed by `0x1800c7340`, vtable `0x18038d8b8`, slot 8 = `0x1800c6c50`.

`0x1800c6c50`: a displacement holds at most 31 decals (0x1f) and drops its oldest to take another. It builds the
decal's texture basis with the same routine brush decals use (`0x1800bd1c0`), then walks the displacement's quad tree
(`0x1800c1400`) marking, in a per-decal bitmask, each quad whose four corners' decal coordinates (u = S·p − dS + 0.5, v
likewise) overlap the unit square [0, 1] (constants 0x18035c93c = 1.0, 0x18035d4b4 = 0.5). No depth limit is applied at
this point.

At draw (`0x1800c4a90` → `0x1800c64b0` → `0x1800c2490`), each marked quad emits the triangles the displacement's own
render mesh uses for it. Per triangle: its normal from two edges; refused unless dot(decal centre − first corner,
normal) is less than the radius (one-sided); corners mapped as for a brush face; the lightmap coordinate copied from
the displacement's own vertices; clipped by the same four-edge clip brush decals use (`0x1800bcbd0`: v < 1, u > 0, u <
1, v > 0); at most six corners kept; each corner pushed 0.1 along the triangle's normal (the clip's own push,
`DAT_18035dd2c`, given the triangle normal instead of a face plane).

In the viewer (interpolated where noted): every displacement is bounds-tested rather than only those in visited
leaves, and every triangle is clipped rather than only those of marked quads; the marking passes every triangle the
clip would keep, so only cost differs. The fragments are cut from the same tessellated triangles the world renderer
draws.

Found on the way, measured: the first build placed terrain decals and none were drawn. The decal pass culls back
faces, and the viewer's displacement triangles are wound the opposite way from Valve's; brush decals keep the BSP's
winding. Fragments are now wound about the surface normal, and a test pins it. Also, a mark in the first screenshots
was read as a decal and was the terrain texture, which a capture at an earlier tick showed; a control frame is what
caught it.

Measured on f12: 1,286 client bullets stop on terrain; by tick 1600 six decals lie on displacements (35 fragments),
one of them the first terrain bullet's hole at tick 1538.

## A decal that declares a fade never fades on the world

Read 2026-09-24 from engine.dll (x64). Twenty shipped materials declare `$decalFadeDuration`, among them `scorchfade`,
the `splash` decals and `redglowfade`. `scorchfade` is precached in ten of the owner's demos. The engine carries the whole
fade mechanism:

- `R_DecalCreate` (`0x1801168b0`) sets flag `0x100` when the decal info's duration (`+0x40`) is positive. It stores the
  duration at `+0x4c` and the start time at `+0x50`, which is the info's offset (`+0x44`) plus the tick clock
  (`0x1800a3020`: tick count × interval, plus the interpolation fraction when not paused).
- The brush draw (`0x18011d4c0`) scales the decal colour's alpha by `clamp(1 − (now − start) / duration, 0, 1)`. A
  negative duration fades in, as `(start − now) / duration`.
- `0x180115d70` removes the decal once `now ≥ start + duration`, after drawing it that frame.

**None of it runs for a world decal.** `R_DecalShoot` (`0x180118730`) builds the decal info on its stack and zeroes the
eight bytes at `+0x40` (`local_78 = 0`). It reads `$decalScale` and never reads `$decalFadeDuration`. It is the only
caller of the node walk (`0x180117d90`), and the walk is the only way to `R_DecalCreate`. So the flag is never set, and a
`scorchfade` on a wall stays until the pool retires it.

The one place that reads `$decalFadeDuration` is the model-decal add (`0x1800edc60`), which refuses such a material
outright. The viewer already does the same (`DecalMaterial.Fades`). *Read from the binary; no capture compared.*

## `GetColorForSurface` gains water, stays short on displacements, static props and light styles

Added 2026-09-25. `SurfaceColour.At` (`GetColorForSurface`, `c_impact_effects.cpp:96`, published) tints the flecks.
Its walk reproduces `R_LightVec` (`engine.dll` `0x1800d4b00`), and it skipped only sky. **Read from the binary:** the
node pass tests `(flags & 4) == 0` and `(flags >> 16 & 1) == 0` — sky and `SURFDRAW_WATERSURFACE` — and the leaf pass
tests `(flags & 0x10812) == 0`: water, displacement, nodraw and node. So a shot into water takes no face and gets
zero. The engine sets the water flag from the material; the viewer uses the texinfo's `SURF_WARP`, which vbsp gives
water faces — **an interpolation**, not read. Test: `SurfaceColourConformanceTests.At_AShotIntoWater_IsBlack`.

### Displacements: after the walk, and they add

**Read from the binary.** Each leaf the walk enters appends its displacements to a list, once each (`0x1800d3640`, a
frame stamp). After the walk `R_LightVec` (`0x1800d40a0`) ray-tests every listed displacement through `CDispInfo`'s
slot `0x78` (`0x1800c2dd0`), which is `AABBTree_Ray` (`dispcoll_common.cpp:564`, published) over the collision tree,
from the ray's start to the nearest fraction taken so far — **the same limit the brush faces set**. On a hit it:

- **adds** the luxel under the hit (`0x1800d37d0`: `samples + dt·smax + ds`, each style's map `smax·tmax` on, four
  times that when bumped) to the light the brush faces already added. The brush light is not cleared.
- takes the hit's face and a texture coordinate from `0x1800bfc90`: the hit's grid position, barycentric over its
  triangle, then bilinear over the base face's corners.

`ds` and `dt` are the lightmap coordinate times the page size minus the face's page offset and a half. Displacement
lightmap coordinates run from luxel centre to luxel centre, so this is `column · (smax − 1)`, truncated. The first test
aimed at an exact grid vertex, and float error truncated 3 to 2. A shot at a vertex can land either side in the engine
too.

Static props never reach `R_LightVec`. `GetColorForSurface` (`c_impact_effects.cpp:110`) sends a prop hit to
`staticpropmgr->GetStaticPropMaterialColorAndLighting`, which is not built here yet.

**Interpolated:** the engine builds each leaf's displacement list at load, and the builder was not found. Here each
displacement's collision box is pushed down the tree, in displacement order. The order only matters to a ray that
meets two displacements, because each hit adds its light.

Still not built: animated light styles, and the static-prop colour.

## Not established

What is not built, and every divergence, is listed under B415 in `docs/RISKS.md`. The largest are displacement decals,
players judged only on a bullet's own tick, and a pick that cannot be the engine's because its random stream is not
recorded.
