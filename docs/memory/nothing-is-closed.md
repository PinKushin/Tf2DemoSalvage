---
name: nothing-is-closed
description: "Never write that something is closed or unknowable — the SDK, its public headers, the game's shipped data and the shipped binaries have answered every such claim made here; covers reading Valve's shader or header for a rendering defect BEFORE measuring our own data, the generated coverage report that already holds the denominator, measuring every hop in a chain rather than re-reasoning about the ones already checked, and suspecting the input and its identity before suspecting a correct algorithm."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:56:11.229Z
---

**Owner: nothing should ever be described as closed and unavailable. It is never unavailable.**

**Why:** every "unknowable" claim made here was false and cost a real defect — it's an instruction to
future readers to stop looking, and they do.

## The order to check, and say which one you checked

1. `source-sdk-2013` — including `utils/` (compilers, lighting) and `src/game/*/tf` (TF2 itself).
2. Public headers and callers of the closed part — `src/public/`, interface declarations, call sites
   in `vbsp`/`vrad`/`stdshaders`/the game DLLs.
3. The game's own shipped data — VMTs, `.res` files, `cvarlist.log`, VPK contents.
4. Shipped binaries — a raw PE scan reads tables no source contains.
5. A decompiler — a normal tool, reach for it readily.

If genuinely not in hand after that, write down what was SEARCHED, not that it cannot be known.

## Four claims that were wrong, all in this project

- Ambient light blending was called "closed engine, cannot be transcribed" — it's published in
  `utils/vrad/leaf_ambient_lighting.cpp`, an inverse-squared-distance weighted average, unread.
- "TF2 is closed" was written in three places, checked in none (see below).
- `$modblend` was filed as needing a decompiler — declared in three shipped VMTs, read by a
  commented-out proxy (see below).
- **The sound mixer is genuinely closed and its cvar's MEANING still wasn't found without decompiling**
  — `snd_mixahead` turned out to be a fixed pipeline DELAY (`GetSoundSystemLatency()`,
  `sceneentity.cpp`), not a target or clamp — settled by grepping the CALLER of the closed component,
  no decompiler needed. **When the implementation is closed, grep for its CALLERS.**

**The compounding danger is a defensible-sounding substitute** — a comment arguing a wrong shortcut
was "a decision this project can defend" is what kept it in place.

---

## `closed-source-check-the-public-api` — a black box has a surface

**Hitting a closed component in `source-sdk-2013` is not the end of the search.** The engine,
materialsystem, and client are unpublished but USED by published code through `src/public/`
interfaces and call sites in `vbsp`/`vrad`/`stdshaders`/the game DLLs. Go to the public API first —
what a black box exposes, and what its callers do, is usually enough. Constants like
`NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS`, `m_DepthBias_Decal`, the bump basis were all public even
though the consuming code isn't — a proxy resolves a name via `FindVar` (`functionproxy.cpp:210`,
`imaterial.h:484`), and a cull-mode question is settled by `imaterialsystem.h:180`
(`MATERIAL_CULLMODE_CCW`) and `imaterial.h:369` (`$nocull` is `MATERIAL_VAR_NOCULL`). See
[[read-the-encoder-not-the-decoder]], [[research-before-code]].

---

## `tf2-game-code-is-in-the-sdk` — 1,318 files nobody had looked for

**`source-sdk-2013` carries TF2's game code** — 1,318 files across `game/{shared,client,server}/tf`,
including all 125 HUD sources, `tf_shareddefs.h`'s full condition enum, übercharge material names
(`c_tf_player.cpp:395,398`), and 55 files of econ/item schema. This project had recorded the opposite
in THREE places, none checked.

**The mechanism worth carrying forward:** a search looked in one subdirectory, found a reference with
no definition, concluded the definition existed nowhere. **An absence found by a search is a fact
about the search.** Third instance of this shape in the project.

---

## `shipped-data-is-a-source` — the game's data explains itself

**The source menu is missing the game's own shipped data**, and it settles questions filed as closed.

- **`$modblend`**, the standing "needs a decompiler" example — declared in three shipped VMTs, read
  by nothing (an `Equals` proxy commented out four lines below), absent from 21 binaries across six
  eras with a positive control confirmed. It was never a shader parameter — a proxy resolves `srcVar1`
  by NAME, so any VMT key becomes a material var. It's an artist-authored variable holding a constant
  for a proxy that's commented out. **Don't call it dead or generalise from it** — `$vertexcolor` is a
  real engine flag, live elsewhere in Source, merely unreachable from a DX9 world face; `$modblend` is
  a name someone typed, a different category entirely.
- **Game event field widths/signedness**, "outside the SDK" — documented in a comment block atop
  `modevents.res`.
- **`tf/cvarlist.log`** — 3,668 convars/concommands with defaults, flags, help text, covering
  `engine.dll`/`materialsystem.dll`/`vguimatsurface.dll`, none in the SDK. It's a dump, not a
  declaration (an `FCVAR_ARCHIVE` convar the user changed may be captured as if default) — cross-check
  against a registration where one exists.

**Why it's skipped:** data doesn't feel like a source, but Valve's data files carry prose explaining
their own format. When the question is about a format the GAME reads, read what the game ships.

Practical: VMTs live in VPKs — `grep -a` works directly on the `.vpk`; extracting `$`-prefixed
strings from a shader DLL gives a usable parameter denominator without a decompiler.

---

## `binaries-answer-what-the-sdk-cannot` — and read them with a byte scan

**"Not in the source" is not "not knowable."** Six clients and three engines answered five questions
the corpus/SDK together couldn't: unnamed message ids, per-era table lengths, a constant across
dates, the header layout, breaking protocol transitions.

- `Register("Name", size)` compiles to `push size; push offset name` on x86 — the whole table is a
  literal sequence, findable by byte scan.
- **Ghidra's analysis is not the reliable instrument** — found a table in two eras, missed it entirely
  in a third (strings present, zero code references), and silently drops undefined strings.
- **The reliable instrument is a raw PE scan**: find `68 <imm32>` where the immediate is a printable
  string's address, sort by offset, cluster. No disassembly, nothing to fail.
- Disable Ghidra's Decompiler Parameter ID analyzer for a 10x+ speedup on scan-only work.
- x64 binaries are useless for this (args in registers, no push to find) — the 32-bit client still
  ships.
- Date any build without launching it: `grep -a "Exe build"` on `engine.dll`.

Everything runs under `D:\ghidra-proj`, outside every git tree; only constants come back. See
[[where-the-game-and-clients-live]].

---

**An absence measured any of these ways needs a positive control in the same sweep.** See
[[instrument-bugs-outnumber-decoder-bugs]], [[the-denominator-decides-what-can-be-lost]].

Related: [[fixtures-are-the-weak-point]], [[a-default-is-not-a-constant]].

---

## `read-the-spec-before-measuring-our-data`

**A visual defect means read the SDK for that feature FIRST, not after theories run out.** Measuring
this project's own data can only find data that's wrong — it can't find a feature never implemented,
and every number will look correct the whole time. One session: six correct measurements of a model,
four wrong renderer theories about wall stripes, before anyone asked the BSP what they were —
answered in minutes once the right file was opened.

**The tell that this is skipped: a series of measurements that all come back correct.** Three in a
row means the question is wrong, not the data — stop and go read.

**How to apply:** name the responsible shader/subsystem, open Valve's file for it, THEN measure the
gap between it and this project.

Related: the entry below on measuring every hop is the same discipline for OUR chain; this one is for
the Valve part of the chain that was never built.

---

## `measure-every-hop-before-blaming-one`

**When a change doesn't take effect, enumerate the hops it travels and measure each one. The bug is
always in the hop nobody measured.** Three hops measured correct, and the picture was still wrong —
the fault was in the fourth, unmeasured hop.

**One symptom can have several INDEPENDENT causes, and fixing one proves nothing about the
diagnosis.** A missing viewmodel had five separate causes; each was fixed correctly and the screen
looked identical after each, reading as a failed hypothesis when it wasn't. **Verify a fix at its own
stage**, not at the far end.

**How to apply:** write the chain down (file, decode, pack, instance, draw), put a number on each
link. Prefer measuring the LAST hop first — closest to the symptom, cheapest to read.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[read-the-map-before-the-renderer]].

---

## `suspect-the-input-not-the-algorithm`

*"When a perfect algorithm keeps giving a wrong answer, suspect the input and the identity of the
thing you're measuring — not the algorithm."*

A map checksum implemented correctly from Valve's description on the FIRST attempt didn't match. A
day was spent rewriting the correct implementation five different ways before decompiling the engine
confirmed the original was right all along — **the actual faults were both on the OTHER side of the
comparison**: chasing the wrong field, and the engine omitting `CRC32_Final` (its number is the
complement of standard CRC32).

**Why single-variable search can't find this:** with two faults present, every test changing one
thing and holding the rest fails, reading as evidence against the variable under test. Widening the
TARGET ("what if the answer I want is a different number?") cost one line and was available from hour
one.

**How to apply:** before rewriting a computation that won't match, check: right input file, right
FIELD, is the expected value transformed (endianness, complement, offset, sign)? If it still fails,
ask whether TWO things are wrong.

---

## `absent-from-the-sdk-is-not-unreadable` — ask which binary implements it

Asked to implement ragdoll physics, checking only the public headers led to writing that the
integrator "is this project's own". Owner: *"remember you have the decomp so no nothing is ours."*
`vphysics.dll` ships with the game — that's what Ghidra is for. Filing a closed component as
own-design silently converts a parity project into an approximation.

**How to apply:** before writing that anything is ours to design, ask which binary implements it. If
the game ships it, decompile it. Reserve "ours" for something no shipped artefact contains at all.

---

## `shipped-data-settles-what-closed-code-cannot` — ask what the content would have to mean

Measured on B328: 403 materials carry a DirectX-gated block with no registered shader of that name in
the SDK — looked like a decompiler question. It wasn't: under "the block doesn't apply", Valve
authored a bump map that draws on NO hardware at all — not a tenable reading, so the block applies.

**General form: ask what the content would have to mean for your reading to be true.** Shipped assets
are made by people who tested them.

**The mistake this corrected:** these blocks were written off as "low-end fallbacks" from reading
block NAMES alone — inside, they declared keys ONLY there (`$bumpmap` in 89 materials, `$envmap` in
49), so ignoring the block loses them outright. Census two things: what a container contains, AND
what's declared solely inside it.

**Honest scope:** the fix changed nothing on TF2's own content (all affected materials are mounted
HL2 assets) — report the population the change reaches, not the population that declares the key.

---

## `a-valve-comment-can-be-stale` — a comment is a claim about the code, not about the game

An SDK comment named a specific default material for detail sprites; 234 installed TF2 maps override
it via `worldspawn`, and only 49 name the SDK's default — every grass capture ever taken used the
wrong texture (B364).

**Why:** the comment is true per-map (structural claims hold), only the literal NAME is wrong, and
the name is the half transcribed into a constant. The override lives in a different file
(`detailobjectsystem.cpp:1516`) from the comment (`public/gamebspfile.h`), so reading the struct never
meets it.

**How to apply:** when an SDK comment names a specific constant, treat it as a lead — ask the shipped
data how many maps/models actually use it. Symptom is invisible: the wrong asset still draws, doing
exactly what the comment promised. See [[a-default-is-not-a-constant]], [[the-base-is-not-the-behaviour]].

---

## `settle-a-constant-in-the-disassembly` — shape from the decompiler, identity from the instructions

**Two wrong conclusions from reading DECOMPILED C rather than disassembly:** a constant taken for π
because its neighbour genuinely is 2π (it was actually an epsilon, four bytes away); a decompiler
local reused for two unrelated SSA values, read as one.

**The tool:** `DisasmWithData.java` — disassembly with every memory operand resolved to its actual
contents on the same line, so an address can't alias:
```
1800386df  MOVAPS XMM4,xmmword ptr [0x1800ff130]   ; = {0.0, …, NaN}
1800387f5  MULPS  XMM7,xmmword ptr [0x180124f70]   ; = {1.0, 1.0, 1.0, 0.5}
```

**The rule: any claim about which memory a value came from is settled in the disassembly, never a
decompiler local** (Ghidra's invented name). Decompiled C is still right for CONTROL FLOW and
expression shape — shape from the decompiler, identity from the disassembly.

**Except a floating-point sum's GROUPING, which is identity too** — doubles aren't associative; the
decompiler reorders commutative operands freely and prints a flat chain, so anything ported bit for
bit (a sum, a dot, a Newton step) must come from the INSTRUCTION order, verified by a test whose
inputs round differently under each grouping.

**The trigger:** a sentence naming a `DAT_`/`_UNK_` symbol, or reaching for a value because it's
ADJACENT to one already known (weaker, earlier tell) — that's the moment to run the script, not after
the paragraph is written.

**A field's WRITER is also a disassembly search, not a decompiler one** — a field filed as "no
writer" in three places had one, found by grepping instructions for a store to its offset; the
decompiled C showed the write as an assignment to a differently-named local, invisible to text
search.

Related: [[instrument-bugs-outnumber-decoder-bugs]]#an-empty-search-needs-a-control and
#print-a-value-somebody-can-recognise — report the value that was USED, carried from where produced,
never recalled or recomputed by a second route.
