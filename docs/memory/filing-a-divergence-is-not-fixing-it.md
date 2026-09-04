---
name: filing-a-divergence-is-not-fixing-it
description: Measuring a divergence as rare decides its PRIORITY, never whether parity is owed — the owner's standing rule is fix it, and a well-written OPEN entry is the most convincing way to not do the work.
metadata:
  type: feedback
---

**A measurement that a divergence is rare decides what to do FIRST. It never decides whether to do
it.** The standing rule is explicit: *"If a divergence is found, FIX IT. Report what was done, not a
menu of what could be."*

The owner, 2026-09-04, on finding five items filed as OPEN in one session: *"OMG did you take more
shortcuts instead of just getting 100% valve parity?"* and *"seriously if you are not going to do it
all, at least give it to a subagent"*. Both were fair. The items were:

- the death animation branch, measured at ~1 corpse in 100 and left open
- two wearable skips, dismissed as needing "the item schema at a point that has neither" — the schema
  was one accessor away on a class the same layer already held
- the gold, ice and zombie overrides, not even read

**The trap is that a good OPEN entry looks like diligence.** Each had a citation, a measurement and a
statement of what was not established. That is exactly what makes it persuasive, and it is still the
work not being done. A divergence with a beautiful writeup is a divergence.

**Delegate rather than defer.** The agent cap is one at a time, so run one and keep working on
something else in the meantime; split by FILE OWNERSHIP so the concurrent edits cannot collide, and
say in the prompt which files are off limits. A subagent finished the item-schema half — with its own
tests and sabotage rounds — in the time it took to do the death animation by hand.

**And the rarity measurements are still worth taking**, just not as an exit. They caught that two of
the three taunt-kill exclusions in `CreateTFRagdoll` are unreachable, and that 0 of 11,497 items can
resolve to `LOADOUT_POSITION_HEAD`. Both are real findings about the engine that only came out of
implementing the thing.

Related: [[an-unrecoverable-input-is-not-an-open-choice]] (the same shape: a hard input is not a
licence to skip the logic), [[measure-the-gate-before-building-the-branch]] (what the measurement IS
for), [[valve-parity-is-the-first-principle]], [[a-divergence-is-asked-not-documented]].
