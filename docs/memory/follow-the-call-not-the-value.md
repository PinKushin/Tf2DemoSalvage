---
name: follow-the-call-not-the-value
description: An override applied one call deeper than the arithmetic it overrides is invisible from the arithmetic.
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T05:32:37.344Z
---

**When a function rewrites one of its own arguments, find every CALLER — do not trace the argument
forward from where it is computed.** The two searches give different answers, and the caller search
is the complete one.

`STUDIO_REALTIME` (B309, 2026-09-04) is decided inside `CalcPoseSingle`, which discards the cycle it
was handed. There are four places that hand it one, and the fourth is
`MaintainSequenceTransitions` — which computes and CLAMPS `flCycle` on the line directly above
`AccumulatePose`, so the clamp reads as the last word on that cycle. Following the cycle forward
from each site where it is computed found three of four; grepping for `AccumulatePose` found all
four.

**The general shape: the override sits one call deeper than the arithmetic it overrides.** Anything
Valve decides inside a leaf function is invisible from every caller, and callers are where our code
is organised.

**Then check each site is EXECUTED, not merely written.** Two of the four branches had tests that
passed while nothing reached them — no wire layers in the fixture, no autolayers declared. A
sabotage that reddens nothing is the only thing that says so, which is why the question to ask a
sabotage is *which* tests reddened rather than whether the right one did
([[a-defect-that-survives-its-cause-is-in-the-instrument]]).

Related: [[half-a-mechanism-is-not-parity]], [[the-half-you-have-may-be-the-wrong-half]],
[[decoding-a-field-is-not-honouring-it]].
