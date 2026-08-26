---
name: a-convar-registration-is-three-pushes
description: A Source cvar's default and flags decode from ~20 bytes of x86 — byte-scan for the push of its name, then read the two pushes before it.
metadata:
  type: reference
---

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
Only the pointer in the initialiser is authoritative.

**Ghidra is installed and the paths are in the global CLAUDE.md** (`D:\ghidra_12.1.2_PUBLIC`,
projects in `D:\ghidra-proj`, all on `D:` and so outside every git tree). `D:\ghidra-proj` already
holds analysed imports of the 2007, 2008 and live engines and clients, plus scripts —
`FindSoundGainCurve.java` documents the same string→initialiser→object hop for when the OBJECT is
what you need rather than the arguments. Reach for that when the question is "who reads this cvar";
reach for the byte scan when the question is "what is it declared as".

Related: [[binaries-answer-what-the-sdk-cannot]], [[a-convar-default-sits-beside-its-name]] — which
this corrects in one respect, since the default sits beside its *pointer*, not its name.
