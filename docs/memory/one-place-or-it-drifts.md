---
name: one-place-or-it-drifts
description: A fix belongs in exactly one place; anything copied or kept in step between files goes out of sync.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:37:54.835Z
---

Every fix should land in a SINGLE place — copying information into two files, or keeping two sites in
step by hand, drifts. Owner: *"if we run into a place we are having to copy or synchronize the
information between files, they are going to get out of sync."* Said after watching exactly that:
players all faced north because the wrong property was read in one method while a comment naming the
correct one sat in another method of the same file.

**How to apply:** put the fix where the data is PRODUCED, not at each point it's consumed. When a
feature will be reached from two paths (a POV camera and a free camera), build one thing both call
with a flag, not two implementations that agree today.

**The rule in the other direction: restating a CONVENTION is the defect.** B400: a coordinate map was
spelled out again instead of calling the existing helper that already held it with its citation — and
the restatement was the FORWARD map instead of its inverse, so every collision hull loaded rotated
180°. The fix was to delete the restatement, not correct it.

Related: [[logs-are-the-debugger]] (how drift gets found), [[fixtures-are-the-weak-point]] (why a
second implementation can't check the first).

---

## `police-the-document-not-just-the-test` — the audit's last instruction is the one skipped

**An audit naming three things to do gets two done.** A conformance-gap test correctly deletes stale
"not implemented" test rows when a feature lands, but its third instruction ("delete its section in
docs too") was skipped four times — a reader planning off that doc would build a feature twice.

**It immediately caught B128** — a parameter's section carried "Implemented 2026-08-21" in its BODY
and "every model is dull" in its HEADING, nobody reads the body. **Fix: police the artefact, not the
reminder.** A heading naming a parameter the production census confirms implemented is now a red
test — two documents contradicting each other, one enforced.
Headings only, never prose, are the claim (prose may discuss an implemented parameter without
claiming it's missing). A structural control (asserting the section is found and non-trivial) stops
a renamed heading from silently checking an empty list.

**Generalises past this file** — wherever a check tells a human to update prose, the prose is what
won't get updated. Point the check at the prose. Same shape as
[[the-denominator-decides-what-can-be-lost]]. The loop closed on B332, the first gap where the
audit's whole instruction was carried out in one change.

---

## `a-fold-leaves-its-paths-behind` — rewrite the PATHS, not just the wiki links

**A fold that rewrites wiki links has done the easy half.** Reconciling this directory with a mirror,
~240 file-path citations (source comments, docs, CI, gate scripts) named files that no longer existed
after earlier folds — every prior fold rewrote its links and none its paths.

**Why paths get missed:** a wiki link sits in the same directory as the file it names, so a grep for
the file finds the link too. A path in a C# comment is found by nothing once the file's gone.

**The convention that keeps a fold citable:** the host keeps a section whose heading carries the
folded slug in backticks (`` ## `slug-name` — … ``), so it's cited externally as
`docs/memory/host-file.md#slug-name`.

**Before deleting a folded file:**
- `grep -rn '<slug>' --exclude-dir=.git .` — every hit outside `docs/memory/` is a path to rewrite.
- Word-diff the standalone against its new section first — a fold can silently drop a paragraph.
- The frontmatter is restamped on every save by the memory tool, so compare BODIES when checking
  whether content survived, not frontmatter.

Related: [[instrument-bugs-outnumber-decoder-bugs]].
