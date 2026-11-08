# 107 — Render-to-texture shadows: what TF2 does every frame (B702)

**Status: engine read in progress (2026-11-07).** Nothing here is implemented yet. This is the reading the port is
written from; each claim carries its evidence class. SDK = `F:/src/source-sdk-2013/src` (read from published source).
`engine.dll` = the live x64 build dated 2026-10-07, Ghidra project `D:/ghidra-proj/tf2enginex64-1007` (decompiled).

## Why it matters

The owner's TF2 config sets `r_shadowrendertotexture "1"` (`tf/cfg/config.cfg`), so in the 2026-11-07 f12 comparison
(149/119 fps at ticks 5970/6104, docs/measurements/2026-11-06-detail-sprites-visible-leaves.md) TF2 drew a shaped shadow under every player, building and dynamic prop and this viewer
drew none. It is the heaviest per-frame work TF2 does that the viewer skips (the "what we still miss" audit of
2026-11-07: B702 shadows, B701 anti-aliasing — off in the owner's config, `mat_antialias "1"` — and B704 shell casings).

## Client side (SDK, read from published source)

- **Cvars:** `r_shadowrendertotexture` (`clientshadowmgr.cpp:91`, SDK default 0; TF2's shipped and the owner's config 1),
  `r_shadows` 1, `r_shadowmaxrendered` 32, `r_shadows_gamecontrol` -1 (`:684-686`). Constants
  `TEXEL_SIZE_PER_CASTER_SIZE` 2, `MAX_FALLOFF_AMOUNT` 240, `SHADOW_CULL_TOLERANCE` 0.5 (`:679-682`).
- **Who casts:** `C_BaseEntity::ShadowCastType` (`c_baseentity.cpp:1502-1509`): none with `EF_NODRAW | EF_NOSHADOW`,
  render-to-texture for any studio model, none otherwise. `C_BaseObject` none at full invisibility
  (`tf/c_baseobject.cpp:1087-1093`). `C_TFPlayer` overrides `ShadowCastType`, `GetShadowRenderBounds`,
  `GetRenderBounds` and `GetShadowCastDirection` (`tf/c_tf_player.h:477-481`). `ShadowCastType`
  (`tf/c_tf_player.cpp:9255-9278`): none when not visible, `EF_NODRAW | EF_NOSHADOW`, ragdoll render fx, or not drawn
  (first person); `SHADOWS_SIMPLE` under `cl_blobbyshadows`; else **`SHADOWS_RENDER_TO_TEXTURE_DYNAMIC`** — a player's
  shadow texture is redrawn every frame it is in view. `GetShadowRenderBounds` (`:9281-9303`): the render bounds
  fattened 36 units in x and y ("the rotation of the guy's upper torso can place his gun a ways out of his bbox");
  blobby uses the collision box. `GetShadowCastDirection` (`:9317-9329`): straight down only for blobby.
- **Who receives:** `ShouldReceiveProjectedTextures` (`c_baseentity.cpp:1566-1590`): not `EF_NODRAW`, not
  `EF_NORECEIVESHADOW`, and **never a studio model**. World surfaces, brush models and static props receive; a static
  prop only from a caster with `FL_NPC | FL_CLIENT` — players (`clientshadowmgr.cpp:3487-3496`, "wickedly expensive").
- **Direction, distance, color:** `Init` direction (0.1, 0.1, -1), distance 50 (`:1287-1289`); `shadow_control` sets
  them (`c_shadowcontrol.cpp:50-57`; server defaults direction (0.2, 0.2, -2), distance 50, color (64, 64, 64), zero
  angles become (80, 30, 0), `shadowcontrol.cpp:73-112`). Color at level start: engine ambient × 3 + 0.3, clamped
  (`clientshadowmgr.cpp:1513-1534`), into `$color` of both shadow materials.
- **Atlas:** one 1024² ARGB8888 render target `_rt_Shadows`, sixteen 256² blocks cut into 16 (one block), 32 (one),
  64 (two), 128 (six) and 256 (six) cells; LRU per size, a cell size chosen from screen area (`:117-673`). A caster's
  texture size is the power of two at or above 2 × its largest bound (`:1584-1615`).
- **Per view:** `CBaseWorldView::DrawExecute` and `CSkyboxView` call `ComputeShadowTextures` before `DrawWorld`
  (`viewrender.cpp:5487-5568`, `:4924`). It enumerates shadows in the view's leaves, frustum-culls each shadow's swept
  box, sorts by screen area, redraws at most `r_shadowmaxrendered` per frame and drops the rest to the blob material
  (`clientshadowmgr.cpp:4001-4109`, `:3733-3826`). A shadow is collected in the first view that sees it each frame.
- **Redraw rule:** a cell is redrawn when newly assigned or `SHADOW_FLAGS_TEXTURE_DIRTY`; the flag clears after the draw
  unless `SHADOWS_RENDER_TO_TEXTURE_DYNAMIC` (animating) set `SHADOW_FLAGS_ANIMATING_SOURCE` (`:3733-3810`).
- **Matrices:** `BuildRenderToTextureShadow` (`:2464-2565`): an orthographic world-to-shadow basis from the shadow
  direction and the box's widest projected axis, `size` padded by 2 × 2 texels, falloff start from
  `ComputeLocalShadowOrigin`, max height = distance + falloff start, leaves from a swept-box ray, then
  `shadowmgr->ProjectShadow(…, MAX_FALLOFF_AMOUNT, …)` and up to four extra clip planes.
- **Drawing into the atlas:** `ShadowBuild` shader, additive alpha of the caster's coverage, cleared to (255,255,255,0)
  (`shadowbuild_dx9.cpp`, `shadowbuildtexture_ps2x.fxc:22-28`); the engine binds it as `engine/shadowbuild`
  (`engine.dll` string at `0x1803930b8`, used in `FUN_1800f6f80`).
- **Materials (the game's own files, `game-file` probe):** `decals/rendershadow` is shader `Shadow` with the `Shadow`
  proxy, `$decal 1`; `decals/rendermodelshadow` is `ShadowModel`, `$decal 1 $model 1`; `decals/simpleshadow` is `Shadow`
  with `$basetexture Decals/simpleshadow`.
- **Applying it:** shader `Shadow` (`shadow.cpp`, `shadow_ps2x.fxc:35-62`): blend ZERO/SRC_COLOR (multiply), five taps
  one texel apart, coverage minus vertex alpha (the fade), `1 + coverage × ($color − 1)`, fog to white.

## Engine side (`engine.dll`, decompiled)

- `CShadowMgr::ProjectShadow` is `FUN_180127de0` (its VPROF string). It clears the shadow's old surfaces
  (`FUN_180128ed0`, `FUN_180128920`), and unless the shadow is disabled (`+0x88 & 4`) stores falloff offset (`+0x40`),
  max distance (`+0x44`), falloff amount (`+0x48`), direction (`+0x60`) and the world-to-shadow matrix; then a swept ray
  from the origin along `dir × maxHeight` with half-extent `|size| / 2`, a bounding sphere at origin + dir × maxHeight / 2
  of radius maxHeight / 2 + |size| / 2 (`+0xe0`, `+0x8c`), and for each leaf `FUN_1801264f0`.
- `FUN_1801264f0` (per leaf): leaf surfaces through `FUN_1801233f0` (shadows; flashlights take `FUN_180123070`), then the
  leaf's displacements, each tested against the swept box.
- `FUN_1801233f0` (per surface, once per projection by a counter): a surface with draw flag `0x1a & 1`, facing against
  the shadow (dot(normal, dir) ≤ −0.01, or either side of 0.01 for a `PLANEBACK` surface the other way), with the ray's
  start in front of its plane and the sphere crossing the plane, and surface flags `& 0x3020` clear (or the flashlight
  bit) is handed to `FUN_180121d50` — add the shadow to the surface.

- `FUN_180121d50` (add shadow to surface): links a shadow-decal record (surface, shadow handle, cache slot −1) into the
  surface's list and the shadow's surface list; warns and drops past the pool ("overflowed m_ShadowDecals").
- `s_ShadowMgr` is `0x1806c5e00` (`VEngineShadowMgr002` factory `FUN_18012da20`); its vtable is `0x1803959f0`.
- **`CShadowMgr::RenderShadows` is `FUN_18012ae80`**: for each material's shadow list, `FUN_18012a670` (the
  "RenderShadowList" strings) binds the shadow material with its proxy data (the client shadow handle), skips a shadow
  whose falloff bias byte (`+0x5c`) is 255, builds or reuses each decal's clipped polygon (`FUN_1801267b0`; cached per
  decal when the shadow was created with the cache flag, `+0x88 & 1`, else rebuilt every frame), and writes one mesh.
- **The polygon** (`FUN_1801238a0`): the surface's vertices into shadow texture space by the world-to-texture matrix
  (`FUN_180128250`: each vertex stores world xyz and (u, v, z); the decal is kept only if some z is below the max
  distance), then Sutherland–Hodgman clips to v < 1 (`FUN_180120670`), the other three unit-square edges
  (`FUN_18011fd50`, `FUN_18011ffc0`, `FUN_1801203f0`), z > 0 (`FUN_18011fbd0`), and the shadow's extra clip planes
  (count `+0xf8`, normals `+0x104`, distances `+0x12c`). At least three vertices remain or nothing is drawn. Every vertex
  is pushed off the surface by **0.1 × the surface normal** (`FUN_1801246f0`).
- **The vertex** (`FUN_180121970`, with the render info from vtable slot `0xf8`, `0x180123830`): position; texcoord =
  `texOrigin + texSize × (u, v)` (`+0x4c..0x58`, set by `SetShadowTexCoord`); color, **all four bytes the same** =
  `min(255, max(z − falloffOffset, 0) × falloffAmount / (maxDist − falloffOffset) + falloffBias)` — the divisor is 1 when
  `maxDist ≤ falloffOffset` (constant `1.0f` at `0x18035e93c`). The `Shadow` shader subtracts that alpha from the coverage,
  so a shadow fades out with distance from its caster. Triangles are fans `(0, i + 1, i + 2)`.

- **When the world draws them:** `R_DrawWorldLists` (`FUN_1800e1030` → `FUN_1800e7010`, `gl_rsurf.cpp`) calls
  `RenderShadows` (vtable slot `0xd8`) after the world's opaque surfaces and its decals and overlays (`FUN_180116ad0`),
  having handed the shadow manager each drawn surface (slot `0xc8`, once per surface in the frame's list). So only the
  surfaces actually drawn this view get their shadows, as the SDK comment (`clientshadowmgr.cpp:20-26`) says.
- **Drawing a model into the atlas — `DrawModelShadow` is `FUN_1800ebd40`:** studio render lighting off, color (1, 1, 1),
  alpha 1; unless the model has `STUDIOHDR_FLAGS_USE_SHADOWLOD_MATERIALS` (`0x100`), forced material override
  `engine/shadowbuild` with type 1 (build shadows); then the model is drawn with flags `0x24` (no flexes, no shadows).
  `engine/shadowbuild.vmt` is shader `ShadowBuild`, `$nocull 1 $model 1`.
- **Drawing a brush model into the atlas — `DrawBrushModelShadow` is `FUN_1800dc280`:** binds `engine/shadowbuild`
  and draws every face of the model except those with flag `0x20` as triangle fans, texcoords zero.

- **Surface flags, by the engine's bits** (`FUN_1801233f0`): `0x200` is tested where the SDK-era `SURFDRAW_NOCULL` sits
  (a NOCULL surface takes a shadow from either side), and `0x3020` is `SURFDRAW_TRANS | SURFDRAW_ALPHATEST |
  SURFDRAW_NOSHADOWS` by the same numbering (interpolated: the numbering comes from the project's own `SURFDRAW_SKY` 4 and
  `SURFDRAW_WATERSURFACE` 0x10000, and the rest is not in the SDK). **`Mod_LoadFaces` is `FUN_180102a40`** and settles
  three of them (decompiled): the word at `+0x1a` is `(texinfo << 1) | !(m_NumPrims >> 15)` — **bit 0 is the face's
  dynamic-shadows-enabled flag**, the inverse of `dface_t::m_NumPrims`'s top bit (`bspfile.h:703-754`, "Top bit, if set,
  disables shadows on this surface"); texinfo `SURF_NOSHADOWS` (`0x1000`) sets `0x2000`; `side` sets `0x40` (PLANEBACK),
  `onNode` `2`, `SURF_NOLIGHT` `1`, `SURF_SKY` `4`, `SURF_WARP` `0x10000`. `0x20` and `0x1000` (TRANS, ALPHATEST) are not
  set here; they come from the material, not yet read.

## Port plan (2026-11-07)

1. **Done:** `ShadowTextureAllocator` — `CTextureAllocator` whole (`ShadowTextureAllocatorConformanceTests`).
2. **Done:** `ShadowProjection` — `BuildRenderToTextureShadow`'s geometry, matrices, falloff and clip planes
   (`ShadowProjectionConformanceTests`).
3. **Done:** `ShadowDecal` — the per-surface clip and fade byte (`ShadowDecalConformanceTests`).
4. **Receivers.** Done: the face's dynamic-shadows bit (`BspSurfaces.DynamicShadowsEnabled`), the swept-box leaf walk
   (`BspLeafTree.EnumerateLeavesAlongSweptBox`, `FUN_1800dec70`, vtable `0x1803907a8` slot 4), and the per-surface test
   (`ShadowReceiver.Takes`, `FUN_1801233f0`). Open: where the engine sets `TRANS`, `ALPHATEST` and `NOCULL` from the
   material; displacements (tested by their box in `FUN_1801264f0`); joining these per frame with only the surfaces drawn
   this view (slot `0xc8`).
5. **Casters and the per-frame rules (Scene):** which props cast (`ShadowCastType`, TF player overrides, objects at full
   invisibility), `shadow_control` direction/distance/color from the demo, dirty and redraw rules, the
   `r_shadowmaxrendered` budget and the blob fallback, the falloff bias.
6. **Render:** the 1024² `_rt_Shadows` target; each caster drawn into its cell with a `ShadowBuild` equivalent (coverage
   alpha from translucent materials' base texture, else 1); the world shadow pass inside the world draw after decals and
   overlays, shader `Shadow` (multiply, five taps, fade byte, fog to white). Static props receive from players only.
7. **Measure** against TF2 at the same tick and camera, and the frame cost with shadows on.

**Not read yet:** displacements (`FUN_1800c58a0` path), brush models
(`AddShadowToBrushModel`), static props (`AddShadowToStaticProp`), `DrawModelShadow` and its `engine/shadowbuild` bind
(`FUN_1800f6f80`), `SetFalloffBias`'s callers, and the TF player overrides (`c_tf_player.cpp`).
