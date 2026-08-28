---
name: a-default-is-not-a-constant
description: A number in Valve's source is usually a ConVar default, not a constant — and how to read a closed engine cvar's real default out of the binary, since cvarlist.log can disagree.
metadata:
  type: feedback
---

**A number found in Valve's source is often a DEFAULT, not a constant.** `viewmodel_fov` was read
from the SDK, its clamp of 54–70 was quoted in a comment, and then 54 was written into the code as a
fixed value. The owner corrected it: it is a setting the player changes, and TF2 reads a separate
`viewmodel_fov_demo` during playback — which is this project's only case.

**Why it matters here specifically:** the whole project decodes off whatever schema a file provides
rather than hardcoding an era's layout ([[fallbacks-do-not-make-guesses-safe]]). Baking a client
setting into the renderer is the same mistake at a different layer, and it fails silently — the
picture looks right, it just is not what the recording would have shown. This is D106's rule:
nothing is hardcoded that Valve does not hardcode.

**When a number comes out of the SDK, grep for it as a `ConVar` before using it.** If it is one, the
questions are what the demo records about it (usually nothing), whether a playback-specific variant
exists, and what the clamp is. Then decide deliberately: follow the default, expose it, or read it
from a config. Write down which, because "54" in the source tells a later reader nothing about
whether it was chosen.

**Two memories were merged into this one on 2026-08-27** — `a-convar-default-sits-beside-its-name`
and `a-convar-registration-is-three-pushes`. They are how to answer the question this rule makes you
ask when the cvar is in the closed engine, and **the second corrects the first**, which is exactly
the pair that must not live in two files.

---

## Reading a closed engine cvar: the registration, three pushes

**To learn what a closed Source ConVar really is — its default, its flags, whether it is even a user
setting — find the `push` of its name string and read the two pushes before it.** No Ghidra project
required; it is a byte scan and twenty bytes of hand-decoded x86.

**The recipe**, done for real on `engine_no_focus_sleep`, 2026-08-26:

1. Find the name string's virtual address. `r2 -q -c 'izz~<name>' engine.dll` gives paddr and vaddr;
   note the delta between them (here `vaddr = paddr + 0x10001800`).
2. Byte-scan for a `push imm32` of it: `grep -aboP '\x68<addr-little-endian>' engine.dll`.
   For `0x1032e4b8` that is `\x68\xb8\xe4\x32\x10`. **One hit** — a cvar name is mentioned only by
   its own static initialiser.
3. `dd` a hundred bytes around the hit and read it. Arguments push right-to-left, so:

```asm
push 0x80              ; flags
push 0x102eb2f8        ; default value, as a STRING pointer
push 0x1032e4b8        ; name
mov  ecx, 0x1066f840   ; the ConVar object
call ConVar::ConVar
```

4. Follow the default pointer with another `dd` — it is a null-terminated string, `"50"` here.
5. Decode the flags against `public/tier1/iconvar.h`. `0x80` is `FCVAR_ARCHIVE`, *"set to cause it
   to be saved to vars.rc"*.

**Count the pushes: three means no help string, four means there is one.** That distinction is the
useful part. `engine_no_focus_sleep` has none, which is exactly why it is undocumented and why
searching the web for it returns advice about graphics settings instead — but its `FCVAR_ARCHIVE`
flag says Valve nonetheless treats it as a user setting and persists it. **An undocumented convar
and an internal one look identical from outside; only the flag separates them, and only the binary
carries the flag.**

**Do not try to find a default by looking near the name.** Short literals are string-pooled: `"50"`
sits beside `dsp_speaker` and `"40"` in a shared pool, nowhere near any of the convars that use it.
Reading the bytes around a cvar's name finds its NEIGHBOURS' help strings and never its own default.
**Only the pointer in the initialiser is authoritative.**

## The earlier, weaker method — and where it is wrong

`a-convar-default-sits-beside-its-name` recorded that the default is the string literal immediately
*before* the name in `.rdata`, because the compiler pools them in order. Measured on
`engine-live-x86.dll`, 2026-08-22:

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

**It worked, and it is not the rule.** The default sits beside its *pointer* in the initialiser, not
beside its name in the pool; those coincided for these three and do not in general — the pooling
counterexample above is `"50"`, sitting next to a convar that does not use it. **Use the
registration method; treat adjacency as a hint to confirm, never as the answer.**

**A second, cleaner counterexample, measured 2026-08-27.** The four sound convar names are pooled
consecutively with **no literal between any of them**:

```
3320988  "60" snd_refdb
3321004  snd_foliage_db_loss
3321020  snd_gain
3321036  snd_gain_max
3321052  snd_gain_min
```

`snd_refdist` and `snd_refdb` do have their defaults four bytes before their names, which is why
adjacency answered them. `snd_gain_min`'s default is at `0x102E9E18` — a different section, three
hundred kilobytes away, reachable only by following the pointer. Reading backwards from that name
finds `snd_gain_max`, not a number.

**And the pools have different deltas, which is the trap under the trap.** File offset to virtual
address is constant *within* a section and not across them: the names here map with `0x10001800`
while the default strings map with `0x10001A00`. Calibrate on a known pair before trusting an
address — `snd_mixahead`/`"0.1"` served here, exactly as `snd_refdist` 36 served for the direction.

Its cautions still hold and are worth keeping:

- **Confirm the direction with a known value before trusting an unknown one.** Both readings are
  plausible — a default could follow its name just as easily. `snd_refdist` 36 and `snd_refdb` 60
  are documented Source values, so they served as the control.
- **A help string is not the default.** `snd_musicvolume` has *"Music volume"* near it, which is the
  ConVar's help text. The default is the short numeric literal.
- **This gives values, never behaviour.** It says `snd_refdist` is 36; it says nothing about the
  curve that consumes it. Do not infer a formula from parameter names — see
  `docs/findings/31-game-audio.md`, where several plausible dB falloff formulas fit these two
  constants and disagree by several dB at ordinary range.

**Cheaper than either, when it applies — and it is a cross-check, never the authority.** The game
ships `tf/cvarlist.log`, a plain-text dump of 3,668 convars with values, flags and help strings —
see [[nothing-is-closed]].

**It prints the value IN FORCE, and that is not always the default even for a convar nobody could
have changed.** Measured 2026-08-27: the dump says `snd_gain_min : 0`; the registration's default
pointer resolves to the literal `"0.01"`. That convar is `FCVAR_CHEAT` and **not** archived, so
"the user cannot have set it and it does not persist" looked like proof the dump was the default —
and it is not, because engine code may set a convar at startup whatever its flags say.

So the rule is **registration first, dump as a cross-check, adjacency never.** The dump is still
worth reading: it agreed with the registration on `snd_refdist`, `snd_refdb`, `snd_gain`,
`cl_updaterate` and all eight movement convars, and it is the only source for anything whose
registration cannot be located. But a number that only the dump supports is provisional, and a
disagreement is settled by the binary.

**Where Ghidra is still right:** `D:\ghidra-proj` holds analysed imports of the 2007, 2008 and live
engines and clients, plus `FindSoundGainCurve.java`, which documents the same
string→initialiser→object hop for when the OBJECT is what you need rather than the arguments. Reach
for that when the question is "who reads this cvar"; reach for the byte scan when it is "what is it
declared as". Paths in [[where-the-game-and-clients-live]].

---

Related: [[a-running-client-caches-its-config]] is the same subject from the testing side,
[[read-the-sdk-for-the-whole-mechanism]] is the general form — finding the declaration is the easy
half — and [[a-constant-carries-no-scope]] is the next question after "is it a constant": what is it
applied TO.
