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
| render order | four layers, `OVERLAY_RENDER_ORDER_NUM_BITS`, packed into `m_nFaceCountAndRenderOrder` and set by `SetRenderOrder` | **read and then ignored.** `BspOverlay.RenderOrder` is parsed and nothing sorts by it | **open** |
| fade distance | `doverlayfade_t` in `LUMP_OVERLAY_FADES` (60), with `r_overlayfadeenable`, `r_overlayfademin`, `r_overlayfademax` | **lump not read at all** | **open** |
| fragment construction | `COverlayMgr::RenderOverlays`, `engine/Overlay.cpp` — not published | face clipped to the overlay's projected volume (B134) | **interpolated** |

### The two still open, and why they are worth doing

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
