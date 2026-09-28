---
name: american-english-for-new-text
description: "New identifiers, comments and docs in this repo use American spelling; pre-existing British spellings stay, because converting them is not worth a refactor."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-11T00:00:29.172Z
---

New text uses American English (color, behavior, center, normalize, gray, initialize, optimization).
Pre-existing British spellings stay, including public names (`HdrColourScale`, `VmtMaterial.Colour`)
— editing a file for another reason is not an occasion to convert its old lines.

**Why:** D158. Owner: *"this repo should use american english"*, then, told the existing British
spellings run to thousands of lines: *"preexisting can stay, its not worth a refactor, expecially in
comments and prose"*.

**How to apply:** write new identifiers/tests/comments/docs/commits in American spelling; don't
bulk-convert or tidy an old line just because it's next to a new one. When a new name would sit one
letter from an existing one, choose a different word rather than the near-twin — `EntityState.RenderRgb`,
since `RenderColor()` already returns the packed field there.

Related: [[one-place-or-it-drifts]], [[test-naming-convention]].
