# 107 — Render-to-texture shadows: what TF2 does every frame (B702)

**Status (2026-11-08): drawn.** Players, buildings, projectiles and dropped items cast shadows on the world's faces and
displacements and on brush entities, `r_shadowrendertotexture 1` by default. Models — static props and studio models —
take none, which is the engine's behaviour: its model-shadow pass draws no mesh (below). The open gaps are listed at the
end of the port plan. Each claim
carries its evidence class. SDK = `F:/src/source-sdk-2013/src` (read from published source). `engine.dll` = the live x64
build dated 2026-10-07, Ghidra project `D:/ghidra-proj/tf2enginex64-1007` (decompiled).

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
- **Who receives:** `ShouldReceiveProjectedTextures` (`c_baseentity.cpp:1566-1590`) is asked twice, with different
  flags. The leaf system asks with the shadow's own flag (`ShouldRenderableReceiveShadow`, `clientleafsystem.cpp:950-957`):
  not `EF_NODRAW`, not `EF_NORECEIVESHADOW`, and **never a studio model**. `AddShadowToReceiver` asks again with the whole
  type mask (`SHADOW_FLAGS_PROJECTED_TEXTURE_TYPE_MASK`, `ishadowmgr.h:47`), so the flashlight branch answers: only
  `EF_NODRAW` or a render mode above normal at alpha 0 refuse. World surfaces, brush models and static props are offered
  the shadow. A static prop takes it only from a caster with `FL_NPC | FL_CLIENT` whose `GetActualShadowCastType` is
  `SHADOWS_RENDER_TO_TEXTURE` (`clientshadowmgr.cpp:3487-3496`, "wickedly expensive"). A studio model takes only a
  flashlight's (`:3510-3522`).
- **A wrong turn, kept:** this session first concluded that a player's shadow never reaches a static prop, because a
  player casts `SHADOWS_RENDER_TO_TEXTURE_DYNAMIC` and the gate asks for `SHADOWS_RENDER_TO_TEXTURE`. The research agent
  sent to read the model path was told that, and refuted it. `GetActualShadowCastType` reads the shadow's flags, not the
  entity's type (`:2215-2241`). Any shadow made with `SHADOW_FLAGS_USE_RENDER_TO_TEXTURE` answers
  `SHADOWS_RENDER_TO_TEXTURE`, and `C_BaseEntity::CreateShadow` sets that flag for every type but simple
  (`c_baseentity.cpp:3958-3977`). So a player's shadow IS added to static props. The engine then draws nothing there
  (below).
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
  `onNode` `2`, `SURF_NOLIGHT` `1`, `SURF_SKY` `4`, `SURF_WARP` `0x10000`. **The material's bits are set by
  `FUN_1800fb5d0`** (per brush model, every surface; decompiled, slots mapped on `IMaterial`, `imaterial.h:446-604`): it
  clears `0x5220`, then `IsTwoSided()` (slot `0x108`, `$nocull`) sets `0x200` NOCULL; texinfo `SURF_TRANS` (`0x10`) or
  `IsTranslucent()` (slot `0x88`) sets `0x20` TRANS; texinfo `SURF_NODECALS` (`0x2000`), `GetMaterialVarFlag` (slot
  `0xf0`) or `IsAlphaTested()` set `0x4000`; `IsAlphaTested()` (slot `0x90`) sets `0x1000` ALPHATEST. So the shadow
  test's `0x3020` is: the material translucent (or the face marked so), the material alpha-tested, or the face marked
  no-shadows; and a two-sided material takes shadows from both sides.

## Port plan (2026-11-07)

1. **Done:** `ShadowTextureAllocator` — `CTextureAllocator` whole (`ShadowTextureAllocatorConformanceTests`).
2. **Done:** `ShadowProjection` — `BuildRenderToTextureShadow`'s geometry, matrices, falloff and clip planes
   (`ShadowProjectionConformanceTests`).
3. **Done:** `ShadowDecal` — the per-surface clip and fade byte (`ShadowDecalConformanceTests`).
4. **Receivers.** Done: the face's dynamic-shadows bit (`BspSurfaces.DynamicShadowsEnabled`), the swept-box leaf walk
   (`BspLeafTree.EnumerateLeavesAlongSweptBox`, `FUN_1800dec70`, vtable `0x1803907a8` slot 4), and the per-surface test
   (`ShadowReceiver.Takes`, `FUN_1801233f0`); the material bits (`FUN_1800fb5d0`, above). **Displacements**
   (`FUN_1801264f0`, decompiled): each displacement in the leaf, once per projection by its tag (vtable `0x68`/`0x70`),
   unless its base surface has `NOSHADOWS`; its bounding box (vtable `0x18`) against the shadow's sphere
   (`FUN_180174320`); then the base surface's dynamic-shadows bit and `flags & 0x3020` as for a face — and the shadow is
   added to the BASE surface, its decal built later by the displacement itself. **That build** (`CDispInfo` vtable
   `0x18038f8f8`, found by RTTI; slot `0x60` is `FUN_1800c57b0`): the displacement's node tree is walked
   (`FUN_1800c7660`, a node only when its bit from `FUN_1800c25b0` is set) and each triangle of a reached node goes to
   `FUN_1800c3bb0`, which lifts **each corner 0.1 along its own vertex normal** and hands the triangle to the shadow
   manager's slot `0xf0` (`FUN_1801279f0` → `FUN_180127810`) — **the same clips as a face** (`FUN_180128250`, the four
   unit-square edges, depth &gt; 0, the extra planes), three or more corners or nothing. So a displacement's shadow is
   `ShadowDecal.Clip` per triangle with the lift applied first. The node bits (`FUN_1800c25b0`) only skip triangles; not
   yet read whether they could skip one the clip would keep. Open: joining receivers per frame with only the surfaces
   drawn this view (slot `0xc8`).
5. **Done (2026-11-08): casters and the per-frame rules.** `ShadowEntities` (cast type: studio models, players
   `SHADOWS_RENDER_TO_TEXTURE_DYNAMIC` with bounds fattened 36 in x and y), `ShadowCasters` (`ShouldUseParentShadow`,
   `DrawShadowHierarchy`'s move children, the swept-box frustum cull, `PrioritySort` by `ComputePixelDiameterOfSphere`,
   the `r_shadowmaxrendered` budget counting only redrawn cells, the cell from the ported allocator), `ShadowControlFeed`
   (`C_ShadowControl::OnDataChanged`'s four setters; nothing undoes them). Before any controller the colour is
   `LevelInitPreEntity`'s ambient × 3 + 0.3, and `GetAmbientLightColor` (`engine.dll` `0x180071000`, IVEngineClient
   slot 46, disassembled) is the intensity of the FIRST `emit_skyambient` world light (`FUN_1801ba290`, type 5), zero
   for a map with none.
6. **Done (2026-11-08): render.** `ShadowReceivers` in the engine's two halves: `Project` is `ProjectShadow` (leaf walk,
   per-surface test, link), `Build` is `RenderShadows`, cutting decals only on the surfaces the main view drew
   (`WorldCulling.Surfaces`, the list `R_DrawWorldLists` hands over surface by surface, and the displacements in view).
   `WorldRenderer.Shadows`: the atlas cleared to (1, 1, 1, 0), each caster and its children drawn into its cell through
   `Scale( 1, -1, 1 ) · Ortho( 0, 0, 1, 1, -9999, 0 )` with the viewport half a pixel in (Direct3D 9's pixel centres),
   additive alpha; the world pass after the decals: five taps one texel apart, coverage minus the fade, `1 + c ·
   ($color − 1)` with `$color` gamma-to-linear, fogged toward white by `(1 − f)^4`, blended `ZERO, SRC_COLOR` through the
   frame's sRGB view. `ShadowRenderConformanceTests` reads the pixel: a floor under a box is the floor times
   `pow( 64/255, 2.2 )` exactly (138 → 29), untouched beside it.
7. **Measured 2026-11-08**, f12 tick 5970 first person, window off screen:
   0.74 ms a frame, 162 fps on and 185 off (measurements/2026-11-08-shadows-frame-cost.md).
   Game thread +0.67 ms, render thread +0.28 ms, atlas GPU 0.28 ms (measurements/2026-11-08-shadows-frame-cost.md).
   Against TF2 at the same tick: not yet.

**Read since (engine.dll, a sonnet agent's Ghidra read, 2026-11-08, decompiled unless marked):**

- **A displacement's decal is built node by node.** `FUN_1800c2520` → `FUN_1800c25b0` gives each node of the
  displacement's tree a bit: a leaf node projects its 3×3 vertex patch (unlifted positions, `disp+0x98`) through the
  shadow's world-to-texture matrix and is set when `minX ≤ 1 && maxX ≥ 0 && minY ≤ 1 && maxY ≥ 0 && maxZ ≥ 0 && minZ ≤
  maxDist` (non-strict); an interior node is the OR of its four children. `FUN_1800c7660` walks only set nodes, and only
  children also set in the displacement's own bitset (`disp+0x198`), handing each reached node's triangles
  (`disp+0x2e8`) to `FUN_1800c3bb0`, which lifts each corner 0.1 along ITS OWN vertex normal (`disp+0xb0`, stride
  `disp+0x4c`).
- **Static props never cast.** `CStaticProp`'s `IClientRenderable` shadow block is `CDefaultClientRenderable`'s stubs,
  `ShadowCastType` returning 0, `SHADOWS_NONE` (`0x1800180c0`, disassembled); no convar or flag gates it.

**Open (each a divergence, in the order they are worth doing):**

- **Done since:** displacements are built node by node as above (`ShadowReceivers.BuildDisplacement`, the tesselation's
  per-node runs recorded by `DisplacementTesselation.Build`), each corner lifted along its own normal from
  `CCoreDispInfo::GenerateDispSurfNormals` (`builddisp.cpp:1732-1913`: each quad's two triangles around the vertex,
  averaged and not renormalised). *Interpolated:* that `disp+0xb0` holds those normals unsmoothed across neighbours; the
  engine's `CDispInfo` load is closed and was not read.
- **Fixed after a Fable review (2026-11-08):** the caster drew its face flexes, where `DrawModelShadow`'s flags 0x24 are
  `STUDIORENDER_DRAW_NO_FLEXES | NO_SHADOWS`; a static caster that turns or animates was never dirty again, where
  `ANGLES_CHANGED` and `ANIMATION_CHANGED` mark it (`baseentity_shared.cpp:1444-1475`), so it did not use the budget;
  a reused entity index kept the old entity's texture size; a displacement whose base face is on the surface list too
  could be built twice; and displacement decals were wound as the physics store keeps them, counter-clockwise from the
  front, which the decal pass culls — now Valve's clockwise.
- **Brush entities as receivers: done** (`ShadowReceiverCull`, `ShadowReceivers.Begin(brushes)`/`BuildBrushes`,
  `ShadowReceiver.TakesOnBrushModel`; `ShadowReceiverCullConformanceTests`, `ShadowBrushReceiversConformanceTests`,
  `ShadowRenderConformanceTests`). The client half (`AddShadowToReceiver`, `clientshadowmgr.cpp:3439-3525`): a shadow
  never lands on its own caster; a receiver must answer `ShouldReceiveProjectedTextures` (above); `CullReceiver`
  (`:3297-3433`) drops one whose bounding sphere does not meet the shadow-space box `(±size/2, 0…maxDist)` — strictly,
  `dmin < r²` (`IsBoxIntersectingSphere`, `collisionutils.cpp:259-301`) — or that lies on the caster's side of a
  separating plane against the shadow direction. The plane is the line between the two spheres' centres when they do not
  meet (`distSq <= (r1+r2)²` is meeting), else `ComputeSeparatingPlane` (`collisionutils.cpp:2158-2464`): fifteen axes in
  order (box 1's three faces, box 2's three, then the nine edge pairs, box 1's edge outer), box 1 the receiver, tolerance
  3, each axis separating when `|origin projection| + 3 > projection sum`, a pair skipped when its edges are within
  1e−3 of parallel. With no plane the receiver is kept. A sweep of 400 seeded poses holds the port's index algebra
  against the same test written as projections in the world. The receivers are found through the leaf system:
  `CClientLeafSystem::ProjectShadow` adds the shadow once to every renderable in its leaves
  (`clientleafsystem.cpp:963-1053`), each renderable filed by its placed bounds (`CalcRenderableWorldSpaceAABB`).
  **The engine half, re-read 2026-11-08 (decompiled):** `AddShadowToBrushModel` (`CShadowMgr` vtable slot 7,
  `FUN_180121f70`, gated on `r_shadows`) never reads the origin. It rotates the shadow direction into the model's space
  by its angles (`FUN_180277a00` → `FUN_18027b000`) and links every surface of the model with `(flags & 0x10) == 0`,
  facing `dot(dir, normal) ≤ 0` or no-cull, and `(flags & 0x3020) == 0`. It reads no dynamic-shadows bit and makes no
  origin-side or sphere test, and its threshold is 0 rather than −0.01. **Bit `0x10` is `SURFDRAW_NODRAW`:**
  `Mod_LoadFaces` (`FUN_180102a40`) matches the SDK-era `SURFDRAW_*` table bit for bit — `1` NOLIGHT, `2` NODE, `4` SKY,
  `0x40` PLANEBACK, `0x400` lightstyles, `0x800` displacement, `0x2000` NOSHADOWS, `0x8000` prims, `0x10000` water, the
  vertex count at bit 24. Its setter is not found: not `Mod_LoadFaces` or its callees `FUN_1800fe180` and
  `FUN_1800fe5d0`, not the material pass (`FUN_1800fb5d0`, whose `0x10` goes to the model's flags at `+0x24`), and no
  `nodraw` string but `r_flashlightnodraw`. So it is set here from the texinfo flags the brush draw drops
  (`BspFace.NotDrawnSurfaces`: sky, 2D sky, nodraw, hint, skip, trigger). That set is interpolated; the effect is not,
  since the engine builds no shadow on a face it does not draw. A Fable review caught the first version linking a door's
  nodraw faces and cutting decals on faces nobody sees. The brush model's own draw (`FUN_1800e05d0`) draws them after all its
  surfaces and their decals: `RenderProjectedTextures` (`FUN_18012a530` → `RenderShadows`) under the model matrix, the
  decal built in model space. Here the surfaces are placed by the entity's pose and cut in the world (the same arithmetic
  to rounding), after every opaque model and the entity decals. The order among opaque draws cannot change a pixel of a
  depth-tested multiply.
- **Models as receivers: the engine draws nothing** (a research agent's read, 2026-11-08; notes kept outside the repo).
  `AddShadowToStaticProp` (`FUN_1802042d0`) is `AddShadowToModel` (`0x1801220c0`) on the prop's model instance.
  `CShadowMgr::SetModelShadowState` (`0x18012bcc0`, disassembly) walks that list before every model draw:
  `DrawModelExecute` (`0x1800ee0e0`), `DrawModelExStaticProp` (`0x1800ec3a0`), `DrawStaticPropArrayFast`. For each
  shadow it calls `IStudioRender::AddShadow( m_pModelMaterial, proxy )` — `decals/rendermodelshadow`, shader
  `ShadowModel` (shipped data, `hl2_misc_dir.vpk`). That call is gated on `r_shadows_gamecontrol != 0`, default −1. Then
  studiorender's shadow pass (`DrawShadows`, `0x18000cfa0`) forces that material and calls the model draw with null
  material and material-flag lists, and `R_StudioDrawPoints` skips every mesh when the flag list is null (disassembly,
  `0x180011b32`). So no triangle reaches the shader, on every route. Flashlights pass the real lists and still draw.
  **Parity is to draw no render-to-texture shadow on a model**, which is what this viewer does. Unconfirmed: a live TF2
  capture of a player's shadow missing on a prop, and the middle hand-off of the null argument, which was read from
  decompiled code only.
- **One decal list for every view**: built from the main view's surfaces, so a water reflection or a monitor misses a
  shadow on a surface the main view did not draw.
- **The blob fallback: done (2026-11-08).** `ComputeShadowTextures` sends a shadow past `r_shadowmaxrendered`, and one the
  atlas gives no cell, to `DrawRenderToTextureShadowLOD` (`clientshadowmgr.cpp:3816-3826`, `:3750-3754`, `:4080-4089`;
  read from published source). That sets `decals/simpleshadow` as both materials, sets the texture coordinates to
  (0, 0, 1, 1) and clears the extra clip planes. The projection is not rebuilt: `UpdateStudioShadow` chooses
  `BuildRenderToTextureShadow` by the cast type, not by the LOD flag (`:2760-2807`). So the blob is the same decal sampling
  the whole 64×64 DXT5 `decals/simpleshadow.vtf` (shipped data, `vtf` probe), its five taps 1/64 apart (`shadow.cpp:95-109`).
  Here `CastShadow.Blob` carries this, and each pass draws two lists, one per material: the cell corners over the atlas,
  then the blob corners over `decals/simpleshadow`. *Interpolated:* cells come first. The engine walks its material lists
  in an order not read; a multiply is commutative, so the order can move a pixel only by rounding where a blob and a cell
  shadow overlap. **The planes are cleared on the frame a shadow becomes the blob**
  (`DrawRenderToTextureShadowLOD` tests `SHADOW_FLAGS_USING_LOD_SHADOW` first, `:3819`). A re-projection adds them back
  with no LOD test (`:2560`), and `DrawRenderToTextureShadow` clears the flag before it knows whether a cell is free
  (`:3738-3743`). So a blob held past the budget keeps its planes once it has moved, and one with no cell loses them every
  frame. A Fable review caught the first version clearing them on every blob frame, and the one-shader version sampling
  both textures, ten taps where the engine takes five. Held by `ShadowCastersConformanceTests` (past the budget, no cell
  left, same projection without planes, the planes' lifetime), `ShadowReceiversConformanceTests` (whole-texture
  coordinates) and `ShadowRenderConformanceTests` (the floor under a blob of alpha 128 lerps 128/255 of the way, and a
  cell shadow beside a blob in one frame).
- **Enumeration by the view's leaves: done (2026-11-08).** `ComputeShadowTextures` enumerates shadows through
  `EnumerateShadowsInLeaves` (`clientleafsystem.cpp:1078-1105`) over the world list's leaves (`viewrender.cpp:5494`). A
  shadow is considered only when a leaf its projection was filed in (`BuildShadowLeafList`, `clientshadowmgr.cpp:2263-2278`)
  is one the view draws, and only then frustum-culled (`EnumShadow`, `:1059-1109`). `ShadowCasters.Frame` takes the view's
  leaves (`ShadowLeafView`, `WorldCulling.IsMainLeaf`), files each projection's leaves, and hands them to the receivers,
  which walk them instead of walking again. *Not ported:* the order shadows come out of the leaf walk, which decides only a
  tie in screen area; the sky view's own enumeration (`viewrender.cpp:4924`), which no TF2 caster reaches, since static
  props cast none.
- **The re-projection schedule: done (2026-11-08).** `PreRender` re-projects a dirty shadow only when created, when its
  caster's render origin or angles differ from the last projection's (`UpdateShadow`, `:3132-3156`: "Figure out if the
  shadow moved"), or when every shadow is forced by a new direction or distance (`UpdateAllShadows`, `:1629-1709`).
  Bounds alone do not re-project. `ShadowCasters` keeps each shadow's projection, its leaves and its cleared planes until
  one of those happens.
- **`EF_NOSHADOW` and `EF_NORECEIVESHADOW`: done (2026-11-08).** Both are bits of `m_fEffects` (`const.h:288-290`), read
  by `EntityState.CastsNoShadow` / `ReceivesNoShadow`, carried on the pose, and applied where the engine asks: no cast type
  under `EF_NOSHADOW` (`c_baseentity.cpp:1504`, `c_tf_player.cpp:9264`), and a brush entity refused as a receiver under
  `EF_NORECEIVESHADOW` (`c_baseentity.cpp:1583`, by the leaf system's shadow-flag ask). Held by `ShadowEffectsWiringTests`
  (decode and the hop to the pose), `ShadowEntitiesConformanceTests` and `ShadowBrushReceiversConformanceTests`.
- **The cast types: done (2026-11-08)**, from a read of every client `ShadowCastType` override (clangd, 23 hits; read
  from published source). Every TF2 studio entity but the player runs `C_BaseAnimating::ShadowCastType`
  (`c_baseanimating.cpp:820-851`). It is `SHADOWS_RENDER_TO_TEXTURE` for a model with no sequences or for a ragdoll.
  Otherwise it is `SHADOWS_RENDER_TO_TEXTURE_DYNAMIC` when the model has any pose parameter (the merged count), bone
  controller or IK chain (the root header's), and render-to-texture when it has none. A sentry is dynamic and a medkit is
  not (`MapAssetsTests`, the real files). `C_TFPlayer` is always dynamic. The port treated every non-player as static, so
  an aiming sentry was redrawn only when its sequence or cycle changed, and never counted against the budget while it
  aimed. `ModelFrames.AnimatingShadow` carries the rule. *Not ported, named:* `C_BaseObject`'s none at invisibility
  level 1 (`c_baseobject.cpp:1087-1093`), because a building's invisibility is decoded nowhere. `C_TFWeaponBase`'s none
  for a weapon that is not active (`tf_weaponbase.cpp:4572-4585`) matters only to a weapon drawn while holstered.
- **The falloff bias: done (2026-11-08).** `ComputeFxBlend` ends with `SetFalloffBias( m_ShadowHandle, 255 -
  m_nRenderFXBlend )` (`c_baseentity.cpp:3548-3552`). `C_TFPlayer::ComputeFxBlend` replaces that with
  `GetPercentInvisible() * 255` when it is not zero (`c_tf_player.cpp:9567-9580`). The bias is added to every decal
  corner's fade. At 255 the shadow is disabled and not enumerated (`UpdateShadow`, `clientshadowmgr.cpp:3101-3110`;
  `EnumShadow`, `:1069-1072`). So a fading entity's shadow fades with it, a cloaking spy's shadow fades by its percent
  (the teammate cap does not apply), and a fully cloaked spy casts none. The port added 0. `EntityModelSet.Instances`
  records each entity's bias from the blend it draws with, before the cloak's early return, and keeps it as the shadow
  keeps it. *Interpolated:* the engine's `UpdateShadow` reads the bias a frame late ("off by a frame", `:3100`); here
  both reads are this frame's.
- **Corpses** cast nothing (a ragdoll's bounds are its physics').
- **The caster's LOD, a divergence** (a research agent's read, 2026-11-08, disassembly and shipped data).
  `engine.dll` `FUN_1800eba80` makes `r_shadowlod` (default −1) and `r_shadowlodbias` (2). It draws the LAST LOD for a
  model whose header has `STUDIOHDR_FLAGS_HASSHADOWLOD` (`0x40`, `studio.h:2047`; at `0x1800ebbb6`) or under
  `r_shadowlod −2`. Under any other negative value it draws the second-to-last, floored at 0 (`0x1800ebc68` →
  `0x1800ebc75`), never below the root LOD. All nine player models carry the flag and seven LODs, so a player's shadow
  is LOD 6: 1421 triangles for the scout against 7814 at LOD 0, summed over every bodygroup. **Fixed**: the loader
  reads that level (`StudioTriangles.ShadowLod`, `Read(…, lod)`) over the same `.vvd` vertices — `Studio_LoadVertexes`
  (`studio.h:3249-3339`) builds one array at the root LOD that every lower level indexes — and packs its corners beside
  the model under `#shadowlod`, frame for frame; the shadow pass draws that. Held by `StudioShadowLodConformanceTests`
  (this reader's counts equal the agent's parser's), `ShadowLodPackingTests`, the scout through the production loader
  (`MapAssetsTests`) and an atlas test whose shadow LOD lies outside the cell. Not read: whether worn items carry a
  shadow LOD (any without one now draws its second-to-last).
- **`$translucent_material`, a divergence** (same read). `studiorender.dll` `FUN_1800041c0` under `OVERRIDE_BUILD_SHADOWS`
  sets each mesh's material `AlphaModulate( 1 )`, binds it so its proxies run, and sets `$translucent_material` to it
  when `IsTranslucent()` OR `IsAlphaTested()`, else NULL (`0x1800042cb`–`0x18000432f`). So an alpha-tested material adds
  its own texture alpha into the cell, continuous, with no threshold; anything else binds the fullbright lightmap. The
  first port took a blended material's alpha and drew an alpha-tested one solid — a guess, half wrong. **Fixed**:
  `WorldRenderer._alphaTested` (`$alphatest`) joins the blended set (`ShadowRenderConformanceTests`, a cut-out at alpha
  128 covering 128/255 of the floor).
- **Cost, not picture**: every cell is redrawn every frame (a clean one would draw the same), and each visible shadow's
  surfaces are linked and cut again every frame, where the engine links them when it re-projects and caches the cut
  polygons (`SHADOW_CACHE_VERTS`, `:1856`). The projection itself now follows the engine's schedule. And every model with more than two LODs
  packs and uploads its shadow LOD, static props' included, though a static-prop placement never casts: a
  `$staticprop`-compiled model can still be an entity's (`prop_dynamic`, `prop_physics`) and cast, and a model path is
  loaded once whichever uses it first, so skipping by the flag (a Fable review's suggestion, declined) would lose those
  entities' shadow LOD. Not measured.

**Not read yet:** the setter of surface bit `0x10`, the `engine/shadowbuild` bind (`FUN_1800f6f80`), `SetFalloffBias`'s
callers, what `TEXTURE_LIGHTMAP_FULLBRIGHT` holds, and the render alpha the engine sets before a caster's draw (its
vertex alpha).
