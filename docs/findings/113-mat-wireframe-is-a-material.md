# 113. `mat_wireframe` is a material, not a fill mode (B803, B808)

What TF2 draws under `mat_wireframe`, read from the x64 `engine.dll` installed 2026-10-07 (Ghidra project
`tf2enginex64-1007`; every address below is that build's, and TF2 updates weekly) and the debug VMTs the game ships.
B788 (findings [75](75-a-tf2-face-is-the-scene-not-the-wire.md)) had already settled the studio models. This is the
rest: the world, brush entities, displacements and decals, the values 2 and 3, and where the line fill comes from.

Evidence classes: **read** means disassembled or decompiled from the closed binary, or read from published source or a
shipped VMT; **measured** means a capture of the viewer; **interpolated** is flagged where it appears.

## The wrong model this replaced

From D75 until B803 the viewer drew `mat_wireframe` as a **fill mode**. Every rasteriser state had a wireframe twin, and
under the switch every pass took its twin. That drew every surface as its own material's edges, the world included.
D75's note on why each pass keeps its own twin still holds. The premise under it, that wireframe is a property of the
pass, does not.

## The engine binds debug materials

`FUN_1800f6f80` loads the engine's debug materials into globals (*read*):

| global | material | shipped VMT (`platform_misc`, read with the `vmt` probe) |
|---|---|---|
| `0x18069b3c8` | `debug/debugwireframe` | `Wireframe`, `$color [1 1 1]`, `$ignorez 0`, `$decal 1` |
| `0x18069b3e0` | `debug/debugworldwireframe` | `Wireframe`, `$color [0.90 0.50 0.50]`, `$ignorez 1`, `$decal 1` |
| `0x18069b3e8` | `debug/debugworldwireframezbuffer` | `Wireframe`, `$color [0 1 .5]`, `$ignoreZ 0` |
| `0x18069b3f0` | `debug/debugbrushwireframe` | `Wireframe`, `[0 1 0]`. Loaded and released (`FUN_1800f8df0`), **never drawn**: no other reader |
| `0x18069b3f8` | `debug/debugdecalwireframe` | `Wireframe`, `$color [1 1 0]`, `$ignorez 1`, `$decal 1` |
| `0x18069b410` | `debug/debugluxels` | (`mat_luxels`, not wireframe) |

**A correction to the brief this read started from:** it said the whole load was skipped below a dxlevel threshold. The
`CMP EAX,0x5a` at `0x1800f71bb` comes *after* these loads. It skips only the eight render-target materials that follow
(`0x18069b478`-`0x18069b4b0`).

## The flag: `sv_cheats && mat_wireframe`

`FUN_1800e1e00` sets the surface-debug flags once per view (*read*). The wireframe flag `DAT_18069d9c0` is set when
`sv_cheats` and `mat_wireframe` are both non-zero, or when `r_drawworld` is 2 (`DAT_18069d398`; the ConVar's
constructor at `FUN_1800066b0` names it). `DAT_18069d9c5` is set when any surface debug view is on. `R_DrawBrushModel`
(`FUN_1800e05d0`) also forces the flag on for `r_drawbrushmodels 2` (`FUN_180007c50` names that ConVar). Both are
cheats (flag `0x4000`), and neither is ported: filed as B817.

## World and brush surfaces: drawn solid, then outlined

`Shader_DrawChains` (`FUN_1800e39d0`, which names `gl_rsurf.cpp`) draws every chain with its surfaces' own materials,
static or dynamic. Then, if any debug view is on, it hands each chain's surface list to `FUN_1800dc720` (*read*). So do
the brush-model paths (`FUN_1800dcb20`, `FUN_1800ddca0` and `FUN_1800e0920`, all reached from `FUN_1800e05d0`) and the
translucent-surface path (`FUN_1800e61d0`, gated by `r_drawtranslucentworld`, constructed at `FUN_180006670`).

`FUN_1800dc720`, with the wireframe flag set:

```
mode = sv_cheats ? mat_wireframe : 0        // the int at ConVar+0x58, via m_pParent at +0x38
if mode == 3:  FUN_1800e6780(list)          // face outlines, below
else:          bind (mode == 2 ? 0x18069b3e8 : 0x18069b3e0)
               FUN_1800e59b0 each surface   // redraw it with that material
```

So **under `mat_wireframe` every world and brush surface draws solid with its own material, and then a second time with
the debug material.** That material's shader sets the line fill (next section). The outline is pink and drawn through
everything (`$ignorez 1`) for 1, or for any value other than 0, 2 and 3. It is green and depth-tested for 2.

## The fill is the material's flag (B808)

`Wireframe_DX9` sets `MATERIAL_VAR_WIREFRAME` (`wireframe_dx9.cpp:37`, *read*). Three reads say nothing else draws a
line fill under `mat_wireframe`:

1. `MaterialSystem_Config_t` (`materialsystem_config.h`, *read*) has no wireframe field. It has `bDrawFlat`,
   `bShowNormalMap` and their neighbours.
2. `shaderapidx9.dll` holds no string with "wire" in it (*read*, a string search). The control: the same search finds
   `PolyMode: invalid poly mode` (`0x180080618`). Its setter (`0x18002e490`) is the shadow state's, which a shader
   drives.
3. The engine draws `mat_wireframe` at a dozen sites by binding these debug materials. It would not need them if the
   material system forced the fill itself.

What was *interpolated* here, and is now *read* (review 1, below): shaderlib's `SetInitialShadowState`, compiled into
`stdshader_dx9.dll`, turns `MATERIAL_VAR_WIREFRAME` into `PolyMode( FRONT_AND_BACK, LINE )`. So **a model with a forced
material draws solid under `mat_wireframe`.** A VMT may also set the flag itself with `$wireframe 1`: of the 31,020 VMTs
in the install's archives none does, where the same search finds 3,113 declaring `$ignorez` (the `vmt-param` probe,
2026-11-23; a map's pakfile was not searched). A gold corpse is gold, filled. The studio render's own swap needs no forced material
(`0x1800041f2`, B788), so there is no line fill anywhere on that model.

## Displacements: the wireframe alone

`FUN_1800c2140` decides, per displacement, between drawing it now and queuing it for the batched draw (*read*). When the
surface-debug pass is on (`FUN_1800c1240`) and `sv_cheats && mat_wireframe` holds, it binds `debug/debugwireframe`
(`0x18069b3c8`) and draws the displacement's mesh (`FUN_1800c2ec0`). Then it returns without queuing the displacement's
own draw. **So a displacement draws only its white wireframe, with no solid under it.** This happens for every non-zero
value, and the value 2 does not change it. Its `$ignorez 0` makes it depth-tested.

## Decals: replaced, not outlined

When the decal batches are built (`FUN_1800c6210` at `0x1800c62fe`-`0x1800c631f` and `0x1800c6a21`; `FUN_18011aa60` at
`0x18011abf0`-`0x18011ac22` and `0x18011b07b`), each decal records `debugdecalwireframe` (`0x18069b3f8`) *in place of*
its own material when `sv_cheats && mat_wireframe` holds (*read*). `FUN_18011aa60` also takes it under a third ConVar
at `0x1806a5348` equal to 2, whose name was not read. A decal therefore draws as yellow edges through everything, and
its own material does not draw.

## What does NOT change under `mat_wireframe`

- **Overlays** (`info_overlay`): `COverlayMgr::RenderOverlays` (`FUN_180111830`, which names `Overlay.cpp`) binds
  `debugworldwireframe` only under `r_overlaywireframe` (`0x1806a4380`, its constructor at `FUN_180008720`).
- **The shadow manager's** two readers of `debugworldwireframe` (`FUN_180129bb0`, `FUN_18012a670`) are gated by
  `r_shadowwireframe` (`0x1806c5740`, `FUN_1800091c0`), not by `mat_wireframe`.
- `FUN_1800cdd70` binds the z-buffered material to draw a debug sphere. It is not a surface pass.

## The value 3: face outlines

`FUN_1800e6780` counts the edges of every surface with more than two vertices. It builds one line list
(`MATERIAL_LINES`, primitive type 1) of each face's own polygon edges, from the last vertex round to the first. It draws
that list with `debugworldwireframe` (*read*). Unlike 1 and 2, it never draws the triangulation's diagonals.

Re-read for the port (*read*, the same build). The count is the byte at surface `+3` (its vertex count), summed over
surfaces with three or more. The mesh is the render context's dynamic mesh with `0x18069b3e0` bound and primitive type 1.
For each surface it walks the world data's vertex-index table (`+0x108`) from the surface's first index (`+0xc`), and it
starts the previous vertex at the *last* one. So the loop closes, and every edge is a pair of positions (`+0x38`, twelve
bytes each). No colour, normal or texture coordinate is written, so all of that comes from the material, and so does the
depth (`$ignorez 1`: drawn through everything). The caller has already drawn the surfaces solid, as for 1 and 2.

**The faces are gone by the viewer's draw time**, because the batches hold triangles. So the two fan builders mark each
corner with whether its edge to the next corner of its triangle is the polygon's (`FaceFan`, `WorldVertex.FaceEdge`).
The upload turns the marks into a line-list index buffer, two indices per vertex, so any run is the same run of lines at
twice the offset. A diagonal's slot holds a zero-length line on its own corner, which lies on the loop anyway. Under 3
each queued world or brush-entity run draws as lines over it. A displacement keeps its triangles, since `FUN_1800c2140`
draws it whatever the value. Tests: `WorldWireframeConformanceTests` (the world square and the displacement under 3) and
`EyeShaderRenderTests` (a brush entity under 3) in Rendering, with the builders' marks in `MapWorldTests` and
`BrushModelsTests`, and `FaceFanTests` and the packing in Scene.

*Measured*, the frame below under 1 and then 3: pink-hued pixels went from 77,362 to 54,240. Looked at, the walls and
floors under 3 are outlined as whole polygons with no fan across them. Not compared against TF2.

**A wrong conclusion, kept:** this section said a line list is not culled, so a face seen from behind is outlined under 3
and not under 1, and called that the engine's (*interpolated*). Review 1 read the lists instead (below): no back face
reaches `FUN_1800e6780` but a `$nocull` world surface, so the engine outlines no other.

## Studio models under 2 and 3

`UpdateStudioRenderConfig` sets `bWireframe` from `sv_cheats && mat_wireframe` and never sets
`bDrawZBufferedWireframe` (B788, *read*). So every non-zero value draws `debugmrmwireframe`, cyan and drawn through
everything. **The value 2 depth-tests the world's outline and not the models'.** The capture below shows exactly that.

## Depth

`$ignorez 1` draws with no depth test and no depth write. A depth-tested debug wireframe has to pass at the depth of the
surface it outlines, or `mat_wireframe 2` would hide its own lines. The viewer gives these materials a `LessEqual` test
and no depth write. It is *interpolated* that this is the engine's default comparison, which shaderapi's shadow state
sets (closed, not read). Writing depth at a depth already written changes nothing. The no-write differs from the engine
only under the lines of a displacement or a translucent surface, which write no depth of their own here.

## Measured: the frame

`tf2demoview tf2-live-build11138802-spec-pov-cp_granary.dem --tick 1000 --first-person --shot` with `+sv_cheats 1` and
`+mat_wireframe 0`, then `1`, then `2`, 984×551, 2026-11-23. Pixels were counted by hue, because the frame's tone-map
scale moves every unsaturated channel:

| value | pink `[0.9 .5 .5]` | green `[0 1 .5]` | cyan `(0, 255, 255)` exact |
|---|---|---|---|
| 0 | 76 | 0 | 0 |
| 1 | 57,675 | 0 | 24,063 |
| 2 | 87 | 13,631 | 23,633 |

Looked at, value 1 shows the walls solid with pink edges through them. Value 2 shows green edges only where the surface
is in view, and the models are cyan through everything in both. **Not compared against TF2 itself:** no TF2 capture of
this frame was taken.

## Review 1: four more reads (2026-11-23)

The review of the port asked four questions only the binaries could answer. Same `engine.dll`; `studiorender.dll` and
`stdshader_dx9.dll` from the same install (both analysed in the same Ghidra project for this).

**Which surfaces reach the outline.** The world's are the walk's: `R_DrawLeaf` keeps a surface whose plane the eye is in
front of, or a NOCULL one (`VisibleWorld.Surfaces` ports it). A brush entity's are `FUN_1800dcb20`'s, which before any
draw tests every surface's plane against the eye in the model's own space, `dot − dist ≥ −0.01`, sets a bit for each
that fails, and leaves the flagged ones out of both the solid draw and the list it hands `FUN_1800dc720` (*read*). So no
back-facing brush-entity face is ever outlined, under any value. The viewer's lines are never culled by the rasteriser,
so each `*N` model now keeps its faces' planes (`FaceFan.Faces`) and its outline draws only the faces whose front the
eye sees (`FacePlane.FacesEye`, the engine's test and tolerance).

**Translucent displacements.** The translucent path `FUN_1800e61d0` builds each sort group's surface list, draws it, hands
it to `FUN_1800dc720`, and only then adds the group's displacements and passes them to `FUN_1800c73a0` with its last
argument 0. That asks `FUN_1800c1240` for the debug flag and calls the same `FUN_1800c2140` as the opaque pass, which
draws `debugwireframe` alone under `mat_wireframe` (*read*). So a translucent displacement is white edges with no solid
and no pink outline, as an opaque one is. The viewer had drawn it solid and outlined.

**The material's own render state.** `stdshader_dx9.dll` `0x180063340` is shaderlib's `SetInitialShadowState`: it reads
the material's flags word and calls `IShaderShadow` slots by bit (*read*; the slots in `ishadershadow.h`'s order):

| flag | bit | call |
|---|---|---|
| `MATERIAL_VAR_IGNOREZ` | 15 | `EnableDepthTest( false )`, `EnableDepthWrites( false )` |
| `MATERIAL_VAR_DECAL` | 16 | `EnablePolyOffset( SHADER_POLYOFFSET_DECAL )`, `EnableDepthWrites( false )` |
| `MATERIAL_VAR_NOCULL` | 13 | `EnableCulling( false )` |
| `MATERIAL_VAR_ZNEARER` | 10 | `DepthFunc( 1 )` |
| `MATERIAL_VAR_WIREFRAME` | 28 | `PolyMode( FRONT_AND_BACK, LINE )` |

So each debug wireframe material culls its back faces, takes the decal polygon offset if its VMT says `$decal 1`, and
nothing else. The viewer had drawn every queued run with the overlay pass's slope-only bias; each now takes its own.
**Not acted on here, and worth the lead's attention:** the `$decal` row says shaderlib adds the decal offset to *any*
material with `$decal 1`, whatever its shader. `DecalState.ConstantBias` rests on the opposite reading, that "a polygon
offset in Source is a property of the shader" and an overlay's `LightmappedGeneric` never asks for one.

**A model's own decals.** `studiorender.dll`'s decal draw `FUN_180007d20` swaps `debugmrmwireframe` in only under the
config's `bWireframeDecals` (byte `+0x25`, `0x20`), which `UpdateStudioRenderConfig` (`engine.dll` `FUN_1800f56c0`) sets
from `r_modelwireframedecal`, not `mat_wireframe` (*read*; B817). So a model's decals keep their own material under
`mat_wireframe`, which the viewer already did and a test now pins.

**What the outline costs** (*measured*, the `outline-memory` probe, 2026-11-23): `cp_process_final` 158,550 world
vertices, a 1.21 MiB index buffer; `cp_granary` 190,260 and 1.45 MiB. The `FaceEdge` mark grows `WorldVertex` from 124
bytes to 128, 0.6 and 0.8 MB on those lists while they are held.

## Tests

In `Tf2DemoSalvage.Rendering.Tests`:

- **`WorldWireframeConformanceTests`:** the face solid and outlined under 1; the z-buffered colour under 2; an edge
  behind a nearer face shown by 1 and hidden by 2; a displacement as its white wireframe alone; a decal replaced; a face
  whose own material is `Wireframe` in line fill without the switch.
- **`EyeShaderRenderTests`:** `…_UnderWireframe_OutlinesABrushEntityAndSwapsAStudioModel`, and
  `…_UnderWireframeWithAForcedMaterial_DrawsTheForcedOneSolid` for B808.
- **`OverrideMaterialLoadingTests.EngineWireframes_…`:** the four VMTs from a real install.
- **Review 1:** a brush entity's back face outlined by no value; a translucent displacement as white edges alone; each
  debug material's own offset (a sloped edge just behind a nearer face); a model decal keeping its material; the cap on
  the outline; a second run's outline offset; the outlines released on a second upload. Every one is listed, with the
  sabotage that reddened it, in RISKS B803 "Review 1".

In `Tf2DemoSalvage.Scene.Tests`: `FaceFanTests` (the faces' planes, the engine's back-face tolerance, a partial triangle
refused). In `Tf2DemoSalvage.Viewer3D.UiTests`: `CaptureUiTests.Capture_UnderMatWireframeOneAgainstZero_OutlinesTheWorldInPink`,
the output-level assertion on a corpus demo.

In `Tf2DemoSalvage.Presentation.Tests`: `ViewerSessionTests` checks that the value, not the tick, reaches the device
(`…MatWireframeTwoAfterOne_TellsTheDeviceTwo`).
