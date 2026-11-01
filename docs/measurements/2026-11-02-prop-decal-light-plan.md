# Plan 2026-11-02: a lit studio decal takes its model's light, not the lightmap's white texel (B642)

Branch `fix/b642-decal-light`, from `56da2627`. Written after the engine read and before any production edit. Evidence
tags: **[bin]** read from the closed binary in Ghidra (addresses only; nothing decompiled is carried into the repo),
**[src]** read from published source, **[code]** read from ours, **[arith]**, **[interp]**.

## 1. The question B642 left open, and its answer [bin]

B642 read `studiorender.dll`. Its decal path lights a decal with the draw's ambient cube and local lights when
`DrawModelInfo_t::m_bStaticLighting` (`+0x40`) is set (`0x18000a880`). When the flag is clear it sets nothing, and the
current state stands. Unread was whether the engine sets that flag for a static prop, and what light it hands over.
`engine.dll` (x64, the `tf2enginex64` Ghidra project) answers both.

**The flag.** The static prop's lighting setup, `0x1800f1bd0`, writes `+0x40` once, at entry:
`m_bStaticLighting = hasColourMesh (*param_7) && hardwareConfig->SupportsStaticPlusDynamicLighting() (+0x150)`. On DX9
it is therefore set exactly when the prop draws with a colour mesh (a `.vhv`, or the CPU bake of findings 65), and clear
for a bump-mapped prop that refused one (B514).

**The light the decal gets, per-prop path** (`0x1800eb1a0` → `0x1800f1bd0` → studiorender `DrawModelStaticProp`,
vtable `+0xf0`). The draw info is built on `0x1800eb1a0`'s stack: `+0x10` is the decal handle, read from the static
prop record (`record + 0x10`, records `0x148` apart at `staticPropMgr + 0x18`). Inside `0x1800f1bd0`, when the flag is
set, the record holds decals (`record + 0x10 != 0`), and cvar `r_decalstaticprops` is nonzero:
- `m_vecAmbientCube` (`+0x44..+0x88`) = the draw's state cube (flags 6, or 7 under a dlight) **plus** the record's own
  cube at `record + 0x88`;
- `m_LocalLightDescs` (`+0x8c` count, `+0x90` descs) = the record's lights, then the draw's lights appended, a light
  already listed skipped, at most four (`0x1800f2120..0x1800f2232`, written by the second `FUN_1800f0e10` call);
- with no lighting handle the same fields are `FUN_1801b9cd0(origin, …, 0xf)`, the full state at the origin.

**`record + 0x88`** is filled by `0x1800eac60` from `FUN_1801ba590(handle, 0, 1)` (copied by `0x1800edac0`): bit 1 alone,
the static state, which is the leaf cube and the style-0 lights at the prop's lighting origin. It is gated on the same
cvar and on the prop having a lighting handle (`record + 0x130`).

**`r_decalstaticprops`** is registered at `0x1800069d0`, help text `"Decal static props test"`, default `"1"`
(`0x18035e7a4`).

**The light the decal gets, batched path** (`0x1800eb5e0`). For every prop holding decals the same sum is built into a
per-prop slot: the draw state's cube plus `record + 0x88`, lights unioned to four. There is no cvar test on this path.
Then, after every prop, per decal holder: `SetAmbientLightColors` (studiorender `+0x80`) with that slot,
`FUN_1800f0e10(…, 1)`, whose flag makes it call `SetLocalLights` (`+0x90`), and `DrawStaticPropDecals` (`+0xf8`) with
`m_bStaticLighting` 0 and no local lights in the draw info. studiorender's "the current state stands" is therefore the
decal light the engine has just set.

**So whichever path TF2 takes, a static prop's decal is lit by a cube plus up to four local lights**, and that light
is the prop's full lighting at its lighting origin: its draw state plus the static state that a colour-mesh prop's own
draw leaves out. It is never the white texel and never the colour mesh.

**A correction to findings 67** ("Not ported"): it read the `+0x44`/`+0x8c` writes under `DAT_18069c368` as "a second
combined state … for a record at `param_1 + 0x18`; its consumer is the second `FUN_1800f0e10` call, not the prop's own
lighting". The cvar is `r_decalstaticprops`, `param_8` is the `DrawModelInfo_t`, and the state is the DECAL's light.

### Settled and not settled

- **[bin], settled:** the flag rule; the cvar and its default; the batched path's state-then-draw order; the per-prop
  fill of `+0x44`/`+0x8c`; `FUN_1800f0e10`'s fifth argument selecting `SetLocalLights`.
- **[interp]:** that studiorender's `DrawModelStaticProp` (slot 30, `+0xf0`) draws the prop's decals and reads
  `+0x44`/`+0x8c` under the flag, as its slot-31 sibling `DrawStaticPropDecals` does (`0x180009bf0` → `0x18000a880`,
  B642). Only slot 31 has been read.
- **[interp]:** a dynamic entity's draw leaves `m_bStaticLighting` clear, so its decals draw under the model's own
  state, the cube and lights set for the model.
- **[interp], unsettled, does not block:** whether `record + 0x88` is ever filled for a bump-mapped prop.
  `0x1800eac60` does not test the bump flag, but it is reached through the colour-mesh path, which a bump-mapped prop
  never takes. If it were filled, the batched path would add the static cube twice for that prop's decals. Ours
  assumes it is not.

## 2. Ours [code]

- **A dynamic model's decals:** `Device3D.DrawOpaqueRenderable` (`managed/Tf2DemoSalvage.Render/Device3D.cs:4678`)
  calls `WorldRenderer.DrawModelDecals` (`WorldRenderer.cs:6755`), which sets the model constants with no cube
  (`:6805-6806`, "Unlit"). The shared pixel shader then takes the light from the lightmap atlas (`WorldRenderer.cs:1379`)
  at the white texel, unless the material is unlit (`combine.w`, `:1534`). DecalModulate is unlit and correct; a
  VertexLitGeneric decal (`decals/bigshot1model`, `break1model`, `bproof1model`, `beersplashmodel`) is lit by the
  texel.
- **A static prop's decals:** `DecalEffects` copies them out of the shared `ModelDecals` pool (static-prop keys,
  `DecalReplay.StaticPropKey`) into the world decal pass (`managed/Tf2DemoSalvage.Presentation/DecalEffects.cs:1168-1203`),
  which lights a lit decal the same way. That routing dates from B421, when static props were baked into the world
  vertex buffer. Since B426/D198 every static prop is a model draw, so the reason is gone.
- **The prop's own light:** `EntityModels.Instances.cs:358-387`. A colour-mesh prop carries the zero cube
  (`LevelLighting.NoStaticState`) and the flags-6 lights; a bump-mapped prop carries the full flags-7 state
  (`LevelLighting.StaticPropFullLightingAt`, `LevelLighting.cs:723`).

## 3. The change, minimal and structural

1. **The model decal draw takes a light.** `WorldRenderer.DrawModelDecals` gets the cube, sun and local lights, and hands
   them to `SetModel`. Nothing else in the decal path changes. A DecalModulate decal still ignores them, through
   `combine.w`.
2. **A dynamic model's decals get the model's own light** (`instance.Light`, `Sun`, `Locals`), in
   `Device3D.DrawOpaqueRenderable`.
3. **A static prop's decals move into the model pass**, drawn right after the prop by the same call, from the shared
   pool under `DecalReplay.StaticPropKey(entity - PropModels.FirstStaticPropEntityIndex)`. The world-pass copy in
   `DecalEffects` is deleted, so nothing draws twice.
4. **The light a static prop's decals draw with:** `ModelInstance.DecalLight`, set in `EntityModels` for a colour-mesh
   static prop to `StaticPropFullLightingAt` at its lighting origin, the flags-7 state. Null means "the instance's own
   light", which is already the full state for a bump-mapped prop and the model's light for an entity.
   `ponytail:` the engine sums the draw state (flags 6) with the record's static state (flags 1) and appends the
   record's lights first without re-ranking; ours ranks the static, styled and dynamic lights once. They differ only past
   four lights at one prop.
   *Superseded by the review batch:* computed only for a prop that holds a decal, and the field is a reference (RISKS
   B642). As first built:
   Computed for every colour-mesh prop, not only the decaled ones. The decal step runs after the model pass has built
   the instances (`FramePresenter.DrawOver`), so a predicate on the pool would leave a just-decaled prop, and a
   single-frame `--shot`, without its light for a frame. The two lookups it adds are cached (`StaticPropLightingAt` by
   point, the handle's ranking by stamp) and fall inside the existing `baked` counter.
5. **Fading static props:** a static prop that joins the translucent list (a screen fade below 255, B432) draws its
   decals after its translucent draw, so a fade does not drop the holes the world pass used to draw.

**Not changed:** water and sky views. The world decal pass drew static-prop decals in those views too, and the model
path in `DrawViewEntities` draws no model decals for anyone. After this change a static prop's holes are missing from a
water reflection, as an entity's already are. Filed in B642 as residue.

## 4. What the owner would see

A bullet hole (`decals/bigshot1model`) or blood decal on a static prop or a player takes the shade of the surface it
sits on. In a dark corner it darkens with the prop, where today it draws at the lightmap white texel's light. A scorch
or other DecalModulate decal does not change. How many real frames carry a lit studio decal is small: B421 counted 4
lit runs held on f12 at tick 40,000 and none at 20,000 or 60,000.

## 5. Tests, before the code

- **Rendering, conformance (synthetic):** a VertexLitGeneric decal quad facing the camera, drawn by
  `OffscreenTarget.DrawPosedModelDecals` under a cube whose `-Y` face is 0.25. Expected about 50 for a texel of 100 at
  tone-map scale 1 (sRGB of `0.1274 · 0.25`, [arith]). The same quad with its normal turned to `+X`, whose face is 1.0,
  is expected to draw 100. The control: a DecalModulate texel of 32 under the same dark cube still draws Valve's 47 over
  188.
- **Scene:** a colour-mesh static prop's instance carries `DecalLight` equal to `StaticPropFullLighting` at its
  lighting origin; an entity's carries none.
- **Wiring:** the static-prop decal leaves the world decal pass (`DecalEffects`), and the device draws it after its
  prop.
