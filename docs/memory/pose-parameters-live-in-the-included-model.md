---
name: pose-parameters-live-in-the-included-model
description: A player model declares only body_pitch and body_yaw; move_x and move_y come from the animation model it includes, and paramindex is local to that group.
metadata:
  type: project
---

`scout.mdl` declares only `body_pitch`/`body_yaw`. `move_x`/`move_y` exist only in
`scout_animations.mdl`, the model it includes. A sequence's `paramindex` is LOCAL to the group that
owns the sequence — reading it against the base model returned cell zero on both axes, so every
moving player ran the backward-left blend-grid animation, forever (B101). Nothing reported it —
running off a list's end is a legitimate answer, and cell zero is a real cell (same shape as
[[sentinels-conflate-unknown-with-answer]]).

**The engine merges the lists** (`CVirtualModel::AppendPoseParameters`, `studio_virtualmodel.cpp:445`),
keeping a per-group map read back by `GetSharedPoseParameter`. Three details: matching is by NAME,
case-insensitive (models declare
the same parameter at different positions); a duplicate WIDENS the shared range across all endpoints
(a range differing between base and animation model normalises against the wrong one); the shared
list is in group order, base model first.

**Not currently observable on any player model** — sabotage confirmed the base model's parameters are
a prefix of the animation model's, so the map is the identity for real content. Keep it anyway (it's
what the engine does), but know the merged LIST is the half the corpus can't falsify.

**How found:** measuring each hop — a POV demo's own `CUserCmd` (`forwardmove 450` with `IN_FORWARD`)
is ground truth for "running forward" independent of the code under test, and it showed the parameter
was never wrong, moving the search downstream to the list. See [[nothing-is-closed]],
[[parity-is-the-search-not-the-defence]].
