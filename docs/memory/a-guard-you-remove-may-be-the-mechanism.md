---
name: a-guard-you-remove-may-be-the-mechanism
description: "Widening a gate to reach more cases can turn a skipped proxy into a wrong answer; the engine's own refusal was what the gate reproduced."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:36.557Z
---

`WorldRenderer.ApplyProxies` built its material-variable table only when carrying `$colortint_base`.
Implementing `YellowLevel` (writes `$yellow` on 7,570 materials, most unpainted) meant widening the
gate to every material — looked like removing an accident.

**It was the mechanism.** With the table always present, `SelectFirstIfNonZero` on a material lacking
`$colortint_base` read it as ZERO, took the wrong branch, overwrote modulation constants. Five
reflection pixel tests went red. A comment beside the gate had already said so: *"a
`SelectFirstIfNonZero` reading a missing variable as zero would paint every unpainted cosmetic
black"* — read as about the seed, not the gate.

**What the gate reproduced, in the engine, is a REFUSAL:** `CFunctionProxy::Init` calls
`pMaterial->FindVar(name, &foundVar, false)` and returns false when undeclared; a proxy whose `Init`
fails never binds. Correct rule: **a proxy whose named sources don't exist does not run** — now an
explicit test in each handler.

**How to apply:** before widening a condition, ask what the narrow version was REFUSING — look for
the engine's own refusal (a failed `Init`, an early return), not just what it was allowing. Related:
[[parity-is-the-search-not-the-defence]]. Keep pixel tests unrelated to the feature under change —
nothing in the proxy/paint suites caught this; the reflection tests on weapon models did, because
they measure a whole draw.
