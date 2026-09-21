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

## Not established

What is not built, and every divergence, is listed under B415 in `docs/RISKS.md`. The largest are displacement decals,
players judged only on a bullet's own tick, and a pick that cannot be the engine's because its random stream is not
recorded.
