# 18 — Decals

243 of them on `cp_process_final`, across 785 face references: signs, scorch marks, the arrows
painted on floors, the numbers on control points. All stored as *overlays* in lump 45 — quads
pinned to the faces underneath rather than geometry of their own, which is how a decal follows a
surface around a corner without the mapper building one.

## The struct, settled by arithmetic

Field order from `bsplib.cpp`'s byteswap descriptor. Summing it gives exactly 352 bytes, and the
lump's **decompressed** length divides by 352 exactly 243 times.

```
   0  nId                          int
   4  nTexInfo                     short
   6  m_nFaceCountAndRenderOrder   short
   8  aFaces[64]                   int      256
 264  flU[2]                       float      8
 272  flV[2]                       float      8
 280  vecUVPoints[4]               Vector    48
 328  vecOrigin                    Vector    12
 340  vecBasisNormal               Vector    12
```

The face count shares its sixteen bits with the render order — top two bits for the order. Reading
the whole field as a count gives tens of thousands for any overlay with a non-zero order.

## The corners' z components are not coordinates

This is the finding, and nothing about the struct hints at it. `vbsp`'s `Overlay_EmitOverlayFace`:

```c
// Encode the BasisU into the unused z component of the vecUVPoints 0, 1, 2
pOverlay->vecUVPoints[0].z = pMapOverlay->vecBasis[0].x;
pOverlay->vecUVPoints[1].z = pMapOverlay->vecBasis[0].y;
pOverlay->vecUVPoints[2].z = pMapOverlay->vecBasis[0].z;

// Encode whether or not the v axis should be flipped.
Vector vecCross = pMapOverlay->vecBasis[2].Cross( pMapOverlay->vecBasis[0] );
if ( vecCross.Dot( pMapOverlay->vecBasis[1] ) < 0.0f )
    pOverlay->vecUVPoints[3].z = 1.0f;
```

A `.vmf` carries three basis vectors — `BasisU`, `BasisV`, `BasisNormal` — and the lump has a field
for exactly one. So the compiler scatters `BasisU` across the unused third component of three
corners, and hides the handedness of `BasisV` in a fourth. `BasisV` is then
`cross(normal, u)`, negated when the flag is set.

**A reader treating the corners as three-dimensional points gets a quad standing on edge**, and one
that ignores the z values entirely never finds the basis at all.

**Verified by orthonormality, on every overlay in the map.** `BasisU` is assembled from bytes
belonging to three different corners, so a wrong offset anywhere gives a vector that is neither
unit length nor perpendicular to the normal. All 243 satisfy both, plus `U ⟂ V`. That is a property
of a real basis and of almost nothing else.

## An overlay's texinfo carries no mapping

Also from the emit, and also worth knowing before something projects through it:

```c
texInfo.lightmapVecsLuxelsPerWorldUnits[iVec][iAxis] = 0.0f;
texInfo.textureVecsTexelsPerWorldUnits[iVec][iAxis] = 0.0f;
...
texInfo.lightmapVecsLuxelsPerWorldUnits[iVec][3] = -99999.0f;
texInfo.textureVecsTexelsPerWorldUnits[iVec][3] = -99999.0f;
```

Every texture vector is zeroed and the last component set to -99999. The material still arrives
through `texdata`, but the texture coordinates come from `flU`, `flV` and the corners — an overlay
is mapped by its own quad, not by a world-to-texture projection. Anything treating an overlay's
texinfo the way it treats a face's gets nonsense out of it.

## Placement, which has no published source behind it

The encoding comes from `vbsp` and is settled. How the engine builds the quad and clips it lives in
`client.dll`, which was never released, so the placement is **inferred** — and the Rust `vbsp`
crate, the obvious differential, implements neither overlays nor cubemaps, so there is nothing to
cross-check against either.

Placing the corners is `origin + x·U + y·V`, and three measurements on `cp_process_final` say it is
right. Two of the three questions were badly posed first, and the numbers are what said so:

| Question | Result | What it meant |
|---|---|---|
| Do overlay and face normals agree, per pairing? | 491 of 785 | **Wrong question.** vbsp attaches an overlay to every face its box touches, so a decal on a doorframe names the frame, the wall beside it and the floor below. Only some share its orientation. |
| Does each overlay agree with at least one of its faces? | 157 of 243 | **Still wrong.** `BspSurface.Normal` is corrected for which side of its plane a face sits on; an overlay's normal is the mapper's. Antiparallel wherever those differ. |
| …ignoring the sign? | **242 of 243** | Right. And the last one names no face the surface reader kept, so there is nothing to compare it to. Every overlay that *can* be checked passes. |

Displacements were the other candidate for the gap and are ruled out by measurement: **0** of the
unaligned overlays touch one.

The other two placement measurements:

- **Median distance from the face plane: 0.00 units.** An overlay's origin lies exactly in the
  plane of the faces it is pinned to. A wrong origin offset lands hundreds of units out.
- **Smallest projected quad: 480 square units, none collapsed.** A swapped or zeroed basis axis
  gives a line or a point, which draws nothing — and draws nothing in a way indistinguishable from
  decals not being implemented.

## Clipping, and how much it matters

`vbsp` records the face list and never clips; the geometric clip is engine-side and closed. But the
face list *is* the clip specification, so the question is not how Valve does it but how much of
each quad falls outside. Sampled on a 12×12 grid per overlay, tested against the named faces'
polygons:

| | |
|---|---|
| Median share of a quad landing on a face it names | **100%** |
| Mean | 93.7% |
| Fully covered | 175 of 242 |
| Less than half covered | 7 |
| Worst | 25% |

**So drawing unclipped is defensible as a first pass** — roughly 6% of decal area would be painted
where it should not be, concentrated in a handful of overlays that wrap edges. Clipping is a
refinement, not a precondition.

**The measurement was wrong first, and the shape of the wrongness was the clue.** It reported a
median of 0% with a bimodal split — 56 overlays fully covered and 162 under half — which reads
exactly like a placement defect. It was the point-in-polygon test: it assumed a winding direction.
`BspSurface.Normal` is corrected for which side of its plane a face sits on, and the vertex order
is *not* corrected with it, so half the faces wind the other way relative to that normal. Requiring
the point to be consistently on one side, rather than on a particular side, is what convexity
actually gives.

Worth separating from the earlier placement checks, which did not cover this: they verified the
origin lies in the face plane and the normals align. Neither says anything about the quad's
**extent**, and a decal can satisfy both while being the wrong size.

## The depth offset is published

Not inferred after all — `materialsystem_config.h` carries the exact values:

```c
m_SlopeScaleDepthBias_Decal = -0.5f;
m_DepthBias_Decal = -262144;
```

Those map straight onto D3D11's rasteriser state: `DepthBias = -262144`,
`SlopeScaledDepthBias = -0.5f`. Against a 24-bit depth buffer, -262144 is a push of
262144 / 2^24 ≈ 1.6% toward the camera.

## Status

**Read, basis recovered, placement and extent verified, 2026-08-13.** Nothing is drawn yet, but
nothing is unknown either: the depth offset is Valve's published pair, and clipping is measured as
worth about 6% of decal area rather than being a precondition.

Evidence class: read from published source (`vbsp`), confirmed by measurement on the corpus.

---

## An overlay is a projection: clip the face to it, not it to the face

*(evidence class: interpolated — vbsp is published and read, the fragment builder is not)*

**The face list is complete and authoritative.** `Overlay_AddFaceToLists`
(`utils/vbsp/overlay.cpp:171`) adds a face because it came from a side the mapper assigned the
overlay to. No normal, no dot product, no angle — the only test is whether the face is already
listed:

```cpp
mapoverlay_t *pMapOverlay = &g_aMapOverlays.Element( pSide->aOverlayIds[iOverlayId] );
if ( pMapOverlay )
{
    if( pMapOverlay->aFaceList.Find( iFace ) == -1 )
    {
        pMapOverlay->aFaceList.AddToTail( iFace );
    }
}
```

This project filtered that list by orientation anyway, in two places, and refused 108 of
cp_process's 634 named faces — all of them on the red and blue wall stripes, 90 at roughly 45°,
which are chamfered corners a mapper picked deliberately.

**And the clipping ran the wrong way round.** Taking the overlay's quad, cutting it with the face's
edge planes and dropping the survivor onto the face's plane bounds every fragment by **BSP splits**
rather than by the band. A stripe of one height arrives as trapezoids of differing heights with gaps
between them, and on a face not parallel to the overlay each corner moves a different distance onto
the plane, skewing the piece as well.

Clipping the **face** against the prism swept from the quad's edges along the basis normal gives the
opposite, and gives three things without any correction step:

| property | why it follows |
|---|---|
| the fragment lies on the wall | it is a subset of the face |
| adjacent fragments tile | neighbours share edges, and the clip planes are the same for both |
| the band is one height everywhere | two of the four planes **are** the band's long edges |

Those are exactly what the live game shows: a uniform band running most of the way across the map,
wrapping corners, unbroken.

### What was verified as correct on the way, and is worth not re-checking

The reader matches `Overlay_EmitOverlayFace` field for field — BasisU packed into the unused `z` of
`vecUVPoints[0..2]`, the V flip in `[3].z`, the face count masked out of
`m_nFaceCountAndRenderOrder`. And the quads are the right size: cp_process's stripes measure
**640×64 along U:V against named faces spanning 640×288**, a U ratio of 1.00. Neither the parse nor
the coverage was ever wrong.

### Why this is interpolated rather than transcribed

`engine/overlay.cpp` builds the fragments and was never released. Searching source-sdk-2013 for the
lump by name — `doverlay`, `Overlay_`, `OVERLAY_BSP_FACE_COUNT` — finds vbsp and `bspfile.h` and
nothing else. The algorithm above is derived from what an overlay is, not read off Valve's. Flagged
per D44; a decompiler is the next step if it is ever found wanting.

---

## Where this project's overlay path differs from the engine's, item by item

*(evidence class: read from published source, except where marked)*

Compiled after B135 was chased through screenshots for an evening. **The list is the deliverable** —
each row is checkable, and the ones already fixed are kept so the next reader can see which were
wrong together.

| aspect | the engine | this project | |
|---|---|---|---|
| pass order | `DrawWorld` (surfaces **and** overlays), then `DrawOpaqueRenderables` (static props, brush models) — `viewrender.cpp:5487` | props were batched **with** the world, so they preceded the overlay pass | **fixed**, B135 |
| overlay cull mode | the material's, and `MATERIAL_CULLMODE_CCW` is the default — `imaterialsystem.h:180` | `CullMode.None`, copied from the world's both-sided state | **fixed**, B135 |
| depth writes on a marking | `EnableDepthWrites( false )` — `DecalModulate_dx9.cpp:66` | wrote depth, so an overlay occluded what was drawn afterwards | **fixed**, B135 |
| depth bias | `SHADER_POLYOFFSET_DECAL` → `m_DepthBias_Decal = -262144` — `materialsystem_config.h:223` | none. Valve's number is a **D3D9** value and the APIs disagree on what a bias is (D46, D48); our fragments are coplanar by construction since B134, so the intent needs no offset | differs **deliberately** |
| render order | four layers, `OVERLAY_RENDER_ORDER_NUM_BITS`, packed into `m_nFaceCountAndRenderOrder` and set by `SetRenderOrder` | batches keyed by order and material, emitted order-major — read from `COverlayMgr::RenderOverlays` in engine.dll (below) | **fixed**, B138 |
| fade distance | `doverlayfade_t` in `LUMP_OVERLAY_FADES` (60), per overlay while `r_overlayfadeenable` is 0 (engine.dll 0x18010a580) | read; faded per batch from the view origin | **fixed**, B455 |
| decal polygon offset | `ApplyZBias` (shaderapidx9 0x180014600) applies the **reciprocals**: 1/−262144 of the range, slope 1/−0.5 | DecalModulate takes −64 steps and −2 | **fixed**, B456 |
| order within a layer | material buckets prepended as the opaque material-sort walk queues each surface's (prepended) fragment list, per frame | the same, queued per view from the cull's own walk (`OverlayRenderLists`) | **fixed**, B457 |
| overlays on translucent surfaces | queued and drawn per surface inside `DrawTranslucentSurfaces` (0x1800e4fd0), with that surface | the same: drawn after their run in the translucent pass | **fixed**, B458 |
| displacement overlays | their own batch, all layers, before any brush overlay; leaf lists by displacement index; a culled one keeps its sort place (0x1800e3a90, 0x1800c61f0) | the same | **fixed**, B457 |
| overlays on brush entities | never drawn — `R_DrawBrushModel` never reaches the overlay manager | not drawn | matches, B457 |
| sort key's lightmap page | sort ID is {material, lightmap page} from materialsystem.dll's allocator | the allocator ported (`LightmapSortIds`); material order assumes a fresh process | **fixed**, B457 |
| fragment construction | `COverlayMgr::RenderOverlays`, `engine/Overlay.cpp` — not published | face clipped to the overlay's projected volume (B134) | **interpolated** |

### The two still open, and why they are worth doing

**Render order, as the engine actually draws it** *(evidence class: read from a decompilation)*.
`COverlayMgr::RenderOverlays` (engine.dll x64, 0x180110630, located by its VProf scope string
`"COverlayMgr::RenderOverlays"`) is NOT a sort. It is a loop over render orders wrapped around the
ordinary per-material fragment walk: the pass starts at 0, every fragment's overlay is asked for its
order, the largest is remembered, and a fragment draws only on the pass equal to its order — so the
whole map's material list is walked once per layer in use. The cost of four layers is four walks,
not a sort, which is why the engine can afford it every frame; ours pays it once at build by keying
the batches on `(order, material)`. Fixed 2026-10-02 (B138).

**The decal bias was a reciprocal all along** *(evidence class: read from a decompilation)*.
Every reading of `m_DepthBias_Decal = -262144` in this project — as D3D9 units, as `glPolygonOffset`
units via togl, as 1/64 of a D24 range — took the config value as the bias. `ApplyZBias` in
shaderapidx9.dll (0x180014600) never uses it that way: it divides one by each of the four config
terms before setting `D3DRS_DEPTHBIAS`/`D3DRS_SLOPESCALEDEPTHBIAS`. So Valve's decal offset is
2⁻¹⁸ of the range, 64 D24 steps, and the "1/64 of the range floats markings off the map" arithmetic
B70 rested on described a bias the engine never applied. The togl read was right about units and
wrong about the value, because togl receives the render state *after* the reciprocal. The lesson
for this project: a config struct's field is an input, and the number on the wire is whatever the
consumer makes of it — read the consumer (B456).

**The order within a layer is three lists deep, and the filing named one of them** *(evidence class:
read from disassembly, engine.dll x64)*. B457 was filed as "buckets prepended in the order the
visible-leaf walk first reaches them". Reading the producers before building found two more lists
between the walk and the buckets, and each one changes the answer:

1. **A surface's own fragment list is prepended at load.** `0x18010b1c0` builds overlays in lump
   order; the fragment writer `0x180111b30` links each new fragment before the surface's head
   (surface +0x14, `0x1801123a1`..`0x180112421`). Queue that list, prepend again into the bucket,
   and two overlays on ONE surface draw in lump order — the two reversals cancel. A port of the
   filing alone would have drawn them backwards.
2. **The walk does not queue overlays; the material sort does.** `R_DrawSurface` (`0x1800dfbb0`)
   appends each opaque surface to its material-sort chain (`0x1800d1140`), and `0x1800da3a0` —
   called per sort group from `0x1800e5e10` just before `RenderOverlays` — walks the sort IDs in
   first-reached order and each chain in order. So surfaces of one material are queued together
   wherever the walk found them. A translucent surface (flag 0x20) is on the other list and its
   overlays are queued and drawn per surface inside `DrawTranslucentSurfaces` (`0x1800e4fd0`) —
   a sibling still open (B458).
3. **Only then the buckets** (`0x18010a580`): a fragment faded past its maximum is never queued, so
   it cannot claim its bucket's position either.

The walk itself is `R_RecursiveWorldNode` (`0x1800e0600`): near child, then the node's surfaces that
a visible leaf has MARKED and whose plane-back flag equals the eye's side, then the far child;
`R_DrawLeaf` (`0x1800df9d0`) marks node surfaces and marks-and-draws the others it faces
(`dot − dist ≥ −0.01` on the unflipped plane). A node surface whose only marking leaf lies in the far
subtree is therefore not drawn at that node — ported as read, not repaired (B457).

**Displacements are a separate batch, drawn first — the second thing the first pass missed.**
Following the overlay manager's getter to every caller found a third queue: `Shader_DrawDispChain`
(`0x1800e3a90`) runs before the brush chains in each sort group, and its `0x1800c61f0` queues the
displacements' overlays and calls `RenderOverlays` itself. So a layer-1 overlay on terrain draws
before a layer-0 overlay on a wall — the render-order passes are per batch, not per frame. The same
search answered the brush-entity question by absence: nothing on `R_DrawBrushModel`'s three paths
reaches the overlay manager, so an overlay on a door is never drawn, and dropping those fragments
was already Valve's behaviour. *Evidence class: read from disassembly.*

**The sort key's lightmap page comes from a packer, and the material order from the process.**
The engine's surface sort ID is whatever materialsystem.dll's `AllocateLightmap` returns while the
engine feeds it every face in a fixed order (lit first, then by material, then by luxel area). A
material change closes every page but the last, so one material's faces can be split across two
sort IDs — which is what the overlay queue sees. The wavefront packer and the 1024×512 page are
fully deterministic and ported. The one input that is not: "by material" means by the material's
ENUMERATION ID, which the material dictionary assigns in-order over a tree keyed by the name's
`CUtlSymbol` — the order the whole PROCESS first interned each name. On a fresh launch a map's
materials are interned by the collision loader in texdata order, so that is the port; a session that
loaded another map first can order shared materials differently, and nothing in a demo records that.
*Evidence class: read from disassembly (engine.dll and materialsystem.dll); the texdata order is
interpolated for a fresh process.*

**Render order is not cosmetic where overlays overlap.** Valve gives every overlay one of four
layers and draws them in that order, which is how a sign on top of a stripe stays on top. This
project draws them in whatever order the material dictionary iterates — stable, arbitrary, and
correct only by luck. With depth writes off (as they now are) two overlapping overlays both draw and
blend, so the order decides the result.

**Fade is why distant signage does not shimmer in the game.** Not read here, so every overlay draws
at every distance. Lump 60 is a fixed-size record per overlay and the reader already walks lump 45
beside it.

### What this list is really evidence of

Every row above was found by reading Valve's source *after* the symptom appeared, and four of them
were wrong at once. They are also all of one kind — **not what the format says, but what the renderer
does with it**: an order, a cull mode, a write mask, a sort. `docs/CONFORMANCE.md` records the same
conclusion from the other end: no conformance suite here describes a frame, so none of these could
have been caught by a test.

### The divergence underneath all of them: state belongs to the material, not to the pass

*(evidence class: read from published source)*

**Valve's code would have survived the pass reorder. Ours did not, and that is the finding.**

Moving static props after the overlays broke them instantly: `DrawDecals` sets depth writes off —
correct for a marking — and the props pass ran next and inherited it, so props stopped writing depth
altogether. Two props no longer occluded each other and the rocks behind a shipping container drew
straight through it.

**In Source that cannot happen, because no pass inherits anything.** Every shader declares its own
render state in a `SHADOW_STATE` block, and the material system snapshots it and applies it when the
material is bound. `DecalModulate_dx9.cpp:62` is the plain example:

```cpp
SHADOW_STATE
{
    pShaderShadow->EnableAlphaTest( true );
    pShaderShadow->AlphaFunc( SHADER_ALPHAFUNC_GREATER, 0.0f );
    pShaderShadow->EnableDepthWrites( false );
    pShaderShadow->EnablePolyOffset( SHADER_POLYOFFSET_DECAL );
    ...
    pShaderShadow->EnableBlending( true );
```

An opaque material turns writes **on** as it binds; a decal material turns them **off**. Order is
therefore free: passes can be reordered, interleaved or sorted without any of them having to know
what ran before.

**This project sets state imperatively per pass** — `RSSetState`, `OMSetDepthStencilState` — which
makes every pass boundary a place where the next one must remember to re-establish what it needs.
B72 was this defect in the other direction: a translucent pass left a read-only depth state behind
and the model pass inherited it, so models drew with no depth writes and a medkit covered the medic
standing in front of it.

So the same fault has now been found twice, from opposite ends, and the local fix both times was "the
pass that depends on a state establishes it". **That rule is a workaround for not having
material-owned state**, and it will keep costing until the render state moves onto the material where
the engine keeps it.

Not attempted here: it is a real change to how materials are bound, and it wants its own decision
entry rather than being folded into a rendering fix.

---

## `$decal` is bit 16, read out of materialsystem.dll

*(evidence class: read from a decompilation — the first in this project)*

**The flag-name table is a plain `const char *` array indexed by BIT POSITION.** No interleaved
values, no pairs: the flag for a key is `1 << index`. Found by importing the live client's
`materialsystem.dll` into the existing Ghidra project and walking the references to each `"$..."`
string (`D:\ghidra-proj`, script `FindMaterialVarFlags.java`).

Base `0x101254c8`, stride 4:

| key | pointer | index | `imaterial.h` |
|---|---|---|---|
| `$additive` | `0x101254e4` | 7 | `MATERIAL_VAR_ADDITIVE = (1 << 7)` |
| `$alphatest` | `0x101254e8` | 8 | `MATERIAL_VAR_ALPHATEST = (1 << 8)` |
| `$decal` | `0x10125508` | **16** | `MATERIAL_VAR_DECAL = (1 << 16)` |
| `$translucent` | `0x1012551c` | 21 | `MATERIAL_VAR_TRANSLUCENT = (1 << 21)` |

**Four keys, one base, every one landing on the bit the published header documents.** That is what
makes it a confirmation rather than a coincidence: a single agreement proves nothing, and the base is
over-determined by four.

So `$decal` → `MATERIAL_VAR_DECAL` is settled. It had been carried as "inferred from naming" since
the flag was first read, and the naming convention was in fact right — but a convention is not a
citation, and this project had two claims resting on one.

**What is still NOT settled, and is a different question:** what the flag *causes*. Both published
reads of `MATERIAL_VAR_DECAL` — `lightmappedgeneric_dx9_helper.cpp:155` and `BaseVSShader.cpp:2134` —
only set `MATERIAL_VAR_NO_DEBUG_OVERRIDE`. Whatever else the engine does with it lives in the surface
renderer rather than in the material system, so a second decompilation target would be needed.

**Worth knowing for the next one:** the whole job took one script and one import. The project at
`D:\ghidra-proj` already had the pattern — `analyzeHeadless`, `-import`, `-postScript` — and the
binaries are ordinary game files on `F:`. A decompilation question here is an afternoon's habit, not
an expedition, and this one had been carried as an unresolved inference for want of trying it.

---

## `m_DepthBias_Decal` is `glPolygonOffset`'s `units`, and it transfers to D3D11 unchanged

*(evidence class: read from published source — Valve's own D3D9-to-OpenGL layer)*

**This project spent two evenings and three reverts on Valve's decal bias, and the whole time the
answer was in the same SDK checkout every other citation here comes from.**

The constant is `m_DepthBias_Decal = -262144`, declared as a `float` in
`public/materialsystem/materialsystem_config.h:226`. It looks impossible: Direct3D 9's
`D3DRS_DEPTHBIAS` is documented as a value in the [0,1] depth range, and −262144 is not in [0,1] by
five orders of magnitude. That apparent impossibility is what produced the wrong conclusion — that
the number was a D3D9 artefact that could not mean anything under D3D11.

**`togl` settles it.** Valve ships their own D3D9-to-OpenGL translation layer, and it handles this
exact render state (`public/togl/linuxwin/dxabstract.h:966`):

```cpp
case D3DRS_DEPTHBIAS:            // kGLDepthBias
{
    // the value in the dword is actually a float
    float fvalue = *(float*)&Value;
    gl.m_DepthBias.units = fvalue;

    m_ctx->WriteDepthBias( &gl.m_DepthBias );
    break;
}
// good ref on these: http://aras-p.info/blog/2008/06/12/depth-bias-and-the-power-of-deceiving-yourself/
case D3DRS_SLOPESCALEDEPTHBIAS:
{
    float fvalue = *(float*)&Value;
    gl.m_DepthBias.factor = fvalue;
```

`units` and `factor` are the two arguments of `glPolygonOffset(factor, units)`, and OpenGL's
definition is:

```
offset = factor · dz + r · units
```

where **`r` is the smallest resolvable depth difference** — 1/2²⁴ on a 24-bit fixed-point buffer.
Direct3D 11 defines its integer `DepthBias` with the same scale on a UNORM format:

```
bias = DepthBias · r + SlopeScaledDepthBias · maxDepthSlope
```

**So it is one quantity under three APIs.** −262144 · r = **−0.015625**, exactly 1/64 of the depth
range, whichever of them draws it. The value even reads as deliberate once it is in the right units:
−262144 is −2¹⁸, chosen so that against a 24-bit buffer it lands on a round binary fraction.

Two things follow, and the second is the more useful:

- **Valve's constant is now this project's constant**, in `DecalState.ConstantBias`, and
  `DecalRenderStateConformanceTests` parses it out of the header rather than restating it.
- **The behaviour it buys is a trade, and it is now measured rather than argued about.**
  `OverlayOcclusionRenderTests` renders an occluder on each side of the bias's reach — 0.05 and 0.005
  against a bias of 0.015625 — and asserts which surface wins the centre pixel. A marking beats
  anything standing less than 1.6% of the depth range in front of it. That is the cost of never
  z-fighting with the surface it lies on, and it is what `SHADER_POLYOFFSET_DECAL` is for.

### What went wrong, since that is the part worth keeping

The false claim was written into `docs/DECISIONS.md` **as the worked example of a true rule** — D46's
qualification that a Valve trade may have been made against a platform that no longer exists. The
rule is sound. Attaching a wrong example to it made the example inherit the rule's authority, and it
was then quoted twice more, in `WorldRenderer` and in `docs/HANDOFF.md`, as settled fact.

**The tell was there and unread: a confident claim about somebody else's system with no citation
attached.** Every other assertion in that file carries a file and a line. This one carried a
plausible-sounding API fact, and plausible-sounding is exactly what nobody checks.

Same family as the `$modblend` and "TF2's game code is closed" corrections — see
`docs/memory/nothing-is-closed.md`. **Three instances now**, and all three were reachable from the
published SDK by somebody willing to grep for the thing they had already concluded was not there.

## A fourth instance, and this time the missing citation was ours (2026-08-23)

**The constant went back in on 2026-08-21 at 21:37, hours after B70 was refuted for the second
time, and it survived two more days.** The owner met it on 2026-08-23 as "there are overlays showing
through everywhere" — the third time the same picture has been reported.

Every number in the case for it was correct. `m_DepthBias_Decal` really is −262144
(`materialsystem_config.h:226`, and the string is in the shipped `MaterialSystem.dll` beside
`mat_depthbias_decal`). It really is `glPolygonOffset`'s `units`
(`togl/linuxwin/dxabstract.h:966`). 262144 really is 2¹⁸, so on a 24-bit buffer it is exactly 1/64
of the depth range — a round number, chosen rather than tuned.

**The unasked question was which surfaces Valve applies it to**, and the answer is published:

| Reading | Says |
|---|---|
| `public/shaderapi/ishadershadow.h:255` | `EnablePolyOffset` is declared **once** in the whole tree, on `IShaderShadow` |
| `imaterialsystem.h`, `imesh.h`, `ishaderapi.h` | contain no polygon-offset entry point at all |
| grep over the tree | nothing outside `stdshaders` calls it |
| `stdshaders` | `decal`, `decalmodulate`, the portal overlays, and `Overlay_Fit` — which is `"Help for TerrainTest2"`, `SHADER_NOT_EDITABLE` |
| `lightmappedgeneric_dx9.cpp` | **zero occurrences** |

A polygon offset in Source is therefore a property of the **shader**, fixed at shader-shadow time,
and the engine has no route to add one to a material whose shader did not request it. An
`info_overlay` is ordinarily LightmappedGeneric. Valve's decal bias governs bullet holes and sprays
and has never touched the stripes painted down a corridor.

**Why it looked plausible for three attempts.** Window depth goes as z ≈ 1 − N/d, so an offset Δz
moves a surface Δd ≈ Δz·d²/N toward the camera — the world distance grows with the SQUARE of range.
At `VIEW_NEARZ` 7 a marking 500 units out tests as though it were at 236. B70 published that
arithmetic on 2026-08-21 and it was not contradicted by the third attempt; it was simply not read.

**The lesson is the inverse of the previous three in this file.** Those were facts about Valve's
system that were asserted without a citation and turned out to be reachable by grep. This one had
citations for every claim it made, and the claim it needed was the one nobody wrote down: *this
constant applies to surfaces drawn by these shaders*. **A constant carries no scope, so quoting it
accurately says nothing about where it belongs** — and a number with a file-and-line beside it reads
as settled in a way a number without one does not.

`OverlayOcclusionRenderTests` kept its two-condition design and had its requirement inverted rather
than deleted, so what is now under guard is that a surface in front of a marking hides it.

## A modulating decal is not light, so the tone map does not touch it (B635, 2026-10-31)

**What the owner saw:** explosion scorches on f12's floor drawn as "massive pixelation", "dark", like "transparency
… set to super low", at a close camera, while the same scorches at another camera looked clean. Every number and
image is in `docs/measurements/2026-10-31-explosive-decals-vs-tf2.md`.

**What it was** (*measured, one change on an unchanged build*): our pixel shader multiplied every material's output by
`LINEAR_LIGHT_SCALE`, the HDR_TYPE_INTEGER exposure, and a `DecalModulate` draw is a material. Its output is not light.
It is a MULTIPLIER the blend applies to the framebuffer (`BlendFunc( DST_COLOR, SRC_COLOR )`, `dst · 2·src`), so a
texel of 128 means "leave the floor alone". Scaled by an exposure of about a half, it means "halve the floor". Every
scorch is a 256-texel square of which 62% is neutral grey outside an alpha mask, so each became a dark square, and
overlapping squares cut the floor into straight-edged blocks. That is the "pixelation".

*Read from published source:* both modulating shaders opt out. `Modulate` ends `FinalOutput( …, TONEMAP_SCALE_NONE )`
(`modulate_ps2x.fxc:37`), and `DecalModulate` does the same (`decalmodulate_ps2x.fxc:44`). `FinalOutput` under NONE is
`result.rgb = vShaderColor.rgb` (`common_ps_fxc.h:356-358`).

**Why it hid, and the wrong first account of that.** Written first: "the far camera sat under an exposure near one".
That was *interpolated* and stated as measured. Measuring it falsified it: every capture, close and far, logged
"drawn with tone-map scale 0.7009725". Yet at the far camera a pre-fix and a fixed build drew identical frames
(0 px, tick 29900 and 30000), and pinning the scale to 1 there moved only 27,644 px by at most 10 levels. At the
close camera the same pin brightened the whole frame. So the logged figure is not the value the shader received.

*Read from our code:* the main view's world constants, `LINEAR_LIGHT_SCALE` among them, are uploaded in
`ReplayUploads` before `DrawFrame` sets the scale (`Device3D.cs:1368-1386`). At that point the scale holds the
previous frame's end value, which is reset to 1 for the HUD (`:2007-2010`). It is re-uploaded at the frame's own scale
only by `ReapplyCamera`, after the 3D skybox (`:1720-1746`) or the water views (`:1753-1766`). The log reports
`_drawnScale`, a second route to the number (B243). *Interpolated:* the close camera's frame draws a 3D-sky view and
the far camera's does not. That is the likeliest reason only the first carried the exposure into the world pass, and
with it the decal fault. That world-exposure fault is its own entry (B636).

The question that separated decal from everything else was still one cvar, `+mat_force_tonemap_scale 1`, which took
the blocks away without touching a line.

**What was wrong first, kept:** the first lead was that the scorch's outside-mask texels were drawn because ours does
not alpha-test DecalModulate where Valve does (`AlphaFunc( SHADER_ALPHAFUNC_GREATER, 0.0f )`,
`DecalModulate_dx9.cpp:64-65`). The texels were measured at 128 within 1% and the prediction said the alpha test would
NOT remove the blocks. It did, and that falsified prediction is what pointed at a multiplier on the decal's output: a
neutral texel can only darken a floor if something scales it. The missing alpha test is still a divergence, filed
with the measurement; it is invisible once the scale is gone.

**Held by** `DecalModulateConformanceTests`, red first: the shipped atlas's neutral texel took a 188 surface to 139
under a scale of 0.5 and to 255 under 2, and a scorch centre drew 100 at scale 1 and 71 at 0.5.

**After review (2026-10-31): the tone map is its own constant, and `$additive` does not beat Modulate.** The exemption
was first keyed on fog mode 2, which a later change giving DecalModulate Valve's grey fog could have dropped. It is now
`tintControl.w`, the tone-map type, set by one predicate (`Modulates`). Reading Valve's shadow state found a second
divergence nearby. *Read:* `Modulate` picks its blend from `$mod2x` alone (`modulate_dx9.cpp:141-148`), and
DecalModulate's is fixed, so `$additive` changes neither its blend nor its scale. *Measured:* five shipped Modulate
materials declare `$additive 1` (`vmt-param $additive shader=Modulate`: `models/props_nucleus/mp_energy`,
`models/vgui/filmscreen_dark`, `models/effects/comball_glow1`, `effects/red`, `effects/combine_binocoverlay`); no
DecalModulate does. Ours drew those five ADDED, tone-mapped and fogged to black; they now multiply. Held by
`ModulateToneMapConformanceTests`: hand-built materials, no game install. Each case carries its own drawn control, and
the lit case is the negative control that a lit draw IS scaled.

**Second review: a Modulate on brushwork had no blended pass at all.** `IsBlendedMaterial` counted translucent and
additive only. A `Modulate` brush run was therefore skipped by neither the opaque pass nor the translucent world, and
drew opaque with no blend: its own texel, 176 over a 188 surface. Moving the five `$additive` Modulates out of the
additive set routed them into that hole. *Read:* Valve's Modulate enables blending in its shadow state
(`modulate_dx9.cpp:141-148`), so it multiplies wherever it is drawn. It now sorts into the translucent world with the
modulate blend, in both `DrawTranslucentWorld` and `DrawTranslucentLeaf`. The first test of it was too weak: drawing
opaque and then multiplying also darkens. It caught the fault only once it asserted the model pass's exact value
(120 against 129).

*Measured:* no shipped material uses a `_DX9`/`_DX8`/`_DX6` alias of these shaders (0 of 30,994). Fog (*read*):
Valve's Modulate fogs toward its neutral value, grey under `$mod2x` (`FogToGrey`), otherwise
`FogToOOOverbright` (`modulate_dx9.cpp:201-209`). Ours fogged a modulating material not at all until B637, below.

## A modulating material reads no light, and the gamma compensation belongs to the material (B639, B638, 2026-11-01)

**The report, and why it was wrong.** B639 was filed as "`ctf_crasher`'s scorch `func_brush` LIGHTENS the floor in ours
where TF2 darkens it", from a tick-5 against tick-2000 pair, with the cause guessed as the model pass lighting the
brush entity. The direction was a misread. A signed count over the filed image itself
(`owner/sbs-crasher-scorch-off-on.png`, every second pixel) gives 13,422 pixels darker and 95 lighter. A same-tick
control, the brush hidden through a scratch switch at tick 2000 and camera `-3116 -1344 300 89 0` (the face's centre
from `shader-faces`, plus 300 along its normal), also darkens: the median linear multiplier over the floor is 0.55.
**Measured.** So no variable flips lighten to darken. What remained was the strength of the darkening.

**The engine (*read*).** `DecalModulate` sets `STATIC_LIGHT_VERTEX 0`, `STATIC_LIGHT_LIGHTMAP 0`, `DYNAMIC_LIGHT 0`,
`NUM_LIGHTS 0` and `VERTEXCOLOR false` (`DecalModulate_dx9.cpp:89`, `:184-191`). Its pixel shader is the texel alone
(`decalmodulate_ps2x.fxc:35`). It reads and writes without sRGB (`:74-75`) and blends `DST_COLOR, SRC_COLOR`, so the
stored value is `dst · 2s` in gamma. `Modulate` draws through `unlitgeneric_vs20` (`modulate_dx9.cpp:186`). Neither the
entity's render colour nor any lightmap reaches either shader. In a linear blend the equivalent is
`dst_lin · (2s)^2.2`, so the shader output must be `s_lin · 2^2.2/2` for mod2x, and `s_lin` for plain Modulate
(*arithmetic*, on the 2.2 approximation).

**Ours.** The scaling rode in two places: the decal's vertex colour (`2^2.2/4`), and the lightmap's white texel at
2.0. A brush entity drawn by the model pass has a white vertex colour, so it got 2.0. *Measured* by ratio against a
scratch run at diffuse 1: the brush entity's effective diffuse was 2.03. The control for that instrument: a run at
2.297 measured 2.353 against the same baseline. A direct readout of the light, written to the screen, said 3.04. It
had no control and disagreed with the ratio, so it was discarded. Plain Modulate took the same 2.0 and drew twice too
bright (B638).

**TF2, live (*measured*, 2026-11-01).** Listen server, `setpos -3116 -1344 232` (eye at z 300, matching ours, checked
by the cap ring's size and place), `mat_force_tonemap_scale 1`, then `ent_fire gatescorch_red enable`. Median linear
on/off multiplier, per region of the 984x551 frame:

| region | TF2 fog on | TF2 `fog_enable 0` | ours before | ours after |
|---|---|---|---|---|
| alpha-0 surround, x 20-120 | 1.000 | 1.000 | 0.882 | 1.000 |
| alpha-0 surround, x 870-970 | 1.000 | 1.000 | 0.867 | 1.000 |
| scorch ring (150-300, 250-400) | 0.585 | 0.519 | 0.401 | 0.465 |
| scorch centre (600-800, 280-400) | 0.428 | 0.348 | 0.265 | 0.307 |

Before the fix the surround was a darker square some 600 units across that TF2 does not draw. It is now exactly
neutral. The interior is still about 10% darker than TF2 with fog off. Fog accounts for the rest of the gap to TF2's
fogged frame (B637). *Interpolated:* that the 10% is the linear blend's sRGB-curve error, which grows toward black.

**The fix.** `WorldRenderer.UnlitDiffuseOf` gives every modulating material an unlit diffuse in `combine.w`:
`ModulateTwiceDiffuse` (2^2.2/2) for DecalModulate and `$mod2x`, 1 for plain Modulate. The shader uses it as the
light on every path (world, overlay, decal, model), and decal corners are white. `mat_fullbright 1` no longer
overrides an unlit diffuse: it substitutes the lightmap, which an unlit shader never samples. That also stops it
doubling UnlitGeneric. **Held by** `ModulateUnlitConformanceTests`, red first: the neutral brush entity drew 177 over
188, now unchanged; plain Modulate of 128 drew 129, now about 94.

**Still open, measured beside it:** no alpha test on DecalModulate. It is invisible now that the surround is neutral
(texels 120-131). Fog is B637.

## The blend multiplies stored values, and that is what made the missing alpha test visible (B641, 2026-11-01)

**The structural fix.** A gamma multiply cannot be written as a fixed-function blend on linear values, because
`lin(2·s·gamma(d))` is not linear in `d`. Every target this renderer draws a world into is 8-bit UNORM viewed through
the sRGB curve. A UNORM view of the same resource writes the stored bytes. So a modulating run now draws through that
twin, and the shader re-encodes its decoded texel with the exact sRGB curve. `DST_COLOR, SRC_COLOR` then computes
Valve's `dst · 2s` on stored values. No compensation constant is left. *Measured offscreen:* over 188, texel 32
draws 47, which is Valve's arithmetic; the linear blend drew 51.

**What the exact blend uncovered.** With the 2^2.2/2 compensation, the scorch's alpha-0 surround (RGB 120-131)
happened to land at about 1.000. Through the exact blend it stored 1.014: the surround was being drawn at all, where
Valve's `AlphaFunc( GREATER, 0 )` (DecalModulate_dx9.cpp:64-65) discards it. That is the divergence B635's measurement
had filed and called invisible. It was invisible only because one error hid another. With the alpha test, the
surround is 1.000, as in TF2's live pair.

**The real frame against TF2** (stored on/off ratios at `-3116 -1344 300 89 0`): the centre went from 0.554 (linear) to
0.577 (exact), against TF2's 0.591 with fog off; the ring from 0.690 to 0.704, against 0.725. The 2-3% that remains
is open in B641 with what it is not.

## A modulating draw fogs toward grey, and the shader API decides which grey (B637, 2026-11-02)

**What was believed:** "fog toward middle grey" meant one value, 0.5, for every modulating shader and every fog type.
The SDK alone supports that: `FogToGrey()` and `FogToOOOverbright()` both name 128.

**What the shader API does** (*disassembly*, `shaderapidx9.dll`, `0x180014200` and `0x180025a50`): the fog-mode apply
stores the pixel fog colour as r/255 RAW whatever the mode; its gamma correction and tone-map scale go only into the
fixed-function `D3DRS_FOGCOLOR`. The c29 upload then makes its own decisions: range fog linearises the colour only
when sRGB writes are on (these two shaders write without), height fog linearises it always, and either is
multiplied by the tone-map scale under integer HDR unless the shader called `DisableFogGammaCorrection( true )`. Only
DecalModulate does ("fog should stay exactly middle grey", `DecalModulate_dx9.cpp:79`). So DecalModulate's grey is
128/255 under range fog; Modulate's, `$mod2x` or not, is 128/255 times the exposure; and under water every one of them
fades toward the linear 0.22 in a gamma pipeline, a dark grey. **That last one reads as a Valve bug and is kept**: the
shader never re-encodes the colour the API linearised for it.

**Which curve, and a wrong first guess (review, 2026-11-02).** The first version took `pow 2.2` for that conversion,
because our water fog colour already did. The API has two: `pow 2.2` and the exact sRGB decode, chosen by
`r_shader_srgb` ("0 = use hardware srgb", the default). At the default it is the sRGB decode, so the grey is 0.2159,
not 0.2195. The difference is under two 8-bit levels, which no render test here can see; the reading holds it. The
same choice applies to the water's own fog colour and to the scene fog colour under sRGB write, both still `pow 2.2` in
ours (RISKS B637). The gamma-correction flag DecalModulate sets is tested only after the conversion, so it skips the
tone-map scale and nothing else.

**Two things learned beside it.** The comment "fog should stay exactly middle grey" is true only for range fog. And
plain Modulate fogs to the same 0.5 as `$mod2x`, which halves what is behind it at full fog, since its blend is `dst · s`
(the shader's own comment, "We need to fog to *white* regardless of overbrighting", `modulate_dx9.cpp:201`, describes
the intent, not the arithmetic).

**Measured on `ctf_crasher`'s scorch** against TF2's live fog-on/fog-off pair: the fog's share of the ring is ×1.065 in
both, the centre ×1.123 against TF2's ×1.116. Numbers and images: `docs/measurements/2026-11-02-modulate-fog-plan.md`.

**An instrument note:** `r_drawentities 0` does not hide the scorch `func_brush` in our viewer (the hidden and shown
frames came out identical); the control frame needed a scratch build.
