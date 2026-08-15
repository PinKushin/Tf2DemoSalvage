# 23 — Drawing what the entity says, not what the model defaults to

A run of defects that all looked like renderer bugs and were not. Each one turned out to be this
project deciding something the demo had already decided, or a shortcut taken when the only camera
was fixed. Written up together because the shape repeats and the shape is the finding.

## Cosmetics ride bones by name, and carry no position at all

*(Measured on the corpus; mechanism read from the SDK.)*

A hat is a separate entity with its own model. It networks no origin, no angles and — for most of
them — no model index in the ordinary table. Everything about where it goes comes from its wearer:

- `EF_BONEMERGE` is `0x001` (`public/const.h:284`). `CBaseEntity::FollowEntity`
  (`shared/baseentity_shared.cpp:2360`) sets it and **zeroes local origin and angles**, which is
  why the wire carries nothing useful.
- `CBoneMergeCache::MergeMatchingBones` (`bone_merge_cache.cpp:122`) copies the wearer's matrix
  into the item's bone **for bones whose names match**, and only those.
- Bones that do not match are *not* left at rest. They are built by the item's own hierarchy walk,
  from a parent that may itself have been merged. That is the whole mechanism, and skipping the
  second half is what tore models apart here: a gibus matched 1 of its 8 bones, and the rest-pose
  fallback left the geometry-carrying bone at the player's feet.

**The attachment field matters more than it looks.** `m_hOwnerEntity` looks like the link and is
not — taking it claimed 220 syringe projectiles as worn items. `moveparent` is the parent; the
owner counts only when `EF_BONEMERGE` is set. An entity handle is the low **11** bits
(`MAX_EDICT_BITS`) of the networked value, and `INVALID_NETWORKED_EHANDLE_VALUE` is tested against
the **whole** value first (`client/recvproxy.cpp:90`) — mask before that test and every invalid
handle becomes a plausible entity index.

## Every cosmetic lives in a table this project was not reading

*(Measured — see also [[negative-model-indices-are-dynamic]].)*

`SendPropModelIndex` is a signed 13-bit int, so model indices go negative, and `ivmodelinfo.h:90`
says an index below −1 is *dynamic*: `dynamic = -2 - index`, **odd is client-only** and **even is
networked** at `dynamic >> 1` of the **`DynamicModels`** string table, not the usual one. Until that
table was read, every cosmetic resolved to nothing and drew nothing — silently, because a missing
path is indistinguishable from an entity that has no model.

## A player standing still is a blend, not a frame

*(Read from the SDK, verified by test.)*

`m_flPoseParameter` stores **normalised 0..1** values (`Studio_SetPoseParameter`,
`bone_setup.cpp:5099`) — not the −1..1 the animation state computes. A sequence names a *grid* of
animations with two pose parameters as its coordinates (`Studio_LocalPoseParameter`, `:1682`), and
`Calc3WayBlendIndices` (`:1840`) picks three of its corners. `BlendBones` (`:1531`) is a
**normalised lerp, not a slerp**, after `QuaternionAlign` (`mathlib_base.cpp:1509`).

Taking the grid's corner instead is right for a prop and wrong for a player: the corner of a
nine-way movement blend is one fixed direction, so the legs run that way whatever the body does.

**And the bug that actually broke standing was none of the above.** The sequence lookup used
`Contains`, so asking for `Stand_PRIMARY` returned `AttackStand_PRIMARY` and laid everyone down.
Three commits blamed the skeleton, the up axis and the matrix convention first — all three wrong,
all three amended. See [[lookups-must-match-exactly]].

## A fixed camera hides every shortcut taken for it

*(Differential — each of these was correct until the camera could move.)*

The viewer had one top-down orthographic view, and several build-time decisions had quietly
assumed it:

- **A back-face cull on `Normal.Z < 0`** — correct-ish looking down, and it deletes walls the
  moment you look sideways.
- **A decal depth bias** standing in for correct placement. A bias cannot move geometry; the
  overlays were floating because they were never clipped.
- **Depth writes left off** for a pass, which does not show from above and puts eyes through the
  back of a head and a medipack over its carrier from every angle.

The camera itself is Valve's: `AngleVectors` returns forward/**right**/up
(`mathlib_base.cpp:919`), Source's +Y points **left**, and `MatrixBuildPerspective`
(`vmatrix.cpp:1048`) negates X and Y. Getting either of those halves alone gives a mirrored world
that no matrix-shaped assertion notices, which is why `FreeCameraTests` asserts on **where a point
lands**, not on matrix entries.

## Ask the map what a thing is before theorising about the renderer

*(The stripes, and the doors.)*

Red and blue stripes drew off the walls. Four renderer theories were proposed and killed by
measurement before anyone opened the BSP and looked at what the stripes *were*: overlays, whose
face list is **not a list of candidate surfaces to place against but the set of surfaces to clip
against** (Sutherland–Hodgman). Clipping them fixed it outright.

The rolling doors are the same question and are still open. Lump 14's `dmodel_t` (mins/maxs/origin/
headnode/firstface/numfaces, stride 48) carries the brush entities, and **no model in cp_process has
a non-zero origin** — brushwork is stored at its compiled position, so a door drawn from the world
buffer is a door welded shut.

## Bodygroups: right at every hop that was measured

*(Measured; unresolved as of this commit.)*

`GetBodygroup` (`shared/animation.cpp:876`) is `(body / base) % nummodels`, over
`mstudiobodyparts_t` (sznameindex 0, nummodels 4, base 8, modelindex 12, stride 16). The capture
point hologram has one part, base 1, **four** alternatives — one sign per ownership state.

Selecting the alternative at *load* time desynchronised the `.vtx` strip groups against the meshes
("strip groups do not fit either known layout"), because the file's layout assumes every
alternative is present. So every alternative is read and tagged `(part, model)`, and the choice
moves to draw time — which is also the only correct place for it, since two entities share one
model and want different signs.

Three measurements, all good:

```
model:  cappoint_hologram.mdl — 1 part, base 1, 4 alternatives, 9 meshes
demo:   cappoint_hologram.mdl — bodies 0, 2, 3
packed: 1 parts, 9 batches spanning 4 alternatives
```

**And the picture still shows "?" — body zero — on every point.** Three correct measurements prove
only that the fault is in the hop nobody measured: the value arriving at `DrawModel`. Open as B73.

## What the run is worth as a lesson

Every defect above was found by asking a *different* question than the one the symptom suggested,
and several cost multiple wrong commits because the first instrument answered confidently about the
wrong quantity. The cheapest habit that would have caught most of them: write down the chain the
data travels, put a number on every link, and instrument the last link first.
