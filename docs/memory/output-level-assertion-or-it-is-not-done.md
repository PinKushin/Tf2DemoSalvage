---
name: output-level-assertion-or-it-is-not-done
description: "A green unit suite says a component works when called; it never says production calls it. Three no-ops shipped this way in one session — covers the three test levels and why only driving the real UI catches missing wiring, why a code move breaks the assignment that used to be implicit, why a superseded type's tests keep passing while its replacement has none, and why extracting a helper without deleting the copies is not DRY."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:41.843Z
---

**Anything that produces output is not finished until an assertion has read that output on a real
demo.**

**Why:** a unit test proves a component behaves when handed values IT chose. It says nothing about
whether production calls it. Three no-ops shipped this way in one session, each with a fully green
suite — a game-event annotation matching the wrong type; a kill feed resolving through a renderer
that returns strings when the lookup was numeric; a decoded, retained, unit-tested playback rate
never read by production, so every animation played at rate 1. Each found by LOOKING at the output,
none by the tests covering the code.

## "Decoded but not drawn" is NOT this bug

Owner's correction, after fog/gestures were misfiled as instances: *"the decode should basically be
completely done for the most part... I required a real demo to be parsed to our quake code then
recompiled byte identical."* So decode is finished by design; a long list of decoded-but-undrawn
values is the architecture working, not a defect.

| | what happened | how found |
|---|---|---|
| a no-op (this entry) | production was SUPPOSED to read a value and didn't | looking at output |
| not yet drawn | decode complete by design, drawing not started | reading the backlog |

**What tells them apart: whether anything CLAIMED the feature was done.** For fog, something did
(conformance tests counted as parity in the doc without an implementation) — that's the defect worth
filing (B139), a gap ledger claiming parity for nothing.

**How to apply:** write component tests as usual, then add ONE assertion against the rendered
artefact for a corpus demo — the only test that can fail when wiring is wrong. Verify by
manipulation: break the wiring, watch the output test go red while unit tests stay green.

Distinct from [[measure-the-output-not-the-capability]] (a report built from a predicate) — this one
is about the test SUITE, where the failure is a feature that never ran.

Related: [[fixtures-are-the-weak-point]], [[logs-are-the-debugger]], [[decode-must-be-total]],
[[engine-accepts-authored-demos]].

---

## `three-test-levels-and-the-third-is-missing`

**A feature can have eleven conformance tests and three real-data tests and still do nothing.**
B145: spectator target cycling was declared, bound to keys, covered by three tests — with no production
code reading it. Clicking cycled nothing. The tests weren't wrong; they asserted a binding table held
what it should, and it did — nothing about a binding table says whether anything CONSULTS it.

**Three levels needed, always the third that's skipped:**
1. Conformance (from source, before code) — what the engine does.
2. Real data (a corpus demo) — the rules meet reality. Both pass on a feature nothing invokes.
3. The wiring: drive the REAL thing, click the real button, ask whether the code ran. **Only this
   level can fail when wiring is removed.**

**Verify level 3 by removing the wiring and watching it, and only it, go red.**

**Choosing the level-2 specimen matters too** — a solo-recording demo would have passed cycling tests
while measuring nothing (a cycle finds one target and stops, indistinguishable from a broken search).
See [[author-the-specimen-the-corpus-lacks]].

Related: [[measure-the-output-not-the-capability]].

---

## `a-moves-regressions-are-wiring`

**Moving code doesn't break the code. It breaks the assignment that used to be implicit.** Extracting
~1,100 lines out of a form (B188, B193): every regression was the same shape and NOT ONE was a logic
error — a call dropped, an assignment never made, an upload never wired. **The viewer suite reported
620/620 green through all three.**

**Why logic is safe and wiring isn't:** a moved method's body is covered by tests written with it, and
the compiler catches a broken call — but `new X(y)` written inline becoming a property assignment
means a property nobody SETS is null, a legal state a guard already handles ("no demo open yet"
cannot be told apart from "nobody wired this").

**The audit, mechanical:** enumerate every settable collaborator on extracted types, count assignments
in the caller. Zero is a regression; one is usually right; two-three means several lifetimes, each
needing checking separately.

**Three more passes that each found something:** diff log STRINGS before/after (normalising
interpolations) — found a lost column; diff the moved BODY against the original, not its shape —
found code moved inside a timer it had been outside of; check a counter that kept its NAME kept its
MEANING.

**How to apply:**
- A null/default collaborator must REPORT itself once there's work it would have done — the null
  object stays (a real object beats a null field, D83), guarded on
  there being something to do (not firing from an idle viewer).
- Assign a demo's sources in ONE place, where the demo arrives, not wherever each collaborator is
  constructed.
- Run the audit at the END of a move, not only when something looks wrong.

Related: [[logs-are-the-debugger]], [[a-partial-thin-view-is-worse-than-none]].

---

## `a-superseded-type-keeps-its-tests`

**When a type is superseded, call sites move and tests don't.** The old type keeps a green suite
describing behaviour nothing executes; the new type inherits responsibility with zero coverage.
Measured (B206): `FreeLookState` had eleven tests and zero production callers; `FreeCameraController`
(D66 created the first, D90/D91 replaced it) had zero tests and ran the ACTUAL mouse look, written
longhand with a duplicated constant.

**Why worse than ordinary dead code:** dead code with a passing suite is a FALSE NEGATIVE — "is the
drag tested?" answers yes, correctly, about the wrong object.

**How to apply:**
- When superseding a type, grep the old one for production callers before leaving it — zero callers
  plus a test file is the signature.
- Migrate coverage SELECTIVELY (some cases were already covered on the live path elsewhere; port only
  the uncovered ones) and let the arithmetic (net test count) be checked by a floor.
- A floor drop is the moment to JUSTIFY a deletion, not a step to get past — the comment recording
  which tests went and why nothing was lost is what makes the deletion reviewable later.

Related: [[most-of-a-decoder-is-untested]], [[one-place-or-it-drifts]], and B196, where two shipped
features were only ever assigned `null` and the compiler couldn't see it either.

---

## `extraction-without-adoption-is-not-dry`

**Extracting a helper and leaving the copies in place adds one more implementation, it doesn't remove
duplication.** Measure the count AFTER extraction; if it didn't fall, nothing was fixed. A shared
install-locator helper (`GameInstall`) was extracted at seventy-three call-site copies; by the time a
related helper was added (D109), the count was NINETY-FOUR — old copies left as "not this change's
business", new files kept copying a neighbour instead of finding the shared type.

**Worse, the copies had DIVERGED** — some accepted a folder merely existing where the shared type
required a recogniser file inside it, so a stale env var made some suites run against the wrong
install while others skipped, invisible because each copy worked alone. Forty of the ninety-four
weren't even copies of the pattern — bare hardcoded paths working on exactly one computer.

**How to apply:**
- When extracting, delete duplicates in the same change, or record the count and a deadline.
- `grep -c` the pattern before and after — a DRY change that doesn't move the number hasn't happened.
- Prefer a shape the compiler enforces (delete the duplicate type outright) over one that must be
  remembered.
- Sweep with counts as the control — a moved test-total number means a test quietly stopped running.

Related: [[one-place-or-it-drifts]], [[edit-files-with-the-file-tools]],
[[read-the-trx-total-not-the-console]].
