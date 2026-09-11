# CLAUDE.md — implementation handoff

**This file is bare rules and pointers, and every fact in it is stated in full somewhere else.** It
loads on every turn of every session, so anything here is paid for thousands of times — a measurement,
a table of names, or a paragraph of reasoning belongs in the document that owns it. `ROADMAP.md` (449
lines) is worth reading through; `docs/DECISIONS.md` is 7,947 lines and is read by number, not in full
— which document answers what is a table in `docs/findings/README.md`.

**When you find a restatement here, move it and leave a pointer.** The last four found were all
stale: a test count off by 829, a probe table naming 18 of 49 and claiming 44, a language constraint
D2 had already superseded, and a document-routing table that had drifted a row apart across three
copies.

## What this project actually is

A standalone TF2 `.dem` parser that works across TF2's full history, including demos the live game client can no longer play due to Valve's own schema changes. The insight that makes this tractable: `.dem` files embed their own entity schema (`SendTables`, via the `dem_datatables` command), so a parser that decodes generically off whatever schema each file provides — rather than hardcoding one era's field layout — doesn't need to "know" every TF2 version, just the container/bit-packing quirks, which change far less often. Full explanation in `ROADMAP.md` §1.

## Commands

The lookup table; reasoning lives in the sections below, not here. Every path is from the repo root.

| do | run |
|---|---|
| build everything | `MSBUILDDISABLENODEREUSE=1 dotnet build Tf2DemoSalvage.slnx` |
| **the merge gate, phase 1** (twelve assemblies, count-floored) | `TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh` |
| **the merge gate, phase 2** (UI — takes the desktop, so the machine-wide lock) | `pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests` |
| full corpus superset (~30 min; decode changes only) | `bash build/gate.sh` (no `GCOR_ONLY`) |
| one test project | `dotnet test tests/Tf2DemoSalvage.Core.Tests` |
| one test | `dotnet test tests/<proj> --filter "FullyQualifiedName~<Name>"` — NOTE: any filter silently drops every `[Explicit]` test |
| list the probes / run one | `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release --` &nbsp;·&nbsp; `… -- carried <demo> <tick> [class]` |
| decompile a demo to text | `dotnet run --project managed/Tf2DemoSalvage.Cli -c Release -- <demo> -t -e -o out.txt -q` |
| viewer, headless screenshot | `TF2VIEW_CAMERA="x y z pitch yaw" pwsh …/run-exclusive.ps1 managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe <demo> --tick <n> --shot out.png` |
| what the viewer accepts | `tf2demoview --help` — every flag and env var, no window, one call |
| **measure the frame** | `TF2VIEW_AUTOPLAY=1 pwsh …/run-exclusive.ps1 tf2demoview <demo> --tick <n> --first-person --measure 20 +fps_max 0` |

**`--measure <seconds>` counts PLAYBACK, not wall clock, and prints to stdout.** Both halves of that
matter and both were learned the hard way: a run timed from process start spends its first twenty
seconds on archives and the map, so a "forty second" measurement was two seconds of frames; and the
log is BUFFERED, so reading it while the viewer runs shows asset loading and nothing else — which
was twice misread, once as the viewer having exited on its own. It replaces a six-call dance of
build, launch, wait for the process, sleep, kill, grep.

Never `--no-build` (a hook blocks it); never one `dotnet test` over the whole solution (assemblies
run concurrently and the UI suite loses the desktop — the two-phase gate exists for that). The
viewer/UI rows take `run-exclusive.ps1` because they take the desktop.

## Non-negotiable constraints (owner-stated, don't relitigate without asking)

- **No Rust**, explicitly rejected, don't suggest it. **No C++ by default** — the one exception is
  wrapping Source SDK code for a specific Phase 3 asset format, behind a C ABI shim (D4). **No Python
  for the core**, too slow for bulk corpus processing.
- **Pure C# everywhere, including work that feels performance-sensitive** (D2). `unsafe`, `Span<T>`,
  `stackalloc`, `MemoryMarshal` before reaching for native code. `native/libtf2dem` is a placeholder
  folder and **not a starting point** — untouched for Phase 1/2, and revisited only if Phase 3
  profiling shows a specific piece needs it. **D2 supersedes the original plan's C decode core, which
  was never built**: do not resurrect it from an old transcript or a stale doc, and C-vs-Zig is
  decided if that trigger fires, not before.
- **Byte-level unit tests for each decode primitive come before its implementation**, on small
  hand-built fixtures — the corpus is too sparse to catch a primitive-level bug. How TDD, SOLID and
  DRY map onto this codebase's actual seams: D6.
- **Stryker.NET runs on every C# test project as part of normal development**, not bolted on at the
  end — coverage says a line ran, mutation says the suite would notice it being wrong. A surviving
  mutant is a finding: add the assertion, or delete the path that turns out not to matter (D6).
- **A user's real TF2 config must work wholesale — `.cfg` or a mastercomfig-style `.vpk`** (D69), so
  the viewer's own vocabulary *is* Source's: keys named `SPACE`, `CTRL`, `MOUSE1`, `'`, `/`, actions
  named `+forward`, `+jump`, `+moveup`. A translation layer defeats the point, because the requirement
  is that a paste works, and **ignoring unknown commands is the primary feature** — a real config is
  hundreds of `mat_*`/`cl_*`/`alias`/`exec` lines this viewer does not implement, and a parser that
  objected would reject every real file. **The wrong turn to recognise:** `ROADMAP.md` filed this for
  weeks as *"the return is small… copying TF2's default bindings gets almost all of the benefit"*, so
  a defaults table got built and mistaken for the feature. Concluding this is nearly done because the
  defaults match TF2 is that same turn.
- **The analyzers are already wired and they fail the build** — `SonarAnalyzer.CSharp` and
  `Microsoft.CodeAnalysis.NetAnalyzers` in `Directory.Build.props`, versions in
  `Directory.Packages.props` (D50), so no `.csproj` mentions either and a grep over project files
  reads as absence. Sonar emits `error S`, so **`grep -E "error C"` cannot see a failing build** — it
  reported success while the build was red, and a stale binary got measured.

## Synthetic fixtures come FIRST; the corpus keeps only what real bytes alone can prove (D38)

**A synthetic fixture is STRONGER, not a compromise** — a corpus test does not know the right answer
and must compare two readings of one file, where a hand-built one HAS ground truth because the test
put the value there, and `SyntheticDemo` can write a demo the engine itself accepts. **And the
question that kills most corpus tests is the owner's:** *"why do we need to verify a demo has
anything?"* — an assertion that a real recording contains a death or a crouch is a claim about TF2,
which reading the SDK establishes and a test does not. Both costs, the whole reasoning and what stays
in the corpus: **D38**.

**So, in order:**

1. **Decode, behaviour, arithmetic → synthetic, in `Core.Tests`** (not `Corpus.Tests`, which nothing
   mutates). Build the entity or the demo, assert the exact value you put there.
2. **Only-real-bytes questions → the corpus.** A writer-side quirk, a truncated `dem_datatables`, a
   protocol nobody can synthesise faithfully.
3. **A MEASUREMENT is not a test.** "How many entities in a real match are translucent" is worth
   running once and recording the number in `docs/RISKS.md` or `docs/findings/`. If the harness is
   worth keeping, it is a `*Diagnostic` marked `[Explicit]` that reports numbers and asserts
   nothing — never a test that fails when a demo changes.

## Corpus reality

**Two corpora, and the distinction decides where a demo you are handed goes.**

- **gcor** — `tools/corpus/demos/`, committed, one specimen per era × point of view, each recorded on
  a period client whose `version` dates it. Most eras carry a POV *and* a SourceTV recording of the
  same session, which is the pairing that has caught two writer-side findings. It grows **only for a
  new protocol**, because GitHub's free LFS tier is 1 GiB/month and every CI job pays it — the bill
  D81 removes by fetching from archive.org instead.
- **lcor** — `tools/corpus/local/`, git-ignored. Modern matches, extra specimens, volume. Tests pick
  it up automatically, so a local run is a superset of CI. **"Add these demos" means lcor unless the
  demo is a new protocol.**

`tools/corpus/local/` is **not** all of lcor — the real pool is several gigabytes across at least
four locations, and consolidating it is part of D81. Counts, sizes and run times: `docs/verification/`.

**`TF2DEMOSALVAGE_GCOR_ONLY=1` is what you want most of the time** — two orders of magnitude faster,
and enough for any run whose purpose is "did I break something". Run the full superset only when the
change touches decoding itself.

**Never walk the whole corpus in a test.** A measurement wants a handful chosen deliberately — real
matches, one or two per era, both points of view. **Era specimens cannot answer a rendering or roster
question at all**: they are the owner's solo recordings, with no other players and no worn items, so
they inflate a denominator and measure nothing. `CorpusPlayerOriginTests` is the worked example of
picking a sample and saying why, and `Corpus.Demo("name")` is how a test asks for one specific demo —
it skips with a reason when the file is absent rather than throwing out of `First`.

**The gate's floors live in `build/gate.sh`, never in a document.** It prints each beside what it
measured and refuses a drop until the reason is written next to it, which is stronger than any number
here could be — `grep -E '^run Tf2DemoSalvage' build/gate.sh`. They are **floors, not equalities**, so
adding tests passes and only a drop is caught. Two rules that each cost a re-run: the check is the
COUNT per project against its floor and never the word `Passed!` (B104), and **do not filter the
gate's output down to summary lines** while iterating, because you lose which test failed.

**The era axis belongs to `docs/TIMELINE.md`** — which protocols have specimens, which windows are
dated, which gaps are open, and which of those a demo could ever close. It has moved twice; do not
restate a gap here. Two facts govern how to read it: **a protocol dates a demo not at all**, because
an old client still runs and still records (`docs/memory/era-axis-is-measured.md`), and older
specimens are genuinely rare because pre-2013 competitive TF2 used live Mumble casts and had no
archive before demos.tf (D5). Build schema-driven *because* of that. A demo that surfaces goes in
`tools/corpus/manifest.json` with a regression fixture in `tests/`.

## `docs/findings/` is a rolling account — keep it current as you go

`docs/findings/` is the **reverse-engineering history of TF2's demo system**: how each part of the
format was worked out, what was believed first, what turned out to be wrong, and which piece of
evidence settled it. It is written to be read end to end and quoted in a write-up.

**Update it in the same commit as the finding, not at the end of the project.** A finding written
up weeks later loses the thing that makes it worth reading — the wrong turn, the measurement that
killed it, the number that made it obvious.

Two things belong there that are easy to leave out: **anything learned about Valve's own code and
engine behaviour** rather than the wire format — vestigial fields, dead guards, clamps — which is the
part with the least prior art; and **wrong conclusions and what killed them**, kept deliberately,
because a conclusion recorded without the reasoning that failed is the kind that gets confidently
repeated.

**Which document answers what — SPEC, findings, RISKS, DECISIONS, TIMELINE, verification — is a table
in `docs/findings/README.md`, and that is its only copy.** Read it before starting a new document;
the version that used to sit here drifted a row out of step with it.

Mark each claim with its evidence class — read from published source, measured on the corpus,
arithmetic, differential, or interpolated. They are not equal, and the difference has repeatedly
decided arguments. Flag interpolations every time.

## AI memory is mirrored into this repo

`docs/memory/` holds the assistant's working memory, committed so it survives a machine
wipe or a move to another computer. **Write every memory change to both places** — the
assistant's own memory directory *and* `docs/memory/`. Updating only one silently diverges
the local copy or leaves the backup restoring something stale.

**No personal or identifying information goes in `docs/memory/`** — this repo is meant to be
public. Personal preferences belong in the assistant's global memory (`~/.claude/memory/`).
The test: would it help on a different project? Then it is global.

Read `docs/memory/MEMORY.md` at session start alongside this file. Several entries record
corrections to earlier wrong conclusions; those are deliberate, because a memory that keeps
only the conclusion is the kind that gets confidently repeated.

## Probes are scripts, not tests — `tools/Tf2DemoSalvage.Probe` (D126)

**A question about one demo at one tick is a PROBE, not a test.** The owner: *"you can script a probe
outside the test suite, having a bunch of probe tests just slows the suite down and putting in a
suite and running the whole damn thing takes forever"*. An `[Explicit]` test still costs a build and
still sits in a floor; a probe costs neither.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- <name> [args]
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release --                # lists them
```

**The list of probes is NOT reproduced here** — the no-argument run prints every one with its own
one-line summary and usage, out of `IProbe.Summary` beside the code. A table here would be a partial
copy that goes stale, and the last one did: it named eighteen and claimed forty-four against a
directory that holds more. Ask the tool. Adding a probe is adding a file — `Program` discovers
implementations by reflection, so there is no registration to forget.

**Probes run the PRODUCTION path or they are worthless.** `DemoCorpus` lives here and
`Corpus.Tests` references the tool rather than the reverse, so the probe and the test cannot disagree
about which file they opened. A probe that reimplements the rule it is checking agrees with whoever
wrote the probe.

**And a probe is an instrument, so it needs a control.** Five separate probes gave confident wrong
answers in one session — a grouping that hid a class, a probe that skipped the resolution step the
viewer runs, a bare `new NetDecodeState()` that decodes nothing at protocol 11, a `Precache` return
value that means "offered" rather than "packed", and a hex search that reported three tables as
absent. **Before believing a probe's absence, ask it for something that must be there.** If that
comes back missing too, the instrument is broken, not the subject.

## Where to start

Phase 1 (see `ROADMAP.md` §3): `managed/Tf2DemoSalvage.Core`, pure C# — container parsing, then `dem_datatables`/`dem_stringtables`, then generic SendTable-driven entity delta decode, emitting a normalized event stream. Validate against `z1800.dem` end to end once the primitives are unit-tested individually. Output target: a Quake-style readable trace — the demo decompiled to text, message by message, in stream order — plus a summary dump and JSON Lines. **No SQLite**: removed 2026-08-10, see `docs/DECISIONS.md` D17. Do not create anything under `native/libtf2dem` for this phase.

Do not start Phase 2 or Phase 3 work before Phase 1 is solid and tested. Do not build toward Phase 4 (demo repair for live-client replay) at all unless explicitly asked — it's parked, see `docs/DECISIONS.md` D1.

## Test naming — `{Subject}_{Scenario}_{Expected}`

**Every test method is named `{Subject}_{Scenario}_{Expected}`.** Classes are `{TypeUnderTest}Tests`,
and a class whose name contains `Conformance` must keep it, because `docs/CONFORMANCE.md` selects
those suites with `--filter 'FullyQualifiedName~Conformance'`.

- **Subject** — the method under test where there is one (`Decode`, `Write`, `Parse`); otherwise the
  operation, for tests that deliberately span layers (`RoundTrip`, `Trace`, `Dump`).
- **Scenario** — the condition (`AtProtocol23`, `AfterAStopWithoutFlags`, `WithNoStopCommand`).
- **Expected** — the predicted observation (`Is14Bits`, `InheritsSndStop`, `ReproducesBytes`).

```
SoundNumberBits_AtProtocol22And23_Are13And14      not  TheSoundIndexIs13BitsThrough22And14BitsAfter
Decode_SoundAfterAStopWithoutFlags_InheritsSndStop     ASoundAfterAStopInheritsSndStopUnlessItSaysOtherwise
RoundTrip_EveryWritableKind_ReproducesBytes            EveryWritableKindCompilesBackToItsOwnBytes
```

**Convert an existing file's names when you are already editing it, and never with a regex** —
choosing the subject, scenario and expectation means reading what the test asserts, and a mechanical
transform produces plausible names that are wrong in a way nobody goes back to fix. Why the
convention is written down at all, what the ~2,132 prose names cost, and what makes bulk conversion
safe: `docs/memory/test-naming-convention.md`.

## The order of work, and where to look

**A conformance test comes first, then unit/integration/UI tests, then the implementation.** The
conformance test is where "what does the engine actually do" gets written down — with its citation —
*before* any code exists to bias the answer. Written afterwards it becomes a description of what was
built, which is the one thing a parity test must never be.

**Read the source before measuring our data.** Measuring this project can only find data that is
wrong; it cannot find a feature that was never implemented, and every measurement comes back correct
while it looks like progress. The tell is three correct measurements in a row: the question is wrong,
not the data. See `docs/memory/nothing-is-closed.md#read-the-spec-before-measuring-our-data`, which was written after a
session spent measuring a model that was never at fault.

**Anything that produces output is not done until an assertion has read that output on a real
demo.** A unit test proves a component works when called with the values the test chose; it says
nothing about whether production calls it, or with what. That gap shipped three no-ops in one
session, all with a green suite — `docs/memory/output-level-assertion-or-it-is-not-done.md`.

So: **write the component tests, then add one assertion against the rendered artefact for a corpus
demo.** It is the only test that can fail when the wiring is wrong.

**A diagnostic is an instrument, and an instrument is proved with a control before it is believed.**
Eight lied in two sessions, each with a confident answer — the casebook is
`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`. Two rules cover all eight:

- **Report the value the code USED, carried to it — never recomputed by a second route** (B243).
- **Before believing an absence, ask the instrument for something that MUST be present**
  (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).

**Four sources. This is a menu, not a ladder — pick the one that holds the answer and skip the rest.**

| Source | Holds | Rules |
|---|---|---|
| `source-sdk-2013` (`F:/src/source-sdk-2013`) | shaders, file formats, math, message lists, material flags | read and cite freely; quoting it in comments is the point |
| [demostf/parser](https://github.com/demostf/parser) (`tf-demo-parser`, demos.tf's own) | demo container and entity decode | read for cross-checking, never port. **Knows nothing about rendering — skip it outright for anything drawn** |
| Valve Developer Community wiki (Networking Entities, Networking Events & Messages) and [demboyz `DemFormat.md`](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md) | conventions and parameter meanings the SDK does not spell out; the container as of July 2015 | secondary; neither is a citation of behaviour |
| a decompiler | the closed engine — the material system, TF2's own shaders, anything the SDK omits | **reach for it readily.** The only hard rule is where its output lives |

**Going through sources in order is a waste when you already know which one holds the answer.** A
question about the demo container does not belong to a decompiler; a question about how `Modulate`
blends does not belong to the Rust parser, which has never drawn anything.

**There is a fifth source and it is the one nobody thinks of: the game's own shipped data.** VMTs,
`.res` files, VPK contents. It is not code, so it does not feel like a source — and it answered two
questions already filed as needing a decompiler: `$modblend`, which is dead and whose correct
implementation is nothing (`docs/findings/12-shader-parity.md`), and game event field widths, which
are documented in a comment block at the top of `modevents.res` (`docs/CONFORMANCE.md`).

**When the question is about a format the GAME reads, read what the game ships** — before
decompiling.

**Decompiler output never goes in a git tree, and the reason is SIZE rather than licensing.** Run it
with its paths under `D:` and carry back only what is written by hand afterwards. The invocation,
the JDK 21 requirement and the disassembly-over-decompiler rule are in `docs/DECOMPILING.md`.

**Two instruments measure conformance and they are not interchangeable** — a generated denominator
that can never go stale catches a MISSING implementation, and only the hand-written suites catch a
WRONG one. Both, with their numbers: `docs/CONFORMANCE.md`.
