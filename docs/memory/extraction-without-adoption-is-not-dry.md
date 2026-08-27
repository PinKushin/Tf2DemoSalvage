---
name: extraction-without-adoption-is-not-dry
description: A shared helper nothing forces you to use is a suggestion; count the duplicates after extracting, not before.
metadata:
  type: project
---

Extracting a helper and leaving the copies in place does not remove the duplication — it adds one
more implementation. Measure the count **after** the extraction; if it did not fall, nothing was
fixed.

**Why:** measured here on 2026-08-27. `SdkReference.GameInstall` was extracted precisely because
test files each carried their own Steam-library lookup, and its own remarks record the count at the
time: **seventy-three**. When the shared skip helper was added (D109), the count was **ninety-four**
— the extraction had been done, the old copies had been left as "not this change's business", and
new files had gone on copying a neighbour rather than finding the shared type. Eight files used
`GameInstall`; ninety-four did not.

Worse, the copies had **diverged**. Three of them were independent locators, and two accepted
`TF2_FOLDER` on the folder merely existing while `GameInstall` required the recogniser VPK inside
it. So a stale or mistyped `TF2_FOLDER` made two suites run against the wrong install while every
other suite skipped — the divergence was invisible because each copy worked.

The cost landed as two red CI runs the same day: with the check written from memory in every file,
one of them said `Assert` where it should have said `Assert.Ignore`, and CI — the only machine
without the game — reported a missing install as a defect in the renderer.

**How to apply:**

- When extracting, either delete the duplicates in the same change or **record the count and the
  deadline**. "The existing copies are worth migrating and are not this change's business" is how
  seventy-three became ninety-four.
- `grep -c` the pattern the helper replaces, before and after. A DRY change that does not move that
  number has not happened yet.
- Prefer a shape the compiler enforces over one that must be remembered. Deleting the duplicate type
  outright is what makes the next file use the shared one — a `using` that fails to compile is a
  reminder, a helper sitting in another project is not.
- Suspect divergence before assuming the copies are equivalent. They all pass; that is what let them
  drift.

Related: [[one-place-or-it-drifts]] is the rule this is the failure mode of —
[[replace-all-is-a-claim-about-every-site]] is why the sweep has to be done by hand, and
[[a-skip-is-not-a-pass-or-a-failure]] is what the copies were getting wrong.
