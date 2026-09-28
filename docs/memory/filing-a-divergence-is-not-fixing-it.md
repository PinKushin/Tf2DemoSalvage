---
name: filing-a-divergence-is-not-fixing-it
description: "Measuring a divergence as rare decides its PRIORITY, never whether parity is owed — the owner's standing rule is fix it, and a well-written OPEN entry is the most convincing way to not do the work; the same shape covers a \"still to read\" note that already diagnoses the live bug, a stale \"not implemented\" comment, a measurement written down as a ranking that expires when the numbers move, and an impossibility claim nobody re-reads once it is disproved."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:15.113Z
---

**A measurement that a divergence is rare decides what to do FIRST, never whether to do it.**
Standing rule: *"If a divergence is found, FIX IT. Report what was done, not a menu of what could
be."* Owner, on finding five items filed OPEN: *"OMG did you take more shortcuts instead of just
getting 100% valve parity?"* / *"seriously if you are not going to do it all, at least give it to a
subagent."*

**A good OPEN entry looks like diligence, and is still work not done.** A citation, a measurement,
and a statement of what's unestablished is exactly what makes it persuasive.

**Delegate rather than defer** — agent cap is one at a time; run one, keep working, split by file
ownership. A subagent finished one item (with tests and sabotage rounds) in the time it took to do
another by hand.

**Rarity measurements are still worth taking, just not as an exit** — they found two of three taunt
exclusions unreachable and 0 of 11,497 items resolving to `LOADOUT_POSITION_HEAD`.

Related: [[a-filed-design-choice-may-not-be-one]], [[valve-parity-is-the-first-principle]],
[[parity-is-the-search-not-the-defence]].

---

## `an-open-item-is-a-defect-report`

`docs/RISKS.md` correctly diagnosed the `update_baseline` flag as unimplemented — under a "Still to
read" heading, inside an already-fixed bug's write-up. Meanwhile the same symptom (spawn props
misplaced) was blamed on parenting, render mode, PVS across three investigations, two reverted merges.

**Why:** filed under a heading that reads as optional background, with nothing saying "this is broken
now". All three wrong theories were real Valve mechanisms we half-implemented, so each produced a
plausible story and genuine fixes.

**How to apply:** when a symptom matches an open item's text, read that item first. File a missing
mechanism as a numbered defect entry a symptom search will surface, not a closed investigation's tail.
See [[nothing-is-closed]], [[parity-is-the-search-not-the-defence]].

## `a-stale-not-implemented-is-a-todo-list`

**Grep for "not implemented", "not reproduced", "is a gap" before planning parity work** — a stale one
costs twice (work looks undone; the checking reader discovers the DOCUMENT was wrong). Found four
false in one pass: a sequence-layer comment claiming no implementation when B307 had just fixed a
branch of it; B82 filed as "open" (a halo or canteen sitting at the wearer's feet) when attachment
parenting already reads and applies it; a flag claimed unread eighty lines above the branch that
reads it; and a type claimed unimplemented, discovered by the same grep to have no production caller
at all.

**Why:** a comment is written when the gap is real; nothing re-reads it when the gap closes. Search
in the same session you plan from. Mirror of the entry below (impossibility claims expiring), opposite
sign.

## `a-measurement-recorded-as-a-conclusion-expires`

Three OPEN RISKS entries were stale in one session, all the same way: B157 described a substitution
already built; B254 said "every prop the tick carries is posed" when nine of 567 are; B258 quoted
"sample 2.0ms" against a measured 0.3. None was wrong WHEN WRITTEN —
what they share is a MEASUREMENT written down as a CONCLUSION. "Sample is 2.0ms" is a fact about one
build on one day; "sample is the biggest cost, fix it next" is a RANKING that expires the moment
either number moves, silently.

**Why it costs more than a wrong note:** these entries ARE the work queue — a stale one sends the next
session to re-derive a fixed problem with the authority of a written record.

**How to apply:**
- Put the runnable command beside the number — cheaper to re-run than to argue with.
- Re-measure before believing a ranking, especially "this is where the frame is".
- Separate the reading of the engine from the ranking of the work — one can die while the other stays
  correct.
- A counter reporting one of two exits reads as a failure of the whole — B254's "0.3 hidden by pvs"
  looked like an idle cull, but the frustum half simply returns first without counting; check both
  exits are counted ([[instrument-bugs-outnumber-decoder-bugs]]).

See [[read-the-trx-total-not-the-console]].

## `an-impossibility-claim-expires`

`ViewerSettings.DefaultFrameRateLimit` said `fps_max`'s default "could not be recovered from the
binary" — a separate finding had already recovered it (400, with flags) weeks earlier by
reconstructing the pooled numeric block. Both sat in the same repo contradicting each other until a
parity audit caught it.

**Why this shape survives when a wrong positive claim doesn't:** a positive claim is load-bearing
(something calls it, a test pins it); an impossibility claim is inert — nothing depends on it, so
nothing drags it back into view, and the disproving finding has no reason to look for it (it's off
doing the thing the claim said couldn't be done). The reasoning is usually correct about ONE
INSTRUMENT (the string-pool argument really is right) — what doesn't follow is "therefore unknowable".

**How to apply:** when a finding establishes something, grep for prior "cannot"/"impossible"/"no way
to"/"not recoverable" claims and retire them in the same commit. Scope such claims to the instrument
when writing them: "not recoverable from the string pool", never "not recoverable". Related:
[[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]].

## `measure-the-gate-before-building-the-branch`

**Before implementing a branch, count how many of this project's own inputs actually reach it.** TF2's
famous death-animation coin flip (`RandomFloat(0,1) > 0.25f`) reads as "a quarter of deaths animate" —
but a `switch` in front with no default routes only headshots/decapitations/backstabs there. Counted
on real demos: about one corpse in a hundred is eligible. A day on the branch would have changed
nothing visible; what actually lays a corpse down is the physics, which every corpse takes.

**The zero needs a control** — a field decoding to default looks identical to real absence. Spread
settled it (a soldier-and-demoman match had no eligible values because no sniper/spy were present).

**"Every X" from one observation is a guess wearing a quantifier** — a claim of "every class model"
came from one tick with a demoman and scout; counted over all 14,109 models, it's THREE classes.

**Rule: find the quantity that decides whether anybody can SEE it, and measure that one** — eligible
inputs for a branch, weighted vertices for a bone, drawn instances for a prop. Related:
[[parity-is-the-search-not-the-defence]], [[measure-the-route-before-building-on-it]],
[[the-denominator-decides-what-can-be-lost]].
