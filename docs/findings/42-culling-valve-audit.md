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

## What changed as a result of this audit

Recorded here rather than folded silently into the code, because the point of the audit was trust:

1. The bone-merge rule is a real divergence and is **open** — the fix needs a worn item to know its
   wearer's world box, which is not available where the instance is built.
2. Everything else in passes 1 and 2 transcribes exactly.
3. The performance defect that prompted the audit was not a parity question at all: the cull was
   recomputed every frame because `MainForm.PlaceCamera` uploads the camera unconditionally. The
   engine builds world lists once per view. Measured: 274 frames a second before the culling work,
   149 after, per-frame drawing time unchanged at ~1 ms — the whole cost sat in a call the drawing
   timer does not measure.
