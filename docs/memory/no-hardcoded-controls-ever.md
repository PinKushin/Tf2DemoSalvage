---
name: no-hardcoded-controls-ever
description: Every key goes through the config; never add a literal Keys comparison or a ShortcutKeys.
metadata:
  type: feedback
---

Owner, three times in a row: *"no hard coded controls ever"* / *"everything gets to be customized so
runs through the config"* / *"do not hard code home, no new hard codes."*

**Why:** follows from D69 — if a real TF2 `.cfg` must work wholesale, the set of bindable actions
can't be a subset chosen per handler. A literal `Keys.X` or `ShortcutKeys` is unrebindable.

**Ordering is his**, not urgent: *"the migration and refactor for the config, will come after we
refactor the views to actually be pure views."* D101 is a rule about what may be ADDED, plus a debt
(B214: fourteen still-hardcoded menu shortcuts). Removals count and should be taken when they appear.

**Three things stay allowed:** a control's own platform behaviour (`TrackBar`/`Home`); a guard naming
no key ("nothing is a shortcut while a textbox has focus" fixed a real defect, B212); deleting keys.

**The tell that a hardcoded key does damage is the ORDER** — `ProcessCmdKey` runs before any control
sees anything, so a literal there reaches over the whole form. `Space` (default switch-camera-mode)
meant typing "cp process" into a search box toggled first person, shipped unnoticed.

Related: [[a-config-is-a-program]], [[silence-about-a-missing-feature-is-not-a-preference]].

---

## `tf2-binds-every-letter-but-o` — a pasted config TAKES keys away

`config_default.cfg` binds every letter except `o`, plus F1/F2/F5/F6/F7/F10/F12, digits, mouse
buttons, punctuation. **D69 loads a user's real config wholesale, and every real config opens with
`unbindall`** — so loading one doesn't just add bindings, it TAKES KEYS AWAY. `bind "f"
"+inspect"` erases whatever we had on `f`.

**A default is safe in exactly two cases:** TF2 binds that key to the same command we do (so a
pasted config moves our action with theirs); or TF2 doesn't bind that key at all (only `o`, F3, F4,
F8, F9, F11). Six free keys isn't enough, so the viewer's own actions live on `CTRL` combinations —
Source's `bind` has no modifier syntax, so no config can claim them (D101, B214).

**Use Valve's command name wherever one exists** — `screenshot`, `demo_togglepause`, `cl_showfps`,
every `mat_*` debug view; `tf/cvarlist.log` lists all 3,668.

**How to apply:** `DefaultBindingConformanceTests` reads Valve's file and reddens on a colliding
default. Related: [[nothing-is-closed]], [[parity-is-the-search-not-the-defence]].

---

## `not-every-setting-needs-a-bind` — a convar can be config-only

A test asserted every `ViewerAction` must be reachable by some key, which is correct about actions
and says nothing about whether a setting should HAVE BEEN an action. Adding `cl_showpos` with no
default key reddened three tests; the fix invented `CTRL+p` (D123). Owner: *"not every cvar or setting needs
a key bind... really if its not something valve normally binds a button too we dont NEED the bind,
but having binds for the debug views is nice and SS's is needed."*

**Why:** a key is scarce (TF2 claims nearly all of them) — inventing a binding to satisfy a test
spends a real resource on a decision nobody made.

**Three tiers, only the last is a default:**
- Valve binds it → bind it, on Valve's key (D101).
- A debug view → nice, use a `CTRL` combination.
- A screenshot → needed, must be reachable mid-frame.
- Anything else → convar + menu item, don't make it a `ViewerAction`.

Ask "is this reachable enough as a convar plus a menu item?" BEFORE adding the enum member. Related:
[[a-config-is-a-program]], [[silence-about-a-missing-feature-is-not-a-preference]].
