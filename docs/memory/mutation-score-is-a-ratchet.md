---
name: mutation-score-is-a-ratchet
description: tf2-core's mutation baseline is 52.16% as of 2026-08-13; it may rise and must never fall.
metadata:
  type: project
---

The `tf2-core` mutation baseline is **52.16%**, measured on mutation-box on **2026-08-13** at sha
`0eb27f9`: 1,574 killed, 405 survived, 11 timeout, **1,049 with no coverage at all**.

**It is a ratchet.** The owner set it as the baseline explicitly: the score may go up, and must not
go down. A change that lowers it is a regression to be explained, not a number to be noted.

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
