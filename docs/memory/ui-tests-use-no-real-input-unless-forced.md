---
name: ui-tests-use-no-real-input-unless-forced
description: UI tests drive the viewer through automation patterns, never the real mouse/keyboard/foreground, unless no other route exists; a test that fails when the owner moves the mouse is a bad test.
metadata:
  type: feedback
---

UI tests must not use the real mouse, synthesized keys or the foreground window unless there is no other option.

**Why:** owner, 2026-10-13: "i cant even move the mouse while the ui is running or it fails, that doesnt work, tests whould not be using the real mouse unless they have to, like no other option". A 7-minute suite that needs the desktop untouched was voided by his use four times in two days (YouTube, Copilot, typing a reply). He called a test that dies in a menu check from that "a bad test".

**How to apply:**
- Menu item, button: `InvokePattern` / `ExpandCollapsePattern`, not FlaUI `.Click()` (which moves the real cursor) and not `Mouse.Click`.
- Text into a dialog field: `ValuePattern.SetValue`, not `Keyboard.Type`.
- Reading state: UIA properties; never require focus to read.
- Real input only where the thing under test IS input (a key binding reaching the window, a drag on the viewport). Say so in a comment at the call, and keep those tests few and grouped.
- The one the owner breaks is `ViewerApplication.Click(element, what)`: it takes the foreground, then `Mouse.Click` at the element's centre, so a moved mouse or a focus change in the gap lands the click elsewhere. FlaUI's own `.Click()` moves and clicks in one step; the owner has not broken those. Fix the helper first.
- Count on 2026-10-13: 11 `.Click(`, 1 `Mouse.Click`, 4 real `Keyboard.*`, 15 focus/foreground calls across `Tf2DemoSalvage.Viewer3D.UiTests`.

Related: [[ui-tests-run-every-time]], [[a-uia-expanded-menu-eats-later-keys]], [[take-the-desktop-lock-dont-defer]].
