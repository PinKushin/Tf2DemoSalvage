---
name: a-config-is-a-program
description: A Source .cfg is executed, not read; aliases redefine each other at runtime, so static resolution is wrong by construction.
metadata:
  type: project
---

**A TF2 `.cfg` is a program.** `alias` is a runtime command that redefines *other* aliases as it
runs — how null-cancelling movement scripts work, which is most competitive configs. In the standard
one, `checkfwd` means `none` before W is pressed and `+forward` after. A reader that resolves a bind
to one meaning is wrong half the time.

The first implementation did exactly that and passed fifteen synthetic tests, because every fixture
came from `config_default.cfg`, which binds movement directly and contains no alias to miss
([[fixtures-are-the-weak-point]]).

`ConfigConsole` in `Tf2DemoSalvage.Presentation` is the interpreter, read entirely from
`src/game/client/in_main.cpp` and `kbutton.h` in `source-sdk-2013` (client code, no decompiler
needed). Four non-guessable mechanisms, all wrong on the first attempt:

1. **A button holds TWO keys** (`int down[2]`), not a bool — `KeyUp` returns early while either slot
   is filled, letting two keys bound to one action release independently.
2. **The key number does not survive into an alias body** — the engine appends it to the command the
   key is *bound* to, and Source aliases take no parameters. `KeyUp` with an empty argument clears
   both slots, which the null-cancel pattern depends on (S's `-forward` releases W's slot).
3. **The release line flips ONE character**: `cmdbuf[0] = '-'`, tested only at `[0]`, so
   `"+forward; +moveright"` releases as `"-forward; +moveright"` and the second button sticks —
   a real Source footgun, reproduced deliberately ([[name-the-trade-before-fixing-valve]]).
4. **Reading a button's state consumes it** (`key->state &= 1`), giving a key tapped within one frame
   partial credit of 0.25. Read each action once per frame.

**How to apply:** for any imported program config, check for runtime state before writing a lookup
table. Also [[read-the-encoder-not-the-decoder]] — input handling states intent a decoder only implies.

---

## A running client caches its config

**Overwriting a `.cfg` while TF2 is running changes nothing until `exec`'d**, and the first read
after an overwrite can still serve the old copy (measured 2026-08-16, building `Pin-Config`'s two
profiles). The damage is not the delay — it's that a correctly-applied fix appears not to work, and
further wrong theories get built on that false negative.

**To test a render setting, type the single cvar in console** — read immediately, one variable
instead of a whole file. Restart the client only when a whole profile needs exercising. Same family
as [[fixtures-are-the-weak-point]] and the `-1`/`-10` wrong turn in
`docs/findings/24-reference-capture.md`.

Two more facts from the same session:
- **`mat_reducefillrate 1` crashes the modern client** — its ps20b shader path requests combos that
  no longer load. TF2's render settings aren't a clean cheap→expensive spectrum: the cheap end is
  fatal.
- **`Pin-Config` has two profiles and only `ultra` is a reference** — a capture under `low` is not
  ground truth here ([[nothing-is-closed]]).
