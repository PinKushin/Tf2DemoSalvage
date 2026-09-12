---
name: no-hardcoded-controls-ever
description: Every key goes through the config; never add a literal Keys comparison or a ShortcutKeys.
metadata:
  type: feedback
---

**The owner, 2026-08-26, three times in a row:**

> *"no hard coded controls ever"*

> *"and everything gets to be customized so runs through the config"*

> *"do not hard code home, no new hard codes"*

**Why:** it follows from D69 rather than adding to it. If a person's real TF2 `.cfg` must work
wholesale, the set of actions it can bind cannot be a subset chosen by whoever wrote each handler. A
literal `Keys.X` comparison, or a `ShortcutKeys` on a menu item, is a control nobody can rebind.

**The ordering is his and it is not "do it now":**

> *"yea the migration and refactor for the config, will come after we refactor the views to actually
> be pure views"*

So D101 is a rule about what may be **added**, plus a debt (B214: fourteen menu shortcuts still
hardcoded). Removals count and should be taken when they appear — the height cut's three keys went
with the feature.

**How to apply, concretely.** It came up while wiring a speed slider, where `Home` → 1× was proposed
by the owner himself and was a good idea — *"'Home means minimum' is literally 1x when it comes to
video playback, its the default too"*. It was still not built, because building it meant one more
literal to migrate later. Three things stayed allowed:

- **A control's own platform behaviour.** A `TrackBar` answers `Home` with its minimum because
  WinForms says so; that is not ours and there is nothing to un-hardcode.
- **A guard that names no key.** "While a text box has focus, nothing is a shortcut" adds nothing to
  the pile and fixed a real defect (B212).
- **Deleting keys.**

**The tell that a hardcoded key is doing damage is not the key, it is the ORDER.** `ProcessCmdKey`
runs before any control sees anything, so every literal in it reaches over the whole form. `Space` —
the *default* bind for switch-camera-mode — meant typing `cp process` into the search box toggled
first person. That was shipped, and nobody noticed, because nobody types in a search box while
thinking about camera modes.

Related: [[a-config-is-a-program]], [[silence-about-a-missing-feature-is-not-a-preference]].

---

## `tf2-binds-every-letter-but-o` — a pasted config TAKES keys away

`<TF2>/tf/cfg/config_default.cfg` is the game's shipped default binding set — 64 `bind` lines,
**every letter of the alphabet except `o`**, plus `F1`, `F2`, `F5`, `F6`, `F7`, `F10`, `F12`, the
digits, the mouse buttons and the punctuation. Read it before choosing any default key.

**The consequence is not "avoid collisions", it is stronger than that.** D69 loads a user's real
config wholesale, and every real config opens with `unbindall`. So loading one does not merely add
bindings — it **takes keys away**. `bind "f" "+inspect"` moves `f` to a command this viewer does not
implement, and whatever we had there is gone.

A default is therefore safe in exactly two cases:

1. **TF2 binds that key to the same command we do** — then a pasted config moves our action with
   theirs, which is what we want. `SPACE` is `+jump` in both.
2. **TF2 does not bind that key at all** — only `o`, `F3`, `F4`, `F8`, `F9`, `F11`.

Anything else loses the action. Six free keys is not enough, which is why the viewer's own actions
live on **`CTRL` combinations**: Source's `bind` has no modifier syntax, so no config can name one
and the whole space is unclaimable. That was added as a deliberate superset of Source's vocabulary
(D101, B214).

**Use Valve's command name wherever Valve has one** — it is what turns case 1 on. More exist than
you would guess: `screenshot`, `demo_togglepause` ("Toggles demo playback"), `cl_showfps`, and every
`mat_*` debug view. `tf/cvarlist.log` lists all 3,668 with their help text.

**How to apply:** `DefaultBindingConformanceTests` enforces all of this by reading Valve's file, so
adding a colliding default goes red rather than shipping. Related:
[[nothing-is-closed]], [[parity-is-the-search-not-the-defence]].

---

## `not-every-setting-needs-a-bind` — a convar can be config-only

The viewer has a test — `ConfigConsoleConformanceTests.Unbound_TheShippedDefaults_LeaveNothing
Unreachable` — asserting that the shipped defaults leave **every** `ViewerAction` reachable by some
key. That test is right about actions and says nothing about whether a setting should have BEEN an
action.

**Measured, 2026-08-29 (D123).** Adding `cl_showpos` as a `ViewerAction` with no default key
reddened three tests, and the fix applied was to invent `CTRL+p`. The owner's response:

> *"not every cvar or setting needs a key bind, but that ctrl p works i guess"*

and then the rule itself:

> *"really if its not something valve normally binds a button too we dont NEED the bind, but having
> binds for the debug views is nice and SS's is needed"*

**Why:** a key is scarce and TF2 takes nearly all of them (the section above), so a binding invented
to satisfy a test spends a real resource on a decision nobody made. And the reasoning runs
backwards: the setting was made an action because that is how settings were done here, then a key
was invented because actions must have one.

**How to apply — three tiers, and only the last is a default:**

- **Valve binds it** → bind it, on Valve's key. D101.
- **A debug view** (`mat_wireframe`, `cl_showfps`, `cl_showpos`) → a bind is *nice*. Take a `CTRL`
  combination, which no Source config can name.
- **A screenshot** → *needed*; it is the one action that must be reachable mid-frame.
- **Anything else** → a convar and a menu item are enough. Do not make it a `ViewerAction`.

So ask "is this reachable enough as a convar plus a menu item?" **before** adding the enum member.
Once it is an action the test is correct to demand a key, and by then the wrong question has already
been answered. Related: [[a-config-is-a-program]],
[[silence-about-a-missing-feature-is-not-a-preference]].
