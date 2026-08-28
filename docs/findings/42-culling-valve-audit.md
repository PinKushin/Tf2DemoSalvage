# 42 — Auditing the cull against Valve, three passes

*2026-08-28. Requested after three visual defects were reported against the culling work in one
evening: "you are going to need to valve audit at least 3 times, clean, before i trust it."*

Three passes, each with a different lens, because they find different things. Pass 1 compares
transcriptions operand by operand. Pass 2 compares CALL SITES — where the engine invokes each
function, in what order, with what arguments. Pass 3 is the ledger of what the engine does that we
do not.

Pass 1 found nothing. Pass 2 found the defect.

---

## Pass 1 — transcription

Every function this project claims to implement, put beside its SDK original.

| Valve | Ours | Verdict |
|---|---|---|
| `GeneratePerspectiveFrustum` (`mathlib_base.cpp:3923`) | `ViewFrustum.Perspective` | **exact** |
| `CalcFovY` (`:3893`) | `ViewFrustum.VerticalFieldOfView` | **exact**, including the out-of-range → 90 substitution |
| `BoxOnPlaneSide` (`:829`) | `CullPlane.SideOf` | **exact** |
| `R_CullBox` (`:3973`) | `ViewFrustum.Cull` | **exact** |
| `TransformAABB` (`:2910`) | `WorldSpaceBounds.Of` | **exact** |
| `DetectBucketedRenderGroup` (`clientleafsystem.cpp:1538`) | `OpaqueBuckets.BucketFor` | **exact** |
| `PVSCheck` (`vrad.h:372`) | `WorldVisibility.Collect` | **exact**, negative cluster treated as visible |

Details worth recording because they are the ones a reimplementation gets wrong:

- **`normalNeg` is built from the UN-normalised `normalPos`.** Valve calls `VectorMA` for both before
  either `VectorNormalize`. Ours matches because `SidePlane` takes a copy rather than normalising in
  place; normalising first would give a different second plane.
- **`BoxOnPlaneSide`'s comparisons are deliberately asymmetric** — `dist1 >= dist` for the front bit
  and a bare `dist2 < dist` for the back — so a box lying exactly on the plane answers 1, not 3.
- **A normal component of exactly `0` or `-0.0f` takes the `maxs` branch** in both, because
  `SignbitsForPlane` tests `< 0` and so does the select.
- **All six frustum planes are `PLANE_ANYZ` (5)**, so `BoxOnPlaneSide`'s axial fast path — gated on
  `type < 3` — never runs for a view frustum. It exists for BSP planes.

## Pass 2 — call sites and order

| Question | Valve | Ours |
|---|---|---|
| Cull before or after bucketing? | cull, **then** `DetectBucketedRenderGroup` | same |
| What is `fDimension`? | `MAX(MAX(\|dims.x\|,\|dims.y\|),\|dims.z\|)` of `absMaxs - absMins` | same |
| Are translucent entities culled? | yes — the cull is above the group split | yes |
| Are translucent entities bucketed? | **no**, only `OPAQUE_STATIC..OPAQUE_ENTITY` | no |
| Opaque draw order | brush models, then buckets big→small, entities then static props | same, minus the prop split we do not have |
| Vis origin | the view origin, one point (`SetupVis`) | same |

### The defect: a bone-merged entity is culled by its PARENT's box

`DefaultRenderBoundsWorldspace` (`clientleafsystem.cpp:342`) opens with a special case, and its
comment names the bug it exists to fix:

```c
// Tracker 37433: This fixes a bug where if the stunstick is being wielded by a combine soldier,
// the fact that the stick was attached to the soldier's hand would move it such that it would
// get frustum culled near the edge of the screen.
if ( pEnt && pEnt->IsFollowingEntity() )
{
    CalcRenderableWorldSpaceAABB_Fast( pParent, absMins, absMaxs );
    pEnt->GetRenderBounds( vAddMins, vAddMaxs );
    float radius = pEnt->GetLocalOrigin().Length();
    float flBloatSize = MAX( vAddMins.Length(), vAddMaxs.Length() );
    flBloatSize = MAX( flBloatSize, radius );
    absMins -= Vector( flBloatSize, flBloatSize, flBloatSize );
    absMaxs += Vector( flBloatSize, flBloatSize, flBloatSize );
    return;
}
```

A following entity does not get its own box at all. It gets the **parent's world box, isotropically
bloated** by the largest of its own bounds' two corner lengths and its local origin's length — on the
stated assumption that it "can be at any point and at any angle within the parent's world space
bounds."

**In TF2 that is every hat, every cosmetic and every weapon**, all bone-merged onto a player.

**What this project does instead**, and it is closer than it was: a worn item's `Origin` is set to its
wearer's position — `EntityModels` does this already, with a comment explaining that a merged item's
own pose is `(0,0,0)` by construction — so with the origin fix it is boxed by its OWN local bounds at
its WEARER's position. That is neither Valve's box nor obviously wrong; it is tighter than Valve's and
centred on the right place. The exposure is exactly the stunstick case: an item whose bones carry it
outside its own local bounds relative to the wearer's origin can be culled at the edge of the screen
while still visible.

### Two further divergences, both pre-existing and neither about culling

- **Two-pass models.** `RENDER_FLAGS_TWOPASS` with alpha 255 adds a renderable to the opaque list
  *and* the translucent one. We have no two-pass concept.
- **Detail props.** Gathered per leaf by `DetailObjectSystem`, on their own path with no render
  handles. We do not draw them.

### One optimisation of Valve's we do not take

`CalcRenderableWorldSpaceAABB_Fast` exists beside the ordinary one and the comment says why: *"This
gets an AABB for the renderable, but it doesn't cause a parent's bones to be setup. This is used for
placement in the leaves, but the more expensive version is used for culling."* Two functions, two
costs, chosen per use. We have one.

Also: `DefaultRenderBoundsWorldspace` special-cases `angles == vec3_angle` to a plain
`VectorAdd(mins, origin)` and only calls `TransformAABB` when the entity is actually rotated. Same
answer, less arithmetic.

## Pass 3 — what the engine does that we do not

Everything here makes our result strictly MORE conservative — we draw what Valve would have removed
— except where marked.

| Mechanism | Status | Note |
|---|---|---|
| Area portals (`DoesBoxTouchAreaFrustum`) | absent | open/shut state is server-side; a demo does not carry it |
| Occlusion (`engine->IsOccluded`, `func_occluder`) | absent | studio models only in Valve |
| Screen-size fade / `r_propsmaxdist` | absent | |
| Detail props | absent | not drawn at all |
| Static-prop vs entity split within a bucket | absent | no static-prop render group here |
| Bone-merged parent box | **divergent** | see above — this one can remove something visible |

---

# Audits two and three

*Run after the first audit's finding was fixed, so they read the changed code rather than the code
that was audited. Different lenses again: audit two follows the DATA — is every input to the cull the
value Valve would use? Audit three is adversarial — what can this remove that the engine keeps?*

## Audit 2 — the inputs

Audit 1 compared function bodies. This one asks where the numbers come from.

### `C_BaseAnimating::GetRenderBounds` has five branches; we had implemented three

`c_baseanimating.cpp:4533`, read in full:

| Branch | Valve | Ours |
|---|---|---|
| ragdoll | `m_pRagdoll->GetRagdollBounds()` | **not special-cased** |
| no model / no sequences / `GetSequence() == -1` | zero box, return | falls back to the header box |
| `view_bbmin/max` authored | the clipping box | same |
| otherwise | `hull_min/max` | same |
| then | union `seqdesc.bbmin/bbmax` | same |
| **finally** | `theMins *= flScale; theMaxs *= flScale;` | **was missing — fixed** |

**The scale line was a live defect.** `ScenePose.Scale` is decoded, interpolated and applied when
this project draws a model, so a scaled model was drawn at its real size and culled by a box at its
authored one. A giant reaches far outside a box a fraction of its size and vanishes at the edge of
the screen. Both corners multiply, not just the extent — the geometry scales about the origin, so
the box must too.

**The `GetSequence() == -1` branch is a deliberate divergence in the safe direction.** Valve returns
a ZERO box, which then culls the entity almost anywhere; we return the header box, which is larger
and culls less. Ours draws more, never less.

**Ragdolls are a known gap rather than a decision.** Valve uses `GetRagdollBounds()` *and*
`GetRagdollOrigin()` (`c_baseanimating.cpp:4581`) — a corpse is bounded by where the body actually
lies, not by its model's hull at its entity origin. This project does not simulate ragdoll physics,
so it has no ragdoll extent to use; the model's box at the networked origin is the best available and
is recorded here as an approximation, not a match.

### Everything else the cull reads

| Input | Valve's source | Ours | |
|---|---|---|---|
| position | `GetRenderOrigin()` | `SceneProp.Pose` X/Y/Z | ✓ |
| rotation | `GetRenderAngles()` | `SceneProp.Pose` pitch/yaw/roll | ✓ |
| wearer | `GetFollowedEntity()` = move parent | `SceneProp.AttachedTo` | ✓ |
| leaf cluster | `dleaf_t.cluster` | `BspLeafTree.Cluster` | ✓ |
| leaf cull box | `dleaf_t.mins/maxs`, *"for frustum culling"* | `BspLeafTree.Bounds` | ✓ |
| node cull box | `dnode_t.mins/maxs`, *"for frustom culling"* | `BspLeafTree.Node` | ✓ |
| PVS row | `dvis_t` bitfield | `BspVisibility.Visible` | ✓ |
| map identity | `svc_ServerInfo.mapCRC` | **decoded and never compared** — D113 | ✗ |

## Audit 3 — adversarial

The question is not "does it match" but "what can it wrongly remove".

| Attack | Result |
|---|---|
| Viewmodel culled by the world frustum? | **No** — the viewmodel pass calls `DrawModel` directly, outside `InDrawOrder` and `Culled`. It has its own camera and a 1-unit near plane; culling it against the world's would be wrong. |
| Wrong cull variant — `R_CullBoxSkipNear`? | No. `clientleafsystem.cpp` uses `engine->CullBox`; `SkipNear` appears only in `clientshadowmgr`. We use all six planes. |
| A model with no bounds culled by a point? | No — `IsPlaced` refuses to cull an empty box. |
| A bone-merged item culled off its wearer? | No — takes the wearer's box, bloated. |
| A scaled model culled by an unscaled box? | **Was yes. Fixed this pass.** |
| Static props culled wrongly? | They are never culled at all — drawn whole through `_props`. Conservative, and a large performance gap. |
| World translucent or additive surfaces? | Never culled; `_sortedTranslucent` is built once at upload. Conservative. |
| Decals and overlay fragments? | Never culled. Conservative. |
| A leaf-orphaned surface (displacements)? | Culled by its own box against the frustum only, never the PVS. |
| Camera in solid space? | PVS filtering is skipped entirely; frustum only. |
| Map with no vis lump? | PVS filtering skipped. `BspVisibility.None` answers false to everything, so testing it without the `HasData` guard would cull the world. |

**Every gap found in audit 3 is in the conservative direction** — we draw things the engine would
have removed. The two that cost performance rather than correctness are static props and the
translucent world.

## What changed as a result of this audit

Recorded here rather than folded silently into the code, because the point of the audit was trust:

1. The bone-merge rule is a real divergence and is **open** — the fix needs a worn item to know its
   wearer's world box, which is not available where the instance is built.
2. Everything else in passes 1 and 2 transcribes exactly.
3. The performance defect that prompted the audit was not a parity question at all: the cull was
   recomputed every frame because `MainForm.PlaceCamera` uploads the camera unconditionally. The
   engine builds world lists once per view. Measured: 274 frames a second before the culling work,
   149 after, per-frame drawing time unchanged at ~1 ms — the whole cost sat in a call the drawing
   timer does not measure. **After the fix: 300 frames a second**, above where it started.
4. Audit 2 found the missing model scale, now applied.
5. Audit 3 found nothing that removes something the engine keeps. Every remaining gap draws MORE
   than Valve would.

## The score

Three passes, three different lenses, two defects — both in the second lens each time:

| Audit | Lens | Found |
|---|---|---|
| 1 | transcription — function bodies | nothing; **bone-merge rule found in its pass 2** |
| 2 | inputs — where the numbers come from | model scale not applied |
| 3 | adversarial — what can it wrongly remove | nothing |

**The pattern is worth keeping.** Comparing transcriptions found nothing both times; comparing what
the engine does with the result found a defect both times. A function can be copied perfectly and
still be called with the wrong argument, at the wrong point, or on the wrong entity — and that is
where all four of this work's defects lived.

## Still open

- **Ragdoll bounds and origin** — approximated, not matched, because no ragdoll extent exists here.
- **Two-pass models** (`RENDER_FLAGS_TWOPASS`) — drawn once, not twice.
- **Detail props** — not drawn.
- **Static props are never culled** — the largest remaining performance gap.
- **The map CRC is decoded and never compared** — D113, and the reason three defects were
  investigated against a map that did not match its demo.
