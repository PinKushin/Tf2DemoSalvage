---
name: a-fixture-can-be-outside-the-algorithms-domain
description: A near-miss result can mean the fixture asked for something the algorithm cannot do.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T07:18:53.998Z
---

**When a numeric test misses by a little, check whether the fixture asked for something the
algorithm is entitled to refuse — before assuming the code is wrong.**

B311, 2026-09-04: an IK lock test predicted the effector back at y = 0 and measured 0.054. That
looks exactly like a real IK bug. It was not. The fixture's chain had links of ±2, giving a reach of
20.40, and moving the root five units put the pinned target 20.62 away — **out of reach**, so
`Studio_SolveIK` correctly placed the foot as close as it could get. Links of ±5 give 22.36 and the
same test lands exactly.

**The tell is a SMALL, non-zero error in a solver, clamp, or search.** Those all have a domain and
all degrade gracefully at its edge, which is precisely what makes the failure look like a bug:
a wrong implementation and a refused input both land near the answer.

**Ask what the algorithm does at its limit, then check whether the fixture is inside it.** Reach,
range, a `StraightEnough` refusal, a clamp, a maximum iteration count — each is a documented edge,
and a fixture built without arithmetic tends to sit on one because the round numbers a person picks
are also the degenerate ones.

**Build fixtures with SLACK, and say in the comment how much.** The same file already needed a chain
that was not perfectly straight, because the solver refuses one at full extension — two different
edges of one algorithm, both hit by the obvious fixture.

Cousin of [[predictions-must-not-sit-on-a-boundary]] — there the arithmetic about the input was
wrong, here the input itself was outside the domain. Both present as "the code is slightly off".
