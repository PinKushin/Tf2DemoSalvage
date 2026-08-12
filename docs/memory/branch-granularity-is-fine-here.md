---
name: branch-granularity-is-fine-here
description: This repo wants many small branches and sub-branches - the features decompose, so a branch per coherent piece is the default, not per feature
metadata:
  type: feedback
---

**Default to a branch per coherent piece, and sub-branch anything larger.** Owner's instruction,
2026-08-12, given while the viewer was being built.

The reason is the shape of the work rather than a general preference: these features decompose
cleanly and deeply. "Open demos" was really four separable pieces — the library that finds them,
the header reader, the playlist wiring, and the UI tests over it — and each is reviewable,
revertable and nameable on its own. A single `feat/open-demos` branch would have buried three of
them.

**A branch whose name has stopped describing its contents is the signal.** It happened twice in
one session: a `git add -A` swept viewer code into a docs commit on `docs/bsp-hardening`, and
`feat/topdown-camera` accumulated the transport bar, full screen and an action row. Both were
caught by noticing the name no longer fit, and both were cheap to fix only because nothing had
merged yet — a soft reset to main and three properly-named branches.

**How to apply:** before starting, ask what the smallest thing worth reviewing on its own is, and
branch for that. When a second concern appears mid-branch, finish and merge the first rather than
carrying both. Merging often is what keeps this cheap.

The owner notes PokemonBattleJournal will want the same once deck comparison, deck building and
PTCG Live log parsing land — it is not there yet, this repo is.

See also [[branch-per-task-not-straight-to-main]] in global memory, which is the weaker
"branch at all" rule this refines.
