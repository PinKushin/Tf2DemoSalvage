---
name: portable-half-and-adapter-half
description: The owner wants view logic copy-pasteable across front ends; split it so the rules sit in net10.0 and only a tiny adapter names the toolkit.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:55:17.070Z
---

Owner: *"try to use cross platform stuff so we wont have to change it if we change the front end"* —
and, told it could live anywhere: *"it can go in the mainform, since it is view logic not domain, i
just want what you use to hopefully be able to be copy pasted instead of needing to redo it from
nothing."*

**Why:** not layering purity (`MainForm` is explicitly allowed) — a future front-end change should be
a port, not a rewrite.

**How to apply:** split view logic in two — the RULES (which keys a slider uses, how a drag maps to
degrees) go in a plain `net10.0` project that **cannot** reference `System.Windows.Forms`, so the
compiler enforces portability rather than a comment ([[a-partial-thin-view-is-worse-than-none]]:
enforcement is the TFM, not the file); the ADAPTER (names the toolkit's types) stays tiny in the view.

Worked example, B216: a key-mapping helper takes a STRING key name and never sees a toolkit `Keys` value —
the whole binding stack (`ViewerAction`, `KeyBindings`, `ConfigConsole`) is already this shape.

**Check it by TFM, not by reading:** `grep -rl "System.Windows.Forms" <portable project>` should match
only prose in doc comments — if it compiles under `net10.0`, it's portable by construction. Related:
[[ask-if-the-view-must-hold-it]], [[conformance-test-before-implementation]], [[no-hardcoded-controls-ever]].
