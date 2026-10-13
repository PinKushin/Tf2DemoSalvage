---
name: A grouped sabotage can redden for the wrong reason
description: Several deliberate breaks in one build save builds, but one break can redden tests that belong to another; predict the failing set per round and give any confounded test a round alone.
type: feedback
---

**When several deliberate breaks share one build, predict which tests each reddens BEFORE running, and compare.**
A round whose failures are "most of them" proves nothing about any one test.

**Why:** thin view slice 2, `DecalEffects`. One break of a round of thirty was `tick < _modelDecalTick - 5`. The
field starts at `int.MinValue`, the subtraction wrapped to a large positive, the guard returned on every first step,
and 27 tests reddened for it, not for their own breaks. Two tests that should have reddened PASSED (their breaks made
a decal appear, and nothing appeared), which is what gave it away. A second case in the same round: removing the
water guard reddened nothing, because another break in the round (`victim.Team != bullet.Team`) skipped the bullet
first.

**How to apply:**

- Write the expected failing tests beside each break. After the run, a test that passed against its own break is a
  finding about the round, not about the test.
- A break must not change control flow for everyone: no arithmetic on a sentinel (`int.MinValue`, `-1`), no break
  earlier on the path of another break's subject.
- Two breaks on one path go in different rounds. A test not seen red by the end gets a round of its own.
- Restore by the inverse replacement and check the file's hash against the one taken before; a break whose
  replacement text is not unique in the file (`return true;`, `||`) cannot be restored by search.
- **A test that stays green against its own break, alone, is guarded a layer down** (slice 3, `ImpactParticles`): a
  sky bullet drew nothing with the presenter's guard removed because `ImpactEffects.Perform` refuses sky too. Give the
  test a case only the layer under test stops, or delete it; do not report it as seen red.
- **Check what the assertion reads before the break.** `ImpactEffectRunner.Count` counts an effect whose spawn was
  refused, so "no effect runs" asserted on it failed green code; the quads are the reading.
- A fixture default can confound every case: an unposed player's hull on the bullet's line stopped the bullets of
  three tests about something else. Put the default off the path and opt in.
- **Restore a round's breaks in REVERSE order** (slice 5). Two breaks touching one statement: break 1 removed a line,
  break 2 changed its twin; restoring in break order found the text twice and refused, leaving the file broken.
  Write restore as `reversed(pairs)`, and write the file only after every assertion has passed.
- A guard the code cannot compile without (`x is not { } y`) cannot be reddened; delete the test that only restates it.
- **A break that only DELETES has no inverse a replace can find** (slice 6): `("x;", "")` restores as
  `("", "x;")`, the count of the empty string is not one, the restore stops, and the NEXT round runs on the broken
  file and reddens for it. It happened twice in one loop of three rounds. Write a deletion with its neighbouring line
  on both sides; assert no side is empty before touching anything; check every step applies before applying any; and
  make the runner refuse to start after a round that did not restore. Keep a COPY from before the break, not only a
  hash: a hash file rewritten by the next round records the broken state as clean.
- **A break's REPLACEMENT must be unique too, not only its original** (slice 7): `_cameraMode = mode;` became a line
  that already stood three times, and the inverse found four. Simulate the round's break and then its restore in
  memory, and compare with the file, before writing anything; a break that fails that check is rewritten with a
  neighbouring line on both sides.
