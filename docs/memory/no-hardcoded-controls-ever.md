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
