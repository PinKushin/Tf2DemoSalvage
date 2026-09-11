---
name: a-fold-leaves-its-paths-behind
description: "Folding a memory into another leaves every docs/memory/<old>.md path in the repository dangling — rewrite them to host.md#slug; and the memory tool restamps frontmatter on every save, so the repo copy takes the new stamp."
metadata: 
  node_type: memory
  type: project
  originSessionId: 71bbc8e9-0f7a-489c-a987-3e0867aae1fa
  modified: 2026-09-10T23:49:52.857Z
---

**A fold that rewrites the wiki links has done the easy half.** Measured 2026-09-10 while
reconciling this directory with the assistant's copy: about 240 `docs/memory/<slug>.md` path
citations in 136 files — source comments, `docs/RISKS.md`, `docs/DECISIONS.md`, findings,
`CLAUDE.md`, a skill, CI and the gate script — named files that no longer existed. About 150 had been
dangling since earlier folds; every one of those folds had rewritten its links and none its paths.

**Why the paths are the half that gets missed:** a wiki link sits in the same directory as the file
it names, so the grep that finds the file finds the link. A path in a C# comment is found by nothing
once the file goes — and a citation that arrives nowhere is the harm `build/assert-risk-citations.sh`
guards against for `B###` numbers.

**The convention that keeps a fold citable:** the host keeps a section whose heading carries the
folded slug in backticks — ``## `an-empty-search-needs-a-control` — …`` — so from outside this
directory it is cited as `docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`.
The file resolves, and the slug still greps to exactly one heading. Inside the directory it is a
wiki link to the host plus the section's name.

**How to apply, before deleting a folded file:**

- `grep -rn '<slug>' --exclude-dir=.git .` — every hit outside `docs/memory/` is a path to rewrite,
  not only the wiki links inside it.
- Word-diff the standalone against its new section first. A fold can drop a paragraph: five of the
  43 checked that day had, and the standalone is the only copy left of what was lost.

**And the frontmatter moves under you.** The assistant's memory tool rewrites a file's frontmatter
each time it saves one — it quotes `description` and adds `node_type`, `originSessionId` and a
`modified:` stamp — so after an edit on the assistant's side, the repo copy has to take that exact
frontmatter or the two differ by a line. It also leaves a file alone, undecorated, when its
`description` opens with a quoted phrase, because that is not valid YAML; quote the whole value
(`"\"…\" …"`) instead.

Related: [[one-place-or-it-drifts]], [[instrument-bugs-outnumber-decoder-bugs]].
