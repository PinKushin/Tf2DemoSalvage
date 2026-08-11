---
name: binaries-answer-what-the-sdk-cannot
description: "Shipped game binaries carry tables and constants no SDK contains; read them with a PE byte scan, not the analyzer"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T17:04:53.248Z
---

**"Not in the source" is not "not knowable".** On 2026-08-11 six TF2 clients and three engines
were read, and they answered five questions the corpus and the SDK together could not: every
unnamed user message id, the per-era table lengths, `VOICE_MAX_PLAYERS` at three dates, the demo
header layout, and which protocol transitions Valve's own engine treats as breaking.

**Why:** Valve publishes *some* source, and the sdk2013 drop describes a build years newer than
its name — it contains `RDTeamPointsChanged`, which the March 2013 client does not have anywhere.
A shipped binary is the only artifact that is exactly one build.

**How to apply:**

- `usermessages->Register("Name", size)` on x86 compiles to `push size; push offset name`, so the
  whole table is a literal sequence in `.text`. Same for any registration API.
- **Ghidra's analysis is not the reliable instrument.** It found the table in 2007/2009 and missed
  it entirely in the 2011 client — strings present, zero code references. It also silently drops
  strings it never defined as data, which cost an hour of believing the 2007 table lacked `Fade`
  and `VGUIMenu`.
- **The reliable instrument is a raw PE scan**: find `68 <imm32>` in an executable section where
  the immediate is the address of a printable string, sort by offset, cluster. No disassembly, so
  nothing to fail. `D:\ghidra-proj\scripts\scan_usermsg.py`. It reproduced the Ghidra result at
  identical addresses and then read the three Ghidra could not.
- Disable Ghidra's **Decompiler Parameter ID** analyzer (`FastAnalysis.java` prescript). It
  decompiles every function to infer signatures and is essentially the entire runtime — 20+ min
  down to ~1.5 min on a 4 MB DLL.
- x64 binaries are useless for this: arguments go in registers, there is no push to find. The
  32-bit client still ships alongside.
- Date any build without launching it: `grep -a "Exe build"` on `engine.dll`, and `StartRecording`
  writes the protocol constants as literals.

Everything runs under `D:\ghidra-proj`, outside every git tree, and only constants come back —
see [[decompiler-output-never-in-a-repo]]. Related: [[read-the-encoder-not-the-decoder]],
[[differential-beats-fixtures]].
