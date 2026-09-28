---
name: ui-tests-run-every-time
description: "The UI suite runs on every change, not just UI ones, and any UI addition gets a UI test."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:34:51.506Z
---

**Run the UI suite every time**, not only when the change looks UI-shaped, and add a UI test with any
UI addition. The defects that reached the owner in one session (full-screen misbehaving at 1fps, black
props) all stayed invisible to unit suites, which stayed green throughout.

**How to apply:** `run-exclusive.ps1 dotnet test <UI project>` — takes the desktop, tell the owner
first. Where a property genuinely can't be observed from the automation tree, say so in the commit
rather than substituting a check that only restates the diff. Related: [[tests-before-codecs]],
[[nunit-shared-fixture-is-the-standard]].

## The phase-scoped exception has expired

A former exemption for "the UI is still small" is closed — twenty tests have caught a silent
full-screen collision that broke it for days (B165), three wiring regressions shipping at 620/620
green (B193), and a log-level silencing.

**Worked example of a UI test that was WORSE than none:** a test proved a click reached a handler by
requiring the handler's success branch to log — but the handler's FAILURE branch also proves the
wiring, and once a precondition tightened, the legitimately-failing branch (a solo demo with nobody to
follow) went red against a viewer working correctly. Owner: *"that seems like a stupid test for a pov
demo... it doesnt actually check anything."* Once B171 required a target to be alive and drawn, this
surfaced against a solo POV specimen. Fixed by asserting the handler RAN, not that it
succeeded.

---

## `a-shared-viewer-test-restores-what-it-changed` — never depend on running last

**A UI test that changes the shared viewer's state must restore it, and may not depend on running
last** — the viewer is launched once for the whole assembly, so state persists across tests, and
NUnit's cross-fixture order isn't reliable. Owner: *"running it first and then restoring state by
pausing and seeking back is fine to do actually."*

**How to apply:** restore in the test itself (not a teardown a failure skips), to an EXACT known
value. When restoring isn't possible (no seek action exists to drive), say so and drop to a lower-
level test instead (e.g. asserting wiring on a headless form). **Open and deliberately undecided**
(B224): sharing setup across tests by leaning on run order was raised and not adopted — the owner's
own caveat was that it "can be flaky... just an idea."

Related: [[output-level-assertion-or-it-is-not-done]], [[read-the-trx-total-not-the-console]],
[[ui-tests-run-every-time]]#a-negative-retry-is-a-sleep, [[nunit-shared-fixture-is-the-standard]].

---

## `a-negative-retry-is-a-sleep` — a retry that must never succeed burns its whole window

**A retry whose condition must NEVER become true always runs the full window** — it's a sleep
disguised as synchronisation, and it's WEAKER than a real check: "the app froze" satisfies it exactly
as well as "the input was correctly ignored". Owner spotted it from outside: *"it sits on the free cam
and i dont see antyhing happen for a little while."*

**Fix: wait for a POSITIVE signal that the app processed the input and carried on** (a per-frame log
line), THEN assert the negative — a frozen viewer now fails the wait instead of passing the assertion.
Measured: suite went 22s → 9s.

**How to apply:** any wait whose predicate is the thing about to be asserted FALSE is this bug — find
a positive liveness signal, synchronise on that. Related: [[read-the-trx-total-not-the-console]],
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `foreground-is-not-focus` — the foreground is not focus, a Panel cannot hold it, and the UIA flag asks the wrong window

**A window holding the foreground is not the same as something inside it holding keyboard focus, and
only the second delivers a key.** Full screen hid the control that held focus, leaving nothing
selectable — the window kept foreground the whole time, so keyboard shortcuts silently stopped
working until an alt-tab away and back. Fix: explicitly clear and refocus on the mode change.

### `a-panel-cannot-hold-focus` — `TabStop` does nothing

A plain `Panel` clears `ControlStyles.Selectable`; `TabStop` cannot override it. Consequence: focus
never tracked what the user was doing (clicking the 3D view left focus on a list), causing keys typed
in a search box to reach global shortcuts (B212: `Space` toggled first person, `Home` moved the map
instead of the caret), and list type-ahead to swallow WASD/space globally (B216: adding type-ahead
against a permanently focused playlist stopped the camera switching and WASD flying, four UI tests
failed at once). Fix: a custom panel subclass enabling `Selectable` and focusing on mouse-down.

**Before building anything that reasons about focus, check the surface can actually hold it** — a
comment already in the file ("a Panel does not take focus") was a workaround note, not read as a bug
report with consequences.

### `focus-check-asks-the-wrong-window` — the UIA flag is about the wrong element

**A top-level window's own "has keyboard focus" flag is false whenever focus sits on a CHILD
control**, which on any real form it always does. A UI helper checking that flag waited out a
five-second retry per test that could never succeed — fixing the check alone cut suite time from 13s
to 2s, no app change. It also silently mislabeled an unrelated failure as "the viewer won't take
focus".

**Fix: check whether the focused ELEMENT (via automation) belongs to your process**, not whether the
window itself reports focus. Applies to any WinForms/WPF suite driven through UIA.

Related: [[nothing-is-closed]], [[logs-are-the-debugger]], [[a-test-can-outlive-its-design]],
[[output-level-assertion-or-it-is-not-done]].
