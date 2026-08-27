---
name: a-panel-cannot-hold-focus
description: WinForms gates focus on ControlStyles.Selectable, which Panel clears; TabStop does nothing, so focus never described what the user was doing.
metadata:
  type: project
---

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

**How to apply:** before building anything that reasons about focus, check the surface can actually
hold it — `Focus()` returning false, or `ActiveControl` never changing when you click, is the
signal. Related: [[foreground-is-not-focus]], [[the-focus-check-asks-the-wrong-window]],
[[three-test-levels-and-the-third-is-missing]].
