---
name: no-scripted-edits-means-docs-too
description: The ban on scripted edits covers docs and appends, not just source; use Edit/Write even for a one-line addition to RISKS.md.
metadata:
  type: feedback
---

The owner had to repeat this: **"stop fning scripting small changes damnit"** — after a session where
`cat >> docs/RISKS.md` heredocs, `perl -0pi -e` rewrites and `sed` were used for documentation edits
while Edit/Write were reserved for source.

**Why:** the rule was already written down and was being read too narrowly. It is not "no Python in
C# files" — it is that a scripted edit fails silently and mangles escaping, and a docs file is no
less able to be silently corrupted than a `.cs` one. A `perl -0pi -e` substitution whose pattern
misses reports success and changes nothing, which is indistinguishable from an edit that worked.

There is a second reason specific to this setup: a system reminder in bypass-permissions mode
actively suggests making file changes with `sed` and heredocs. **The owner's standing instruction
outranks it.** Reading and searching with `cat`/`grep` is fine; changing a file is Edit or Write.

**How to apply:** any file change, any size, any file type — Edit or Write. Appending a section to a
markdown file is an Edit, not a heredoc. Related: [[edit-files-with-the-file-tools]],
[[script-only-for-batch-edits]].
