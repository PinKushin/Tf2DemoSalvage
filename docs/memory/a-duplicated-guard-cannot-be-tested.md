---
name: a-duplicated-guard-cannot-be-tested
description: When a guard repeats a check something downstream already makes, no input distinguishes keeping it from dropping it — enrich the input so the mutation has somewhere to show, never strengthen the assertion.
metadata:
  type: feedback
---

**A test for a guard that is redundant with a downstream guard cannot fail, and no assertion fixes
it.** Measured on 2026-09-05 while building B353.

The code reproduced Valve's `if ( iBodyOverride > -1 && iBodyStateOverride > -1 )` before calling
`SetBodygroup`. The test wore an item declaring the part but no state and asserted a body of 0.
**Deleting the state clause reddened nothing** — and not because the fixture chose a poor value:
`SetBodygroup` already returns the body unchanged for a negative value, in this code and in Valve's
(`shared/animation.cpp:863` returns early for an out-of-range value). There is no integer for which
the guarded and unguarded versions disagree. The clause is behaviourally dead **in the engine too**.

**Why:** the test asserted "nothing happened", and nothing happening is what BOTH versions do. This
is the `CLAUDE.md` **wrong condition** trap, and the instinct it defeats is the usual one — the
assertion was already exact.

**The fix is to the INPUT.** Setting a part to 0 is only observable from a body that is not already
0, so the item was given a named entry as well: it hides `hat`, a correct read leaves 1, and a
reader treating the missing state as 0 puts the part back and reads 0. That version reddens alone
under exactly the mutation it was written for, which was then verified by making it.

**Ask this before writing the assertion**, and it is a different question from "is my assertion
tight enough":

> Is there an input for which the correct and broken versions predict different observations?

**Keep the guard.** It is where Valve writes it and the citation is the point — but document it as
redundant with the downstream check rather than leaving the next reader believing it load-bearing.
That is [[a-guard-you-remove-may-be-the-mechanism]] read from the other end: there the narrow version
refused something, here it refuses nothing.

**The sabotage that found this was a subagent's**, run against tests that all passed. Related:
[[unreachable-can-be-proved-not-just-observed]], [[most-of-a-decoder-is-untested]].

**Analyzers at error level make a lazy sabotage impossible**, which cost three attempts in the same
session: `&& false` is S1125, dropping a call left a private method unreferenced (S1144), and
`x = 0` on an int field is CA1805. A sabotage must compile, so pick one that keeps every symbol
used — OR-ing `int.MaxValue` into a flag set, or `+ 500` on an index. See
[[tf2demosalvage-build-gates]].
