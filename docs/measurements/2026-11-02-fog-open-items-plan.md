# The fog-colour work's four open items (plan, 2026-11-02)

Branch `fix/fog-open-items`, from `e82e081d` (the B653 review merge). RISKS B653, B637. Owner: *"can we settle
those?"* The four items B653's review left open, and the six the Opus review of that batch handed over for the same
files. Reads are `shaderapidx9.dll` (`D:\ghidra-proj\tf2shaderapi`), `materialsystem.dll` (`tf2materialsystem`) and
`client.dll` x64 (`tf2clientx64`), read-only, addresses only.

## 1. The clear and the sRGB-write state

**Question.** D3D9 applies `D3DRS_SRGBWRITEENABLE` to `Clear`; D3D11's `ClearRenderTargetView` on an sRGB view takes
LINEAR values. Ours clears the sRGB view with `SrgbDecode( byte )`, so the target stores the byte: right only if
shaderapi clears with sRGB writes OFF.

**Reads, and what each decides.**
- shaderapi's `ClearBuffers`: whether it touches `D3DRS_SRGBWRITEENABLE` (194, `0xc2`) around `IDirect3DDevice9::Clear`
  (vtable `+0x158`). Off: the byte is stored, ours stands. Left as the last draw set it: the stored value depends on
  the previous material, and the clear stores `encode( byte/255 )`.
- The clear colour's path from `ClearColor4ub` to `Clear`: any conversion of the bytes (HDR, linear framebuffer).
- `SetClearColorToFogColor` (`viewrender.cpp:754-768`) in `client.dll`: does `ucFogColor[i] *= scale` clamp or wrap
  above 255 (review item 1); and the byte `GetFogColor` returns: rounded or truncated when `FogColor3f(v)` stored it
  (`materialsystem.dll`), which decides `WaterClearColour`'s `MathF.Round`.
- Every clear in `viewrender.cpp` that takes a fog colour, and whether it scales: `SetClearColorToFogColor` (`:1423`,
  `:6061`, `:6097`) scales; `GetFogColor` straight into `ClearColor4ub` (`:5780`, `:6178`, `:6228`) does not. Ours scales
  every water target's fog clear (`WorldRenderer.Water.cs:792`), and clears no back-buffer view to fog.

**Test.** `WaterClearConformanceTests` (Rendering): clear each target type we clear (an sRGB view, the B641 gamma twin,
the water targets) with byte X at scale s through `WaterClearColour`, read back the stored byte; expected is what the
read says Valve stores.

## 2. A real frame with water

**Question.** Does the B653 water change (water tints through `GammaToLinear`, height fog through the sRGB decode,
clear colour) move a real frame toward TF2? Held only by unit tests so far.

**Method.** A map with water from the installed maps (water material in the BSP or pakfile), a camera from the data
(`spec_goto` coordinates read from the map's water volume), `+mat_force_tonemap_scale` pinned on both sides. Ours
before (a `git archive` of `38a984c5`, built in a separate directory) and after (this branch). TF2 by the live-pair
method of `docs/measurements/2026-10-31-explosive-decals-vs-tf2.md`. Report the water surface, an underwater and a
shoreline pixel for all three.

**Decides:** whether ours-after is nearer TF2 than ours-before at each of the three pixels.

## 3. `r_shader_srgb`

**Question.** Height fog takes `pow 2.2` at `r_shader_srgb 1` and the sRGB decode at 0 (B653). Every other shaderapi
site that branches on it, and whether the viewer stores arbitrary cvars.

**Reads.** Every reader of the ConVar (its registration, then each access to its parent's `m_nValue`), and every caller
of the hardware-config query that wraps it.

**Decides:** which sites change the frame, and so what the viewer must model. **Test:** one per value (0, 1, -1) on the
height-fog colour constant the water view uploads.

## 4. The LDR side

**Question.** What `0x180025a50` (the c29 upload) and the clear do under `HDR_TYPE_NONE`, and the range branch's gate.

**Reads.** The upload's branches on `GetHDRType` (`+0x1b0`), its outer gate, the range gate's ConVar by name, and the
clear's (item 1) HDR dependence.

**Decides:** whether LDR changes the colour curve, the tone-map scale, or the clear. Ours honours `mat_hdr_level` (B537);
**test:** the LDR path's fog constant and clear at `HdrType.None`.

## Results (2026-11-02)

### 1. The clear (*disassembly*, settled)

| read | address | what it does |
|---|---|---|
| `ClearBuffers` | `shaderapidx9.dll` `0x180014bd0` | if colour is cleared and the current shadow state (`+0x337c`, stride `0x20c`, byte `+0x209` bit 2) has sRGB write, `SetRenderState( 0xc2, 0 )` (device vtable `+0x1c8`), then `Clear` (`+0x158`) with `0x18001aee0( clear colour )`, then re-applies the shadow sRGB state through `0x180031db0` |
| clear colour conversion | `0x18001aee0` | only when byte `+0x3860` is set: each channel through mathlib's `GammaToLinear` table (`0x18003dd70`). No instruction writes `+0x3860` by displacement from the base or from base+`0x388`, and the constructor `0x180011f00` does not touch it; writes through a computed address are not excluded (*interpolated*: unset on PC, an Xbox-era linear-framebuffer flag) |
| `ClearColor3ub`, `ClearColor4ub` | `0x180014f60`, `0x180014f90` | pack the bytes into a `D3DCOLOR`, no conversion |
| `ClearBuffersObeyStencil` | `0x180014e20` | draws a quad through the shader util with the same colour conversion; no TF2 water view asks for it |
| `SetClearColorToFogColor` | `client.dll` x64 `0x18034d560` | `GetFogColor` (`+0x1a0`), and when `GetHDRType()` (`+0x1b0`) is 1: `LinearToGammaFullRange` (`0x18085bcf0`: `pow` in double, exponent `0x3FDD1745C6E043B8` = `1.0/(double)2.2f`), each byte `CVTDQ2PS`, `MULSS`, `CVTTSS2SI`, low byte stored (wraps); `ClearColor4ub( …, 255 )` (`+0x248`). Found as the call in `CIntersectionView::Draw` (`0x180342d20`) |
| fog colour store | `materialsystem.dll` `0x18002bed0`/`0x18002bf40` | `FogColor3f`/`FogColor3fv` of the queued context (vtable `0x1800b6da8`, `0x1800b6db0`, `FogColor3ub` at `+0x10`): `clamp( (int)( x · 255.0f ), 0, 255 )`, truncated |
| fog bytes into c29 | `shaderapidx9.dll` `0x180014200` | by shader fog mode: 1 and 3 `0x80`, 2 zero, 4 the scene bytes (`SceneFogColor3ub` `0x180020ef0`), else `0xFF`; floats = byte · 1/255, then the c29 upload |

**Per target** (each clear in `viewrender.cpp` that takes a fog colour): the above-water refraction view (`:6061`) and
the intersection view (`:6097`, which clears no colour) scale; the eye-in-volume simple view (`:5780`), the under-water
main view (`:6178`, depth only, so no colour clear) and the under-water refraction view (`:6228`) do not. The main
view's only fog clear is the radial-vis one (`:1423`), not modelled (B655). The sky view (`CSkyboxView::Setup`,
`:4977`) takes the caller's flags with the black left by the previous views' `ClearColor4ub( 0, 0, 0, 255 )`; black is
0 under either sRGB state. **Ours before:** rounded the byte, clamped above 255, scaled the under-water refraction
clear, took the reflection-quartered scale had a reflection view cleared to fog, and never cleared the simple view. All
fixed; the B641 gamma twins are never cleared (every clear is on the sRGB view) and read the same texels.

**Tests** (red by sabotage, each restored by inverse edit): `WaterClearConformanceTests` clears the offscreen sRGB view
with every byte 0-255 and reads it back raw (control: 128 handed as linear stores 188), plus scaled bytes at 0.6 and 4;
`WaterColourConformanceTests` holds the wrap (200 at 4 is 119, 190 at 2 is 4), the truncation (0.5 is 127), which views
scale, and the simple view's clear.

### 2. A real frame with water (*measured*)

`ctf_2fort` (water `materials/water/water_2fort.vmt`: refracting, `$fogcolor {51 43 13}`, fog -100..400). TF2: listen
server, `+mat_hdr_level 2` before the map, `sv_cheats 1`, `jointeam spectator`, `spec_mode 7`, `mat_force_tonemap_scale
1`, `mat_disable_bloom 1`, `cl_drawhud 0`, `record fogwater2fort`; read back after: `mat_hdr_level 0` (the owner's),
`cl_drawhud 1`, `mat_force_tonemap_scale 0.0`, `mat_disable_bloom 0`, `sv_cheats` restored. `r_shader_srgb 0`,
`mat_colorcorrection 0`, `mat_dxlevel 95`. Ours: the recorded demo, tick 400, `TF2VIEW_CAMERA` the same three
`spec_goto` values, `TF2VIEW_VIEWPORT_SIZE 1280x720`, `+mat_hdr_level 2 +mat_force_tonemap_scale 1 +mat_disable_bloom 1
+cl_drawhud 0`, Debug `--shot`. Cameras: A `-294 29 -127 30 0` (the water cubemap's origin, looking down), B
`-294 29 -200 10 0` (under the surface), C `-294 29 -90 35 90` (the shore). Builds: `38a984c5` (before the B653 review's
water changes) and `e82e081d` (after), each from `git archive` into a scratch folder, and this branch. Images:
`D:/measure-f12/fog-open/{tf2,before,mid,mid2,after}/`.

| camera, box (20x20 mean) | TF2 | 38a984c5 | e82e081d | this branch |
|---|---|---|---|---|
| A surface near (600,560) | 41.1 30.6 9.8 | 60.0 60.6 44.0 | 61.2 60.6 44.0 | 61.2 60.6 44.0 |
| A surface mid (400,380) | 86.6 64.7 38.3 | 106.9 90.3 59.5 | 111.5 90.3 60.4 | 111.5 90.3 60.4 |
| B far fog, under water (560,280) | 56.9 71.1 67.8 | 79.7 84.0 66.7 | 79.7 84.0 66.7 | 79.7 84.0 66.7 |
| B floor near (400,620) | 33.1 35.1 31.5 | 46.6 39.9 28.6 | 46.6 39.9 28.6 | 46.6 39.9 28.6 |
| C water at the shore (600,420) | 42.6 32.0 11.7 | 63.0 64.7 47.0 | 64.3 64.7 47.8 | 64.3 64.7 47.8 |

- **Control:** `e82e081d` shot twice: 0 px differ on all three cameras.
- `38a984c5` against `e82e081d`: 663,973 / 207,650 / 623,795 px changed, at most 7 / 8 / 6 levels; the surface's red
  went UP by 1-5 levels, away from TF2.
- This branch against `e82e081d`: 1 / 3 / 0 px, 1 level. Byte-derived colours quantise to themselves, and at a pinned
  scale of 1 the clear's scale changes cannot show.
- **Verdict:** inconclusive for the B653 water change. Ours is 20-50 levels off TF2 on every water pixel, grey-green
  where TF2 is brown, for a reason that is not these changes (B657). The live pair cannot judge a 1-7 level change under
  that. The water's normal map animates (TF2 at its own time, ours at tick 400), so the boxes are compared, not pixels.

### 3. `r_shader_srgb` (*disassembly*)

The ConVar is registered at `0x180001120` (`0x1800a16e0`, default "0", help "-1 = use hardware caps. 0 = use hardware
srgb. 1 = use shader srgb(software lookup)"). Every reader of its parent pointer (`0x1800a1718`):
`0x180004bc0` (hardware-config `+0x1f0`, called from `0x180019970`, `0x180025a50`, `0x180031db0`) and `0x1800057c0`
(`+0x1f8`, no caller in shaderapi). What each does: B656. **The viewer has no generic cvar store**: each cvar is a typed
`ViewerSettings` property with a declaration in `EngineConVars`. `r_shader_srgb` is now one, read from a config or
`+r_shader_srgb`, handed through `IViewerDevice` to `WorldRenderer.ShaderSrgb`. Tests: `FogConstantsTests`
(`HeightFogChannel` at 0, 1 and -1; `PackHeightFog` at 0 and 1), `ViewerSettingsTests` (the config at "", 1, -1),
`SettingsPresenterTests` (the assignment).

### 4. LDR (*disassembly*)

`0x180025a50`: an outer gate (hardware-config `+0x4f8`, the DX level, above 89), the range gate (caps `+0x1c0`, which is
`+0x5bc` bit 7 unless shaderapi's config `+0x30` asks for DX level below 90, and `r_pixelfog`, ConVar `0x1800a2340`,
default "1"), the curves, and the tone-map multiply only when `GetHDRType()` is 1. No other HDR branch. c29.w is
1 / `+0x2044` (not traced here). The clear (item 1) has no HDR branch in shaderapi; the client scales only under integer
HDR. Lit shaders set sRGB write unconditionally (`lightmappedgeneric_dx9_helper.cpp:614`). **Ours:** `mat_hdr_level`
0/1 is honoured (B537); `LinearLightScale` is 1 outside integer HDR (`Device3D`), so the shader's fog scale is 1; the
clear's scale is now gated on `HdrType` itself (`WaterClearColour( …, scaled )`, its test the `scaled: false` case).

### What is still unread

- `+0x3860` in `0x18001aee0` has no writer found by displacement; a computed-address writer is not excluded.
- The caps bits `+0x5c0` bit 6 (`r_shader_srgb -1`) and `+0x5bc` bit 7 (the range gate) are unnamed.
- `0x1800057c0`'s consumers outside shaderapi; what c29.w's `+0x2044` is.
- B657's cause.

## Review items folded in (Opus review of `e82e081d`)

1. Clamp or wrap in `WaterClearColour`, round or truncate the stored byte: item 1's reads.
2. The stale comments at `WorldRenderer.Water.cs:790` and `WaterColourConformanceTests`: reworded from item 1.
3. The clear colour's scale must be the frame's, not the reflection view's quartered one.
4. A test that `c16` follows `HdrType` both ways; a wiring test of `LinearLightScale` into `SetRefract`.
5. `FogConstantsTests`' constant tests that cannot fail against the pre-change code; `RefractModelRenderTests`' trivial
   control.
6. `ModelDecals`, `MomentSceneTests`, `DecalEffectsTests`: B642's files, listed for the lead, not edited here.

**Done:** 1 (wrap, truncation; read), 2 (comments reworded from the reads), 3 (the clear takes the saved frame scale), 4a
(`ReflectTintConstant`, both HDR types, sabotage-checked), 5 (bit-exact `SrgbDecode` cases; the refract control is the
same fogged draw at scale 1, which must store 255). **Not done, 4b:** a test of `Device3D` handing
`_world.LinearLightScale` to `SetRefract` needs a window-backed `Device3D`, which no test builds; open for the lead.
