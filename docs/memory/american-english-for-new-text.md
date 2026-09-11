---
name: american-english-for-new-text
description: New identifiers, comments and docs in this repo use American spelling; pre-existing British spellings stay, because converting them is not worth a refactor.
metadata:
  type: feedback
---

New text in this repository uses American English: color, behavior, center, normalize, gray,
initialize, optimization. Pre-existing British spellings stay where they are — including in public
names such as `HdrColourScale` and `VmtMaterial.Colour` — and editing a file for another reason is not
an occasion to convert its old lines.

**Why:** the owner's decision, D158. *"this repo should use american english"* — then, told that the
British spellings already here run to thousands of lines, all written by earlier sessions: *"preexisting
can stay, its not worth a refactor, expecially in comments and prose"*.

**How to apply:** write new identifiers, test names, comments, documents and commit messages in American
spelling from the start. Do not bulk-convert, and do not tidy an old line just because it is next to
a new one. When a new name would sit one letter away from an existing one, choose a different word
rather than the near-twin — `EntityState.RenderRgb`, because `RenderColor()` already returns the packed
field there.

Related: [[one-place-or-it-drifts]], [[test-naming-convention]].
