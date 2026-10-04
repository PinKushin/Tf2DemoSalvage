---
name: a-uia-expanded-menu-eats-later-keys
description: "Expanding a WinForms menu through UIA leaves keyboard menu mode on after a dialog; every later key in the shared viewer is eaten. UI tests open dialogs by clicking buttons. Also: CI's file dialog ignores ValuePattern text, and a long status line reads empty."
metadata:
  node_type: memory
  type: project
---

**Never open a dialog in the UI suite by expanding the File menu through UIA.** `ExpandCollapse.Expand`
on a WinForms `ToolStripMenuItem` puts the form in keyboard menu mode, and after the dialog it opened
closes, that mode is still on: every later keystroke goes to the menu, whatever has focus. In the
shared-viewer session that failed ten key-press and full-screen tests after `ExportCompileUiTests`
(CI red from c3b05a7b to 0b0ae93a).

**Why:** measured, not inferred, on branch `fix/ci-ui-dialog` (2026-10-03):
- With the test ignored, the later tests passed (run 37138141067).
- With only "File menu, Export, Cancel" and no export, nine of them failed (run 37147488078).
- Reproduced locally: SPACE switched the camera before the menu and did nothing after, Escape included.
- Moving focus back to the viewport did not help. On CI the next test started with the viewport
  focused and the viewer in the foreground, and SPACE still did nothing (run 37155302709).
- Collapsing the menu afterwards did not help either.

**How to apply:**
- Open dialogs with `ViewerApplication.Click(buttonId)`. It is a real click that takes and verifies the
  foreground, and it enters no menu mode. Do not use UIA `Invoke` on the button, because the click
  handler calls `ShowDialog` synchronously and the call blocks.
- In the common file dialog on the GitHub Windows runner, **type** the path into the focused name box.
  A path set through `ValuePattern.SetValue` reads back correctly right up to Save, yet the dialog
  saves its default name in its default folder (`Documents`). That held for a `D:` path, with one
  visible Save button and the viewer in front. Locally SetValue works, so a local pass proves nothing
  here.
- Wait on the viewer's log line, not the status bar. A long path makes the status sentence wider than
  the strip, WinForms drops the label, and `StatusText()` reads `''`. Log lines end in CR, so trim
  before comparing.
