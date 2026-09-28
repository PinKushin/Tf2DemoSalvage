---
name: a-default-is-not-a-constant
description: A number in Valve's source is usually a ConVar default, not a constant — and how to read a closed engine cvar's real default out of the binary, since cvarlist.log can disagree.
metadata:
  type: feedback
---

**A number found in Valve's source is often a DEFAULT, not a constant.** `viewmodel_fov` was read
from the SDK, its 54–70 clamp quoted in a comment, then 54 hardcoded — but it's a player setting, and
TF2 reads a separate `viewmodel_fov_demo` during playback. This is D106: nothing is hardcoded that
Valve does not hardcode; baking a client setting into the renderer fails silently, same class of bug
as [[fallbacks-do-not-make-guesses-safe]].

**When a number comes from the SDK, grep for it as a `ConVar` first.** If it is one: what does the
demo record about it (usually nothing), is there a playback-specific variant, what's the clamp? Then
choose deliberately (follow the default / expose it / read from config) and write down which.

## Reading a closed engine cvar: the registration, three pushes

**To learn a closed Source ConVar's real default, flags, or whether it's a user setting at all: find
the `push` of its name string and read the two pushes before it.** No Ghidra project needed — a byte
scan plus twenty bytes of hand-decoded x86. Recipe (done on `engine_no_focus_sleep`, 2026-08-26):

1. `r2 -q -c 'izz~<name>' engine.dll` → name string's paddr/vaddr, note the delta (here `+0x10001800`).
2. Byte-scan for `push imm32` of that address: `grep -aboP '\x68<addr-LE>' engine.dll` — one hit, since
   a cvar name is referenced only by its own static initialiser.
3. `dd` around the hit; args push right-to-left:
```asm
push 0x80              ; flags
push 0x102eb2f8        ; default value, a STRING pointer
push 0x1032e4b8        ; name
mov  ecx, 0x1066f840   ; the ConVar object
call ConVar::ConVar
```
4. Follow the default pointer → null-terminated string, `"50"` here.
5. Decode flags against `public/tier1/iconvar.h` — `0x80` is `FCVAR_ARCHIVE` (saved to vars.rc).

**Three pushes means no help string, four means there is one.** `engine_no_focus_sleep` has three —
undocumented, yet `FCVAR_ARCHIVE` says Valve treats it as a persisted user setting. An undocumented
and an internal convar look identical from outside; only the flag (only readable from the binary)
tells them apart.

**Do not look for a default near the name** — short literals are string-pooled (`"50"` sits beside
`dsp_speaker`, nowhere near its actual users). Only the initialiser's pointer is authoritative.

## The earlier, weaker method — and where it is wrong

The prior belief was that a default sits in `.rdata` immediately *before* its name (compiler pooling
order), confirmed on `engine-live-x86.dll` 2026-08-22 for `snd_refdist`(36), `snd_refdb`(60),
`snd_mixahead`(0.1). **It worked there and is not the rule**: the default sits beside its *pointer*
in the initialiser, not beside its *name* in the pool. A second case (2026-08-27) pools four sound
convar names with no literal between any of them; `snd_gain_min`'s default sits 300KB away, reachable
only via the pointer. File-offset-to-VA deltas also differ per section (names `+0x10001800`, default
strings `+0x10001A00`) — calibrate on a known pair first.

Cautions that still hold: confirm direction with a known value first; a help string ("Music volume")
is not the default; this method gives values only, never behaviour — don't infer a formula from
parameter names (`docs/findings/31-game-audio.md` has several plausible dB-falloff formulas that fit
the same two constants and disagree by several dB).

**Cheaper cross-check, never the authority:** `tf/cvarlist.log` ships a plain-text dump of 3,668
convars with values/flags/help ([[nothing-is-closed]]). **It prints the value IN FORCE, not always
the default** — `snd_gain_min` dumps as `0`, but the registration's default is `"0.01"`; it's
`FCVAR_CHEAT`, not archived, yet engine code set it at startup regardless of flags. So: **registration
first, dump as cross-check, adjacency never.** The dump did agree with the registration on
`snd_refdist`, `snd_refdb`, `snd_gain`, `cl_updaterate`, and all eight movement convars.

`D:\ghidra-proj` holds analysed 2007/2008/live engine+client imports plus `FindSoundGainCurve.java`
(same string→initialiser→object hop, for when you need the OBJECT rather than the arguments). Paths
in [[where-the-game-and-clients-live]].

---

Related: [[a-config-is-a-program]] is the same subject from the testing side,
[[parity-is-the-search-not-the-defence]] is the general form, [[a-constant-carries-no-scope]] is the
next question after "is it a constant": what is it applied TO.
