# Decompiling the closed engine

The procedure for reading `engine.dll`, `vphysics.dll` and TF2's own shaders when the published SDK
does not hold the answer. **Loaded when you need it** — it was inline in `CLAUDE.md`, which is read
every turn of every session, for a tool used occasionally.

**Reach for it readily.** It is a normal source, not a last resort. What is NOT negotiable is where
its output lives.

## The one hard rule: output never goes in a git tree

Decompiler projects and output are enormous, they cannot be moved to another disk once committed,
and a folder committed once lives in the history for ever. Run it with its project and output paths
under a directory outside every repo, and carry back only what is written by hand afterwards — a
constant, a field order, a formula, a note saying where it came from.

**Never paste a decompiled function into source.** The owner's position on the legal question is
that it is not a practical concern here; the size problem is real and permanent. Enforced by
`~/.claude/hooks/block-standards-violations.ps1`.

| what | path |
|---|---|
| Ghidra install | `D:\ghidra_12.1.2_PUBLIC` |
| headless runner | `D:\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat` |
| projects / output | `D:\ghidra-proj` |
| scripts | `D:\ghidra-proj\scripts` |

All on `D:`, which is the point — no repository lives there, so the rule is satisfied by the paths
themselves rather than by remembering to redirect each run.

## It needs JDK 21, and the failure on 25 does not look like a JDK problem

On JDK 25 Ghidra 12.1.2 dies in its OSGi layer with

```
ERROR: Bundle org.apache.felix.framework [0] The data file must be inside the data dir.
```

followed by a `dataFile is null` abort. **That reads like a corrupt bundle cache and is not** —
deleting the cache changes nothing, and `JAVA_TOOL_OPTIONS` does not reach it. Point it at a 21.

```bash
JAVA_HOME="C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot" \
  "/d/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "D:\ghidra-proj" tf2engine \
  -import "F:\SteamLibrary\steamapps\common\Team Fortress 2\bin\x64\engine.dll" -overwrite

JAVA_HOME="…jdk-21…" "/d/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "D:\ghidra-proj" \
  tf2engine -process engine.dll -noanalysis \
  -scriptPath "D:\ghidra-proj\scripts" -postScript DecompAt.java 1800683d0
```

Importing and analysing `engine.dll` is a few minutes; scripts against the analysed program are
seconds.

**A zero exit means nothing.** `analyzeHeadless.bat` exits 0 on a Java stack trace, so grep the
output for `ERROR` rather than trusting the status — the first attempt "succeeded" having done
nothing at all.

## The string search is the way in

Valve left the assert strings, and a function's `__FILE__` names its own source file. So
`CL_CopyNewEntity: GetClassBaseline(%d) failed.` names the function holding it, and finding it is one
script run. `D:\ghidra-proj\scripts` holds small `GhidraScript` helpers written for this: decompile
at an address, find callers of an address, find the function holding a string, list functions in a
range.

## Identity comes from the disassembly, shape from the decompiler

`DisasmWithData.java` prints each instruction with every memory operand resolved to its four lanes
inline, so a constant's value sits on the line that reads it:

```
1800386df  MOVAPS XMM4,xmmword ptr [0x1800ff130]   ; 1800ff130 = {0.0, 0.0, 0.0, ffffffff}
```

**Two wrong conclusions in one session came from settling a constant in decompiled C instead** —
`DAT_1800eea1c` taken for π because its neighbour is genuinely 2π (it is `1e-16`), and `uVar6` read
as a dumped mask in one expression and a comparison result three lines later, because Ghidra reuses a
local name for unrelated values. Full account: `docs/memory/settle-a-constant-in-the-disassembly.md`.

**The tell to run this script** is a sentence naming a `DAT_`/`_UNK_` symbol, or reaching for a value
because it is ADJACENT to a known one.

## Outstanding

An earlier assistant left a Ghidra output folder inside WindowsDriverCore that still needs cleaning
up.
