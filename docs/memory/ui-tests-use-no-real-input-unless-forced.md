---
name: ui-tests-use-no-real-input-unless-forced
description: UI tests drive the viewer through automation patterns, never the real mouse/keyboard/foreground, unless no other route exists; a test that fails when the owner moves the mouse is a bad test.
metadata:
  type: feedback
---

UI tests must not use the real mouse, synthesized keys or the foreground window unless there is no other option.

**Why:** owner, 2026-10-13: "i cant even move the mouse while the ui is running or it fails, that doesnt work, tests whould not be using the real mouse unless they have to, like no other option". A 7-minute suite that needs the desktop untouched was voided by his use four times in two days (YouTube, Copilot, typing a reply). He called a test that dies in a menu check from that "a bad test".

**How to apply:**
- Menu, menu item, button: `ViewerApplication.Activate` (D215), not FlaUI `.Click()` (which moves the real cursor) and not `Mouse.Click`.
- **A button is sent a posted `BM_CLICK`, not `InvokePattern`** (measured 2026-10-13). Invoke on a WinForms button whose handler opens a modal dialog never returns, and while it is outstanding the viewer answers no other UIA call, from any thread: eight tests fell with 0x80131505. A menu item's Invoke does return.
- **A WinForms menu that opens takes the foreground**, with no click sent. Menus need nothing from the desktop but still pull the viewer forward; a button press does not.
- Text into a field of the viewer: `ValuePattern`. Text into the Win32 common dialog's name box stays typed (`TypeRealText`): on CI a ValuePattern set is ignored ([[a-uia-expanded-menu-eats-later-keys]]).
- Reading state: UIA properties; never require focus to read.
- Real input only where the thing under test IS input (a key binding reaching the window, a click on the viewport). It goes through `RealClick`, `PressRealKey` or `TypeRealText`, with a comment at the call saying why.
- Count on 2026-10-13, before: 11 `.Click(`, 1 `Mouse.Click`, 5 `Keyboard.*`, 17 `PressKey` calls. After D215: 1 `Mouse.Click` (`RealClick`, 3 viewport callers), 0 FlaUI `.Click()`, 3 `Keyboard.*` (all inside `PressRealKey` and `TypeRealText`), 16 `PressRealKey` calls, 2 `TypeRealText` calls.
- Full screen through View > Full screen instead of F11 was tried in `MapFullScreenUiTests` and put back: entered by the item and left with Escape, the shared viewer drew no position readout (`PositionReadoutUiTests` 362 == 362). Unexplained; possible viewer defect.

Related: [[ui-tests-run-every-time]], [[a-uia-expanded-menu-eats-later-keys]], [[take-the-desktop-lock-dont-defer]].
