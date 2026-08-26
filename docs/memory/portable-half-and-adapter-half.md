---
name: portable-half-and-adapter-half
description: The owner wants view logic copy-pasteable across front ends; split it so the rules sit in net10.0 and only a tiny adapter names the toolkit.
metadata:
  type: feedback
---

Asked for a shortcut guard, the owner added:

> *"try to use cross platform stuff so we wont have to change it if we change the front end"*

and, when told it could live anywhere:

> *"it can go in the mainform, since it is view logic not domain, i just want what you use to
> hopefully be able to be copy pasted instead of needing to redo it from nothing"*

**Why:** the goal is not layering purity — they explicitly allowed `MainForm`. It is that a future
front-end change should be a port, not a rewrite. "View logic, not domain" is a real category, and it
still deserves to survive the view.

**How to apply.** Split any piece of view logic in two:

- **The rules** — which keys a slider uses, how a drag maps to degrees, what a readout says. These
  are UI *conventions*, true in WinForms, Avalonia, WPF, GTK and HTML alike. Put them in
  `Tf2DemoSalvage.Presentation`, which targets plain `net10.0` and therefore **cannot** reference
  `System.Windows.Forms` — the compiler enforces the portability rather than a comment asking for it
  (see [[a-partial-thin-view-is-worse-than-none]]: enforcement is the TFM, not the file).
- **The adapter** — the part that names the toolkit's types. Keep it in the view and keep it tiny.
  `MainForm.FocusKind()` is ten lines mapping WinForms controls onto a five-value `FocusedWidget`
  enum; every toolkit has a text field, a slider, a list and a button.

Worked example, B216: `WidgetKeys.Keeps(FocusedWidget, keyName)` holds the rules and takes a *string*
key name, so it never sees a `Keys` value. The binding stack was already this shape —
`ViewerAction`, `KeyBindings` and `ConfigConsole` are all in `Presentation` with keys as strings, and
only `KeyNames` translates.

**Check it by TFM, not by reading.** `grep -rl "System.Windows.Forms" managed --include=*.cs` over
`Presentation` should match only prose in doc comments; if it compiles under `net10.0`, it is
portable by construction. Related: [[ask-if-the-view-must-hold-it]],
[[decide-home-and-parity-before-writing]], [[no-hardcoded-controls-ever]].
