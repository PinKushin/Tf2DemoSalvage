---
name: an-unused-method-may-be-the-engines
description: An analyzer's "remove the unused private method" can mean a call site was lost, not that the code is dead — ask what engine function it transcribes before deleting it.
metadata:
  type: feedback
---

**`error S1144: Remove the unused private method 'X'` is a question, not an instruction.** It says
nothing called `X`; it does not say `X` should not be called. When the method transcribes engine
behaviour, an unused one means a CALL SITE was lost — which is a divergence, and deleting the method
makes it permanent and invisible.

Measured, B382: mid-refactor the build reported

```
error S1144: Remove the unused private method 'Renormalise'.
error S1144: Remove the unused private method 'Neighbours'.
```

`Neighbours` was genuinely superseded. `Renormalise` was **Valve's `TimeFixup_Hermite`**
(`interpolatedvar.h:1372`) — the respacing that makes a hermite spline usable on unevenly spaced packets
— and the rewrite had simply stopped calling it. One keystroke from being committed as a cleanup, with a
green suite and a build at zero warnings.

**Why:** this codebase's private methods are largely transcriptions of named engine functions, and an
analyzer cannot know that. The reachability question ("does anything call it") and the parity question
("should something call it") have the same symptom and opposite answers.

**How to apply:** before deleting anything the analyzer calls unused, read its doc comment for a Valve
name or a `file:line`. If it has one, the fix is to restore the call, not to delete the method — and the
right home for it is usually where the engine keeps it, which in that case was on the history class
rather than on the track. Two more losses from the same refactor turned up the same way, and neither
raised a warning at all: the causality gate (B94) and the wake scheduler still describing a structure the
sampler no longer read. **A refactor's real damage is the calls it stops making, and only one of those
three was visible to a tool.**

Related: [[filing-a-divergence-is-not-fixing-it]], [[the-base-is-not-the-behaviour]],
[[a-guard-you-remove-may-be-the-mechanism]], [[the-prune-keeps-two-stale-entries]].
