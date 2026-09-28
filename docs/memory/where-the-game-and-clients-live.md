---
name: where-the-game-and-clients-live
description: "Paths to the live TF2 install, the period clients used to date the era corpus, and the Source SDK checkout — all on F:, plus the Ghidra project and imported binaries on D:."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:47.842Z
---

**Almost everything read from outside the repo lives on `F:` — the decompilation is the exception, on
`D:`.**

| What | Where |
|---|---|
| Live TF2 install | `F:\SteamLibrary\steamapps\common\Team Fortress 2` |
| Source SDK 2013 | `F:\src\source-sdk-2013` |
| Period clients | `F:\tf2-builds\tf2-{2007,2008,2011,2013}` |
| Probe builds | `F:\tf2-builds\probe-{2011,2013}` |
| A 380-demo competitive archive | `D:\tf2-demo-archive` — real leagues, not the 53-demo corpus |

**The archive settles rate questions the small corpus can't** (e.g. header truncation rates by
source), by seeking a few bytes per file rather than parsing. See [[a-header-written-last-is-absent]].

**Period clients date the era axis exactly** — see [[era-axis-is-measured]],
[[hl2sdk-branches-are-per-era-headers]], [[engine-accepts-authored-demos]].

**A decompilation EXISTS on disk, outside the repo, on `D:`** — this entry once wrongly denied it;
corrected after the owner said so. `D:\ghidra-proj` holds the Ghidra project, imported binaries per
era (2007-live), custom scripts and driver scripts. The renderer/materialsystem ARE imported now —
an earlier version of this entry said otherwise and nearly stopped a rendering-state question from
being asked. **`ls D:\ghidra-proj\bin` is the check, and it takes a second** — a fact about what had
been done was carried as a fact about what could be done
([[filing-a-divergence-is-not-fixing-it]]).

**Decompile the LIVE client by default**; reach for a period build only for an era-specific question.
The only `shaderapidx9.dll` under `F:\tf2-builds` is an old build — the live one is under the Steam
install path.

**Everything above stays outside every git tree.**

**No test hardcodes any of this any more** — one shared helper resolves the game path and honours an
env-var override first; ninety-four private copies were removed (D109) — see
[[output-level-assertion-or-it-is-not-done]].

**Grep the SDK checkout; never fetch it a file at a time** — a whole-tree grep answers what a
`WebFetch` answers only if you guessed the filename. Landmarks: `src/tier1/bitbuf.cpp`
([[research-before-code]]), `src/public/bspfile.h`, `src/utils/vbsp/overlay.cpp`.

**It does not contain the engine** — a real limit, not a search failure (e.g. overlay UV ordering is
engine-side, never released; settled by measuring a real map instead). See [[nothing-is-closed]],
[[fixtures-are-the-weak-point]].
