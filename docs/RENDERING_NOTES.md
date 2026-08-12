# Rendering notes — what TF2's assets need before D3D11 will draw them

Forward-looking, for Phases 2 and 3. **Nothing here is built.** It exists because
D10 chose D3D11 over DX9 and that choice is only honest if the actual porting
work is written down rather than waved at.

Same confidence tags as `SPEC.md`. **CONFIRMED** items were measured from
`koth_harvest_final.bsp` (TF2's shipped copy, read in place — see D9, we don't
bundle it).

The short version: none of this is hard *because* of D3D11. Almost all of it is
work you would do under DX9 too, because it comes from Source's 2004-era data
conventions rather than from any rendering API. D10's reasoning stands. But
"just point D3D11 at it" is not a thing, and the gap deserves to be visible.

---

## 1. Coordinate system and winding — the one that will definitely bite

**DOCUMENTED.** Source is **right-handed, Z-up**: +X forward, +Y left, +Z up.
Direct3D's convention is **left-handed, Y-up**. Every position, normal, and
camera transform needs converting.

The trap is not the conversion itself — it's what the conversion does to
triangle winding. Negating an axis mirrors the space, which reverses the
handedness of every triangle. Geometry then renders **inside-out**: you see the
back faces of the world and the front faces get culled.

Fix it in exactly one place and write down which:

- reverse index order per triangle, **or**
- flip the rasterizer state's cull mode / front-face winding.

Doing both silently cancels out and looks correct until something asymmetric
shows up. Doing neither gives you a world you can see through from inside.

Units: Source is nominally inches, 16 units to the foot. Not a correctness
problem, but it sets sane near/far planes and movement speeds.

## 2. Render geometry lives in FACES, not BRUSHES — **CONFIRMED**

`ROADMAP.md` §3 describes Phase 3.0 as flat-shaded *"BSP brushes only"* and
Phase 2 as a wireframe *"projected from BSP world brushes"*. That names the
collision data, not the visible surfaces.

Measured in `koth_harvest_final.bsp`:

| lump | records | what it is |
|---|---|---|
| FACES | 9,034 | the surfaces that are drawn |
| SURFEDGES | 78,674 | signed indices into EDGES, per face, in winding order |
| EDGES | 54,723 | vertex index pairs |
| VERTEXES | 18,551 | positions |
| BRUSHES | 3,110 | convex **collision** volumes |
| BRUSHSIDES | 20,635 | planes bounding those volumes |

The drawing path is FACES → SURFEDGES → EDGES → VERTEXES. A face is a polygon
fan; `firstedge`/`numedges` index into SURFEDGES, whose sign tells you which way
to read the edge.

Rendering *from brushes* is possible — intersect each brush's planes to recover a
convex polyhedron — and gives a blockier, deliberately simplified world. That is
a legitimate choice for a v0.1 look, but it must be a **choice**, not a
misreading of which lump is which. It is also more work than reading faces, and
brushes include tool volumes (nodraw, clip, trigger, skip) that must be filtered
or you will draw invisible walls and trigger boxes as solid geometry.

**Recommendation: draw FACES.** Less work, correct silhouette, and the tool-texture
filtering problem mostly goes away.

## 3. Displacements are separate geometry — **CONFIRMED**

`koth_harvest_final.bsp` contains **533 displacements** and 15,397 displacement
vertices (DISPINFO, DISP_VERTS).

Displacements are Source's terrain: a base quad face subdivided into a grid and
pushed along per-vertex offsets. A face with `dispinfo >= 0` **must not be drawn
as its flat polygon** — the flat quad is only the base. Ignore DISPINFO and every
hill, ramp and rocky slope in the map renders as a flat plate, with the world
visibly not matching where players walk.

This is real work — tessellation from the base face plus displacement vertex
offsets — and it is easy to not notice is missing until player positions float
above or sink into the ground.

## 4. Textures: format names changed, layout gotchas didn't — **DOCUMENTED**

Applies to Phase 3.x, not 3.0 (which is untextured).

- DXT1/DXT3/DXT5 are **BC1/BC2/BC3** in DXGI naming. D3D11 supports them natively;
  it is a rename, not a conversion.
- VTF also carries uncompressed and legacy formats (BGRA8888, BGR565, IA88, UV88,
  A8) that need real conversion to a DXGI format.
- **VTF stores mipmaps smallest-first.** Upload them in that order and every
  texture is a blurry mess. This one is famous for a reason.
- Block-compressed uploads want row pitch per *block row* (4×4 texels), not per
  texel row. Get it wrong and textures shear diagonally.
- **sRGB.** Source authored diffuse textures in gamma space. D3D11 wants an
  explicit `_SRGB` format so sampling linearises. Miss it and everything is
  washed out; apply it twice and everything is crushed dark.

## 5. There are no shaders to port — **DOCUMENTED**

VMT files reference Valve's *named* shaders (`LightmappedGeneric`,
`VertexLitGeneric`, `UnlitGeneric`, …) and parameters. **No shader bytecode
ships.** So there is nothing to translate from DX9 assembly or HLSL — the shaders
must be written from scratch in HLSL to approximate what those named shaders did.

That is worth stating plainly, because "port the shaders" is the natural
assumption and it does not apply. It also means D10's choice costs nothing here:
we would be writing shaders regardless, and DX9's fixed-function pipeline (which
does not exist in D3D11) would not have helped.

## 6. Lighting is baked, and there are two sets — **CONFIRMED**

`koth_harvest_final.bsp` carries **both** LDR and HDR lighting: LIGHTING and
LIGHTING_HDR are each 6,095,136 bytes, with FACES and FACES_HDR both at 9,034
records.

So a decision is required at load time about which set to use, and HDR luxels are
stored as RGBExp888 — three bytes plus a shared exponent — which needs decoding
before it can be sampled. Phase 3.x only; Phase 3.0 is flat-shaded and ignores
lighting entirely.

---

## What this means for the phases

- **Phase 3.0 (locked scope: capsules over flat-shaded world)** needs items 1, 2
  and 3 only. Coordinate/winding conversion, FACES-based geometry, and
  displacements. No textures, no shaders beyond a trivial flat-colour pass, no
  lighting. That is genuinely small.
- **Phase 3.x (fidelity)** is where 4, 5 and 6 land, alongside MDL/VVD/VTX
  skeletal animation — which D4 already flags as the format most likely to justify
  reaching for Source SDK headers.
- **Phase 2 (2D top-down)** needs item 1's transform and item 2's geometry, and
  should also read FACES rather than brushes. `ROADMAP.md` should be corrected on
  that point.

## 7. The overhead view is backface culling, not a filter we invented — **CONFIRMED**

The top-down view drops downward-facing faces, keeping floors, walls and the tops of anything
players stand on. That is not a stylistic rule: **it is what the engine already does**, evaluated
for one fixed camera direction instead of per frame.

The confirming observation is one anybody can make in game: noclip beneath a TF2 map and the world
goes transparent, leaving only some walls. Source draws a face from its front side only, so from
below, every floor's back is culled — and walls survive because their normals are horizontal, so
one side always faces the viewer whatever height they are at.

Two consequences worth having written down:

- **The result should look like the game**, not like an interpretation of it. The same faces
  survive that survive in a freecam looking straight down.
- **"Roofs players jump on" are handled by construction.** A hut is solid brushwork with a top
  face pointing up and an underside pointing down; the walkable top survives and the interior
  ceiling does not, because they are separate faces filtered independently. No special case, and
  nothing to tune.

**And it is an optimisation, not only a correctness rule.** A mid-sized TF2 map is around 9,000
faces and roughly half of them point away from an overhead camera. Culling once at load halves
the vertex buffer and means the GPU never transforms the hidden half — work that a fixed camera
would otherwise repeat every frame for a result that is thrown away every frame.

The general form, worth applying at whichever point knows the camera direction:

| View | Where the cull happens | Why |
|---|---|---|
| Top-down (fixed) | **CPU, once at load** | The camera direction never changes, so the answer never changes |
| Free 3D camera (later) | **GPU rasteriser state**, per frame | The direction changes constantly; precomputing would be wrong the moment it moves |

Doing it in both places for the same view is the trap `section 1` already warns about in a
different guise: two corrections that cancel, and geometry that looks right until something
asymmetric appears.

Implemented as `BspGeometry.OverheadFaces`.
