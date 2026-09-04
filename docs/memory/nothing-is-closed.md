---
name: nothing-is-closed
description: Never write that something is closed or unknowable — the SDK, its public headers, the game's shipped data and the shipped binaries have answered every such claim made here; covers reading Valve's shader or header for a rendering defect BEFORE measuring our own data, the generated coverage report that already holds the denominator, measuring every hop in a chain rather than re-reasoning about the ones already checked, and suspecting the input and its identity before suspecting a correct algorithm.
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
never in one piece. Their headings are kept below. **Four more were folded in on 2026-09-04** —
about the same failure one step earlier, before any search has even started: measuring our own data
before reading Valve's, re-deriving a denominator the project had already generated, blaming an
algorithm for a hop nobody measured, and suspecting a correct implementation because the wrong side
of the comparison was never checked.

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

Related: [[differential-beats-fixtures]], [[a-default-is-not-a-constant]].

---

## `read-the-spec-before-measuring-our-data`

**A visual defect means read the SDK for that feature FIRST. Not after the theories run out.**

**Why:** measuring this project's own data can only find data that is wrong. It cannot find a
feature that was never implemented, because every number will be correct — and it will look like
progress the whole time. One session, one capture point:

- Six measurements of the model — bodygroup tags, vertex spans, `.vvd` fixups, `.vtx`/`.mdl`
  pairing, material indices, instance census. Every one correct. The model was never wrong.
- Four renderer theories about the wall stripes before anyone asked the BSP what they were.
- The answers, each found in minutes once the right file was opened:
  `stdshaders/unlittwotexture_ps2x.fxc` (two textures MULTIPLIED, alpha forced to 1),
  `imaterialsystem.h:180` (MATERIAL_CULLMODE_CCW, so front faces are clockwise),
  `imaterial.h:369` (`$nocull` is MATERIAL_VAR_NOCULL, a per-material flag).

The owner had already made this a standing rule, in CLAUDE.md and in
[[parity-is-the-search-not-the-defence]], and had to repeat it. That is the actual failure: the rule
was known and applied late.

**How to apply.** On any "it looks wrong" report, before writing a probe or a log:

1. Name the shader or subsystem responsible — the VMT's shader name, the material flag, the engine
   routine.
2. Open Valve's file for it. `F:/src/source-sdk-2013`, `stdshaders/` for shaders, `public/` for the
   flags and enums. Reading published source is not decompilation.
3. Only then measure, and measure the gap between what that file says and what this project does.

**The tell that this is being skipped:** a series of measurements that all come back correct. Three
in a row means the question is wrong, not the data. Stop and go read.

Related: the entry below on measuring every hop is the same discipline for OUR chain; this one is
for the part of the chain that is Valve's and was never built.

---

## `measure-every-hop-before-blaming-one`

**When a change does not take effect, enumerate the hops it travels and measure each one. The bug is
always in the hop nobody measured.**

**Why:** bodygroups on the capture point hologram. Three hops were measured and all correct — the
model offers 4 alternatives, the demo carries bodies 0/2/3, the packer produced "9 batches spanning
4 alternatives" — and the picture still showed one sign on every point. Three correct measurements
proved only that the fault was in the fourth hop, which was the one never looked at: the value
arriving at `DrawModel`.

The same shape recurred all session:

- The overlay stripes: four renderer theories, all killed by measurement, and the answer was in the
  BSP's face list the whole time.
- The player pose: decode, matrix maths and Euler conversion all verified against the SDK, and the
  fault was a substring lookup one layer above them.
- Doors: a submodel geometry reader was nearly built before counting showed the faces were already
  in the world buffer.

**One symptom can have several INDEPENDENT causes, and fixing one proves nothing about the
diagnosis.** "The viewmodel does not appear" had five: not loaded, not uploaded, wrong sequence,
wrong owner, wrong posing mechanism. Each was real, each was fixed correctly, and after each fix the
screen looked exactly the same as before — so every fix read as a failed hypothesis when it was not.
A pipeline with N stages can be broken at N of them at once, and it usually is when the whole
pipeline is new. **Verify a fix at its own stage** (was the model in the packed set? did the upload
happen?) rather than at the far end, or a run of correct work looks like a run of wrong guesses.

**How to apply:** write the chain down — file, decode, pack, instance, draw — and put a number on
each link before touching code. A hop that "obviously works" is exactly the one to instrument,
because the hops that obviously work are the ones nobody instrumented. And prefer measuring the
LAST hop first: it is closest to the symptom and cheapest to read.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[read-the-map-before-the-renderer]].

---

## `suspect-the-input-not-the-algorithm`

> **"When a perfect algorithm keeps giving a wrong answer, suspect the input and the identity of the
> thing you're measuring — not the algorithm."**

Supplied by the owner (written by another AI) after a day spent proving it the hard way.

Measured 2026-08-28. A map checksum was implemented from Valve's published description on the first
attempt and was **correct from the first attempt**. It did not match. What followed: a whole-file
variant, a file-order variant, five lump-count ceilings, an exhaustive sweep of every single extra
lump exclusion, a padding-inclusive variant, every lump alone, and finally a decompilation of the
2007 `engine.dll` — which confirmed the original implementation exactly. Two write-ups were committed
blaming the wrong thing: first the map files, then the byte selection.

The actual faults were both on the other side of the comparison. **The field was the wrong one** —
`svc_ServerInfo` carries two checksum-shaped values and the code had chased the wrong one, with its
own comment saying that branch was *"flagged rather than trusted"*. And **the engine omits
`CRC32_Final`**, so its number is the complement of a standard CRC32.

**Why single-variable search cannot find this.** With two faults present, every test that changes one
thing and holds the rest fails — and each failure reads as evidence against the variable being
tested. That is how a correct implementation gets rewritten and a correct conclusion gets abandoned.
Widening the TARGET instead — "what if the answer I want is a different number?" — cost one line and
was available from the first hour.

**How to apply:** before optimising or rewriting a computation that will not match, spend one cheap
check on each of: is this the right input file, is this the right FIELD, and is the expected value
transformed on its way to me (endianness, complement, offset, sign). Then, if it still fails, ask
whether TWO things could be wrong — because the one-at-a-time discipline that is right for a single
fault is exactly what conceals a pair. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[the-denominator-decides-what-can-be-lost]].
