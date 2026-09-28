---
name: a-gap-can-be-filed-backwards
description: "\"We do not do X\" and \"we do X unconditionally\" produce the same next task, so read the code before trusting a handoff that names a missing feature."
metadata: 
  node_type: memory
  type: project
  originSessionId: 71bbc8e9-0f7a-489c-a987-3e0867aae1fa
  modified: 2026-09-10T22:55:13.580Z
---

**A handoff calling a feature missing may mean the opposite: applied everywhere.** Both produce the
same next task — *implement X* — only one leads anywhere.

`docs/HANDOFF.md` (2026-08-28) filed two-pass models as next: *"This project has no two-pass concept
and draws every model once."* `Device3D.RenderFrame` in fact drew every model TWICE, filtered by
`WorldRenderer.DrawModel` via `STUDIORENDER_DRAW_OPAQUE_ONLY`/`_TRANSLUCENT_ONLY` verbatim. Machinery
complete; missing was the question of which models the engine actually splits — measured at 88 of
14,109. The renderer did MORE than the engine; the fix removes work.

**Why:** the note was written from the SDK alone, which says what the engine does, not what this
project already does. Owner: *"that previous session didnt really research and look into the 2 pass
much that im aware"*.

**How to apply:** before implementing anything a handoff/RISKS entry calls missing, grep the repo for
the mechanism, not just the name — two-pass drawing was present as `bool blended`. An empty grep is a
fact about the grep ([[instrument-bugs-outnumber-decoder-bugs]]). The tell: machinery present,
decision (a caller that chooses) absent — that's unconditional, not missing. Related:
[[measure-the-output-not-the-capability]], [[filing-a-divergence-is-not-fixing-it]] (same shape,
other direction).
