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
miss. Related: [[put-the-real-file-in-the-fixture]].

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
