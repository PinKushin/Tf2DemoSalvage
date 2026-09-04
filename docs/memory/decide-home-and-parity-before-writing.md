---
name: decide-home-and-parity-before-writing
description: Three days of rework against "a couple of new things" — answer where it lives and what Valve does BEFORE writing, not in a later refactor.
metadata:
  type: feedback
---

The owner, 2026-08-25:

> "every time we implement a couple of new things, we have to go back and fix all the archetectural
> and parity issues, the going back over and over is the annoying part."

**The ratio is the evidence.** The initial MVP switch took a day. Undoing the drift that grew back
took another. Bringing the same code to Valve's conventions took a third. Three days of rework
against a couple of features.

**Why:** the project already requires a conformance test with its citation BEFORE implementation,
and that works. Nothing said the same about **structure**, so two questions got answered by
proximity instead — new code went where its neighbours were and copied what was already there.
`AddViewmodel` was written into `MainForm` for exactly that reason, and it cost three viewmodel bugs
their testability.

**How to apply — answer both before writing, and put the answers in the commit:**

1. **What is the engine's arrangement for this job?** One grep of `source-sdk-2013`. If Valve models
   it as a game system, a presenter or a per-frame pass, take that shape and preferably that name —
   `SoundscapeSystem` (`C_SoundscapeSystem`) and `UpdateClientSideAnimations`
   (`C_BaseAnimating::UpdateClientSideAnimations`) are named for their originals so the parity is
   checkable by the next reader rather than rediscoverable.
2. **Which project does it belong in, and can it be tested there?** "The viewer, because that is
   where the caller lives" is the drift starting. A misplaced type takes its tests with it, and those
   tests are what stop the next regression.

**Both are cheap before and expensive after.** A divergence written into a NEW type reads as
deliberate, which is harder to spot than one left in an old method. The going-back is not caused by
the refactors — it is caused by the two minutes not spent when the code was written.

Recorded as a decision in `docs/DECISIONS.md` under D89.

Related: [[valve-parity-is-the-first-principle]], [[refactors-are-when-to-check-parity]],
[[conformance-test-before-implementation]], [[output-level-assertion-or-it-is-not-done]].
