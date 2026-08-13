---
name: focus-check-asks-the-wrong-window
description: HasKeyboardFocus on a top-level window is false whenever a child holds focus, which is always
metadata:
  type: reference
---

**`Window.Properties.HasKeyboardFocus` is the top-level window's own flag, not "will a keystroke
reach this application".** It is false whenever focus sits on a child control — which on any real
form it always does, because something takes focus when the window opens. On the viewer it is the
playlist.

Measured 2026-08-13: the UI helper checked it, found false on a window that was foreground and
perfectly typable, clicked the title bar, then waited out a five-second `Retry` for a flag that
could never become true. Every test that focused the window paid five seconds. The UI suite went
from 13 seconds of test time to 2 by fixing the check alone — no application change.

Worse than the cost: it logged `focus acquired: False`, so an unrelated failure elsewhere in the
run read as *"the viewer would not take focus"* and sent two sessions after the wrong subsystem.

**The working form**, FlaUI:

```csharp
AutomationElement? focused = _automation.FocusedElement();
return focused is not null &&
    focused.Properties.ProcessId.ValueOrDefault == _application.ProcessId;
```

**Why:** this is a *wrong instrument*, not flake — the measurement was perfectly faithful to a
variable nobody cared about. That is the failure mode that survives a sabotage check, because the
check only proves a test CAN fail.

**How to apply:** on any UI suite, ask whether the focused ELEMENT belongs to your process rather
than whether a particular element reports focus. Applies beyond this repo — any WinForms or WPF
suite driven through UIA has the same trap. Related: [[a-test-can-outlive-its-design]].
