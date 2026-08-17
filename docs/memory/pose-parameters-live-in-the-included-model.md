---
name: pose-parameters-live-in-the-included-model
description: A player model declares only body_pitch and body_yaw; move_x and move_y come from the animation model it includes, and paramindex is local to that group.
metadata:
  type: project
---

`scout.mdl` declares **two** pose parameters, `body_pitch` and `body_yaw`. `move_x` and `move_y`
exist only in `scout_animations.mdl`, the model it includes. Every class is the same shape.

A sequence's `paramindex` is **local to the group that owns the sequence**, so the run sequence asks
for index 5 — which means something in the animation model's six-entry list and nothing in the base
model's two-entry one. Reading it against the base model returned cell zero with a setting of zero
on both axes, which is the blend grid's `move_x = −1, move_y = −1` corner: **every moving player ran
the backward-left animation, in every direction, forever** (B101).

Nothing reported it. Running off the end of a list is a legitimate answer for a model that has no
such parameter, and cell zero is a real cell — the same shape as [[sentinels-conflate-unknown-with-answer]].

**The engine merges the lists** in `CVirtualModel::AppendPoseParameters`
(`studio_virtualmodel.cpp:445`) and keeps a per-group map, `virtualgroup_t::masterPose`, read back by
`CStudioHdr::GetSharedPoseParameter`. Three details matter:

- matching is **by name, case-insensitive** (`stricmp`) — models declare the same parameter at
  different positions, and scout and soldier really do differ in `move_x`/`move_y` order;
- a duplicate **widens** the shared range across all four endpoints — `body_pitch` is −45..45 in the
  base model and −45..90 in the animations, and normalising against the narrower one puts every
  pitch at the wrong fraction;
- the shared list is in **group order**, base model first.

**The translation is not currently observable on any player model**, established by sabotage: with
`masterPose[local]` replaced by `local` the whole suite stays green, because the base model's
parameters are a prefix of the animation model's so the map is the identity. Keep it anyway — it is
what the engine does — but know that the merged LIST is the half the corpus can falsify. See
[[most-of-a-decoder-is-untested]].

**How it was found, which is the transferable part.** Three divergences from
`ComputePoseParam_MoveYaw` were found by reading the SDK and every one was real and irrelevant. The
answer came from measuring each hop: a POV demo carries the recorder's own `CUserCmd`, so
`forwardmove 450` with `IN_FORWARD` held is ground truth for "running forward" that owes nothing to
the code under test. It gave `move_x = 1.000` at seven of nine samples — the parameter was never
wrong — which moved the search downstream to the list. See
[[measure-every-hop-before-blaming-one]] and [[read-the-sdk-for-the-whole-mechanism]].
