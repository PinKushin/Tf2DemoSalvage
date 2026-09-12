---
name: a-config-is-a-program
description: A Source .cfg is executed, not read; aliases redefine each other at runtime, so static resolution is wrong by construction.
metadata:
  type: project
---

**A TF2 `.cfg` is a program.** `alias` is a runtime command that redefines *other* aliases as it
runs, which is how null-cancelling movement scripts work — and those are what most competitive
configs are. In the standard one, `checkfwd` means `none` before W is pressed and `+forward`
afterwards. **A reader that resolves a bind to an action once has to pick a meaning, and whichever
it picks is wrong half the time.**

The first implementation did exactly that and passed fifteen synthetic tests, because every fixture
came from `config_default.cfg` — which binds movement directly and therefore contains no alias to
miss. Related: [[fixtures-are-the-weak-point]].

`ConfigConsole` in `Tf2DemoSalvage.Presentation` is the interpreter. Everything in it is read from
`src/game/client/in_main.cpp` and `kbutton.h`, both in `source-sdk-2013` — this is client code, not
the closed engine, so no decompiler was needed.

**Four mechanisms that are not guessable and were all wrong on the first attempt:**

1. **A button holds TWO keys** (`int down[2]`), not a bool. `KeyUp` returns early while either slot
   is filled, which is what lets two keys bound to one action release independently.
2. **The key number does NOT survive into an alias body.** The engine appends it to the command the
   key is *bound* to, and Source aliases take no parameters. `KeyUp` with an empty argument clears
   both slots unconditionally — **and the whole null-cancel pattern depends on that**, because it
   is how `-forward` issued by the S key releases a button the W key holds.
3. **The release line flips ONE character.** `cmdbuf[0] = '-'`, after testing only `[0]`. So
   `"+forward; +moveright"` releases as `"-forward; +moveright"` and the second button sticks down
   for ever. Real Source footgun, reproduced deliberately — see [[name-the-trade-before-fixing-valve]].
4. **Reading a button's state consumes it** (`key->state &= 1`), which is what gives a key tapped
   inside one frame partial credit of 0.25 instead of nothing. Read each action once per frame.

**How to apply:** for anything that imports another program's configuration, ask whether that format
has runtime state before writing a lookup table. Also [[read-the-encoder-not-the-decoder]] — the
input handling states the intent that a decoder only implies.

---

## `a-running-client-caches-its-config` — a stale .cfg makes a good fix look failed

**Overwriting a `.cfg` while TF2 is running changes nothing until it is `exec`'d, and the first read
after an overwrite can still serve the old copy.** Measured 2026-08-16 while building `Pin-Config`'s
two profiles.

The damage is not the delay, it is the shape of the failure: a fix that was applied correctly appears
not to work. Two further theories were built on that false negative before the log falsified both.
**A false negative from a caching layer invalidates the experiment without invalidating anyone's
confidence in it**, which is why this is worth remembering rather than rediscovering.

**How to test a render setting instead: type the single cvar in the console.** It is read
immediately, and it is one variable rather than a file of them — a measurement rather than a change
of state. Restart the client only when a whole profile genuinely needs exercising.

Same family as [[fixtures-are-the-weak-point]] and the `-1` versus `-10` wrong turn in
`docs/findings/24-reference-capture.md`: a procedure chosen for convenience, insensitive to the thing
it was meant to detect.

Two more facts from the same session:

- **`mat_reducefillrate 1` crashes the modern client.** It selects the ps20b shader path, which has
  decayed to the point of requesting combos that no longer load. TF2's render settings are not a
  clean cheap-to-expensive spectrum: the bottom end is fatal and the expensive path is the one that
  works.
- **`Pin-Config` has two profiles and only `ultra` is a reference.** A capture taken under `low` is
  not ground truth for this project. See [[nothing-is-closed]] for why a
  capture with an unstated configuration is not evidence at all.
