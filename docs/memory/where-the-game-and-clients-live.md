---
name: where-the-game-and-clients-live
description: Paths to the live TF2 install, the period clients used to date the era corpus, and the Source SDK checkout — all on F:, none of them searchable quickly.
metadata:
  type: reference
---

**Everything this project reads from outside the repository lives on `F:`.** Written down because
finding it costs a directory sweep of a large disk, and one such sweep timed out at two minutes.

| What | Where |
|---|---|
| Live TF2 install | `F:\SteamLibrary\steamapps\common\Team Fortress 2` (game files under `…\tf`) |
| Maps, including stock | `…\Team Fortress 2\tf\maps` — `koth_harvest_final.bsp` is a loose file, not in a VPK |
| Source SDK 2013 | `F:\src\source-sdk-2013` — the `SourceSdk` test helper resolves this |
| Period clients | `F:\tf2-builds\tf2-2007`, `tf2-2008`, `tf2-2011`, `tf2-2013` |
| Probe builds | `F:\tf2-builds\probe-2011`, `probe-2013` |
| Download and extract logs | `F:\tf2-builds\*.log`, `*.err` — how each build was obtained |

**The period clients are the instrument behind the era axis.** Each one's `version` output dates its
build exactly, which is what turned protocol numbers into real dates
([[era-axis-is-measured]], [[proto-version-h-enumerates-the-boundaries]]). They are also what proved
the 2007 client will play files this project generated ([[engine-accepts-authored-demos]]).

**No decompiler output exists anywhere, and that is deliberate.** The rule is about repository SIZE:
decompiler projects and their output are enormous, a folder committed once lives in the history for
ever, and a repo that has swallowed one cannot easily be moved. Run Ghidra or IDA with project and
output paths under a temp directory outside every git tree, and carry back only what is written by
hand afterwards. Nothing is cached, so a decompilation question starts from scratch each time — which
is the intended trade.

**Test files hardcode the install path rather than searching for it**, using the exact string
`F:/SteamLibrary/steamapps/common/Team Fortress 2/tf` (see `ArmsModelProbe`, `ClassScriptProbe`,
`ControlPointMaterialProbe`). There is no shared helper for it; adding one would be a tidy-up worth
doing if a third form of the path appears.
