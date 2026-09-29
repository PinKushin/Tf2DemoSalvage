# An unbaked static prop is lit once, not every frame

B426 moved every static prop onto the model draw, and filed an unbaked prop (no `.vhv`) as lit **every frame** from its
lighting handle's cube and local lights. That was a reading of the draw (`engine.dll` `0x1800f1bd0`) and not of what
happens before it. The prop almost never reaches that path. B429.

## What the engine does (evidence: read from `engine.dll`, the table from published source)

**The colour mesh is decided by the model, not by the `.vhv`.** `FUN_1800eac60(mgr, propIndex)` builds a static prop's
colour mesh when the hardware check passes (hardware config vtable `+0x148`, `SupportsColorOnSecondStream` counted from
`imaterialsystemhardwareconfig.h`) and the model's `studiohdr_t.flags & 0x10` (`STUDIOHDR_FLAGS_STATIC_PROP`) is set.
A model without it returns with no colour mesh, and that is the only case lit per frame; `CStaticProp::Init`
(`0x1802052c0`) warns about it — "used as a static prop, but not compiled as a static prop".

**The builder, `FUN_1800f36e0`, has two sources.** A prop with static lighting (`+0x12e & 2`) whose `.vhv` loads
(`FUN_1800f0940`) takes the file. Otherwise it lights every vertex itself:

- the lighting is the handle's, `FUN_1801ba590(handle, 0, flags)` with flags **1** when hardware config `+0x150`
  (`SupportsStaticPlusDynamicLighting`) is true — every DX9 part — and 5 otherwise; with no handle, the light cache at
  the model's illumination point (`FUN_1801b9cd0`);
- per mesh, `FUN_1800ee4a0` transforms the vertex position and normal by `AngleMatrix(GetRenderAngles, GetRenderOrigin)`
  (`FUN_180276390` — **no scale**), then calls `IStudioRender::ComputeLighting` (vtable `+0x128`), or
  `ComputeLightingConstDirectional` (`+0x130`) with `constdirectionallightdot / 255` when the model has
  `STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT` (`0x2000`, `studio.h:2073`, the byte at `studiohdr_t + 0x178`);
- each channel becomes `round( lineartovertex[ clamp( round( light · 1024 ), 0, 4095 ) ] · 255 )`, written as RGB with
  alpha `0xFF`. The clamp is an unsigned compare, so a negative light is 0.

`lineartovertex` is `BuildGammaTable` (`0x180279a40`), called as `(2.2, 2.2, 0, 2)` from `0x1800d7890`, `0x1800d5e70`
and `0x18012ecf0`. The loop is `mathlib/color_conversion.cpp:248`: `min( 1, pow( i / 1024, 1 / 2.2 ) · 0.5 )`, the half
being `overbright == 2`. So the byte is the same kind of byte vrad writes into a `.vhv` — gamma space, half the light —
and the vertex-lit shader doubles it the same way.

Two debug branches sit in the builder and are not ported: `DAT_18069c448` paints random colours, and
`DAT_18069c4d8 == 2` paints a colour by the prop's lighting flags.

## What "static state" holds, and why the rebake changes nothing

Flags 1 read the handle's `+0x18` state, built once by `ComputeStaticLightingForCacheEntry` (`FUN_1801b8270`, named by
its VProf counter): the leaf cube at the handle's own origin (`FUN_1801bb4b0`) and `FUN_1801b5a50`'s lights. That
function adds a world light (`FUN_1801b60a0`) **only when its style is 0**; a styled light that reaches the prop only
sets its style's bit in the handle's mask. Bit 4 of the flags (`FUN_1801b5400`) is what would add the styled list with
current values, and flags 1 do not ask for it.

B424 found the draw's one-frame-style check `FUN_1801bb8b0`, which calls `FUN_1800f36e0` again — a rebake — when a
listed single-letter style changes. For a `.vhv` prop the rebake reloads the same file. For a CPU-lit prop it relights
from the same static state, which holds no styled light. **Both reproduce the colours they replace**, so the port builds
the colour mesh once and never rebuilds it. On flags-5 hardware the rebake would matter; no hardware this viewer runs on
is that.

## What the port had, and what it does now

The port lit an unbaked prop per frame with the light-cache cube, the four strongest lights **with their current style
values**, and the sun — the model path. Now `EntityModelSet` builds the colour mesh at the first draw for a static prop
whose model is compiled static (`StaticPropVertexLighting`), from `LevelLighting.StaticPropLightingAt` (the cube at the
lighting origin itself rather than a cache cell, style-0 lights only, the sun), through the existing
`StudioPointLighting.At` (the B415 port of `ComputeLighting`), and draws it like a `.vhv` one: no cube, no locals.

**Measured 2026-09-28 on `koth_harvest_final`:** all eight unbaked placements are compiled static (flags `0x11`,
`box_cluster01`, `box_cluster02`, `tractor_tire001`), none const-directional.

**Not ported:** `ComputeLightingConstDirectional`. Its body is in `studiorender.dll`, which was not read; a model with
`0x2000` stays on per-frame lighting.
