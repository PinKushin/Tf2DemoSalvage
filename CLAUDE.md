# CLAUDE.md — implementation handoff

This project was planned in a Cowork conversation before any code existed. Everything below is context an implementer needs that isn't obvious from an empty repo. Read `ROADMAP.md` and `docs/DECISIONS.md` in full before writing code — this file is a pointer/summary, those are the source of truth.

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

- **No Rust.** Explicitly rejected, don't suggest it.
- **No C++ by default.** C for the perf/correctness-critical decode core, C# for everything else. C++ is only acceptable if deliberately reached for to wrap Source SDK code for one specific hard-to-reverse-engineer Phase 3 asset format (see `docs/DECISIONS.md` D4), and even then it must be isolated behind a C ABI shim, not spread through the codebase.
- **No Python** for the core — too slow for bulk corpus processing at the scale this is meant to eventually handle.
- **Default to C# for everything, including work that feels performance-sensitive.** Use `unsafe`/`Span<T>`/`stackalloc`/`MemoryMarshal` before reaching for C. Only drop into `libtf2dem` when C# has actually proven inadequate for that specific piece (profiled, not assumed) — the C surface should stay limited to the varint/bit-level decode primitives that genuinely need it, not expand by default just because it's "the perf layer."
- **No native code for Phase 1/2, full stop.** The decode engine lives in `managed/Tf2DemoSalvage.Core`, pure C#. `native/libtf2dem` is a placeholder folder, not a starting point — don't build in it unless Phase 3 profiling has actually shown a specific piece needs it. If that ever happens: default to C (MSBuild/vcxproj, same VS solution as everything else); Zig is an open long-shot alternative (not C++) since it exports a plain C ABI natively — same P/Invoke story as C, better memory safety, no C++-style naming/template chaos — but it needs its own build step (`build.zig`) outside the main `.sln`. Decide C-vs-Zig only when this trigger actually fires, not preemptively.
- **TDD, SOLID, DRY are standing requirements**, not just this project's style — see `docs/DECISIONS.md` D6 for how they map onto this codebase's actual seams (decode-vs-interpret separation, per-version-quirk strategy objects instead of branchy conditionals, one schema/quirk table as single source of truth). Write the byte-level unit tests for `libtf2dem`'s primitives (varint reader, bit reader, SendTable delta decode, string table decode) *before* the implementation, using small hand-built fixtures — don't rely on end-to-end corpus tests alone to catch primitive-level bugs, the corpus is too sparse for that to be safe (see next section).
- **A user's real TF2 config must work wholesale — `.cfg` or a mastercomfig-style `.vpk`** (D69). This means the viewer's own vocabulary *is* Source's: keys named `SPACE`, `CTRL`, `MOUSE1`, `'`, `/`, and actions named `+forward`, `+jump`, `+moveup`. A translation layer defeats the point, because the requirement is that a paste works. Ignoring unknown commands is the primary feature, not an afterthought — a real config is hundreds of `mat_*`/`cl_*`/`alias`/`exec` lines this viewer does not implement, and a parser that objected would reject every real file. **`ROADMAP.md` filed this for weeks as "the return is small… copying TF2's default bindings gets almost all of the benefit", and that assessment governed the work until it was reversed** — so a defaults table was built and mistaken for the feature. If you find yourself concluding this is nearly done because the defaults match TF2, that is the same wrong turn.
- **Run Stryker.NET on every C# test project** as part of normal development, not bolted on at the end. It mutates the code and checks whether the test suite actually kills the mutants — proves the tests do something, unlike coverage percentage alone. A surviving mutant is a real finding: either add the missing assertion, or the mutated code path genuinely doesn't matter and can be deleted. Doesn't apply to `libtf2dem` (Stryker is .NET/JS-only) — for the C core, equivalent rigor comes from adversarial hand-built byte fixtures per primitive, and every malformed-input bug found becomes a permanent regression fixture.
- **Wire up SonarLint + Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`) from the first C# project**, with `.editorconfig` set to `warning`/`error` for correctness-related rules so violations surface at build time, not in a later cleanup pass.

## Synthetic fixtures come FIRST; the corpus keeps only what real bytes alone can prove (D38)

**This is already D38 and it was not being followed.** The owner, 2026-08-29: *"they are suppose to
be used above real demos in tests"*, and *"we have a bunch of tests and stuff that cant be mut
tested and takes longer than required to test"*. It is repeated here because `docs/DECISIONS.md` is
long and this file is the one read at session start — the rule existed and the reminder did not.

**Two costs, and neither is style.** A corpus test needs Git LFS, so it **cannot run on the
measurement boxes at all** — a test placed in `Corpus.Tests` is one Stryker never mutates. And it is
slow: `Core.Tests`' synthetic suite runs in a few hundred milliseconds where corpus suites take
tens of seconds each.

**A synthetic fixture is STRONGER, not a compromise.** A corpus test does not know the right answer
and must compare two readings of the same file; a hand-built one HAS ground truth, because the test
put the value there. `SyntheticDemo` can write a demo the engine itself accepts.

**The question that kills most corpus tests**, and it is the owner's: *"why do we need to verify a
demo has anything?"* An assertion that a real recording contains a death, a crouch, an observer mode
or a translucent entity is a claim about **TF2**, not about this parser. Reading the SDK establishes
that; a test does not.

**So, in order:**

1. **Decode, behaviour, arithmetic → synthetic, in `Core.Tests`** (not `Corpus.Tests`, which nothing
   mutates). Build the entity or the demo, assert the exact value you put there.
2. **Only-real-bytes questions → the corpus.** A writer-side quirk, a truncated `dem_datatables`, a
   protocol nobody can synthesise faithfully.
3. **A MEASUREMENT is not a test.** "How many entities in a real match are translucent" is worth
   running once and recording the number in `docs/RISKS.md` or `docs/findings/`. If the harness is
   worth keeping, it is a `*Diagnostic` marked `[Explicit]` that reports numbers and asserts
   nothing — never a test that fails when a demo changes.

**Live examples of the mistake, from the session that produced this note:**
`CorpusObserverModeTests` and `CorpusRenderModeTests` were both written as corpus tests asserting
what real demos contain, when the decode belonged in a synthetic test and the counts belonged in a
diagnostic. Both were converted.

## Corpus reality

**This section described a one-demo corpus and is now badly out of date in the good direction.** As of 2026-08-10 the committed corpus (**gcor**) is 10 demos / 20.3 MB spanning **five measured protocols** — 11, 14, 15, 16 and 24 — each recorded on a period client whose `version` output dates it exactly. Most eras carry a POV and a SourceTV recording of the same session, which is the pairing that has caught two writer-side findings. Metadata in `tools/corpus/manifest.json`, era table in `docs/TIMELINE.md`.

`z1800.dem` is still there and is still the founding specimen, but the guess about it in the original text was wrong twice over: it is **2020 or later, not ~2015** (protocol numbers date nothing — see `docs/memory/z1800-is-modern-not-2015.md`), and it decodes end to end rather than being a target.

Two corpora, and the distinction matters when you are told to add a demo:

- **gcor** — `tools/corpus/demos/`, committed, one specimen per era × point of view. It grows **only for a new generation**, because GitHub's free Git LFS tier is 1 GiB/month and every CI job pays for it. Era specimens are kept to 2–4 minutes deliberately (`manifest.json` notes).
- **lcor** — `tools/corpus/local/`, git-ignored, **49 demos there**. Modern matches, extra specimens, anything for volume. Tests pick it up automatically, so a local run is a superset of CI. **"Add these demos" means lcor unless the demo is a new protocol.**

**`tools/corpus/local/` is NOT all of lcor, and assuming it is will mislead you about scale.** The owner, 2026-08-26: *"the FULL lcor includes the 3 gigs of esea and etf2l demos, and the benroads demos, and the 20 demos found by another agent on d:, and the tf2 research repo"*. So the real pool is several gigabytes across at least four locations, and the 774 MB in `tools/corpus/local/` is the part a test currently sees.

**Planned, after the current refactor:** consolidate onto `D:` as one demo archive and point lcor at it — *"i kinda want all the lcor demos in the d: demo archive, although that still leaves a bunch of demos actually in another repo too, but we might be able to use symlinks for those or for all of the consolidation so nothing has to actually move"*. Symlinks mean nothing is copied and no repo loses its own copy.

**Meanwhile: never walk the whole thing in a test.** A measurement wants a handful of demos chosen deliberately — real matches, one or two per era, both points of view. **Era specimens cannot answer a rendering or roster question at all**: they are solo recordings on period clients with no other players and no worn items, so they inflate a denominator and measure nothing. `CorpusPlayerOriginTests` is the worked example of picking a sample and saying why.

**Both of these are about to change, and D81 is why.** The corpus is moving to archive.org and will
be fetched rather than committed. The gcor/lcor split exists because GitHub's free Git LFS tier is
1 GiB/month and every CI job pays it — that bill is the reason era specimens are trimmed to 2–4
minutes and the reason protocols 21 and 22 currently sit in lcor while filling a measured gap.
Fetching removes it, after which the distinction that earns its keep is **fast tier vs deep pool**
rather than committed vs local.

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

The UI phase goes inside `run-exclusive.ps1`, since it takes the desktop. Twelve assemblies plus a
UI suite; roughly 4,690 and 31 as of 2026-09-03.

**The per-assembly counts are NOT reproduced here, and that is the correction.** This file used to
carry a table of them under a warning that it would drift — and it drifted, by about four hundred
tests, while the warning sat directly beneath it. A snapshot that says "this will go stale" is still
a stale number that somebody reads.

`build/gate.sh` holds the authoritative floors, prints each beside what it measured, and refuses a
drop until the reason is written next to it. Ask it:

```bash
grep -E '^run Tf2DemoSalvage' build/gate.sh
```

`Tf2DemoSalvage.Audio.Tests` became part of the gate when the audio project stopped being
unreachable (B168), and `Scene.Tests` exists because Scene had grown its own layer (B184). The UI
suite went from 8 to 19 and is worth running: it caught the F11 collision that silently broke full
screen for days (B165).

**Two of those counts went DOWN on 2026-08-26, and a falling count is normally a defect.** Viewer
lost 10 to B207 and presentation lost 11 to B206 — both were tests on types with no production
caller at all, and each drop is recorded next to its floor in `build/gate.sh` with what went and why
nothing was lost. The gate refuses a drop until that is written, which is the point.

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

Remaining gaps on the era axis: protocols **12–13** and **17–20, 23**.

**21 and 22 are covered, locally.** Four demos recovered by the `tf2-comp-archive` agent from
GotFrag MediaFire links still live fourteen years later — `leeko_badlands_4_63800.dem` at protocol
21, and three at 22 — fill the middle of the range where TF2 changed fastest. They are in **lcor**,
so CI and a fresh clone do not see them, and the container test still allows only
`[11, 14, 15, 16, 24]` and therefore reports all four as failures (B162).

**Widen that list — it is a fact about the files, not a claim about history.** An earlier version of
this note said to date them first, which conflated two independent questions: the container test
asks whether a header parses to a plausible protocol, and `docs/TIMELINE.md` asks when that protocol
was current. A demo can be a legitimate protocol-22 specimen with no known date. Tying the assertion
to dating would have left four known-good demos red indefinitely, because **these are scrims and
pugs rather than league matches** and there is no ESEA or ETF2L record to date them from.

**And note what a demo can and cannot establish, because it is easy to blur.** A demo's protocol
says which protocol it speaks and **nothing else** — it does not bound the recording date even
loosely, because an old client still runs and still records. **The counterexample is this corpus:**
every gcor era specimen was recorded on a period client in 2026, so `tf2-2007-build3258-pov` speaks
protocol 11 and is weeks old.

So there are two gaps and these demos close only one. The **specimen** gap for 21 and 22 is now
closed; the **dating** gap is still open and needs a *build* — a client of known stamp reporting a
`version`. The windows between those stamps are estimated from changelogs and forum posts rather
than measured, and always have been. Filenames and map release windows can date an individual demo,
but that only informs the protocol window if the demo is independently known to be contemporaneous,
which for a recovered scrim it is not.

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

## Probes are scripts, not tests — `tools/Tf2DemoSalvage.Probe` (D126)

**A question about one demo at one tick is a PROBE, not a test.** The owner: *"you can script a probe
outside the test suite, having a bunch of probe tests just slows the suite down and putting in a
suite and running the whole damn thing takes forever"*. An `[Explicit]` test still costs a build and
still sits in a floor; a probe costs neither.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- <name> [args]
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release --                # lists them
```

Thirty-one of them. The ones worth knowing before writing a thirty-second:

| probe | answers |
|---|---|
| `carried <demo> <tick> [class]` | per player, per entity: what they hold and WHICH RULE dropped the rest |
| `props <demo> <tick> [filter]` | props at a tick, grouped by model AND class |
| `instance <demo> <tick> <model>` | where the renderer puts a model — matrix and bone 0 |
| `baseline <demo> [class] [prop]` | which classes carry an instance baseline, and what it declares |
| `attachments [item|substring]` | items that hang extra models on themselves |
| `weapon-models`, `viewmodels`, `spy-draw` | weapons that resolve to nothing; what the recorder holds |
| `parity [filter]` | engine functions we cite, ranked by Valve's branch count |
| `bone-flags <demo>` | which per-bone flags and procedural rules real models set, with denominators |
| `procedural-bones` | the same question over every `.mdl` the GAME ships, not one demo's models |
| `ragdoll-constraints` | what a `.phy` holds — masses, joint limits and bone names are TEXT; only the hulls are Havok |
| `vmt-blocks [name]` | sub-blocks the 30,684 shipped materials open, and the parameters they hide |
| `corpses <demo>` | what each `CTFRagdoll` says about itself, and how many are drawn at a tick |
| `autoplay <demo>` | which models animate themselves off the clock — `STUDIO_AUTOPLAY` |

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

**A diagnostic is an instrument, and an instrument is proved with a control before it is believed.**
Eight lied in two sessions, every one with a confident answer: a log line that reported the
illumination point as a position, a cull census that passed on an accident of geometry, a materials
line keyed by the wrong field, a probe grouping that hid a class inside another's label, a probe
that skipped the resolution step production runs, a hex search whose "absent" was true of three
tables that certainly exist, a `Precache` return meaning "offered" rather than "loaded", and a
decode state built without the protocol that silently reads nothing. Two rules cover all eight:

- **Report the value the code USED, carried to it — never recomputed by a second route** (B243). The
  second route is free to be wrong, and when it is, it is wrong in a way that looks authoritative.
- **Before believing an absence, ask the instrument for something that MUST be present.** An empty
  answer for that too means the instrument is broken, not the subject
  (`docs/memory/an-empty-search-needs-a-control.md`).

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

**It works, and here is the invocation that works, because getting there cost an hour.** Ghidra
12.1.2 needs **JDK 21** — on JDK 25 it dies in its OSGi layer with
`ERROR: Bundle org.apache.felix.framework [0] The data file must be inside the data dir.` followed by
a `dataFile is null` abort. That reads like a corrupt bundle cache and is not: deleting the cache
changes nothing, and `JAVA_TOOL_OPTIONS` does not reach it. Point it at a 21 instead.

```bash
JAVA_HOME="C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot" \
  "/d/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "D:\ghidra-proj" tf2engine \
  -import "F:\SteamLibrary\steamapps\common\Team Fortress 2\bin\x64\engine.dll" -overwrite

JAVA_HOME="…jdk-21…" "/d/ghidra_12.1.2_PUBLIC/support/analyzeHeadless.bat" "D:\ghidra-proj" \
  tf2engine -process engine.dll -noanalysis \
  -scriptPath "D:\ghidra-proj\scripts" -postScript DecompAt.java 1800683d0
```

Analysis of `engine.dll` is a few minutes; scripts against the analysed program are seconds.
`D:\ghidra-proj\scripts` holds small `GhidraScript` helpers written for this — decompile at an
address, find callers of an address, find the function holding a string, list functions in a range.
**The string search is the way in**: Valve left the assert strings, so
`CL_CopyNewEntity: GetClassBaseline(%d) failed.` names its own function and finding it is one script
run.

**A zero exit means nothing here.** `analyzeHeadless.bat` exits 0 on a Java stack trace, so grep the
output for `ERROR` rather than trusting the status — the first attempt "succeeded" while having done
nothing at all.

**Two instruments measure conformance and they are not interchangeable.** `SdkCoverageTests`
generates the denominator from the SDK — 489 shader parameters, 66 lumps, 54 studio structures — and
can never go stale. The hand-written suites carry the semantics and the COST: whether `$detail` is
implemented with the right blend mode, and what a gap looks like on screen. Only the second kind
catches a wrong implementation, and only the first kind catches a missing one.

## Reference material (external, not vendored)

- [demostf/parser](https://github.com/demostf/parser) / `tf-demo-parser` crate — mature Rust reference implementation (demos.tf's actual parser). Read for cross-checking behavior, do not port code directly (different language, and the point is to actually understand the format).
- [demboyz DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md) — container format writeup, as documented for the TF2 build active July 2015.
- Valve Developer Community wiki: Networking Entities, Networking Events & Messages.
