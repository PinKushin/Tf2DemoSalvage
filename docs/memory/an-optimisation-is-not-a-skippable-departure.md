---
name: an-optimisation-is-not-a-skippable-departure
description: Valve's optimisations are not decoration; do not classify one as skippable without checking the number already in the risk register.
metadata:
  type: feedback
---

Proposing to match Valve's bone pipeline, the assistant offered one departure: keep `SetupBones` as
the entry point but **skip Valve's threaded bone setup**, on the grounds that it is "an optimisation
rather than semantics". The owner rejected it:

> *"go for the threading too, full parity thats an optimization valve did for vary good reason, the
> bones are heavy, they need speed."*

**Why:** an optimisation in shipping engine code was written against a frame budget by people
measuring it. The default assumption is that it earns its place. And in this specific case the
evidence was already in the repository and went unchecked — B99 records posing at **~420 ms of every
second**, which is precisely the cost Valve's pre-pass targets. The proposal to skip it was made
without looking at the number that had already been measured.

**How to apply:** when tempted to file something as "merely an optimisation", first find the
measurement that would decide it — this project usually has one already. Then treat a departure from
an optimisation as needing the same evidence as a departure from a behaviour ([[name-the-trade-before-fixing-valve]],
and D86 in `docs/DECISIONS.md`).

Note also what the threading actually is, because the name misleads and that was part of the
misjudgement: it is a **speculative prefetch of last frame's expensive roots**, run between simulate
and render, not a parallel-for over the draw loop. See `docs/findings/35-the-bone-pipeline-audit.md`
§8 and D88. Related: [[parity-is-the-search-not-the-defence]], [[measure-the-output-not-the-capability]].
