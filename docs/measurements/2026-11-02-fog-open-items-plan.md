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

## Review items folded in (Opus review of `e82e081d`)

1. Clamp or wrap in `WaterClearColour`, round or truncate the stored byte: item 1's reads.
2. The stale comments at `WorldRenderer.Water.cs:790` and `WaterColourConformanceTests`: reworded from item 1.
3. The clear colour's scale must be the frame's, not the reflection view's quartered one.
4. A test that `c16` follows `HdrType` both ways; a wiring test of `LinearLightScale` into `SetRefract`.
5. `FogConstantsTests`' constant tests that cannot fail against the pre-change code; `RefractModelRenderTests`' trivial
   control.
6. `ModelDecals`, `MomentSceneTests`, `DecalEffectsTests`: B642's files, listed for the lead, not edited here.
