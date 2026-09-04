---
name: a-sabotage-that-reddens-nothing-names-the-missing-input
description: When a sabotage reddens no test, the finding is not "the code is fine" — it is that no fixture distinguishes correct from broken, and the sabotage tells you exactly which input is absent.
metadata:
  type: feedback
---

**A sabotage that reddens nothing is a result, not a null result.** It says the suite contains no
input for which correct and broken differ — and because you know precisely what you broke, it also
tells you what that input would have to look like. Write that test.

Twice in one session, both on code that was correct:

- **`RagdollFade`.** Removing the engine's early `return` from the visible branch left
  `Gone_ForACorpseWatchedThroughout_IsNeverTrue` green: watching a corpse from before its deadline
  re-arms the timer ahead of the clock on every call, so the stale expiry check never fires. The
  distinguishing input is a corpse first checked while visible AFTER its unseen deadline.
- **`QuatInterpBones`.** Removing `MathF.Abs` from the dot product left all four conformance tests
  green: every fixture's half-angles fall in [-π/2, π/2], so no dot product was ever negative and
  the call was a no-op on the whole suite. The distinguishing input is a trigger stored as its own
  NEGATION — the same rotation, opposite sign, raw dot −1.

Both tests looked like they covered the line. Neither could.

**The instinct to resist is strengthening the assertion.** Both suites already asserted exact
values; nothing about the assertions was weak. It is the CONDITION that was wrong — `CLAUDE.md`'s
second failure route — and no amount of tightening a prediction fixes an input for which both
answers agree.

**So the routine after writing a conformance suite is: sabotage each line the suite claims to cover,
and treat a green run as a to-do rather than a pass.** Cheap, and it found two real gaps in one day.
Delegating it works — the sabotage-verifier reached the same missing input from the opposite
direction, having watched nothing redden while I predicted it from the edit.

**Two ways the sabotage itself can be at fault, and both happened here**, so rule them out before
believing the finding:

- **It did not test the claim.** [[a-sabotage-can-change-behaviour-without-testing-the-claim]] — an
  edit that changes behaviour is not automatically one that removes the property under test.
- **It did not compile.** Strict analyzers refuse many of the obvious edits: `if (false)` is CS0162
  and orphans a constant (S1144/CA1823), deleting a branch trips S1199, and removing the only call
  to a private method makes it unused. **"It would not build" is a reason to find another edit, not
  a reason to conclude the test is weak** — the fallback branch that resisted three sabotages was
  proved sensitive by pointing it at the LAST trigger instead of the first.

**A zero from a new instrument is the same shape and gets the same treatment.** The magnitude added
to prove `QUATINTERP` mattered reported `furthest move 0 units` across ten driven bones. That is not
evidence the rule does nothing — it measured the bone's TRANSLATION, and `hlp_forearm` is a twist
that rotates about a fixed origin, so translation is identical by construction. Measuring the axes
as well gives 0.72 units at unit distance, a 42-degree twist. **An instrument written that same hour
to separate "it ran" from "it mattered" was itself measuring the one quantity that could not
change** — so before believing a zero, ask which variable actually carries the effect.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[most-of-a-decoder-is-untested]],
[[a-sabotage-must-compile]], [[boundaries-find-what-tests-cannot]],
[[it-ran-and-it-mattered-are-two-claims]].
