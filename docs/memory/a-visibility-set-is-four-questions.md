---
name: a-visibility-set-is-four-questions
description: "One \"is it visible\" set fed two engine rules that ask different things; ask which predicate, not which frame."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T11:36:19.576Z
---

**"Visible" is not one predicate.** Source has at least three, sharing no inputs:

| the engine's question | what it tests | `file:line` |
|---|---|---|
| `IsVisible()` | `m_hRender != INVALID_CLIENT_RENDER_HANDLE` — leaf-system membership, no frustum/frame/bones | `c_baseentity.h:691`, `.cpp:1421` |
| `ShouldDraw()` | `kRenderNone`, then `model != 0 && !EF_NODRAW && index != 0` | `c_baseentity.cpp:1435` |
| `IsRagdollVisible()` | `IsBoxInViewCluster` then `CullBox` around a ±1 box — live PVS + frustum | `c_tf_player.cpp:1350` |

One `HashSet<int>` served both `ShouldInterpolate` (wants the first) and the corpse fade (wants the
third), filled at bone setup (none of the three). Consequences, all invisible to a green suite: no
brush entity interpolated in any map (no door ever moved smoothly); the viewmodel pass cleared and
refilled the set with two props, dropping the whole world off both lists on any first-person frame;
the frustum removed entities the engine keeps.

**The tell was a sentence, not a symptom:** four files repeated *"`IsVisible()` is the LAST render's
answer, so gating this frame on the previous one is not an approximation — it is what Valve does"* —
confident, restated four times, never quoted. A single grep of the one-line inline accessor settled
it.

**How to apply:** before wiring any "was it visible" set, name the ENGINE FUNCTION each consumer
calls, grep its definition, give each consumer its own set if inputs differ. A frame-latency argument
hides the prior question of *what* — ask what, first.

Related: [[the-base-is-not-the-behaviour]], [[parity-is-the-search-not-the-defence]],
[[measure-the-output-not-the-capability]], [[an-entity-index-does-not-name-a-track]],
[[instrument-bugs-outnumber-decoder-bugs]].
