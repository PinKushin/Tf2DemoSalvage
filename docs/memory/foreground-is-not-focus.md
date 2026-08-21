---
name: foreground-is-not-focus
description: A window can hold the foreground with nothing inside it focused, and then no keystroke arrives.
metadata:
  type: project
---

**A window holding the foreground is not the same as something inside it holding keyboard focus, and
only the second delivers a key.** Full screen in the viewer hid the playlist, which is the control
that has focus from window open, leaving the form with no focused child — the one still-visible
control, `_viewport`, is a plain `Panel` and not selectable. The window kept the foreground the whole
time and `ProcessCmdKey` never ran, so Escape and F11 did nothing until the user alt-tabbed away and
back.

Fix in `MainForm.SetFullScreen`: `ActiveControl = null; Focus();`. The clear is load-bearing —
`ActiveControl` still points at the hidden playlist and focusing a container walks to its active
control.

**Why:** "the window lost focus" was the natural reading and it was wrong in its detail, which sent
two hypotheses to their deaths first — a hidden menu strip swallowing F11's shortcut, and the
border-style change recreating the HWND. `Activate()` was already in the code from an earlier round
of the same symptom and is not enough, because activation is not focus.

**How to apply:** when a key does not arrive, log whether it reached the handler at all *before* any
guard — that one line separates "went to another window", "went nowhere", and "arrived and was
ignored", which are three different investigations. `ForegroundProbe` and `MainForm.FocusHere` write
the foreground owner and `ContainsFocus`; read both. Hiding a focused control is the general hazard,
not a full-screen one. Full write-up in `docs/findings/29-full-screen-focus.md`. Related:
[[measure-every-hop-before-blaming-one]], [[focus-check-asks-the-wrong-window]], [[logs-are-the-debugger]].
