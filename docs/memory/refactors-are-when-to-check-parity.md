---
name: refactors-are-when-to-check-parity
description: Read the engine's arrangement while extracting a type — the check is nearly free then, and a divergence written into a new type is harder to see later.
metadata:
  type: feedback
---

The owner, 2026-08-25, during the `ShowMoment` extraction:

> "the refactors are perfect times to double check stuff like that"

**Why:** the code is being moved anyway, so reading Valve's arrangement for the same job costs one
grep at exactly the moment the new shape is being decided. And the failure mode is worse than
skipping the check elsewhere — **a divergence written into a NEW type is harder to spot afterwards
than one left in an old method**, because a freshly extracted class reads as deliberate.

It also works in reverse: an extraction is a chance to find a divergence nobody was hunting, because
the boundary being drawn has an equivalent in the engine, and comparing the two asks "why is ours
shaped differently" while changing the answer is still free.

**Found this way within minutes of starting**, and it decided the design:

| ours | Valve |
|---|---|
| `ShowMoment(double tick)` reads the camera off the form | `BuildRenderablesList( const SetupRenderInfo_t &info )` is TOLD the camera |
| — | `SetupRenderInfo_t` carries the output list, render origin, render forward, render frame |

`clientleafsystem.h:75` and `:169`. That ambient-state coupling is precisely what makes `ShowMoment`
untestable without a window, which is B188.

**How to apply:**

- Before choosing the shape of an extracted type, find the engine's equivalent and read what it is
  PASSED versus what it reaches for. Parameters versus ambient state is usually the whole difference
  between testable and not.
- Do it during the extraction, not after. Afterwards it is a rewrite instead of a naming decision.
- Record what the comparison found even when ours turns out to match — the check having been done is
  part of what a later reader needs.

Related: [[valve-parity-is-the-first-principle]], [[parity-is-the-search-not-the-defence]],
[[conformance-test-before-implementation]], [[nothing-is-closed]].
