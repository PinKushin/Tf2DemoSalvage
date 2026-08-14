---
name: mutation-score-is-a-ratchet
description: tf2-core's mutation baseline is 52.16% as of 2026-08-13; it may rise and must never fall.
metadata:
  type: project
---

The `tf2-core` mutation baseline is **52.16%**, measured on mutation-box on **2026-08-13** at sha
`0eb27f9`: 1,574 killed, 405 survived, 11 timeout, **1,049 with no coverage at all**.

**It is a ratchet in intent, not a gate.** The owner was explicit that it will not simply stay put:
the run happens daily or weekly, new code lands in between, and a week's work almost always lands
mutants nothing kills yet. So the score dropping is expected and routine — what is not optional is
**noticing and fixing it** rather than letting it drift down run after run.

**And the baseline itself is provisional.** The owner set it while iterating hard on features, and
said plainly that a large feature landing can drop it a long way — in which case the number gets
re-set rather than defended. What is being ratcheted is attention, not a specific figure.

That means the number is a thing to check after each run and act on, not a threshold to fail a
build against. Treating it as a hard gate would either block ordinary work or, worse, invite
whatever change makes the number go up fastest.

**Do not quote 80 as a floor here.** That figure came from general guidance and was repeated in
this project as though it were a rule; the owner corrected it — no baseline had ever been
established for this codebase, and now the baseline is the measured one above.

**The 1,049 uncovered mutants are the interesting number, not the 52.** A mutant with no coverage
is code no test reaches at all, which is a different problem from a test that fails to notice a
change — see [[most-of-a-decoder-is-untested]], where three of four sabotages survived because the
corpus never exercises those paths. Raising the score by writing tests for reachable-but-unkilled
mutants is worth doing; chasing uncovered ones may just mean deleting dead code.

Runs land in `~/measurements/<stamp>-<sha>-tf2-core/` on mutation-box, scheduled at 09:00 daily
(`~/tf2demosalvage/build/run-measurements.sh core`). The `stryker-core` runs on the same box belong
to PokemonBattleJournal — do not read their score as ours. Prune only by the `.owner` marker.
