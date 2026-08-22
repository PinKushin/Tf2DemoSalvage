---
name: a-convar-default-sits-beside-its-name
description: A Source ConVar's default value is a string literal immediately before its name in .rdata, so engine tuning constants are readable with ripgrep and od — no decompiler, no function bodies.
metadata:
  type: reference
---

**A `ConVar` is constructed with its name and its default as adjacent string literals, and the
compiler pools them in order — so the default sits immediately BEFORE the name in `.rdata`.**
Recovering an engine tuning constant therefore needs no decompiler, no function bodies, and no
Ghidra analysis: ripgrep for the name, hexdump around the offset, read the null-terminated string in
front of it.

Measured on `engine-live-x86.dll`, 2026-08-22:

```
3320972  "36"            3049240  "0.1"
3320976  "snd_refdist"   3049244  "snd_mixahead"
3320988  "60"
3320992  "snd_refdb"
```

giving `snd_refdist` 36, `snd_refdb` 60, `snd_mixahead` 0.1 — three constants the SDK does not
contain, in about a minute each.

```bash
off=$(rg -a -o -b "snd_mixahead" engine-live-x86.dll | head -1 | cut -d: -f1)
od -A d -t x1z -j $((off-32)) -N 96 engine-live-x86.dll
```

**Why this matters more than the three numbers.** The same session spent far longer trying to reach
those values the expensive way — importing, scanning for address constants, hunting function bounds
— and got nothing, because the use sites turned out to be the ConVar REGISTRATION block (six
constructors `0x30` apart in a static initialiser Ghidra never turned into a function). The cheap
path went straight past all of it.

**How to apply:**

- **Confirm the direction with a known value before trusting an unknown one.** Both readings are
  plausible — a default could follow its name just as easily — and here it precedes. `snd_refdist`
  36 and `snd_refdb` 60 are documented Source values, so they served as the control that fixed the
  direction before `snd_mixahead` was read.
- **Watch for the neighbour.** In the `snd_refdist` block, `"36"` sits between `snd_musicvolume` and
  `snd_refdist`; keying on "the string before" is only safe once the direction is established.
- **A help string is not the default.** `snd_musicvolume` has *"Music volume"* near it, which is the
  ConVar's help text. The default is the short numeric literal.
- **This gives values, never behaviour.** It says `snd_refdist` is 36; it says nothing about the
  curve that consumes it. Do not infer a formula from parameter names — see
  `docs/findings/31-game-audio.md`, where several plausible dB falloff formulas fit these two
  constants and disagree by several dB at ordinary range.

Related: [[binaries-answer-what-the-sdk-cannot]] for the byte-scanning habit this belongs to, and
[[nothing-is-closed]] — the meaning of these cvars came from published game code that consumes them,
even though the mixer that implements them never will.
