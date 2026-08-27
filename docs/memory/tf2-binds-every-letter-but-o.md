---
name: tf2-binds-every-letter-but-o
description: config_default.cfg binds 64 keys, so a default anywhere else is taken away by a pasted config; CTRL combos are the only safe space.
metadata:
  type: project
---

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
[[nothing-is-closed]], [[no-hardcoded-controls-ever]],
[[ask-which-engine-mechanism-you-are-copying]].
