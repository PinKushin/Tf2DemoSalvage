---
name: nothing-is-closed
description: Never write that something is closed or unknowable — the SDK, its public headers, the game's shipped data and the shipped binaries have answered every such claim made here.
metadata:
  type: feedback
---

**Owner's rule, stated 2026-08-17: nothing should ever be described as closed and unavailable. It
is never unavailable.**

**Why:** every time this project has written that something is unknowable, the claim was false and it
cost a real defect. It is never a neutral note — it is an instruction to future readers to stop
looking, and they do.

**Four memories were merged into this one on 2026-08-27** — `closed-source-check-the-public-api`,
`tf2-game-code-is-in-the-sdk`, `shipped-data-is-a-source` and `binaries-answer-what-the-sdk-cannot`.
Each was a different *place to look*, and having them as separate entries meant the search order was
never in one piece. Their headings are kept below.

## The order to check, and say which one you checked

1. **`source-sdk-2013`** — including `utils/`, which holds the compilers and is where the lighting
   answers live, and `src/game/*/tf`, which is TF2 itself.
2. **Its public headers and the callers of the closed part** — `src/public/`, interface
   declarations, and the call sites in `vbsp`, `vrad`, `stdshaders` and the game DLLs.
3. **The game's own shipped data** — VMTs, `.res` files, `cvarlist.log`, VPK contents.
4. **The shipped binaries** — a raw PE scan reads tables no source contains.
5. **A decompiler**, which the owner considers a normal tool to reach for readily.

If after that it is genuinely not in hand, write down *what was searched* rather than *that it
cannot be known*.

## The four that were wrong

- `BspAmbientLight.Nearest` carried *"The engine blends a leaf's samples in `LightcacheGetDynamic`,
  which is in the closed engine — the weighting is not in `source-sdk-2013` and cannot be
  transcribed."* The function is `Mod_LeafAmbientColorAtPos`, published in
  `utils/vrad/leaf_ambient_lighting.cpp`, and it is an inverse-squared-distance weighted average.
  Nobody had looked. It made one capture point on cp_process draw at 0.10 while its mirror image on
  a symmetric map drew at 0.39.
- *"TF2 is closed"* was written in three places and checked in none — see below.
- `$modblend` was filed as needing a decompiler. It is declared in three shipped VMTs and read by a
  commented-out proxy — see below.
- **The sound mixer is genuinely closed and its cvar's MEANING still was not** (2026-08-22). The
  claim in hand was that `snd_mixahead` is how far ahead the mixer renders. Half right, and the
  useful half was missing: `game/server/sceneentity.cpp` reads it through a function called
  `GetSoundSystemLatency()`, with `SOUND_SYSTEM_LATENCY_DEFAULT (0.1f)` as the fallback, to align
  lipsync with speech that will not be heard for 100 ms. It is a fixed pipeline DELAY the engine
  schedules around, not a target and not a clamp.

**That last one adds a case the rule did not cover: a closed component's behaviour can be documented
by published code that merely CONSUMES it.** `snd_dma.cpp` is not in the SDK and never will be, so
"the mixer is closed" was true — and answering the actual question needed no decompiler at all,
because a game-side caller had to reason about the engine's latency and wrote down what it is. When
the implementation is closed, grep for its CALLERS.

**The compounding danger is a defensible-sounding substitute.** The nearest-sample comment did not
merely admit ignorance; it argued that nearest was "a decision this project can defend" and that a
blend would be "a guess wearing parity's clothes". That reasoning is what kept it in place. Worse,
nearest is not a coarse approximation of the real answer: vrad's `CompressAmbientSampleList` deletes
every sample the blend can already predict, so the stored set only reconstructs the original lighting
when interpolated, and taking the nearest reads back an arbitrary survivor of that thinning.

---

## `closed-source-check-the-public-api` — a black box has a surface

**Hitting a closed-source component in `source-sdk-2013` is not the end of the search.** The
engine, `materialsystem` and the client are not published, but every one of them is *used* by code
that is — through headers in `src/public/`, interface declarations, and the call sites in `vbsp`,
`vrad`, `stdshaders` and the game DLLs.

**Go to the public API first.** What a black box exposes, and what its callers do with it, is
usually enough to reverse what is needed — because a demo or a BSP only ever exercises that public
surface anyway. If the private implementation mattered, the file format would not be readable by
anything but the engine.

The alternative failure is treating "closed" as "unknowable" and falling back to guessing, or to
copying another implementation's workaround, which imports that implementation's bugs along with its
behaviour.

**When a grep lands in a closed component, immediately search `src/public/` for the interface, the
constants, and the callers.** Constants especially:
`NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS`, `m_DepthBias_Decal` and the bump basis were all public
even though the code consuming them is not. See [[read-the-encoder-not-the-decoder]] and
[[valve-publishes-bitbuf]].

---

## `tf2-game-code-is-in-the-sdk` — 1,318 files nobody had looked for

**`source-sdk-2013` carries TF2's game code.** 1,318 files across `src/game/shared/tf`,
`src/game/client/tf` and `src/game/server/tf`. Verified 2026-08-16. Concretely:

- **All 125 HUD sources**, `tf_hud_deathnotice.cpp` (the kill feed) among them.
- **`tf_shareddefs.h`** — the full `TF_COND_*` player-condition enumeration with values
  (`TF_COND_INVULNERABLE = 5`, `TF_COND_BURNING = 22`, …), and `TF_CLASS_UNDEFINED` /
  `TF_FIRST_NORMAL_CLASS` at lines 205 and 198.
- **`c_tf_player.cpp:395,398`** — the übercharge material names, `models/effects/invulnfx_blue.vmt`
  and `invulnfx_red.vmt`.
- **`game/shared/econ/`**, 55 files: the item schema, the attribute system, `CEconStyleInfo`,
  paint-kit definitions, per-team attached models.

**This project had recorded the opposite in three separate places** — `docs/CONFORMANCE.md`, the
client-system conformance batch and the entity batch — and none of them had checked. One of them
named a decompiler as the next step for a constant that is an ordinary `#define`.

**The mechanism of the error is the part worth carrying forward.** The search looked in
`client/replay/`, found a reference with no definition, and concluded the definition existed nowhere.
**An absence found by a search is a fact about the search.** Third instance of that shape in this
project — see also the level-name filter in `docs/findings/24-reference-capture.md` and the "Econ"
substring count that briefly reported 405 matches inside words like "second".

**The cost is invisible, which is why it lasted.** Nothing was blocked; work was just deferred as
expensive when it was cheap. Anything else previously waved off with "TF2 is closed" is worth
reopening — the item system, the HUD and the material overrides all were, and are now specified.

---

## `shipped-data-is-a-source` — the game's data explains itself

**The source menu lists the SDK, the Rust parser, the wiki and a decompiler. It is missing the
game's own shipped data**, and that fifth source settled two questions this project had filed as
closed. Both on 2026-08-16, within an hour of each other.

- **`$modblend`** was the standing worked example for "the SDK cannot answer this, decompile it". No
  decompiler needed. It is declared in three shipped VMTs and read by **nothing in TF2** — the only
  consumer is an `Equals` proxy **commented out four lines below it** in the same file. No published
  shader declares it, and it is absent from 21 TF2 binaries across six eras including all five
  `stdshader_*.dll` (re-verified 2026-08-19 with a positive control, because the claim rests on an
  absence). Correct implementation here is nothing.

  **It was never a shader parameter.** A proxy resolves `srcVar1` by NAME on the material —
  `pMaterial->FindVar( pSrcVar1, &foundVar, true )` (`functionproxy.cpp:210`,
  `imaterial.h:484`) — and any key written into a VMT becomes a material var. So `$modblend` is an
  **artist-authored variable** holding a constant for the `Equals` proxy that is commented out
  beside it. No shader declares it because none ever could; no binary names it because none would.
  All three declaring materials are TF2 content and two are MvM (2012), so it is not inherited
  boilerplate either.

  **Do not call it dead, and do not generalise from it.** "Dead" was the old wording and the owner
  corrected it: a parameter this game ignores may be live elsewhere in the Source family. That is
  true for `$vertexcolor` — a `MATERIAL_VAR_*` flag, engine-level, consumed by `cable_dx6.cpp` and
  the fixed-function `decal.cpp`, merely unreachable from a DX9 `LightmappedGeneric` world face. It
  is NOT true for `$modblend`, which is a name someone typed. The distinction is flag versus
  authored key, and it decides whether "unimplemented" means anything.
- **Game event field widths and signedness** were "outside the SDK, `GameEventManager` is closed".
  They are in the comment block atop `game/mod_hl2mp/resource/modevents.res`: `short` is 16-bit
  **signed**, `long` 32-bit signed, `bool` 1 bit unsigned. Signedness had been assumed, and getting
  it backwards yields a plausible number rather than an error.

**Why it gets skipped: data does not feel like a source.** The habit is to look for code, and a
`.res` or `.vmt` reads as content rather than as documentation — but Valve's data files carry prose
explaining their own format, written for the people who author them. **When the question is about a
format the GAME reads, read what the game ships.** When it is about engine behaviour, the code
sources still apply.

**A third instance, 2026-08-26: `tf/cvarlist.log`.** The game ships a plain-text dump of **3,668
convars and concommands with their defaults, flags and help strings**, in fixed columns:

```
fps_max                                  : 400      :                  : Frame rate limiter, cannot be set while connected to a server.
engine_no_focus_sleep                    : 50       : , "a"            :
```

Look one up with `grep -E "^<name> +:"` — anchored and with the colon, or `fps_max` matches inside
other convars' help text and `volume` matches eleven others. It covers everything registered in
`engine.dll`, `materialsystem.dll` and `vguimatsurface.dll`, none of which are in `source-sdk-2013`.

**It is a dump, not a declaration**, so an `FCVAR_ARCHIVE` convar the user has changed could be
captured as if it were the default. Cross-check against a registration where one exists; where none
does, this beats scanning PE strings by a wide margin. Detail in
`docs/findings/40-the-game-ships-its-own-cvar-list.md`.

Practical notes for doing it:

- VMTs live inside VPKs. `grep -a` works directly on the `.vpk`, and `dd` around the byte offset from
  `grep -abo` gives readable context without unpacking anything.
- Extracting `$`-prefixed strings from `bin/stdshader_dx9.dll` yields **515** parameter names — a
  usable denominator for what the shipped shaders actually accept, no decompiler involved.

---

## `binaries-answer-what-the-sdk-cannot` — and read them with a byte scan

**"Not in the source" is not "not knowable".** On 2026-08-11 six TF2 clients and three engines
were read, and they answered five questions the corpus and the SDK together could not: every
unnamed user message id, the per-era table lengths, `VOICE_MAX_PLAYERS` at three dates, the demo
header layout, and which protocol transitions Valve's own engine treats as breaking.

Valve publishes *some* source, and the sdk2013 drop describes a build years newer than its name — it
contains `RDTeamPointsChanged`, which the March 2013 client does not have anywhere. **A shipped
binary is the only artifact that is exactly one build.**

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

Everything runs under `D:\ghidra-proj`, outside every git tree, and only constants come back — the
paths are in [[where-the-game-and-clients-live]] and the rule is a global memory,
`never-decompile-into-a-repo`.

---

**An absence measured any of these ways needs a positive control in the same sweep.** A grep
returning zero has been a fact about the grep three times in this project — "it is closed" is an
absence claim about a search nobody ran. See [[an-empty-search-needs-a-control]] and
[[an-uncoverable-gap-is-usually-your-reader]].

Related: [[read-the-spec-before-measuring-our-data]], [[the-denominator-is-already-written-down]],
[[differential-beats-fixtures]], [[a-default-is-not-a-constant]].
