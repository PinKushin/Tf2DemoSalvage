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

---

## The capture the suite produced was of a wall, and that found two more defects

The owner, on the picture the first-person UI test leaves behind: *"im pretty sure the SS's are not
actually showing anything either, its looking at a fning wall"*. He was right, and the assertion
guarding it — a count of lit pixels — passes on a wall as happily as on a map.

**The tick was chosen for the wrong property.** The test jumped to the END of the demo, because that
is where the resolved viewmodel is a model the current install still ships. At the last tick of a
recording the round is over, and on this one the eye is inside geometry. Surveying the demo instead
of assuming:

```
tick 0..1209   v_scattergun_scout      tick 3763..4032  c_pyro_arms
tick 1344..    v_rocketlauncher_soldier tick 4166        c_demo_arms
tick 4301..    v_stickybomb_launcher_demo
tick 6317..    v_sniperrifle_sniper     tick 7527..8065  c_sniper_arms
```

Which incidentally settles the question from `04-entities.md` for good: five classes in one file,
scout then soldier then pyro then demo then sniper.

**And the demo was chosen for the wrong property too.** Every era specimen in the committed corpus is
a solo recording — the owner alone on a local server — so there is nothing in frame but the map:

| demo | most players | installed viewmodel |
|---|---|---|
| 2007–2013 era specimens, POV and STV | **1** | 0–26 of 41 samples |
| z1800 | **25** at tick 2883 | 41 of 41 |

`z1800` is the only real match in gcor, which the owner named before the table did: *"in the gcor i
think z1800 is the only real demo and not just an era specimine"*. It also disposed of the 2013
foundry SourceTV recording as a candidate — *"i dont think theres a real MP game on foudnry in 2013
... foundry was never a comp map"* — and the measurement agrees: one player.

So captures moved off the UI suite entirely. `--first-person` joins `--shot` and `--tick`, and a
picture of any moment of any demo is now one command:

```
tf2demoview z1800.dem --tick 40000 --first-person --shot out.png
```

## The SourceTV camera is a player, and it never moves

The first three captures at ticks 2883, 20000 and 40000 came out **pixel-identical in viewpoint** —
the same resupply room, with only the world around it changing. The camera reads the current tick,
so the subject was not moving.

`FirstPersonCamera` spectated `players[0]`, and on this demo that is:

```
entity 1 team 1 class (none) health 1 at 288,2312,69   <- tick 2883
entity 1 team 1 class (none) health 1 at 288,2312,69   <- tick 20000
entity 1 team 1 class (none) health 1 at 288,2312,69   <- tick 40000
```

**Team 1 is `TEAM_SPECTATOR`.** That is the SourceTV camera, which the wire describes as a player
like any other. The owner named the mechanism immediately: *"its probably source tv since a comp
server starts empty with only the stv there before the players join"* — so it holds the lowest
entity slot and sorts first for the whole match.

`SpectatorTarget.Choose` now applies the engine's own predicate, `tf_shareddefs.h:225`:

```cpp
inline bool IsValidTFTeam( int iTeam ) { return iTeam == TF_TEAM_RED || iTeam == TF_TEAM_BLUE; }
```

and takes the lowest entity index among those, so the answer is stable from tick to tick. Both
callers that must agree — the camera, and the entity hidden from its own view — ask it rather than
each picking. Null when nobody qualifies: the first seconds of a competitive match really are
SourceTV alone.

**A deliberate subject is still not built, and TF2 says what it should look like.** The owner: *"in
tf2 you press space and it changes what cam you are in, and clicking mouse one changes
position/player following"*.

## What the fixed capture then showed

A real first-person view of harvest: a building, decals, an ammo pack, several players in frame. And
a defect that no assertion in this repository would have caught — **the players are drawn purple and
magenta**, which is missing-material colouring rather than team colour. Filed here rather than
chased, because it belongs to the material system and not to the camera.

Three defects in this session found by looking at a picture, and none by a test:
the wall, the static camera, and the purple players.
