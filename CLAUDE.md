# CLAUDE.md — implementation handoff

This project was planned in a Cowork conversation before any code existed. Everything below is context an implementer needs that isn't obvious from an empty repo. Read `ROADMAP.md` and `docs/DECISIONS.md` in full before writing code — this file is a pointer/summary, those are the source of truth.

## What this project actually is

A standalone TF2 `.dem` parser that works across TF2's full history, including demos the live game client can no longer play due to Valve's own schema changes. The insight that makes this tractable: `.dem` files embed their own entity schema (`SendTables`, via the `dem_datatables` command), so a parser that decodes generically off whatever schema each file provides — rather than hardcoding one era's field layout — doesn't need to "know" every TF2 version, just the container/bit-packing quirks, which change far less often. Full explanation in `ROADMAP.md` §1.

## Non-negotiable constraints (owner-stated, don't relitigate without asking)

- **No Rust.** Explicitly rejected, don't suggest it.
- **No C++ by default.** C for the perf/correctness-critical decode core, C# for everything else. C++ is only acceptable if deliberately reached for to wrap Source SDK code for one specific hard-to-reverse-engineer Phase 3 asset format (see `docs/DECISIONS.md` D4), and even then it must be isolated behind a C ABI shim, not spread through the codebase.
- **No Python** for the core — too slow for bulk corpus processing at the scale this is meant to eventually handle.
- **Default to C# for everything, including work that feels performance-sensitive.** Use `unsafe`/`Span<T>`/`stackalloc`/`MemoryMarshal` before reaching for C. Only drop into `libtf2dem` when C# has actually proven inadequate for that specific piece (profiled, not assumed) — the C surface should stay limited to the varint/bit-level decode primitives that genuinely need it, not expand by default just because it's "the perf layer."
- **No native code for Phase 1/2, full stop.** The decode engine lives in `managed/Tf2DemoSalvage.Core`, pure C#. `native/libtf2dem` is a placeholder folder, not a starting point — don't build in it unless Phase 3 profiling has actually shown a specific piece needs it. If that ever happens: default to C (MSBuild/vcxproj, same VS solution as everything else); Zig is an open long-shot alternative (not C++) since it exports a plain C ABI natively — same P/Invoke story as C, better memory safety, no C++-style naming/template chaos — but it needs its own build step (`build.zig`) outside the main `.sln`. Decide C-vs-Zig only when this trigger actually fires, not preemptively.
- **TDD, SOLID, DRY are standing requirements**, not just this project's style — see `docs/DECISIONS.md` D6 for how they map onto this codebase's actual seams (decode-vs-interpret separation, per-version-quirk strategy objects instead of branchy conditionals, one schema/quirk table as single source of truth). Write the byte-level unit tests for `libtf2dem`'s primitives (varint reader, bit reader, SendTable delta decode, string table decode) *before* the implementation, using small hand-built fixtures — don't rely on end-to-end corpus tests alone to catch primitive-level bugs, the corpus is too sparse for that to be safe (see next section).
- **Run Stryker.NET on every C# test project** as part of normal development, not bolted on at the end. It mutates the code and checks whether the test suite actually kills the mutants — proves the tests do something, unlike coverage percentage alone. A surviving mutant is a real finding: either add the missing assertion, or the mutated code path genuinely doesn't matter and can be deleted. Doesn't apply to `libtf2dem` (Stryker is .NET/JS-only) — for the C core, equivalent rigor comes from adversarial hand-built byte fixtures per primitive, and every malformed-input bug found becomes a permanent regression fixture.
- **Wire up SonarLint + Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`) from the first C# project**, with `.editorconfig` set to `warning`/`error` for correctness-related rules so violations surface at build time, not in a later cleanup pass.

## Corpus reality

**This section described a one-demo corpus and is now badly out of date in the good direction.** As of 2026-08-10 the committed corpus (**gcor**) is 10 demos / 20.3 MB spanning **five measured protocols** — 11, 14, 15, 16 and 24 — each recorded on a period client whose `version` output dates it exactly. Most eras carry a POV and a SourceTV recording of the same session, which is the pairing that has caught two writer-side findings. Metadata in `tools/corpus/manifest.json`, era table in `docs/TIMELINE.md`.

`z1800.dem` is still there and is still the founding specimen, but the guess about it in the original text was wrong twice over: it is **2020 or later, not ~2015** (protocol numbers date nothing — see `docs/memory/z1800-is-modern-not-2015.md`), and it decodes end to end rather than being a target.

Two corpora, and the distinction matters when you are told to add a demo:

- **gcor** — `tools/corpus/demos/`, committed, one specimen per era × point of view. It grows **only for a new generation**, because GitHub's free Git LFS tier is 1 GiB/month and every CI job pays for it. Era specimens are kept to 2–4 minutes deliberately (`manifest.json` notes).
- **lcor** — `tools/corpus/local/`, git-ignored, currently 14 demos / 774 MB. Modern matches, extra specimens, anything for volume. Tests pick it up automatically, so a local run is a superset of CI. **"Add these demos" means lcor unless the demo is a new protocol.**

**`TF2DEMOSALVAGE_GCOR_ONLY=1` runs against gcor alone, and it is what you want most of the time.**
The corpus suite over lcor takes about **30 minutes**; over gcor it takes **28 seconds**, because
lcor is 774 MB of modern matches against 20 MB of short era specimens. Use it for any run whose
purpose is "did I break something" — the merge gate, a quick check after an edit — and run the full
superset when the change touches decoding itself.

**The gate runs in TWO PHASES, and one `dotnet test` on the solution is not a valid way to run it.**
`dotnet test` executes test ASSEMBLIES concurrently, so `Viewer3D.UiTests` — which launches the
viewer and drives a real window — ends up competing with roughly 1,700 other tests. Measured
2026-08-16: the UI suite passes in 2 seconds alone and failed one of eight at 10 seconds inside a
single-invocation gate (B89). `run-exclusive.ps1` does not help, because it serialises this machine
against OTHER agents and says nothing about what one `dotnet test` does with itself.

```bash
bash build/gate.sh
```

```bash
pwsh run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests
```

The UI phase goes inside `run-exclusive.ps1`, since it takes the desktop. Green as of 2026-08-18:
**2,062 across six assemblies**, plus 8 UI.

**`build/gate.sh` replaced a solution-wide `dotnet test --filter`, and the reasons are worth knowing
because both bit.**

- **It asserts the COUNT of every project** against a floor, via `build/assert-test-count.sh`. A run
  that reports `Passed! ... Total: 50` against a suite of 350 is a failure wearing a pass, and one
  happened here on 2026-08-17 (B104). Reading six console lines by eye is not a check.
- **A solution-wide run writes one `.trx` per project all under the same name**, so nothing
  afterwards can tell the counts apart. One project at a time is what makes the floors possible.
- **`--filter` changes which tests EXIST.** NUnit's adapter includes `[Explicit]` tests when no
  filter is given and drops them the moment any filter is present, so the old
  `--filter 'FullyQualifiedName!~UiTests'` silently omitted every `[Explicit]` test in the
  repository — measured as 441 against 436 on Content.Tests alone.

**Do not filter the gate's output down to summary lines while iterating.** A run filtered to
`Passed!|Failed!` loses which test failed, which cost a re-run today — the same "log what you will
need before you need it" rule the subsystem logs follow.

**A test that needs a specific demo asks for it with `Corpus.Demo("name")`**, which skips with a
reason when the file is absent rather than throwing out of `First`. That distinction matters here:
the committed era specimens are the owner's own SOLO recordings, so they carry no other players and
no worn items at all — the 2013 badlands POV has 11 props and zero wearables. A cosmetics test
redirected onto one would pass while measuring nothing, which is worse than skipping.

Remaining gaps on the era axis: protocols **12–13** and **17–23**.

Do not assume a broad multi-era test corpus exists or will exist soon. TF2's pre-2013 competitive scene mostly used live Mumble casts rather than recorded demos, and there was no centralized archive before demos.tf, so older specimens are genuinely rare (`docs/DECISIONS.md` D5). Build defensively (schema-driven, not hardcoded) *because* of this, not despite it. If/when more demos surface (community outreach is a parallel, non-blocking effort), add them to `tools/corpus/manifest.json` and give each one a regression fixture in `tests/`.

## `docs/findings/` is a rolling account — keep it current as you go

`docs/findings/` is the **reverse-engineering history of TF2's demo system**: how each part of the
format was worked out, what was believed first, what turned out to be wrong, and which piece of
evidence settled it. It is written to be read end to end and quoted in a write-up.

**Update it in the same commit as the finding, not at the end of the project.** A finding written
up weeks later loses the thing that makes it worth reading — the wrong turn, the measurement that
killed it, the number that made it obvious.

What belongs there, in rough order of value:

- **Anything learned about Valve's own code and engine behaviour**, not just about the wire format.
  Vestigial fields, placeholder bytes, dead guards, clamps, how `bf_write` behaves under pressure.
  This is the part with the least prior art and the most interest.
- **Dates and history.** Valve publishes *what* changed between protocols and never *when*. The
  protocol-to-build-date table is original research and every new specimen extends it.
- **Wrong conclusions and what killed them.** Kept deliberately — a conclusion recorded without the
  reasoning that failed is the kind that gets confidently repeated.

Keep the division of labour clean, because duplicated prose goes stale:

| Document | Answers |
|---|---|
| `docs/SPEC.md` | what the format **is** |
| `docs/findings/` | **how we know**, and what we got wrong |
| `docs/RISKS.md` | what is still **open**, numbered |
| `docs/DECISIONS.md` | **why the project** is built this way |
| `docs/TIMELINE.md` | the **era axis** |

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

**This is written down because its absence is what caused the problem.** The repository grew ~2,132
prose-named tests across 371 files and no decision was ever recorded for it — one early file set the
style, every later file matched its neighbours, and nobody compared practice against the standard.
A convention that lives only in the surrounding code is a convention that drifts, and this one
drifted to the exact opposite of what was written.

**The reason for converting is debugging cost, not tidiness.** `Failed
TheTraceNamesEveryKindItWalksPast` names the CLAIM and not the SUBJECT, so a red run starts by
opening the file to find out what the test even touches. That is paid every time something fails.

Two facts make the conversion safe, and both were checked: **nothing outside the test assemblies
references a test method name** — no `--filter` pins one, no Stryker config filters by test — and
**the count cannot change**, because `build/gate.sh`'s floors are exact, so a rename that drops or
merges a test reddens the gate immediately.

**Do not attempt it with a regex.** Choosing the subject, scenario and expectation means reading
what the test asserts. A mechanical transform produces plausible names that are wrong, and nobody
goes back to fix a name that already looks like it follows the rule.

## The order of work, and where to look

**A conformance test comes first, then unit/integration/UI tests, then the implementation.** The
conformance test is where "what does the engine actually do" gets written down — with its citation —
*before* any code exists to bias the answer. Written afterwards it becomes a description of what was
built, which is the one thing a parity test must never be.

**Read the source before measuring our data.** Measuring this project can only find data that is
wrong; it cannot find a feature that was never implemented, and every measurement comes back correct
while it looks like progress. The tell is three correct measurements in a row: the question is wrong,
not the data. See `docs/memory/read-the-spec-before-measuring-our-data.md`, which was written after a
session spent measuring a model that was never at fault.

**Anything that produces output is not done until an assertion has read that output on a real
demo.** A unit test proves a component works when called with the values the test chose. It says
nothing about whether production calls it, or with what — and that gap has shipped three no-ops in
one session, every one with a green suite:

- The dumper's kill annotation matched `int`. Game event fields are typed by their definition and
  `customkill` arrives as a **byte**, so it matched nothing and annotated nothing.
- The kill feed resolved its whole field list through a renderer that returns strings, so the same
  three fields reached it as text and its numeric lookup returned null. **Not one of 407 lines**
  carried "(headshot)".
- `m_flPlaybackRate` was decoded, retained and unit-tested, and no production code ever read it, so
  every animation played at rate 1.

Each was found by looking at the output, never by the tests that covered the code. So: **write the
component tests, then add one assertion against the rendered artefact for a corpus demo** — the text
the dump produces, the poses the timeline builds, the frame the renderer selects. It is one test and
it is the only one that can fail when the wiring is wrong.

The same rule stated from the other side: a passing test whose inputs were written by the same
person who wrote the code proves the two agree, not that either matches the demo.

**Four sources. This is a menu, not a ladder — pick the one that holds the answer and skip the rest.**

| Source | Holds | Rules |
|---|---|---|
| `source-sdk-2013` (`F:/src/source-sdk-2013`) | shaders, file formats, math, message lists, material flags | read and cite freely; quoting it in comments is the point |
| [demostf/parser](https://github.com/demostf/parser) | demo container and entity decode | read for cross-checking, never port. **Knows nothing about rendering — skip it outright for anything drawn** |
| Valve Developer Community wiki | conventions and parameter meanings the SDK does not spell out | secondary; a wiki page is not a citation of behaviour |
| a decompiler | the closed engine — the material system, TF2's own shaders, anything the SDK omits | **reach for it readily.** The only hard rule is where its output lives |

**Going through sources in order is a waste when you already know which one holds the answer.** A
question about the demo container does not belong to a decompiler; a question about how `Modulate`
blends does not belong to the Rust parser, which has never drawn anything.

**There is a fifth source, and it is missing from the table above: the game's own shipped data.**
VMTs, `.res` files, VPK contents. It is not code, so it does not feel like a source, and it answered
two questions filed as needing a decompiler:

- **`$modblend`** was the worked example here for "the SDK cannot answer this, decompile it". It
  needed no decompiler. The parameter is declared in three shipped VMTs and read by nothing — the
  only consumer is an `Equals` proxy **commented out four lines below it** in the same file. No
  published shader declares it and no shipped binary contains the string, so the material system
  ignores it. It is dead, and the correct implementation is nothing. See
  `docs/findings/12-shader-parity.md`.
- **Game event field widths and signedness** were filed as outside the SDK because
  `GameEventManager` is closed. They are documented in the comment block at the top of
  `game/mod_hl2mp/resource/modevents.res` — `short` is 16-bit *signed*, `bool` is 1 bit unsigned,
  and so on. See `docs/CONFORMANCE.md`.

So: **when the question is about a format the GAME reads, read what the game ships.** Valve's data
files carry prose explaining themselves, and nobody thinks to look there because the habit is to look
for code. The decompiler rule below stands unchanged — it is still a normal tool, reach for it
readily — but check the shipped data first when the question is about content rather than about
engine behaviour.

**The decompiler rule is about REPOSITORY SIZE, and that is the whole of it.** Decompiler projects
and output are enormous, they cannot be moved to another disk easily once committed, and a folder
committed once lives in the history for ever. So: run it with its project and output paths under a
temp directory outside every git tree, and carry back only what is written by hand afterwards — a
constant, a field order, a formula, a note saying where it came from. Never paste a decompiled
function into source. The owner's position on the legal question is that it is not a practical
concern here; the size problem is real and permanent.

**Two instruments measure conformance and they are not interchangeable.** `SdkCoverageTests`
generates the denominator from the SDK — 489 shader parameters, 66 lumps, 54 studio structures — and
can never go stale. The hand-written suites carry the semantics and the COST: whether `$detail` is
implemented with the right blend mode, and what a gap looks like on screen. Only the second kind
catches a wrong implementation, and only the first kind catches a missing one.

## Reference material (external, not vendored)

- [demostf/parser](https://github.com/demostf/parser) / `tf-demo-parser` crate — mature Rust reference implementation (demos.tf's actual parser). Read for cross-checking behavior, do not port code directly (different language, and the point is to actually understand the format).
- [demboyz DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md) — container format writeup, as documented for the TF2 build active July 2015.
- Valve Developer Community wiki: Networking Entities, Networking Events & Messages.
