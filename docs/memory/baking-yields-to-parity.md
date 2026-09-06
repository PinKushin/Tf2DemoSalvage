---
name: baking-yields-to-parity
description: "Our static-prop baking is an optimisation Valve does not have; when it conflicts with engine behaviour, the baking changes."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-06T15:04:22.910Z
---

The owner, 2026-09-06, while B363 (detail props that are MODELS) was being designed: *"valve doesnt
have baking, soo, their version isnt going to bake, and we might have to change our baking if valve
does something that is imcompatable"*. Recorded as D143.

**Why:** a detail model's alpha is the per-view distance fade (`CDetailModel::GetFxBlend` returns
`m_Alpha`), so it cannot live in a baked vertex buffer whose colours are fixed at load. The design
question "which side gives" has one answer: the baking. It is ours, no engine behaviour depends on
it, and parity is not tradeable for it — D89 applied to a structure instead of a branch.

**How to apply:** when a piece of engine behaviour will not fit the baked static-prop path, do not
look for the version of the behaviour that fits. Change the path. The trap is that a baked buffer
reads as architecture rather than as an optimisation, so it quietly acquires a veto it was never
given — and the symptom is a design discussion about what to implement rather than about what the
engine does.

Does not mean the baking is wrong: it stays wherever it reproduces what the engine draws, which is
most of a map. See [[an-optimisation-is-not-a-skippable-departure]] and
[[valve-parity-is-the-first-principle]].
