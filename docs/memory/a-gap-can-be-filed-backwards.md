---
name: a-gap-can-be-filed-backwards
description: "We do not do X" and "we do X unconditionally" produce the same next task, so read the code before trusting a handoff that names a missing feature.
metadata:
  type: project
---

**A handoff that says a feature is missing may mean the opposite: that it is applied everywhere.**
Both produce the same next task — *implement X* — and only one of them is a starting point that
leads anywhere.

Measured 2026-08-28. `docs/HANDOFF.md` filed two-pass models as the next task, in these words:

> *"This project has no two-pass concept and draws every model once."*

`Device3D.RenderFrame` drew every model **twice**, and `WorldRenderer.DrawModel` filtered each pass
by material — which is `STUDIORENDER_DRAW_OPAQUE_ONLY` / `_TRANSLUCENT_ONLY` verbatim. The machinery
was right and complete. What was missing was the *question* it should have been asking: which models
does the engine split? Answer, measured over TF2's archives: **88 of 14,109**.

So the real defect was the opposite of the filed one. The renderer was doing MORE of the feature
than the engine, and the fix removes work rather than adding it.

**Why:** the note was written from the SDK alone. Reading Valve's code tells you what the engine
does; it cannot tell you what this project already does, and the gap between them is the only thing
a task list is about. The owner's read: *"that previous session didnt really research and look into
the 2 pass much that im aware"*.

**How to apply:** before implementing anything a handoff, RISKS entry or comment calls missing,
**grep the repository for it first** — for the mechanism, not only the name. Two-pass drawing was
absent under every spelling of "two pass" and present as `bool blended`. A capability can be
implemented under a name nobody thought to search, which is the same reason
[[an-empty-search-needs-a-control]] exists: an empty grep is a fact about the grep.

The tell is that the feature's *machinery* turns up while its *decision* does not. Correct
implementation, no caller that chooses — that is a feature applied unconditionally, not one that is
missing. Related: [[measure-the-output-not-the-capability]], and
[[an-impossibility-claim-expires]] for the same shape in the other direction.
