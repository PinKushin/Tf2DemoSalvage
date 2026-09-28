---
name: branch-granularity-is-fine-here
description: "This repo wants many small branches and sub-branches - the features decompose, so a branch per coherent piece is the default, not per feature"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:23.882Z
---

**Default to a branch per coherent piece, sub-branch anything larger.** Owner's instruction,
2026-08-12. Reason: this project's features decompose cleanly — "open demos" was really four
separable pieces (finder library, header reader, playlist wiring, UI tests), each reviewable,
revertable, nameable alone.

**A branch whose name has stopped describing its contents is the signal.** Happened twice in one
session (`git add -A` swept viewer code into a docs commit; a camera branch accumulated three
unrelated features) — both caught by noticing the name no longer fit, both cheap to fix since nothing
had merged.

**How to apply:** before starting, ask what the smallest reviewable thing is, branch for that. When a
second concern appears mid-branch, finish and merge the first rather than carry both.

See also `branch-per-task-not-straight-to-main` (assistant's global memory), the weaker rule this
refines.

## Broken on 2026-09-04 — the failure mode is a session with no task boundary

Four commits went straight to `main`, a fifth sat uncommitted, before the owner asked "have you been
branching?" — no. **The condition:** a continuous parity loop where every defect was found BY the
prior fix, so each step felt like the same thread and no branch-creation moment ever arrived. The
"name stopped describing contents" signal can't fire when no name was ever chosen — the check has to
be at the START of a piece of work. Repair was cheap only because nothing had been pushed (D140).

## `branch-scope-and-toolchain-prefs` — what a branch OWNS

**Split a branch when it grows a second, genuinely unrelated concern** — not every artefact that
isn't source code. A feature branch **owns everything that serves it**: memory entries, docs, and
research feeding the feature belong there. Sub-branches merging into a parent feature branch are
welcomed for larger features.

**Origin matters for citation:** this rule was inferred from the owner merely *asking* whether a
branch name still made sense, then written up as his instruction — he corrected that (*"i didnt say
anything… do not infer my intentions more than what i say"*), then separately endorsed it explicitly.
Genuinely his now, but became so by being proposed and accepted.

Refinements: research tangents need no branch unless they produce committed work; docs stay current
alongside code rather than batched, though docs-only branches still happen when genuinely docs-only.

**Dropped 2026-09-08:** a note about Rust toolchain placement — moot, since No Rust is a hard
constraint; fuzzing here is SharpFuzz on .NET (D8), not Rust.
