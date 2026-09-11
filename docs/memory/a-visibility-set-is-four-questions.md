---
name: a-visibility-set-is-four-questions
description: "One \"is it visible\" set fed two engine rules that ask different things; ask which predicate, not which frame."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T11:36:19.576Z
---

**"Visible" is not one predicate, and a set named for it will be wrong for at least one of its
readers.** Source has at least three, and they share no inputs:

| the engine's question | what it actually tests | `file:line` |
|---|---|---|
| `IsVisible()` | `m_hRender != INVALID_CLIENT_RENDER_HANDLE` — leaf-system membership, set by `UpdateVisibility` from `ShouldDraw() && !IsDormant()`. **No frustum, no frame, no bones.** | `c_baseentity.h:691`, `c_baseentity.cpp:1421` |
| `ShouldDraw()` | `kRenderNone`, then `model != 0 && !IsEffectActive( EF_NODRAW ) && index != 0` | `c_baseentity.cpp:1435` |
| `IsRagdollVisible()` | `engine->IsBoxInViewCluster` then `engine->CullBox` around a ±1 box — a live PVS and frustum test | `c_tf_player.cpp:1350` |

One `HashSet<int>` here served `ShouldInterpolate` (which wants the first) and the corpse fade (which
wants the third), and was filled at **bone setup**, which is none of them. Consequences, all four at
once and all invisible to a green suite: no brush entity was ever interpolated in any map, so no door
anywhere ever moved smoothly; the viewmodel pass cleared the set and refilled it with two props, so on
any first-person frame the whole world dropped off both lists; and the frustum removed entities the
engine keeps.

**The tell was a sentence, not a symptom.** Four files repeated *"`IsVisible()` is the LAST render's
answer, so gating this frame on the previous one is not an approximation of what Valve does — it is what
Valve does."* A claim that confident, restated in four places, and none of them a quote. Reading the
one-line inline accessor took a single grep.

**How to apply.** Before wiring any "was it visible" set: name the ENGINE FUNCTION each consumer calls,
grep its definition — these are one-line inline accessors, not deep code — and give each consumer its
own set if the inputs differ. A frame-latency argument ("the engine uses last frame's answer") is a
claim about *when*, and it hides the prior question of *what*. Ask what, first.

Related: [[the-base-is-not-the-behaviour]], [[parity-is-the-search-not-the-defence]],
[[measure-the-output-not-the-capability]], [[an-entity-index-does-not-name-a-track]],
[[instrument-bugs-outnumber-decoder-bugs]].
