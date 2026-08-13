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

## Status

**The reader is done and the basis is recovered, 2026-08-13.** Nothing is drawn yet: placing the
quads against the faces they name, and the depth-offset that stops a decal z-fighting the surface
it sits on, are still to come.

Evidence class: read from published source (`vbsp`), confirmed by measurement on the corpus.
