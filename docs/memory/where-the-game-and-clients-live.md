---
name: where-the-game-and-clients-live
description: Paths to the live TF2 install, the period clients used to date the era corpus, and the Source SDK checkout — all on F:, plus the Ghidra project and imported binaries on D:.
metadata:
  type: reference
---

**Almost everything this project reads from outside the repository lives on `F:` — the decompilation is the exception and lives on `D:`.** Written down because
finding it costs a directory sweep of a large disk, and one such sweep timed out at two minutes.

| What | Where |
|---|---|
| Live TF2 install | `F:\SteamLibrary\steamapps\common\Team Fortress 2` (game files under `…\tf`) |
| Maps, including stock | `…\Team Fortress 2\tf\maps` — `koth_harvest_final.bsp` is a loose file, not in a VPK |
| Source SDK 2013 | `F:\src\source-sdk-2013` — the `SourceSdk` test helper resolves this |
| Period clients | `F:\tf2-builds\tf2-2007`, `tf2-2008`, `tf2-2011`, `tf2-2013` |
| Probe builds | `F:\tf2-builds\probe-2011`, `probe-2013` |
| Download and extract logs | `F:\tf2-builds\*.log`, `*.err` — how each build was obtained |
| **A 380-demo competitive archive** | `D:\tf2-demo-archive` — ESEA seasons 29–31, ETF2L seasons 29/30/32, plus the owner's own |

**The archive is the population the corpus is a sample of, and it answers questions the corpus
cannot.** `tools/corpus` holds 53 demos chosen for era coverage; this holds 380 from real leagues,
which is what makes a rate meaningful. It settled two on 2026-08-21 in a couple of minutes, both by
seeking 12 bytes per file rather than parsing anything:

- **ESEA demos declare zero ticks, 152 of 152**; ETF2L, 5 of 218. See
  [[a-header-written-last-is-absent]].
- **No demo anywhere has a negative tick, frame or signon length** — 0 of 380, and 0 of the 53 in
  the corpus.

It is **not** in the repository and must not be; it is a reference set, like the SDK checkout.
Reading a header field across all of it costs seconds:

```bash
find "D:/tf2-demo-archive" -name "*.dem" | while read -r f; do
  od -An -td4 -j1060 -N4 "$f" | tr -d ' '
done
```

**The period clients are the instrument behind the era axis.** Each one's `version` output dates its
build exactly, which is what turned protocol numbers into real dates
([[era-axis-is-measured]], [[proto-version-h-enumerates-the-boundaries]]). They are also what proved
the 2007 client will play files this project generated ([[engine-accepts-authored-demos]]).

**A decompilation EXISTS, on disk, outside the repository — and this entry used to deny it.**
Corrected 2026-08-21 after the owner said so: *"i have the decomp on disk, i dont have it in repo"*
and *"the decomp paths were supposed to be added to memory"*.

**It is on `D:`, not `F:` — which is why every search of `F:` for it failed.**

| What | Where |
|---|---|
| Ghidra itself | `D:\ghidra_12.1.2_PUBLIC` (headless at `support\analyzeHeadless.bat`) |
| Its settings dir | `D:\ghidra-settings` — must be passed as `_JAVA_OPTIONS=-Dapplication.settingsdir=…` |
| The project | `D:\ghidra-proj\tf2engine.gpr` / `.rep`, project name **`tf2usermsg`** |
| Imported binaries | `D:\ghidra-proj\bin\` — client 2007/2008/2009/2011/2013/live-x86/live-x64, engine 2007/2008/live-x86, server 2007/2008 |
| Custom scripts | `D:\ghidra-proj\scripts\` — `DemoProtocolCheck`, `FindPacketEntities`, `FindEntityParse`, `FindDeletionLoop`, `UserMsgTable` |
| Driver scripts | `D:\ghidra-proj\run-all.sh`, `run-engine.sh`, `run-usermsg.sh` |
| Extracted results | `D:\ghidra-proj\out\`, plus `entityparse.txt`, `packetentities.txt`, `deletionloop.txt` at the root |

**The binaries are ALSO on F: as ordinary game installs** — `F:\tf2-builds\{tf2-2007,tf2-2008,tf2-2011,tf2-2013,probe-2011,probe-2013}`, each with `bin\engine.dll` and most with `bin\shaderapidx9.dll`. Those are the source material; `D:\ghidra-proj\bin` holds the copies that were imported and analysed.

**Note what is NOT imported: `shaderapidx9.dll`.** So a rendering-state question needs a fresh import before it can be asked. The existing project is aimed at the demo format, not the renderer.

**Decompile the LIVE client by default; reach for a period build only when the question is about
that era.** Owner's direction, 2026-08-21: *"you shoiuld probably use the modern client for most
decomps really, unless we are doing something that we need to check the old clients for, like why
demos failed or something"*.

The era builds exist to answer era questions — why a 2011 demo will not play, when a protocol
changed, which message id moved. A question about how the renderer works today is answered by the
binary that renders today, and picking a 2008 DLL for it means measuring a build nobody runs.

It bites here in particular: **the only `shaderapidx9.dll` under `F:\tf2-builds` is the 2008 one**,
so a sweep of that folder finds exactly the wrong binary and finds it easily. The live one is at
`F:\SteamLibrary\steamapps\common\Team Fortress 2\bin\shaderapidx9.dll`.

**The pattern for running it** is in `run-engine.sh`: set `_JAVA_OPTIONS`, call `analyzeHeadless` with the project directory and name, `-import` the DLL, `-scriptPath` the scripts folder, `-postScript` the analysis, and redirect both streams to a log with `</dev/null`. Output goes to `out/`.

**Everything above stays outside every git tree, which is the rule and always was.**

**What the old text got wrong, because the shape of the mistake matters.** It read:

> "No decompiler output exists anywhere, and that is deliberate."

The rule it was reasoning from is real and unchanged — decompiler output must never live inside a
git tree, because a folder committed once lives in the history for ever and the projects are
enormous. But "not in the repository" was written down as "does not exist", which is a different
claim, and it was never checked. It then read as authoritative and cost a real lookup: a session
searching for the engine's overlay and poly-offset code argued from this paragraph that no
decompilation was available, while one was sitting on the disk.

Same family as [[an-empty-search-needs-a-control]] — an absence asserted rather than measured — with
the extra sting that nothing was searched at all. **A rule about where something may live says
nothing about whether it exists.**

**No test hardcodes any of this any more, and none may.** `SdkReference.GameInstall` is the one
place that knows where the game is and `SourceSdk` the one place that knows where the SDK is; both
honour `TF2_FOLDER` / `SOURCE_SDK` first. All ninety-four private copies were removed on 2026-08-27
(D109) — see [[extraction-without-adoption-is-not-dry]], which is also where the reason lives: forty
of them were a bare `F:` path with no override, so they measured nothing on any other machine and
reported it as a skip.

**This paragraph used to say the opposite** — that the path was hardcoded in `ArmsModelProbe`,
`ClassScriptProbe` and `ControlPointMaterialProbe`, and that a helper "would be a tidy-up worth
doing if a third form of the path appears". All three of those files are converted, and the third
form had long since appeared. Kept rather than deleted because it is the failure mode this whole
directory is about: a memory that names files goes stale silently, and this one would have sent the
next session looking for something that is gone.

**Grep the SDK checkout; never fetch it a file at a time.** `F:/src/source-sdk-2013` (sources under
`src/`) is the whole tree, and a whole-tree grep answers in one call what a `WebFetch` answers only
if you guessed the right filename. This was hunted for twice in one session before the owner said it
existed, after fetching `bspfile.h` and `utils/vbsp/map.cpp` from GitHub one at a time in between.
Landmarks: `src/tier1/bitbuf.cpp` ([[valve-publishes-bitbuf]]), `src/public/bspfile.h`,
`src/utils/vbsp/overlay.cpp`, `src/utils/common/bsplib.cpp`.

**It does not contain the engine**, which is a real limit rather than a search failure. `vbsp` writes
an overlay's `uv0`–`uv3` straight through from the VMF and nothing in the SDK reads them back, so the
corner-to-texture-coordinate order is not answerable from source at all — that one was settled by
measuring the corners in a real map. When a whole-tree grep comes back empty for a *consumer*, the
answer is "engine-side, never released", and the next move is measurement or the decompiler above,
not another fetch. See [[nothing-is-closed]] and [[differential-beats-fixtures]].
