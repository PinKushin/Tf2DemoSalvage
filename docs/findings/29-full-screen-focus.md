# 29 — Full screen took the foreground and lost the focus

*(measured on the running viewer, 20 August 2026)*

Full screen could not be left. F11 went in, and then nothing came back out: Escape did nothing, F11
did nothing, and the window sat there with the map and the overlay bar until the user alt-tabbed
away and back — at which point every key worked again.

The owner's description named the shape of it before any measurement did:

> the window is losing focus or something on the first full screen test and it stalls from there,
> if i alt tab to another program then alt tab back, then manually hit f11 to go back to windowed,
> the tests will finish

## What it did to the test suite, which is how it kept being noticed

The UI fixtures share one launched viewer. A run that ends full screen hands the next test a window
already the size of the screen, so **the failure lands on a later test than the fault**:

| Test | What it reported | What was actually wrong |
|---|---|---|
| `Shell_FullScreenThenEscape…` | the viewport never grew | it was already full screen |
| `Shell_EveryControl_IsReachableByAutomation` | "the playlist is missing" | the playlist is hidden in full screen |
| `F12WritesAPictureOfWhatTheViewerDrew` | "No menu named 'View menu' appeared" | the menu strip is hidden in full screen |

Five failures, 2 minutes 18 seconds, and the count varied between runs — which reads exactly like
flake and was not. It is the reason this repository's rule is that **flake is a defect in
synchronisation or in the app, never noise**.

## Two hypotheses, both wrong, both cheap to kill

**The menu strip hides F11's shortcut.** The binding is `ShortcutKeys = Keys.F11` on a
`ToolStripMenuItem`, and entering full screen sets `MainMenuStrip.Visible = false`; WinForms does not
fire a shortcut on an item that is not visible. Plausible, and false — the owner: "f11 works when
the menu is hidden".

The test written for it was worse than wrong, it was **insensitive**: driving the protected key
handler on a form that was never shown, `ProcessCmdKey(F11)` returns false *windowed too*, because
`ToolStripManager` shortcut processing needs a shown form. Correct and broken predict the same
observation, so the test could not have told them apart. Wrong instrument — the first of the four
routes in the testing doctrine.

**The border-style change recreates the window handle.** A window that loses its HWND loses the
foreground, and `SetForegroundWindow` from a process that is no longer foreground is *refused* rather
than obeyed — so `Activate()` would flash the taskbar button and do nothing. That would fit the
alt-tab cure exactly, since alt-tabbing supplies the input Windows wants first.

Also false. WinForms updates the styles in place. The test is kept as
`EnteringFullScreen_TheWindowHandle_SurvivesTheTransition`, because the invariant is real even though
the bug was not.

## The measurement that ended it

Two guesses is where guessing stops being cheaper than measuring. `ForegroundProbe` logs which window
owns the foreground and which process it belongs to; `MainForm.FocusHere` logs the active control and
`ContainsFocus`; and `ProcessCmdKey` logs Escape and F11 **before** its guards, so a key that arrived
is distinguishable from one that never did.

One run answered it:

```
[render] F11 reached the form; full screen is False
[render] full screen: foreground: 44052c pid 28264 (ours); this window 44052c, has it;
         active control none (visible n/a), form contains focus False
[render] full screen after Activate: ... has it; ... form contains focus False
[render] full screen on took 74 ms to the first frame at 1920x1080
```

and then nothing. **No Escape line at all**, across an entire suite.

So: the window holds the foreground on both sides of the transition — the "lost focus" reading was
wrong in its detail — and `ContainsFocus` is false. **Foreground and focus are different things, and
only the second delivers a keystroke.** A form with no focused child receives no key messages, so
`ProcessCmdKey` never ran and Escape had nowhere to land.

## The cause

Entering full screen hides everything that could hold focus:

- `MainMenuStrip.Visible = false`
- `_hiddenInFullScreen` — the action row, **the playlist panel**, the status strip
- `Controls.Remove(_transport)`, re-parented onto the overlay window

The playlist takes focus when the window opens. Hiding it leaves the form with no focused child, and
the one control still visible is `_viewport` — a plain `Panel`, which is not selectable. Nothing left
in the form can hold focus.

`Activate()` was already there, added against an earlier round of this same symptom. It was not
enough, because activation is not focus.

## The fix

```csharp
ActiveControl = null;
_ = Focus();
```

The clear is load-bearing: `ActiveControl` still points at the hidden playlist, and focusing a
container walks to its active control — handing focus straight back to the control that cannot take
it.

Verified by manipulation rather than by the suite going green. Same run, after the change:

```
[render] full screen after Activate: ... has it; ... form contains focus True
[render] Escape reached the form; full screen is True
[render] full screen off took 110 ms to the first frame at 984x551
```

Twelve of twelve UI tests, 14 seconds, down from five failures in 2 minutes 18.

**The alternative fix is better and was not taken.** Subclassing the viewport panel to be selectable
would give full screen a real focus target rather than parking focus on the form — and the viewport
is where camera keys belong anyway. It is a larger change; this one is measured working.

## What generalises

**A shared-fixture UI suite reports the fault on the wrong test.** Three of the five failures named
controls that were "missing" and were merely hidden. Any state a test changes and does not restore
becomes somebody else's failure message.

The same run turned up a second, unrelated instance: `Transport_JumpToEnd_MovesTheScrubBar` read the
current tick, pressed End, and waited for the reading to change. Another fixture leaves playback at
the last tick, so End had nothing to do and the test reported `tick 8065 / 8065` against a button
that works. It now seeks to the start first and asserts the two halves of the readout meet — a
claim about where End *goes*, rather than about something having changed.

**Log the distinguishing question, not the outcome.** "Full screen would not exit" is consistent with
at least four mechanisms. One line saying whether the key reached the form eliminated three of them
at once, and it took less time to write than either wrong hypothesis took to test.
