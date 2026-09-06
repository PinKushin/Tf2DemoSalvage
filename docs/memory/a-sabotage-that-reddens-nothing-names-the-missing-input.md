---
name: a-sabotage-that-reddens-nothing-names-the-missing-input
description: A sabotage that reddens nothing is a result, not a failed check; it names an input no test supplies, and sometimes the code that input would have caught.
metadata:
  type: feedback
---

**Break the code on purpose, watch nothing fail, and the instinct is to call the sabotage a dud.**
It is a measurement. It says: for every input the suite supplies, correct and broken predict the
same observation. That is a statement about the INPUTS, so write the input rather than strengthen
an assertion.

**Three outcomes, and they need different answers:**

1. **The behaviour is genuinely unreachable from any input.** `.phy` files all end with a trailing
   `editparams` block, so removing the reader's final block-close changes no count on any shipped
   file — and the line is still load-bearing, because the format does not require that block. The
   answer was an authored specimen ending on its last joint. See
   [[author-the-specimen-the-corpus-lacks]].
2. **The two positions are genuinely equivalent today.** IVP's event loop tests its stop flag after
   the fire; moving that test to the top of the body reddens nothing, because the only thing between
   the two placements is a pure read. Keep the engine's placement, say out loud that no test can
   tell, and note what would make it observable again.
3. **The code is wrong and the missing input is what would have shown it.** This is the one that
   gets missed, because a null result feels like nothing happened.

**The worked example of the third, 2026-09-06.** `ShouldCollide` tested a ragdoll's
`selfcollisions` flag before consulting its pair list — which reads as obviously right. Removing
that test reddened nothing. The reason was that the only case exercised had an EMPTY pair list,
where both readings agree; and chasing the missing input showed the check was **wrong**. Nothing in
the engine ever calls `DisableCollisions`, so a pair enabled BEFORE the flag went off stays enabled
for the ragdoll's life. The flag belongs in the parser, where it decides what enters the list, and
testing it a second time would have dropped pairs the engine keeps.

**Why:** a sabotage measures the suite's sensitivity, and insensitivity has a cause. Two of the
three causes are about the tests; the third is about the code, and it is indistinguishable from the
others until the distinguishing input is written. Treating a null result as "nothing to do" throws
away the only signal that pointed at it.

**How to apply:** never move on from a sabotage that reddens nothing. Ask what input would separate
correct from broken, and then write it — the act of constructing it is what exposes case 3. If no
such input can exist, say which of case 1 or case 2 it is, in the source, next to the line. Related:
[[most-of-a-decoder-is-untested]], [[a-duplicated-guard-cannot-be-tested]],
[[two-accumulators-cannot-see-order]], [[unreachable-can-be-proved-not-just-observed]].
