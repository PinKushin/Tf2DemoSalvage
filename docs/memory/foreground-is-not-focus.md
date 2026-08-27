---
name: foreground-is-not-focus
description: Everything about focus here — the foreground is not focus, a Panel cannot hold it, and the UIA flag that asks the wrong window.
metadata:
  type: project
---

**A window holding the foreground is not the same as something inside it holding keyboard focus, and
only the second delivers a key.**

**Two memories were merged into this one on 2026-08-27** — `a-panel-cannot-hold-focus` and
`focus-check-asks-the-wrong-window`. They are three layers of one subject: whether a key arrives,
whether a surface can receive it, and whether the test can tell. Keeping them apart also hid a
dangling link — `a-panel-cannot-hold-focus` pointed at the focus-check entry with a leading "the"
in the slug, which never matched anything.

## The foreground half

Full screen in the viewer hid the playlist, which is the control that has focus from window open,
leaving the form with no focused child — the one still-visible control, `_viewport`, was a plain
`Panel` and not selectable. The window kept the foreground the whole time and `ProcessCmdKey` never
ran, so Escape and F11 did nothing until the user alt-tabbed away and back.

Fix in `MainForm.SetFullScreen`: `ActiveControl = null; Focus();`. The clear is load-bearing —
`ActiveControl` still points at the hidden playlist and focusing a container walks to its active
control.

*"The window lost focus"* was the natural reading and it was wrong in its detail, which sent two
hypotheses to their deaths first — a hidden menu strip swallowing F11's shortcut, and the
border-style change recreating the HWND. `Activate()` was already in the code from an earlier round
of the same symptom and is not enough, because **activation is not focus**.

**When a key does not arrive, log whether it reached the handler at all *before* any guard** — that
one line separates "went to another window", "went nowhere", and "arrived and was ignored", which
are three different investigations. `ForegroundProbe` and `MainForm.FocusHere` write the foreground
owner and `ContainsFocus`; read both. Hiding a focused control is the general hazard, not a
full-screen one. Full write-up in `docs/findings/29-full-screen-focus.md`.

---

## `a-panel-cannot-hold-focus` — `TabStop` does nothing

`MainForm._viewport` was a plain `System.Windows.Forms.Panel` with `TabStop = true`. **That setting
did nothing.** WinForms decides whether a control can be focused with `ControlStyles.Selectable`,
which `Panel` clears; `TabStop` cannot override it, and `Focus()` returns quietly having done
nothing.

**The consequence is not a missing focus rectangle. It is that focus never describes what the user is
doing.** Clicking the 3D view left focus wherever it was — the playlist — so the window's idea of
"the focused control" was a list while somebody flew a camera across a map.

Two defects came out of it, a fortnight apart:

- **B212**: `ProcessCmdKey` reached over whatever held focus, because nothing else could. `Space` in
  the search box toggled first person; `Home` moved the map instead of the caret.
- **B216**: the shortcut guard asks *what does the focused widget use*, which is only meaningful if
  focus tracks intent. Adding list type-ahead against a permanently focused playlist swallowed
  `SPACE` and every letter **globally** — the camera stopped switching, `w`/`a`/`s`/`d` stopped
  flying, four UI tests failed at once.

The fix is a `ViewportPanel : Panel` doing `SetStyle(ControlStyles.Selectable, true)`, focusing on
mouse-down, plus `ActiveControl = _viewport` at construction. `ShowFocusCues` is false — a dotted
outline over a 3D view reads as a rendering fault.

**The tell, and it was written in the file already.** A comment beside the wheel handler said *"A
Panel does not take focus, so its own wheel event may never fire"* — recorded as a workaround for one
symptom rather than as a fact with consequences. A note explaining why a control behaves oddly is
worth re-reading as a bug report.

**Before building anything that reasons about focus, check the surface can actually hold it** —
`Focus()` returning false, or `ActiveControl` never changing when you click, is the signal.

---

## `focus-check-asks-the-wrong-window` — the UIA flag is about the wrong element

**`Window.Properties.HasKeyboardFocus` is the top-level window's own flag, not "will a keystroke
reach this application".** It is false whenever focus sits on a child control — which on any real
form it always does, because something takes focus when the window opens. On the viewer it is the
playlist.

Measured 2026-08-13: the UI helper checked it, found false on a window that was foreground and
perfectly typable, clicked the title bar, then waited out a five-second `Retry` for a flag that
could never become true. Every test that focused the window paid five seconds. **The UI suite went
from 13 seconds of test time to 2 by fixing the check alone** — no application change.

Worse than the cost: it logged `focus acquired: False`, so an unrelated failure elsewhere in the
run read as *"the viewer would not take focus"* and sent two sessions after the wrong subsystem.

**The working form**, FlaUI:

```csharp
AutomationElement? focused = _automation.FocusedElement();
return focused is not null &&
    focused.Properties.ProcessId.ValueOrDefault == _application.ProcessId;
```

This is a *wrong instrument*, not flake — the measurement was perfectly faithful to a variable
nobody cared about. **That is the failure mode that survives a sabotage check**, because the check
only proves a test CAN fail.

**On any UI suite, ask whether the focused ELEMENT belongs to your process** rather than whether a
particular element reports focus. Applies beyond this repo — any WinForms or WPF suite driven
through UIA has the same trap.

---

Related: [[measure-every-hop-before-blaming-one]], [[logs-are-the-debugger]],
[[a-test-can-outlive-its-design]], [[three-test-levels-and-the-third-is-missing]],
[[a-negative-retry-is-a-sleep]].
