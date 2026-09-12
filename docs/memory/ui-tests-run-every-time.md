---
name: ui-tests-run-every-time
description: "The UI suite runs on every change, not just UI ones, and any UI addition gets a UI test."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:34:51.506Z
---

**Run the UI suite every time**, alongside unit and integration, not only when the change looks
UI-shaped. And **anything added to the UI gets a UI test added with it.**

Owner's instruction, given after a session where the UI suite was run twice in several hours of
viewer work.

**Why:** the viewer is the product here, and the defects that reached the owner this session were
all in it — a borderless mode that behaved as exclusive, full screen at one frame a second, props
that drew black. None were caught by the unit suites, which stayed green throughout. The existing
full-screen UI test also opened no demo, so it had no map and could not distinguish the fast build
from the slow one; that was fixed by giving it a demo, which is the same lesson in miniature.

**How to apply:** `run-exclusive.ps1 dotnet test <UI project>` — it takes the desktop, so it holds
the machine-wide lock, and the owner should be told before it starts. When adding a control, a
mode, a menu item, or a rendering path, add the UI assertion in the same change rather than
promising it later. Where the property genuinely cannot be observed from the automation tree — z
order against another application, for instance — say so in the commit instead of substituting a
check on the flag, which only restates the diff. Related: [[tests-before-codecs]],
[[nunit-shared-fixture-is-the-standard]].

## The phase-scoped exception has expired, 2026-08-26

There used to be a companion entry saying the UI suite was optional *while the UI was small*. It is
not small any more — twenty tests, and they have earned their keep: the F11 collision that silently
broke full screen for days (B165), three wiring regressions that shipped at 620/620 green (B193), and
a per-second diagnostic that a log-LEVEL change silenced while its unit tests stayed green. The
exception is closed and this entry is the whole rule.

**The worked example it carried is worth keeping**, because it is the reason a UI test can be worse
than none. `Click_TheCycleTargetButton_ReachesTheSpectatorCode` counted the log line
`"following entity N"` to prove a click reached `CycleTarget`. That line is written only when the
target search SUCCEEDS; the other branch writes `"nobody else to follow at this tick"`, and **both
prove the wiring**. So the test asserted "the click reached the handler" by requiring "and it found
somebody" — a fact about the demo and the tick, not about the code. Once B171 required a target to be
alive and drawn, the solo POV era specimen the UI session opens legitimately produced no target, and
the test went red against a viewer the owner was watching work correctly. His verdict: *"that seems
like a stupid test for a pov demo or a demo with a single player, it doesnt actually check
anything"*.

Fixed by counting the `[spectate]` area instead of either message — which also sharpened the negative
control, since in the free camera `CycleTarget` returns before logging anything, so the count proves
the handler never RAN rather than merely that it found nobody.

---

## `a-shared-viewer-test-restores-what-it-changed` — never depend on running last

**A UI test may change the shared viewer's state, and if it does it restores it. It may not depend
on running last.**

**Why:** `ViewerSession` launches ONE viewer for the whole assembly, deliberately — a runtime, a
Direct3D device against a real adapter and a hundred-megabyte map read, paid once. Everything a test
changes is therefore seen by every test after it, and NUnit's ordering across fixtures is not
something to lean on.

The owner, 2026-08-29, on where autoplay should be tested:

> *"problem with the test, if we play other tests will fail, it basically has to be the last test
> and theres no way to set that, if it was first then it wouldnt be an issue, but last requires you
> actually set everything to a set order"*

and then allowing the alternative:

> *"'running it first and then restoring state by pausing and seeking back' is fine to do actually"*

**How to apply:** restore in the test itself, not in a teardown that a failure skips, and restore to
a value the next test can name — not "roughly back". The reason to be exact here: this suite opens
at tick 1900 because the recorder is ALIVE there and dies at 2008, so "near 1900" silently breaks
every viewmodel test after it.

**When restoring is not possible, say so and drop to a lower level.** Autoplay is not tested in the
UI suite for a specific reason rather than a general one: **the viewer has no seek action a test can
drive.** The scrub bar does not support the RangeValue pattern, and `ViewerAction` has `PlayPause`
and go-to-start but nothing that reaches a tick, so the restore cannot be written at all. The wiring
is asserted on a real `MainForm` with no window instead (`LaunchOptionWiringTests`). If a seek
command is ever added — Source spells it `demo_gototick` — this becomes writable.

**Open and deliberately undecided** (B224): the owner also raised sharing setup ACROSS tests by
leaning on the deterministic run order — enter first person once, run three tests there, leave —
with his own caveat that it *"can be flaky at times and requires you to reason about the programs
state so you have to make it a finite state machine or you will never reason it"*, and then *"idk if
thats what we should do actually, just an idea"*. Do not treat that as adopted. The measurement that
would settle it — what the mode transitions actually cost against an 11-second suite — has not been
taken.

Related: [[output-level-assertion-or-it-is-not-done]],
[[read-the-trx-total-not-the-console]], [[ui-tests-run-every-time#a-negative-retry-is-a-sleep]],
[[nunit-shared-fixture-is-the-standard]].

---

## `a-negative-retry-is-a-sleep` — a retry that must never succeed burns its whole window

The standing rule is *"synchronise on the condition, never on the clock — no `Thread.Sleep`, no
`Task.Delay`, no 'usually long enough'"*. Three UI tests broke it without containing either call:

```csharp
Retry.WhileFalse(() => Count(Spectated) > before, TimeSpan.FromSeconds(2));
Count(Spectated).ShouldBe(before, "the free camera does not spectate anybody");
```

**A retry whose condition must NEVER become true always runs the full window.** It is a sleep, and it
reads as synchronisation, which is why it survives review. One of them even carried a comment
defending it: *"the claim is that nothing happens, so the only honest instrument is to give it the
same window the positive test gets and then look."*

That is wrong twice over. It costs the whole window on every green run — the owner spotted it from
outside, watching the suite: *"it sits on the free cam and i dont see antyhing happen for a little
while"* — and it is **weaker** than a synchronised check, because "the app froze" satisfies it
exactly as well as "the input was correctly ignored".

**There is always something to wait for: evidence the app processed the input and carried on.** Here
that is `viewmodel pass skipped`, written once per frame while the camera is not first-person. Wait
for it to advance, then assert the negative — which now means something, because a frozen viewer
fails the wait instead of passing the assertion.

Measured: the UI suite went **22s to 9s**, and no test now exceeds 1.04s. Two of the three seconds
saved were pure clock.

**How to apply:** any `Retry`/`WaitFor` whose predicate is the thing you are about to assert is
FALSE is this bug. Find a positive signal that proves the app is alive and past the input — a frame
counter, a log line the loop writes, a state that must change — synchronise on that, and assert the
negative afterwards. Related: [[read-the-trx-total-not-the-console]],
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `foreground-is-not-focus` — the foreground is not focus, a Panel cannot hold it, and the UIA flag asks the wrong window

**A window holding the foreground is not the same as something inside it holding keyboard focus, and
only the second delivers a key.**

**Two memories were merged into this one on 2026-08-27** — `a-panel-cannot-hold-focus` and
`focus-check-asks-the-wrong-window`. They are three layers of one subject: whether a key arrives,
whether a surface can receive it, and whether the test can tell. Keeping them apart also hid a
dangling link — `a-panel-cannot-hold-focus` pointed at the focus-check entry with a leading "the"
in the slug, which never matched anything.

### The foreground half

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

### `a-panel-cannot-hold-focus` — `TabStop` does nothing

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

### `focus-check-asks-the-wrong-window` — the UIA flag is about the wrong element

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

Related: [[nothing-is-closed]], [[logs-are-the-debugger]],
[[a-test-can-outlive-its-design]], [[output-level-assertion-or-it-is-not-done]].
