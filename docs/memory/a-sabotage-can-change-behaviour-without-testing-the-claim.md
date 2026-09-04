---
name: a-sabotage-can-change-behaviour-without-testing-the-claim
description: A mutation that reddens something is not proof it exercised the claim you meant.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T08:56:33.333Z
---

**A sabotage that changes behaviour is not automatically one that tests the claim.** Check that the
mutation reproduces the SPECIFIC defect, and that the test which reddened is the one whose claim it
attacks.

B313, 2026-09-04. The claim was that dereferencing an entity handle by MASKING resolves a dangling
handle to a real, different entity (B231). The sabotage written for it was:

```csharp
(handle & 2047) is var student        // irrefutable — always true
```

`is var` always matches, so the guard became unconditionally true and the method returned null for
every input. That reddened the HAPPY PATH — the same shape as any broken-key mutation — while the
invalid-handle test it was aimed at stayed green. **Behaviour changed, a test failed, and nothing
about masking was exercised.**

The real sabotage keeps the mask AND the lookup: `int student = handle & 2047;` then look it up.

**And it exposed that the test could not have failed anyway.** Masking gives slot 2047; nothing
occupied it, so the lookup found nothing and answered null — **the same null correct code returns,
for a different reason**. Correct and broken agreed on every observation. The fix was the fixture:
put a bystander at 2047 on the other team, so masking answers RED where resolving answers nothing.

**The general rule: for an absence claim, the wrong answer must be REACHABLE.** A test asserting
"resolves to nothing" is vacuous unless something is standing where the broken code would look —
the same shape as [[an-empty-search-needs-a-control]], applied to a dereference.

**A subagent that flags its own sabotage as inconclusive is doing the job.** It would have been
easy — and wrong — to substitute an edit that produced the expected red.
